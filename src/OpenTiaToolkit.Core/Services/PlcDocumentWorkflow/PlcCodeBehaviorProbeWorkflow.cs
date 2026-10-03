using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Models;

namespace OpenTiaToolkit.Core.Services;

internal sealed class PlcCodeBehaviorProbeWorkflow
{
    private readonly IPlcService _plcService;
    private readonly IPlcSimulationRuntime _simulationRuntime;
    private readonly PlcCodeEvidenceRegistry _evidenceRegistry;
    private readonly PlcCodeProbeWorkflow _probes;
    private readonly PlcCodeTransactionCoordinator _transactions;

    internal PlcCodeBehaviorProbeWorkflow(IPlcService plcService, IPlcSimulationRuntime simulationRuntime, PlcCodeEvidenceRegistry evidenceRegistry, PlcCodeProbeWorkflow probes, PlcCodeTransactionCoordinator transactions)
    {
        _plcService = plcService; _simulationRuntime = simulationRuntime; _evidenceRegistry = evidenceRegistry; _probes = probes; _transactions = transactions;
    }
    public PlcBehaviorVectorValidationResult ValidateBehaviorVector(string vectorPath) => new PlcBehaviorVectorService().Validate(vectorPath);

    public PlcCodeArtifactValidationResult ValidateBehaviorPackageArtifact(string packagePath) => ValidateBehaviorPackage(packagePath).Composite;

    public PlcSimulatorCapabilityDiscovery DiscoverSimulatorCapabilities(string plcName, string? instanceName = null, bool refreshTags = false) =>
        _simulationRuntime.Discover(plcName, instanceName, refreshTags);

    public async Task<PlcCodeEvidenceQueryResult> QueryBehaviorEvidenceAsync(string plcName, string capabilityIds, string packagePath, string vectorPath, string? instanceName)
    {
        BehaviorPackageValidation package = ValidateBehaviorPackage(packagePath);
        PlcCodeArtifactValidationResult artifact = package.Composite;
        IReadOnlyList<PlcCodeCapabilityDiscovery> capabilities = await ValidateProbeCapabilitiesAsync(capabilityIds, artifact, plcName, requireDisposableCreate: false).ConfigureAwait(false);
        string[] owners = BehaviorOwners(artifact);
        PlcBehaviorVectorValidationResult vector = new PlcBehaviorVectorService().Validate(vectorPath, owners);
        if (!vector.Valid) throw new InvalidDataException("Behavior vector validation failed: " + string.Join("; ", vector.Errors));
        PlcSimulatorCapabilityDiscovery simulator = _simulationRuntime.Discover(plcName, instanceName);
        RequireSimulatorCompatibility(simulator, capabilities[0]);
        PlcCodeEvidenceCompatibility compatibility = PlcCodeEvidenceRegistry.BuildCompatibility(capabilities, artifact);
        return new PlcCodeEvidenceQueryResult
        {
            QualificationSignature = artifact.QualificationSignature,
            Compatibility = compatibility,
            Matches = _evidenceRegistry.FindVerifiedBehavior(compatibility.Fingerprint, artifact.QualificationSignature, vector.VectorHash, simulator.SelectedInstance!.Fingerprint)
        };
    }

