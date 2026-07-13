using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OpenTiaToolkit.Core.Models;

namespace OpenTiaToolkit.Core.Services;

public sealed partial class PlcDocumentWorkflowService
{
    public async Task<PlcCodeEvidenceQueryResult> QueryProbeEvidenceAsync(string capabilityIds, string codeFormat, string inputPath)
    {
        PlcCodeArtifactValidationResult validation = ValidateProbeArtifact(codeFormat, inputPath);
        IReadOnlyList<PlcCodeCapabilityDiscovery> capabilities = await ValidateProbeCapabilitiesAsync(capabilityIds, validation, requireDisposableCreate: false).ConfigureAwait(false);
        PlcCodeEvidenceCompatibility compatibility = PlcCodeEvidenceRegistry.BuildCompatibility(capabilities, validation);
        return new PlcCodeEvidenceQueryResult
        {
            QualificationSignature = validation.QualificationSignature,
            Compatibility = compatibility,
            Matches = _evidenceRegistry.FindVerified(compatibility.Fingerprint, validation.QualificationSignature)
        };
    }

    public async Task<PlcCodeProbeRunResult> RunProbeAsync(string plcName, string capabilityIds, string codeFormat, string inputPath, string? groupPath, string? typeGroupPath, string? compilePolicy, int? maximumNewWarnings, bool acceptUnknownImpact, bool force)
    {
        PlcCodeArtifactValidationResult validation = ValidateProbeArtifact(codeFormat, inputPath);
        IReadOnlyList<PlcCodeCapabilityDiscovery> capabilities = await ValidateProbeCapabilitiesAsync(capabilityIds, validation, plcName, requireDisposableCreate: true).ConfigureAwait(false);
        PlcCodeEvidenceCompatibility compatibility = PlcCodeEvidenceRegistry.BuildCompatibility(capabilities, validation);
        IReadOnlyList<PlcCodeEvidenceRecord> verified = _evidenceRegistry.FindVerified(compatibility.Fingerprint, validation.QualificationSignature);
        if (!force && verified.Count > 0)
            return new PlcCodeProbeRunResult { ReusedEvidence = true, CleanupProven = true, Evidence = verified[0] };

        var evidence = new PlcCodeEvidenceRecord
        {
            EvidenceId = Guid.NewGuid().ToString("N"),
            StartedAtUtc = DateTimeOffset.UtcNow,
            CodeFormat = validation.Format,
            ValidatorCatalogVersion = PlcCodeFormatService.ValidatorCatalogVersion,
            QualificationSignature = validation.QualificationSignature,
            InputHash = validation.InputHash,
            Compatibility = compatibility,
            CapabilityIds = capabilities.Select(capability => capability.CapabilityId).ToArray(),
            CapabilityFingerprints = capabilities.Select(capability => capability.Fingerprint).ToArray(),
            DeclaredObjects = validation.DeclaredObjects,
            Constructs = validation.DeclaredObjects.Select(item => "object:" + item.ObjectType)
                .Concat(validation.Findings.Select(item => item.Construct + ":" + item.Classification))
                .Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            Dependencies = validation.Dependencies
        };
        _evidenceRegistry.Save(evidence);

        string transactionId = string.Empty;
        PlcCodeTransactionApplyResult? apply = null;
        PlcCodeTransactionApplyResult? cleanup = null;
        try
        {
            PlcCodeTransaction transaction = await BeginTransactionAsync(plcName, "Disposable qualification probe " + evidence.EvidenceId, compilePolicy, maximumNewWarnings, null, string.Join(",", evidence.CapabilityIds)).ConfigureAwait(false);
            transactionId = transaction.TransactionId;
            evidence.TransactionId = transactionId;
            _evidenceRegistry.Save(evidence);
            if (validation.Format.Equals("SCL", StringComparison.OrdinalIgnoreCase))
                AddSclTransactionOperation(transactionId, validation.InputPath, groupPath, typeGroupPath);
            else
                AddTransactionOperation(transactionId, Path.GetDirectoryName(validation.InputPath)!, Path.GetFileNameWithoutExtension(validation.InputPath), groupPath, "Override");

            transaction = await PlanTransactionAsync(transactionId).ConfigureAwait(false);
            if (transaction.State == "ImpactBlocked") throw new InvalidOperationException("Disposable probe impact is blocked and cannot be qualified.");
            if (transaction.State == "ImpactReviewRequired")
            {
                bool unknown = transaction.ImpactAnalysis?.Status == "Unknown";
                if (unknown && !acceptUnknownImpact) throw new InvalidOperationException("Disposable probe impact is Unknown. Review the impact and rerun with accept-unknown-impact=true only when the unresolved scope is acceptable.");
                transaction = await ApproveTransactionImpactAsync(transactionId, transaction.ImpactAnalysis!.Fingerprint, acceptUnknownImpact).ConfigureAwait(false);
            }
            apply = await ApplyTransactionAsync(transactionId, transaction.ApplyToken!).ConfigureAwait(false);
            evidence.BaselineCompilation = apply.Transaction.BaselineCompilation;
            evidence.ProbeCompilation = apply.Transaction.FinalCompilation;
            evidence.CanonicalResultHashes = apply.Transaction.Operations.SelectMany(EffectiveSnapshots).Select(snapshot => snapshot.CanonicalResultHash).Where(hash => !string.IsNullOrWhiteSpace(hash)).Cast<string>().ToArray();
            if (apply.Applied && apply.Verified) cleanup = await DisposeSuccessfulProbeTransactionAsync(transactionId).ConfigureAwait(false);
            else cleanup = apply;
            CompleteEvidence(evidence, apply.Applied && apply.Verified && cleanup.RolledBackAndVerified ? "Verified" : cleanup.RolledBackAndVerified ? "Failed" : "Quarantined", cleanup, apply.Transaction.Failure ?? string.Empty);
            if (cleanup.RolledBackAndVerified) DiscardTransaction(transactionId);
            return new PlcCodeProbeRunResult { ProbeApplied = apply.Applied, CleanupProven = cleanup.RolledBackAndVerified, Evidence = evidence, ApplyResult = apply, CleanupResult = cleanup };
        }
        catch (Exception ex)
        {
            bool cleanupProven = string.IsNullOrWhiteSpace(transactionId);
            if (!string.IsNullOrWhiteSpace(transactionId))
            {
                try
                {
                    PlcCodeTransaction current = GetTransaction(transactionId);
                    if (current.State == "Succeeded") cleanup = await DisposeSuccessfulProbeTransactionAsync(transactionId).ConfigureAwait(false);
                    else if (new[] { "Applying", "Verifying", "DisposingProbe", "RollingBack", "RecoveryRequired" }.Contains(current.State, StringComparer.Ordinal)) cleanup = await RecoverTransactionAsync(transactionId).ConfigureAwait(false);
                    else cleanupProven = true;
                    cleanupProven |= cleanup?.RolledBackAndVerified == true;
                    if (cleanupProven) DiscardTransaction(transactionId);
                }
                catch { cleanupProven = false; }
            }
            CompleteEvidence(evidence, cleanupProven ? "Failed" : "Quarantined", cleanup, ex.InnerException?.Message ?? ex.Message);
            return new PlcCodeProbeRunResult { ProbeApplied = apply?.Applied == true, CleanupProven = cleanupProven, Evidence = evidence, ApplyResult = apply, CleanupResult = cleanup };
        }
    }

