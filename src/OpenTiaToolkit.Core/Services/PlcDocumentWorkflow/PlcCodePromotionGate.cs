using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OpenTiaToolkit.Core.Models;

namespace OpenTiaToolkit.Core.Services;

internal sealed class PlcCodePromotionGate
{
    private readonly PlcCodeCapabilityGate _capabilities;
    private readonly PlcCodeEvidenceRegistry _evidence;
    private readonly PlcCodeFormatService _formats;
    internal PlcCodePromotionGate(PlcCodeCapabilityGate capabilities, PlcCodeEvidenceRegistry evidence, PlcCodeFormatService formats) { _capabilities = capabilities; _evidence = evidence; _formats = formats; }

    internal async Task VerifyAsync(PlcCodeTransaction transaction)
    {
        PlcCodePromotionBinding? promotion = transaction.Promotion; if (promotion == null) return;
        PlcCodeArtifactValidationResult validation = ValidateArtifact(promotion.CodeFormat, promotion.ArtifactPath);
        if (!PlcWorkflowPrimitives.FixedEquals(promotion.ArtifactInputHash, validation.InputHash) || !PlcWorkflowPrimitives.FixedEquals(promotion.QualificationSignature, validation.QualificationSignature)) throw new InvalidOperationException("The promoted production artifact changed after evidence was bound. Begin a new promoted transaction.");
        if (!string.Equals(promotion.ValidatorCatalogVersion, PlcCodeFormatService.ValidatorCatalogVersion, StringComparison.Ordinal)) throw new InvalidOperationException("The PLC code validator catalog changed after promotion was bound. Re-query evidence and begin a new promoted transaction.");
        PlcCodeCapabilityDiscoveryService discovery = _capabilities.RequireDiscovery(); var current = new List<PlcCodeCapabilityDiscovery>();
        foreach (PlcCodeCapabilityBinding binding in transaction.CapabilityBindings) current.Add(await discovery.ValidateCurrentAsync(binding.CapabilityId, transaction.PlcName).ConfigureAwait(false));
        PlcCodeEvidenceCompatibility compatibility = PlcCodeEvidenceRegistry.BuildCompatibility(current, validation);
        if (!PlcWorkflowPrimitives.FixedEquals(promotion.CompatibilityFingerprint, compatibility.Fingerprint)) throw new InvalidOperationException("The promoted artifact is no longer compatible with the current TIA, PLC, route, safety, or object-shape evidence.");
        RequireEvidence(_evidence.Get(promotion.EvidenceId), validation, compatibility); RequireOperationCoverage(transaction, promotion);
    }

    internal PlcCodeArtifactValidationResult ValidateArtifact(string format, string inputPath)
    {
        PlcCodeArtifactValidationResult validation = _formats.Validate(format, inputPath);
        if (!validation.Valid) throw new InvalidDataException("Probe artifact validation failed: " + string.Join("; ", validation.Errors));
        if (!validation.Format.Equals("SCL", StringComparison.OrdinalIgnoreCase) && !validation.Format.StartsWith("S7DCL", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Disposable probe runner increment 1 supports SCL and S7DCL create probes. SimaticML remains baseline-bound and VCI remains a synchronization route.");
        if (validation.DeclaredObjects.Count == 0) throw new InvalidOperationException("The probe artifact declares no supported PLC object.");
        return validation;
    }

    internal static void RequireEvidence(PlcCodeEvidenceRecord evidence, PlcCodeArtifactValidationResult validation, PlcCodeEvidenceCompatibility compatibility)
    {
        if (!evidence.Status.Equals("Verified", StringComparison.Ordinal) || !evidence.CleanupProven || evidence.EvidenceKind.Equals("Behavior", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException($"Evidence '{evidence.EvidenceId}' is not reusable verified qualification evidence with proven cleanup.");
        if (!string.Equals(evidence.ValidatorCatalogVersion, PlcCodeFormatService.ValidatorCatalogVersion, StringComparison.Ordinal)) throw new InvalidOperationException($"Evidence '{evidence.EvidenceId}' was produced by validator catalog '{evidence.ValidatorCatalogVersion}', not the current '{PlcCodeFormatService.ValidatorCatalogVersion}'.");
        if (!PlcWorkflowPrimitives.FixedEquals(evidence.QualificationSignature, validation.QualificationSignature) || !PlcWorkflowPrimitives.FixedEquals(evidence.Compatibility.Fingerprint, compatibility.Fingerprint)) throw new InvalidOperationException($"Evidence '{evidence.EvidenceId}' does not exactly match this artifact's normalized construct signature and current compatibility fingerprint.");
    }

    private static void RequireOperationCoverage(PlcCodeTransaction transaction, PlcCodePromotionBinding promotion)
    {
        if (transaction.Operations.Count != 1) throw new InvalidOperationException("An evidence-bound promotion transaction must contain exactly one operation for its bound artifact.");
        PlcCodeTransactionOperation operation = transaction.Operations[0]; string operationPath;
        if (promotion.CodeFormat.Equals("SCL", StringComparison.OrdinalIgnoreCase) && operation.Kind == "GenerateSclSource") operationPath = Path.GetFullPath(operation.SourcePath ?? string.Empty);
        else if (promotion.CodeFormat.StartsWith("S7DCL", StringComparison.OrdinalIgnoreCase) && operation.Kind == "UpsertBlockDocuments") operationPath = Path.GetFullPath(Path.Combine(operation.ImportDirectory, operation.FileName + ".s7dcl"));
        else throw new InvalidOperationException($"The promotion binding for {promotion.CodeFormat} does not match transaction operation kind '{operation.Kind}'.");
        if (!operationPath.Equals(Path.GetFullPath(promotion.ArtifactPath), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The transaction operation does not use the exact artifact path bound to verified promotion evidence.");
    }
}