    public async Task<PlcCodeBehaviorProbeRunResult> RunBehaviorProbeAsync(string plcName, string capabilityIds, string packagePath, string vectorPath, string? instanceName, string? compilePolicy, int? maximumNewWarnings, bool acceptUnknownImpact, bool onlyChanges, bool force)
    {
        BehaviorPackageValidation package = ValidateBehaviorPackage(packagePath);
        PlcCodeArtifactValidationResult artifact = package.Composite;
        IReadOnlyList<PlcCodeCapabilityDiscovery> capabilities = await ValidateProbeCapabilitiesAsync(capabilityIds, artifact, plcName, requireDisposableCreate: true).ConfigureAwait(false);
        string[] owners = BehaviorOwners(artifact);
        PlcBehaviorVectorValidationResult vectorValidation = new PlcBehaviorVectorService().Validate(vectorPath, owners);
        if (!vectorValidation.Valid) throw new InvalidDataException("Behavior vector validation failed: " + string.Join("; ", vectorValidation.Errors));
        PlcBehaviorVector vector = vectorValidation.Vector!;
        PlcSimulatorCapabilityDiscovery simulator = _simulationRuntime.Discover(plcName, instanceName);
        RequireSimulatorCompatibility(simulator, capabilities[0]);
        PlcCodeEvidenceCompatibility compatibility = PlcCodeEvidenceRegistry.BuildCompatibility(capabilities, artifact);
        IReadOnlyList<PlcCodeEvidenceRecord> verified = _evidenceRegistry.FindVerifiedBehavior(compatibility.Fingerprint, artifact.QualificationSignature, vectorValidation.VectorHash, simulator.SelectedInstance!.Fingerprint);
        if (!force && verified.Count > 0) return new PlcCodeBehaviorProbeRunResult { ReusedEvidence = true, Evidence = verified[0] };

        var behavior = new PlcCodeBehaviorEvidence
        {
            PlcName = plcName,
            VectorId = vector.VectorId,
            VectorHash = vectorValidation.VectorHash,
            SimulatorFingerprint = simulator.SelectedInstance!.Fingerprint,
            Simulator = simulator,
            LifecycleState = "PreflightVerified"
        };
        var evidence = new PlcCodeEvidenceRecord
        {
            EvidenceId = Guid.NewGuid().ToString("N"),
            EvidenceKind = "Behavior",
            StartedAtUtc = DateTimeOffset.UtcNow,
            CodeFormat = artifact.Format,
            ValidatorCatalogVersion = PlcCodeFormatService.ValidatorCatalogVersion,
            QualificationSignature = artifact.QualificationSignature,
            InputHash = artifact.InputHash,
            Compatibility = compatibility,
            CapabilityIds = capabilities.Select(item => item.CapabilityId).ToArray(),
            CapabilityFingerprints = capabilities.Select(item => item.Fingerprint).ToArray(),
            DeclaredObjects = artifact.DeclaredObjects,
            Constructs = artifact.DeclaredObjects.Select(item => "object:" + item.ObjectType).Concat(artifact.Findings.Select(item => item.Construct + ":" + item.Classification)).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            Dependencies = artifact.Dependencies,
            Behavior = behavior
        };
        _evidenceRegistry.Save(evidence);

        string transactionId = string.Empty;
        bool simulatorMayContainProbe = false;
        PlcCodeTransactionApplyResult? apply = null;
        PlcCodeTransactionApplyResult? cleanup = null;
        try
        {
            PlcSoftwareDownloadResult dryRun = await _plcService.DownloadSoftwareAsync(true, true, false, onlyChanges).ConfigureAwait(false);
            if (!dryRun.Success) throw new InvalidOperationException("Simulator-only download preflight failed: " + dryRun.Message);
            behavior.OnlineStatus = await _plcService.GetOnlineStatusAsync(true).ConfigureAwait(false);
            if (!behavior.OnlineStatus.Success || !behavior.OnlineStatus.IsOnline) throw new InvalidOperationException("TIA could not establish an online connection for the baseline comparison: " + behavior.OnlineStatus.Message);
            behavior.BaselineCompare = CompactCompare(await _plcService.CompareSoftwareToOnlineAsync().ConfigureAwait(false));
            if (!behavior.BaselineCompare.Success || !behavior.BaselineCompare.ComparedToOnline) throw new InvalidOperationException("Baseline offline/online comparison failed: " + behavior.BaselineCompare.Message);
            PlcOnlineStatusResult offlineStatus = await _plcService.GoOfflineAsync().ConfigureAwait(false);
            if (!offlineStatus.Success || offlineStatus.IsOnline) throw new InvalidOperationException("TIA could not return offline after baseline comparison: " + offlineStatus.Message);
            behavior.LifecycleState = "DownloadPreflightVerified";
            _evidenceRegistry.Save(evidence);

            PlcCodeTransaction transaction = await BeginTransactionAsync(plcName, "Disposable behavioral probe " + evidence.EvidenceId, compilePolicy, maximumNewWarnings, null, string.Join(",", evidence.CapabilityIds)).ConfigureAwait(false);
            transactionId = transaction.TransactionId;
            evidence.TransactionId = transactionId;
            _evidenceRegistry.Save(evidence);
            foreach (BehaviorPackageArtifact item in package.Artifacts)
            {
                if (item.Validation.Format.Equals("SCL", StringComparison.OrdinalIgnoreCase)) AddSclTransactionOperation(transactionId, item.Validation.InputPath, item.Specification.GroupPath, item.Specification.TypeGroupPath);
                else AddTransactionOperation(transactionId, Path.GetDirectoryName(item.Validation.InputPath)!, Path.GetFileNameWithoutExtension(item.Validation.InputPath), item.Specification.GroupPath, "Override");
            }
            transaction = await PlanTransactionAsync(transactionId).ConfigureAwait(false);
            if (transaction.State == "ImpactBlocked") throw new InvalidOperationException("Disposable behavior probe impact is blocked.");
            if (transaction.State == "ImpactReviewRequired")
            {
                bool unknown = transaction.ImpactAnalysis?.Status == "Unknown";
                if (unknown && !acceptUnknownImpact) throw new InvalidOperationException("Disposable behavior probe impact is Unknown. Explicit review and accept-unknown-impact=true are required.");
                transaction = await ApproveTransactionImpactAsync(transactionId, transaction.ImpactAnalysis!.Fingerprint, acceptUnknownImpact).ConfigureAwait(false);
            }
            apply = await ApplyTransactionAsync(transactionId, transaction.ApplyToken!).ConfigureAwait(false);
            evidence.BaselineCompilation = apply.Transaction.BaselineCompilation;
            evidence.ProbeCompilation = apply.Transaction.FinalCompilation;
            evidence.CanonicalResultHashes = apply.Transaction.Operations.SelectMany(EffectiveSnapshots).Select(snapshot => snapshot.CanonicalResultHash).Where(hash => !string.IsNullOrWhiteSpace(hash)).Cast<string>().ToArray();
            if (!apply.Applied || !apply.Verified) throw new InvalidOperationException(apply.Transaction.Failure ?? "The disposable probe did not apply and verify.");
            behavior.LifecycleState = "OfflineProbeVerified";
            _evidenceRegistry.Save(evidence);

            simulatorMayContainProbe = true;
            behavior.ProbeDownload = await _plcService.DownloadSoftwareAsync(true, false, true, onlyChanges).ConfigureAwait(false);
            if (!behavior.ProbeDownload.Success) throw new InvalidOperationException("Probe download to PLCSIM Advanced failed: " + behavior.ProbeDownload.Message);
            behavior.OnlineStatus = await _plcService.GetOnlineStatusAsync(true).ConfigureAwait(false);
            if (!behavior.OnlineStatus.Success || !behavior.OnlineStatus.IsOnline) throw new InvalidOperationException("TIA could not prove the selected PLC online after simulator download: " + behavior.OnlineStatus.Message);
            behavior.ProbeCompare = CompactCompare(await _plcService.CompareSoftwareToOnlineAsync().ConfigureAwait(false));
            if (!behavior.ProbeCompare.Success || !behavior.ProbeCompare.ComparedToOnline || !HasNoNewCompareDifferences(behavior.BaselineCompare, behavior.ProbeCompare)) throw new InvalidOperationException("Offline/online comparison introduced differences beyond the recorded baseline: " + behavior.ProbeCompare.Message);
            behavior.LifecycleState = "SimulatorProbeVerified";
            _evidenceRegistry.Save(evidence);

            PlcSimulatorCapabilityDiscovery runtimeCurrent = _simulationRuntime.Discover(plcName, simulator.SelectedInstance.Name, refreshTags: true);
            RequireSimulatorCompatibility(runtimeCurrent, capabilities[0]);
            if (!SameSimulator(simulator.SelectedInstance, runtimeCurrent.SelectedInstance!)) throw new InvalidOperationException("The selected simulator instance changed between preflight and execution.");
            behavior.Execution = _simulationRuntime.Execute(vector, runtimeCurrent, owners);
            if (!behavior.Execution.Passed || !behavior.Execution.CleanupApplied) throw new InvalidOperationException(behavior.Execution.Failure.Length == 0 ? "Behavior assertions failed." : behavior.Execution.Failure);
            behavior.LifecycleState = "BehaviorVerified";
            _evidenceRegistry.Save(evidence);

            offlineStatus = await _plcService.GoOfflineAsync().ConfigureAwait(false);
            if (!offlineStatus.Success || offlineStatus.IsOnline) throw new InvalidOperationException("TIA could not return offline before disposing the behavioral probe: " + offlineStatus.Message);
            cleanup = await DisposeSuccessfulProbeTransactionAsync(transactionId).ConfigureAwait(false);
            if (!cleanup.RolledBackAndVerified) throw new InvalidOperationException("Offline probe cleanup was not proven.");
            evidence.CleanupCompilation = cleanup.Transaction.RollbackCompilation;
            behavior.LifecycleState = "OfflineCleanupVerified";
            _evidenceRegistry.Save(evidence);
            await ProveSimulatorCleanupAsync(evidence, simulator, owners, onlyChanges).ConfigureAwait(false);
            await RequireBehaviorOfflineAsync("after simulator cleanup verification").ConfigureAwait(false);
            CompleteBehaviorEvidence(evidence, "Verified", cleanup, string.Empty);
            DiscardTransaction(transactionId);
            return new PlcCodeBehaviorProbeRunResult { Evidence = evidence, ApplyResult = apply, CleanupResult = cleanup };
        }
        catch (Exception ex)
        {
            string failure = Message(ex);
            try { await RequireBehaviorOfflineAsync("before behavioral failure cleanup").ConfigureAwait(false); }
            catch (Exception offlineError) { failure += " Offline transition failed: " + Message(offlineError); }
            bool offlineClean = string.IsNullOrWhiteSpace(transactionId);
            if (!string.IsNullOrWhiteSpace(transactionId))
            {
                try
                {
                    cleanup = await CleanupBehaviorTransactionAsync(transactionId).ConfigureAwait(false);
                    offlineClean = cleanup?.RolledBackAndVerified == true || IsTransactionClean(transactionId);
                    evidence.CleanupCompilation = cleanup?.Transaction.RollbackCompilation ?? evidence.CleanupCompilation;
                }
                catch (Exception cleanupError) { failure += " Offline cleanup failed: " + Message(cleanupError); offlineClean = false; }
            }
            bool simulatorClean = !simulatorMayContainProbe;
            if (simulatorClean) behavior.SimulatorCleanupProven = true;
            if (offlineClean && simulatorMayContainProbe)
            {
                try { await ProveSimulatorCleanupAsync(evidence, simulator, owners, onlyChanges).ConfigureAwait(false); simulatorClean = behavior.SimulatorCleanupProven; }
                catch (Exception cleanupError) { failure += " Simulator cleanup failed: " + Message(cleanupError); simulatorClean = false; }
            }
            CompleteBehaviorEvidence(evidence, offlineClean && simulatorClean ? "Failed" : "Quarantined", cleanup, failure);
            if (offlineClean && simulatorClean && !string.IsNullOrWhiteSpace(transactionId)) try { DiscardTransaction(transactionId); } catch { }
            return new PlcCodeBehaviorProbeRunResult { Evidence = evidence, ApplyResult = apply, CleanupResult = cleanup };
        }
    }