    private async Task<PlcCodeTransactionApplyResult> DisposeSuccessfulProbeTransactionAsync(string transactionId)
    {
        PlcCodeTransaction transaction = LoadTransaction(transactionId, updateExpiry: false);
        RequireState(transaction, "Succeeded");
        PlcCodeTransactionTargetSnapshot[] targets = transaction.Operations.SelectMany(EffectiveSnapshots).ToArray();
        if (targets.Length == 0 || targets.Any(target => target.Action != "Create" || target.BaselineHash != "missing"))
            throw new InvalidOperationException("A successful probe can be disposed automatically only when every exact target was newly created from a missing baseline.");
        transaction.State = "DisposingProbe";
        AddJournal(transaction, null, "ProbeCleanup", "Starting", "Verified disposable probe will now remove every newly created exact target and prove the baseline compile condition.");
        SaveTransaction(transaction);
        return await RollbackTransactionAsync(transaction, "Disposable probe cleanup after successful canonical verification.").ConfigureAwait(false);
    }

    private PlcCodeArtifactValidationResult ValidateProbeArtifact(string codeFormat, string inputPath)
    {
        var formats = new PlcCodeFormatService(this);
        PlcCodeArtifactValidationResult validation = formats.Validate(codeFormat, inputPath);
        if (!validation.Valid) throw new InvalidDataException("Probe artifact validation failed: " + string.Join("; ", validation.Errors));
        if (!validation.Format.Equals("SCL", StringComparison.OrdinalIgnoreCase) && !validation.Format.StartsWith("S7DCL", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Disposable probe runner increment 1 supports SCL and S7DCL create probes. SimaticML remains baseline-bound and VCI remains a synchronization route.");
        if (validation.DeclaredObjects.Count == 0) throw new InvalidOperationException("The probe artifact declares no supported PLC object.");
        return validation;
    }

    private async Task<IReadOnlyList<PlcCodeCapabilityDiscovery>> ValidateProbeCapabilitiesAsync(string capabilityIds, PlcCodeArtifactValidationResult validation, string? plcName = null, bool requireDisposableCreate = true)
    {
        if (_capabilityDiscovery == null) throw new InvalidOperationException("Capability discovery is unavailable in this host.");
        string[] ids = (capabilityIds ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(value => value.Trim()).Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) throw new InvalidOperationException("capability-ids is required for every declared disposable target.");
        var capabilities = new List<PlcCodeCapabilityDiscovery>();
        foreach (string id in ids)
        {
            PlcCodeCapabilityDiscovery stored = _capabilityDiscovery.Get(id);
            string expectedPlc = plcName ?? stored.Request.PlcName;
            PlcCodeCapabilityDiscovery current = await _capabilityDiscovery.ValidateCurrentAsync(id, expectedPlc).ConfigureAwait(false);
            if (requireDisposableCreate && (!current.Request.Operation.Equals("Create", StringComparison.OrdinalIgnoreCase) || current.Target.Exists))
                throw new InvalidOperationException($"Capability '{id}' is not bound to a missing Create target; production objects cannot be used as disposable probes.");
            if (current.Safety.MutationBlocked || current.Safety.IsFailSafeTarget)
                throw new InvalidOperationException($"Capability '{id}' targets a protected or fail-safe context that the standard disposable runner cannot qualify.");
            capabilities.Add(current);
        }
        foreach (PlcCodeDeclaredObject declared in validation.DeclaredObjects)
        {
            bool matched = capabilities.Any(capability => capability.Request.ObjectName.Equals(declared.Name, StringComparison.OrdinalIgnoreCase) && capability.Request.ObjectKind.Equals(declared.ObjectType == "TYPE" ? "Type" : "Block", StringComparison.OrdinalIgnoreCase));
            if (!matched) throw new InvalidOperationException($"No capability is bound to declared disposable target '{declared.ObjectType}:{declared.Name}'.");
        }
        if (capabilities.Count != validation.DeclaredObjects.Count) throw new InvalidOperationException("Supply exactly one capability id for each declared probe object.");
        return capabilities;
    }

    private void CompleteEvidence(PlcCodeEvidenceRecord evidence, string status, PlcCodeTransactionApplyResult? cleanup, string failure)
    {
        evidence.Status = status;
        evidence.CompletedAtUtc = DateTimeOffset.UtcNow;
        evidence.CleanupProven = cleanup?.RolledBackAndVerified == true || (status == "Failed" && cleanup == null);
        evidence.CleanupCompilation = cleanup?.Transaction.RollbackCompilation;
        evidence.Failure = failure;
        _evidenceRegistry.Save(evidence);
    }
}