    public async Task<PlcCodeBehaviorProbeRunResult> RecoverBehaviorProbeAsync(string evidenceId, bool onlyChanges)
    {
        PlcCodeEvidenceRecord evidence = _evidenceRegistry.Get(evidenceId);
        if (!evidence.EvidenceKind.Equals("Behavior", StringComparison.OrdinalIgnoreCase) || evidence.Behavior?.Simulator?.SelectedInstance == null) throw new InvalidOperationException("Evidence is not a recoverable behavior probe.");
        PlcCodeBehaviorEvidence behavior = evidence.Behavior;
        if (evidence.Status == "Verified" || (evidence.CleanupProven && evidence.Status == "Failed")) return new PlcCodeBehaviorProbeRunResult { Evidence = evidence };
        if (!_plcService.IsSoftwareOpen || !string.Equals(_plcService.CurrentPlcName, behavior.PlcName, StringComparison.OrdinalIgnoreCase))
            await _plcService.OpenPlcAsync(behavior.PlcName).ConfigureAwait(false);
        string[] owners = evidence.DeclaredObjects.Where(IsDataBlock).Select(item => item.Name).ToArray();
        PlcCodeTransactionApplyResult? cleanup = null;
        try
        {
            await RequireBehaviorOfflineAsync("before behavioral recovery").ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(evidence.TransactionId)) cleanup = await CleanupBehaviorTransactionAsync(evidence.TransactionId).ConfigureAwait(false);
            bool offlineClean = string.IsNullOrWhiteSpace(evidence.TransactionId) || cleanup?.RolledBackAndVerified == true || IsTransactionClean(evidence.TransactionId);
            if (!offlineClean) throw new InvalidOperationException("Offline transaction recovery was not proven.");
            await ProveSimulatorCleanupAsync(evidence, behavior.Simulator!, owners, onlyChanges).ConfigureAwait(false);
            await RequireBehaviorOfflineAsync("after behavioral recovery verification").ConfigureAwait(false);
            CompleteBehaviorEvidence(evidence, "Failed", cleanup, "Interrupted behavioral run recovered; behavioral evidence was not promoted.");
            if (!string.IsNullOrWhiteSpace(evidence.TransactionId)) DiscardTransaction(evidence.TransactionId);
        }
        catch (Exception ex) { CompleteBehaviorEvidence(evidence, "Quarantined", cleanup, "Recovery failed: " + Message(ex)); }
        return new PlcCodeBehaviorProbeRunResult { Evidence = evidence, CleanupResult = cleanup };
    }

    private async Task ProveSimulatorCleanupAsync(PlcCodeEvidenceRecord evidence, PlcSimulatorCapabilityDiscovery simulator, IReadOnlyList<string> owners, bool onlyChanges)
    {
        PlcCodeBehaviorEvidence behavior = evidence.Behavior!;
        try
        {
            behavior.CleanupDownload = await _plcService.DownloadSoftwareAsync(true, false, true, onlyChanges).ConfigureAwait(false);
            if (!behavior.CleanupDownload.Success) throw new InvalidOperationException("Cleanup download failed: " + behavior.CleanupDownload.Message);
            behavior.OnlineStatus = await _plcService.GetOnlineStatusAsync(true).ConfigureAwait(false);
            if (!behavior.OnlineStatus.Success || !behavior.OnlineStatus.IsOnline) throw new InvalidOperationException("TIA could not establish an online connection for cleanup verification: " + behavior.OnlineStatus.Message);
            behavior.CleanupCompare = CompactCompare(await _plcService.CompareSoftwareToOnlineAsync().ConfigureAwait(false));
            bool compareClean = behavior.BaselineCompare != null
                ? HasNoNewCompareDifferences(behavior.BaselineCompare, behavior.CleanupCompare)
                : !CompareMentionsOwners(behavior.CleanupCompare, owners);
            if (!behavior.CleanupCompare.Success || !behavior.CleanupCompare.ComparedToOnline || !compareClean) throw new InvalidOperationException("Cleanup online comparison failed or still references disposable probe objects: " + behavior.CleanupCompare.Message);
            if (!_simulationRuntime.AreTagsAbsent(simulator, owners, out string absentFailure)) throw new InvalidOperationException(absentFailure);
            behavior.SimulatorCleanupProven = true;
            behavior.LifecycleState = "CleanupVerified";
            _evidenceRegistry.Save(evidence);
        }
        finally
        {
            await RequireBehaviorOfflineAsync("after simulator cleanup comparison").ConfigureAwait(false);
        }
    }

    private async Task<PlcCodeTransactionApplyResult?> CleanupBehaviorTransactionAsync(string transactionId)
    {
        await RequireBehaviorOfflineAsync("before deleting disposable behavioral objects").ConfigureAwait(false);
        PlcCodeTransaction current = GetTransaction(transactionId);
        if (current.State == "Succeeded") return await DisposeSuccessfulProbeTransactionAsync(transactionId).ConfigureAwait(false);
        if (new[] { "Applying", "Verifying", "DisposingProbe", "RollingBack", "RecoveryRequired" }.Contains(current.State, StringComparer.Ordinal)) return await RecoverTransactionAsync(transactionId).ConfigureAwait(false);
        return null;
    }

    private async Task RequireBehaviorOfflineAsync(string phase)
    {
        PlcOnlineStatusResult status = await _plcService.GoOfflineAsync().ConfigureAwait(false);
        if (!status.Success || status.IsOnline) throw new InvalidOperationException("TIA could not return offline " + phase + ": " + status.Message);
    }

    private bool IsTransactionClean(string transactionId)
    {
        string state = GetTransaction(transactionId).State;
        return string.Equals(state, "RolledBackAndVerified", StringComparison.Ordinal);
    }

    private void CompleteBehaviorEvidence(PlcCodeEvidenceRecord evidence, string status, PlcCodeTransactionApplyResult? cleanup, string failure)
    {
        evidence.Status = status;
        evidence.CompletedAtUtc = DateTimeOffset.UtcNow;
        bool offline = string.IsNullOrWhiteSpace(evidence.TransactionId) || cleanup?.RolledBackAndVerified == true || TryIsTransactionClean(evidence.TransactionId);
        evidence.CleanupProven = offline && evidence.Behavior?.SimulatorCleanupProven == true;
        evidence.CleanupCompilation = cleanup?.Transaction.RollbackCompilation ?? evidence.CleanupCompilation;
        evidence.Failure = failure;
        if (evidence.Behavior != null && status == "Quarantined") evidence.Behavior.LifecycleState = "RecoveryRequired";
        _evidenceRegistry.Save(evidence);
    }

    private bool TryIsTransactionClean(string transactionId) { try { return IsTransactionClean(transactionId); } catch { return false; } }
    private static string[] BehaviorOwners(PlcCodeArtifactValidationResult artifact)
    {
        string[] owners = artifact.DeclaredObjects.Where(IsDataBlock).Select(item => item.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (owners.Length == 0) throw new InvalidOperationException("A behavioral probe must declare at least one disposable DATA_BLOCK that exclusively owns every stimulated and observed tag.");
        return owners;
    }
    private static bool IsDataBlock(PlcCodeDeclaredObject item) => IsGlobalDataBlock(item) || item.ObjectType.Equals("InstanceDB", StringComparison.OrdinalIgnoreCase);
    private static bool IsGlobalDataBlock(PlcCodeDeclaredObject item) => item.ObjectType.Equals("DB", StringComparison.OrdinalIgnoreCase) || item.ObjectType.Equals("DATA_BLOCK", StringComparison.OrdinalIgnoreCase) || item.ObjectType.Equals("GlobalDB", StringComparison.OrdinalIgnoreCase);
    private static bool SameSimulator(PlcSimulatorInstanceCapability left, PlcSimulatorInstanceCapability right) => left.Id == right.Id && left.Name.Equals(right.Name, StringComparison.OrdinalIgnoreCase) && left.ArticleNumber.Equals(right.ArticleNumber, StringComparison.OrdinalIgnoreCase);
    internal static bool HasNoNewCompareDifferences(PlcSoftwareCompareResult baseline, PlcSoftwareCompareResult current) => PlcBehaviorCompareAnalyzer.HasNoNewCompareDifferences(baseline, current);
    private static PlcSoftwareCompareResult CompactCompare(PlcSoftwareCompareResult result) => new PlcSoftwareCompareResult(result.Success, result.ComparedToOnline, result.Message, result.Entries.Where(item => item.HasDifference).ToArray());
    private static bool CompareMentionsOwners(PlcSoftwareCompareResult compare, IEnumerable<string> owners) => compare.Entries.Where(item => item.HasDifference).Any(item => owners.Any(owner =>
        new[] { item.Path, item.LeftName ?? string.Empty, item.RightName ?? string.Empty, item.Detail ?? string.Empty }.Any(value => value.IndexOf(owner, StringComparison.OrdinalIgnoreCase) >= 0)));

    private BehaviorPackageValidation ValidateBehaviorPackage(string packagePath)
    {
        string path = Path.GetFullPath(packagePath ?? string.Empty);
        if (!File.Exists(path)) throw new FileNotFoundException("Behavior probe package was not found.", path);
        byte[] bytes = File.ReadAllBytes(path);
        PlcBehaviorProbePackage package;
        try { package = JsonSerializer.Deserialize<PlcBehaviorProbePackage>(bytes, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("Behavior probe package JSON is empty."); }
        catch (JsonException ex) { throw new InvalidDataException("Behavior probe package JSON is invalid: " + ex.Message, ex); }
        if (package.SchemaVersion != "1.0") throw new InvalidDataException("Unsupported behavior probe package schema; expected 1.0.");
        if (string.IsNullOrWhiteSpace(package.PackageId)) throw new InvalidDataException("Behavior probe package requires packageId.");
        if (package.Artifacts == null || package.Artifacts.Count < 2 || package.Artifacts.Count > 20) throw new InvalidDataException("Behavior probe package requires 2-20 artifacts.");
        string root = Path.GetDirectoryName(path)!;
        var artifacts = new List<BehaviorPackageArtifact>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (PlcBehaviorProbeArtifact specification in package.Artifacts)
        {
            if (string.IsNullOrWhiteSpace(specification.InputPath) || Path.IsPathRooted(specification.InputPath)) throw new InvalidDataException("Behavior probe artifact paths must be non-empty and relative to the package manifest.");
            string input = Path.GetFullPath(Path.Combine(root, specification.InputPath));
            string rootPrefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!input.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Behavior probe artifact paths cannot escape the package directory: " + specification.InputPath);
            if (!paths.Add(input)) throw new InvalidDataException("Behavior probe package contains the same artifact path more than once: " + input);
            PlcCodeArtifactValidationResult validation = ValidateProbeArtifact(specification.CodeFormat ?? "auto", input);
            artifacts.Add(new BehaviorPackageArtifact(specification, validation));
        }
        PlcCodeDeclaredObject[] objects = artifacts.SelectMany(item => item.Validation.DeclaredObjects).ToArray();
        string duplicate = objects.GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1)?.Key ?? string.Empty;
        if (duplicate.Length > 0) throw new InvalidDataException("Behavior probe package declares a duplicate object: " + duplicate);
        if (!objects.Any(IsGlobalDataBlock)) throw new InvalidDataException("Behavior probe package requires a disposable global DATA_BLOCK for its runtime contract.");
        if (!objects.Any(item => item.ObjectType.Equals("OB", StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("Behavior probe package requires a disposable ORGANIZATION_BLOCK so the code is executed by the simulator scheduler.");
        string inputHash = BehaviorHash(Convert.ToBase64String(bytes) + "|" + string.Join("|", artifacts.Select(item => item.Validation.InputHash)));
        string signature = BehaviorHash(string.Join("|", artifacts.Select(item => item.Validation.Format + ":" + item.Validation.QualificationSignature).OrderBy(value => value, StringComparer.Ordinal)));
        var composite = new PlcCodeArtifactValidationResult
        {
            Valid = true,
            Format = "BehaviorPackage",
            Classification = artifacts.Any(item => !item.Validation.Classification.Equals("verified", StringComparison.OrdinalIgnoreCase)) ? "experimental" : "verified",
            InputPath = path,
            InputHash = inputHash,
            QualificationSignature = signature,
            HasUtf8Bom = artifacts.All(item => item.Validation.HasUtf8Bom),
            DeclaredObjects = objects,
            Findings = artifacts.SelectMany(item => item.Validation.Findings).ToArray(),
            Dependencies = artifacts.SelectMany(item => item.Validation.Dependencies).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            Warnings = artifacts.SelectMany(item => item.Validation.Warnings).ToArray()
        };
        return new BehaviorPackageValidation(package, artifacts, composite);
    }

    private static string BehaviorHash(string value)
    {
        using (var sha = SHA256.Create()) return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(value)).Select(item => item.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
    }
    private static void RequireSimulatorCompatibility(PlcSimulatorCapabilityDiscovery simulator, PlcCodeCapabilityDiscovery capability)
    {
        if (!simulator.Available || simulator.SelectedInstance == null) throw new InvalidOperationException("PLCSIM capability preflight failed: " + simulator.Failure);
        PlcSimulatorInstanceCapability selected = simulator.SelectedInstance;
        if (!selected.OperatingState.Equals("Run", StringComparison.OrdinalIgnoreCase) && !selected.OperatingState.Equals("Stop", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The selected simulator must be powered on in Run or Stop state.");
        string order = capability.Environment.PlcTypeIdentifier ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(selected.ArticleNumber) && order.IndexOf(selected.ArticleNumber, StringComparison.OrdinalIgnoreCase) < 0) throw new InvalidOperationException($"Simulator article number '{selected.ArticleNumber}' does not match TIA target '{order}'.");
        if (!string.IsNullOrWhiteSpace(selected.ControllerName) && !selected.ControllerName.Equals(capability.Request.PlcName, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException($"Simulator controller '{selected.ControllerName}' does not match selected PLC '{capability.Request.PlcName}'.");
    }

    private sealed class BehaviorPackageArtifact
    {
        public BehaviorPackageArtifact(PlcBehaviorProbeArtifact specification, PlcCodeArtifactValidationResult validation) { Specification = specification; Validation = validation; }
        public PlcBehaviorProbeArtifact Specification { get; }
        public PlcCodeArtifactValidationResult Validation { get; }
    }

    private sealed class BehaviorPackageValidation
    {
        public BehaviorPackageValidation(PlcBehaviorProbePackage package, IReadOnlyList<BehaviorPackageArtifact> artifacts, PlcCodeArtifactValidationResult composite) { Package = package; Artifacts = artifacts; Composite = composite; }
        public PlcBehaviorProbePackage Package { get; }
        public IReadOnlyList<BehaviorPackageArtifact> Artifacts { get; }
        public PlcCodeArtifactValidationResult Composite { get; }
    }

    private PlcCodeArtifactValidationResult ValidateProbeArtifact(string format, string path) => _probes.ValidateProbeArtifact(format, path);
    private Task<IReadOnlyList<PlcCodeCapabilityDiscovery>> ValidateProbeCapabilitiesAsync(string ids, PlcCodeArtifactValidationResult validation, string? plcName, bool requireDisposableCreate) => _probes.ValidateProbeCapabilitiesAsync(ids, validation, plcName, requireDisposableCreate);
    private Task<PlcCodeTransaction> BeginTransactionAsync(string plcName, string? description, string? compilePolicy, int? maximumNewWarnings, string? capabilityId, string? capabilityIds) => _transactions.BeginTransactionAsync(plcName, description, compilePolicy, maximumNewWarnings, capabilityId, capabilityIds);
    private PlcCodeTransaction AddTransactionOperation(string id, string directory, string fileName, string? groupPath, string? option) => _transactions.AddTransactionOperation(id, directory, fileName, groupPath, option);
    private PlcCodeTransaction AddSclTransactionOperation(string id, string path, string? groupPath, string? typeGroupPath) => _transactions.AddSclTransactionOperation(id, path, groupPath, typeGroupPath);
    private Task<PlcCodeTransaction> PlanTransactionAsync(string id) => _transactions.PlanTransactionAsync(id);
    private Task<PlcCodeTransaction> ApproveTransactionImpactAsync(string id, string fingerprint, bool acceptUnknown) => _transactions.ApproveTransactionImpactAsync(id, fingerprint, acceptUnknown);
    private Task<PlcCodeTransactionApplyResult> ApplyTransactionAsync(string id, string token) => _transactions.ApplyTransactionAsync(id, token);
    private Task<PlcCodeTransactionApplyResult> RecoverTransactionAsync(string id) => _transactions.RecoverTransactionAsync(id);
    private Task<PlcCodeTransactionApplyResult> DisposeSuccessfulProbeTransactionAsync(string id) => _probes.DisposeSuccessfulProbeTransactionAsync(id);
    private PlcCodeTransaction GetTransaction(string id) => _transactions.GetTransaction(id);
    private bool DiscardTransaction(string id) => _transactions.DiscardTransaction(id);
    private static IEnumerable<PlcCodeTransactionTargetSnapshot> EffectiveSnapshots(PlcCodeTransactionOperation operation) => PlcCodeTransactionIntegrity.EffectiveSnapshots(operation);
    private static string Message(Exception exception) => PlcWorkflowPrimitives.Message(exception);
}
