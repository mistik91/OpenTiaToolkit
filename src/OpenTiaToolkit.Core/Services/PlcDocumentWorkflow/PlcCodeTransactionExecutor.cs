using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Models;
using ResolvedBlock = OpenTiaToolkit.Core.Services.PlcCodeTargetRepository.ResolvedBlock;
using ResolvedType = OpenTiaToolkit.Core.Services.PlcCodeTargetRepository.ResolvedType;

namespace OpenTiaToolkit.Core.Services;

internal sealed class PlcCodeTransactionExecutor
{
    private static readonly TimeSpan TransactionLifetime = TimeSpan.FromMinutes(30);
    private readonly IPlcService _plcService;
    private readonly EngineeringSessionContext _sessionContext;
    private readonly PlcCodeFormatService _formats;
    private readonly PlcCodeTargetRepository _targets;
    private readonly PlcCodeTransactionStore _store;
    private readonly PlcCodeCapabilityGate _capabilityGate;
    private readonly PlcCodePromotionGate _promotionGate;
    private readonly PlcCodeRollbackCoordinator _rollback;
    private readonly PlcCodeImpactAnalyzer _impactAnalyzer;

    internal PlcCodeTransactionExecutor(IPlcService plcService, EngineeringSessionContext sessionContext, PlcCodeFormatService formats, PlcCodeTargetRepository targets, PlcCodeTransactionStore store, PlcCodeCapabilityGate capabilityGate, PlcCodePromotionGate promotionGate, PlcCodeRollbackCoordinator rollback, PlcCodeImpactAnalyzer impactAnalyzer)
    {
        _plcService = plcService;
        _sessionContext = sessionContext;
        _formats = formats;
        _targets = targets;
        _store = store;
        _capabilityGate = capabilityGate;
        _promotionGate = promotionGate;
        _rollback = rollback;
        _impactAnalyzer = impactAnalyzer;
    }


    internal async Task<PlcCodeTransaction> ApproveImpactAsync(string transactionId, string impactFingerprint, bool acceptUnknown)
    {
        PlcCodeTransaction transaction = LoadTransaction(transactionId);
        RequireState(transaction, "ImpactReviewRequired");
        if (transaction.ImpactAnalysis == null) throw new InvalidOperationException("The transaction has no impact analysis to approve.");
        if (!FixedEquals(transaction.ImpactAnalysis.Fingerprint, impactFingerprint)) throw new InvalidOperationException("The impact fingerprint does not match the current transaction analysis.");
        if (transaction.ImpactAnalysis.Status == "Blocked") throw new InvalidOperationException("Blocked impact findings cannot be approved.");
        if (transaction.ImpactAnalysis.Status == "Unknown" && !acceptUnknown) throw new InvalidOperationException("The impact analysis contains unknown evidence. Set accept-unknown=true only after reviewing the unresolved scope.");
        await EnsureTransactionContextAsync(transaction).ConfigureAwait(false);
        await _capabilityGate.VerifyCurrentAsync(transaction).ConfigureAwait(false);
        await _promotionGate.VerifyAsync(transaction).ConfigureAwait(false);
        VerifyTransactionPreconditions(transaction);
        PlcCodeImpactAnalysis current = await _impactAnalyzer.AnalyzeAsync(transaction).ConfigureAwait(false);
        if (!FixedEquals(transaction.ImpactAnalysis.Fingerprint, current.Fingerprint))
        {
            transaction.State = "Stale";
            transaction.Failure = "PLC dependencies, consumers, or semantic impact changed after planning.";
            AddJournal(transaction, null, "Impact", "Stale", transaction.Failure);
            SaveTransaction(transaction);
            throw new InvalidOperationException(transaction.Failure);
        }
        transaction.ImpactAcknowledged = true;
        transaction.ImpactAcknowledgementFingerprint = current.Fingerprint;
        transaction.ImpactAnalysis = current;
        transaction.State = "Planned";
        transaction.ApplyToken = RandomToken();
        transaction.ExpiresAtUtc = DateTimeOffset.UtcNow.Add(TransactionLifetime);
        transaction.PlanHash = ComputeTransactionPlanHash(transaction);
        AddJournal(transaction, null, "Impact", "Acknowledged", $"Impact {current.Status} was explicitly acknowledged with fingerprint {current.Fingerprint}.");
        SaveTransaction(transaction);
        return transaction;
    }

    private async Task ApplySclOperationAsync(PlcCodeTransactionOperation operation)
    {
        PlcSourceOperationResult imported = await _plcService.ImportSourceAsync(operation.SourcePath!, dryRun: false, confirm: true).ConfigureAwait(false);
        if (!imported.Success || !imported.Applied) throw new InvalidOperationException($"SCL source import failed: {imported.Message}");
        PlcSourceOperationResult generated = await _plcService.GenerateBlocksFromSourceAsync(operation.SourceName!, dryRun: false, confirm: true).ConfigureAwait(false);
        if (!generated.Success || !generated.Applied) throw new InvalidOperationException($"SCL block generation failed: {generated.Message}");
        foreach (PlcCodeTransactionTargetSnapshot snapshot in operation.TargetSnapshots)
        {
            if (IsTypeTarget(snapshot.Target))
            {
                if (ResolveExactType(RequireTypeRoot(), snapshot.Target, allowMissing: true) == null) throw new InvalidOperationException($"SCL generation did not produce exact PLC type target '{snapshot.Target}'.");
                continue;
            }
            ResolvedBlock? resolved = ResolveExact(RequireBlockRoot(), snapshot.Target, allowMissing: true);
            if (resolved == null || !IdentityMatches(snapshot.Target, resolved.Identity, requireNumber: !string.IsNullOrWhiteSpace(snapshot.Target.Number))) throw new InvalidOperationException($"SCL generation did not produce exact target '{snapshot.Target}'.");
        }
    }


    internal async Task<PlcCodeTransactionApplyResult> ApplyAsync(string transactionId, string applyToken)
    {
        PlcCodeTransaction transaction = LoadTransaction(transactionId);
        RequireState(transaction, "Planned");
        if (string.IsNullOrWhiteSpace(transaction.PlanHash) || !FixedEquals(transaction.PlanHash!, ComputeTransactionPlanHash(transaction)))
        {
            transaction.State = "Stale";
            transaction.ApplyToken = null;
            transaction.Failure = "The persistent transaction manifest changed after planning.";
            AddJournal(transaction, null, "Preflight", "Stale", transaction.Failure);
            SaveTransaction(transaction);
            throw new InvalidOperationException(transaction.Failure);
        }
        if (string.IsNullOrWhiteSpace(transaction.ApplyToken) || !FixedEquals(transaction.ApplyToken!, applyToken ?? string.Empty))
            throw new InvalidOperationException("The transaction apply token is invalid or already consumed.");
        if (!transaction.ExpiresAtUtc.HasValue || transaction.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            transaction.State = "Expired";
            transaction.ApplyToken = null;
            AddJournal(transaction, null, "Preflight", "Expired", "The transaction expired before apply.");
            SaveTransaction(transaction);
            throw new InvalidOperationException("The transaction expired. Create and review a new transaction.");
        }

        await EnsureTransactionContextAsync(transaction).ConfigureAwait(false);
        try
        {
            await _capabilityGate.VerifyCurrentAsync(transaction).ConfigureAwait(false);
            await _promotionGate.VerifyAsync(transaction).ConfigureAwait(false);
            VerifyTransactionPreconditions(transaction);
            PlcCodeImpactAnalysis currentImpact = await RequireImpactAnalyzer().AnalyzeAsync(transaction).ConfigureAwait(false);
            if (transaction.ImpactAnalysis == null || !FixedEquals(transaction.ImpactAnalysis.Fingerprint, currentImpact.Fingerprint))
                throw new InvalidOperationException("PLC dependencies, consumers, or semantic impact changed after planning.");
            if (currentImpact.Status != "Safe" && (!transaction.ImpactAcknowledged || !FixedEquals(transaction.ImpactAcknowledgementFingerprint ?? string.Empty, currentImpact.Fingerprint)))
                throw new InvalidOperationException("The current PLC code impact requires a matching explicit acknowledgement before apply.");
        }
        catch (Exception ex)
        {
            transaction.State = "Stale";
            transaction.ApplyToken = null;
            transaction.Failure = Message(ex);
            AddJournal(transaction, null, "Preflight", "Stale", transaction.Failure);
            SaveTransaction(transaction);
            throw;
        }

        transaction.ApplyToken = null;
        transaction.State = "Applying";
        AddJournal(transaction, null, "Apply", "Starting", "All transaction preconditions matched atomically; mutation is starting.");
        SaveTransaction(transaction);
        try
        {
            foreach (PlcCodeTransactionOperation operation in transaction.Operations.OrderBy(item => item.ExecutionOrder).ThenBy(item => item.Sequence))
            {
                AddJournal(transaction, operation.Sequence, "Apply", "Starting", operation.Kind == "GenerateSclSource" ? $"Importing and generating SCL source '{operation.SourceName}'." : operation.Kind == "PatchSimaticMlType" ? $"Importing baseline-bound SimaticML PLC type patch for '{operation.Target}'." : operation.Kind == "PatchSimaticMlBlock" ? $"Importing baseline-bound SimaticML patch for '{operation.Target}'." : $"Importing exact target '{operation.Target}'.");
                operation.ApplyState = "Starting";
                SaveTransaction(transaction);
                if (operation.Kind == "GenerateSclSource") await ApplySclOperationAsync(operation).ConfigureAwait(false);
                else if (operation.Kind == "DeletePlcType")
                {
                    PlcCodeTargetRepository.ResolvedType exactType = _targets.ResolveExactType(operation.Target!, allowMissing: false)!;
                    if (!_targets.Delete(exactType) || _targets.ResolveExactType(operation.Target!, allowMissing: true) != null)
                        throw new InvalidOperationException($"Operation {operation.Sequence} could not delete exact PLC type target '{operation.Target}'.");
                }
                else if (operation.Kind == "PatchSimaticMlType")
                {
                    bool imported = await _plcService.ImportTypeAsync(operation.SourcePath!, operation.Target!.GroupPath).ConfigureAwait(false);
                    ResolvedType? resolved = imported ? ResolveExactType(RequireTypeRoot(), operation.Target, allowMissing: true) : null;
                    if (!imported || resolved == null)
                        throw new InvalidOperationException($"Operation {operation.Sequence} did not produce the exact planned SimaticML PLC type target '{operation.Target}'.");
                }
                else if (operation.Kind == "PatchSimaticMlBlock")
                {
                    bool imported = await _plcService.ImportBlockAsync(operation.SourcePath!, operation.Target!.GroupPath).ConfigureAwait(false);
                    ResolvedBlock? resolved = imported ? ResolveExact(RequireBlockRoot(), operation.Target, allowMissing: true) : null;
                    if (!imported || resolved == null || !IdentityMatches(operation.Target, resolved.Identity, requireNumber: !string.IsNullOrWhiteSpace(operation.Target.Number)))
                        throw new InvalidOperationException($"Operation {operation.Sequence} did not produce the exact planned SimaticML target '{operation.Target}'.");
                }
                else
                {
                    bool imported = await _plcService.ImportBlockFromDocumentsAsync(operation.ImportDirectory, operation.FileName, operation.Target!.GroupPath, operation.ImportOption).ConfigureAwait(false);
                    ResolvedBlock? resolved = imported ? ResolveExact(RequireBlockRoot(), operation.Target, allowMissing: true) : null;
                    if (!imported || resolved == null || !IdentityMatches(operation.Target, resolved.Identity, requireNumber: !string.IsNullOrWhiteSpace(operation.Target.Number)))
                        throw new InvalidOperationException($"Operation {operation.Sequence} did not produce the exact planned target '{operation.Target}'.");
                }
                operation.ApplyState = "Applied";
                AddJournal(transaction, operation.Sequence, "Apply", "Applied", "Exact target imported and read back.");
                SaveTransaction(transaction);
            }

            transaction.State = "Verifying";
            AddJournal(transaction, null, "Verify", "Starting", "All operations applied; compiling and exporting canonical results.");
            SaveTransaction(transaction);
            CompilationResult compilation = await _plcService.CompileSoftwareAsync().ConfigureAwait(false);
            transaction.FinalCompilation = ToEvidence(compilation);
            int baselineErrors = transaction.BaselineCompilation?.ErrorCount ?? 0;
            int baselineWarnings = transaction.BaselineCompilation?.WarningCount ?? 0;
            bool compileAccepted = transaction.CompilePolicy == "CleanCompile"
                ? compilation.Success && compilation.ErrorCount == 0
                : compilation.ErrorCount <= baselineErrors && (baselineErrors > 0 || compilation.Success);
            int newWarnings = Math.Max(0, compilation.WarningCount - baselineWarnings);
            if (!compileAccepted || (transaction.MaximumNewWarnings.HasValue && newWarnings > transaction.MaximumNewWarnings.Value))
                throw new InvalidOperationException($"Final compile violated policy {transaction.CompilePolicy} (baseline errors/warnings {baselineErrors}/{baselineWarnings}, final {compilation.ErrorCount}/{compilation.WarningCount}, new warnings {newWarnings}). {compilation.Message}");
            VerifyCanonicalResults(transaction);
            transaction.State = "Succeeded";
            transaction.RecoveryPackageRetained = false;
            transaction.Failure = null;
            AddJournal(transaction, null, "Lifecycle", "Succeeded", "Every exact target was exported canonically and the transaction introduced no compile errors.");
            SaveTransaction(transaction);
            Cleanup(TransactionBaselineRoot(transaction.TransactionId));
            return Result(transaction, applied: true, verified: true, rollbackAttempted: false, rolledBack: false, recoveryRequired: false, "Transaction applied, compiled, and verified successfully.");
        }
        catch (Exception ex)
        {
            transaction.Failure = Message(ex);
            AddJournal(transaction, null, "Apply", "Failed", transaction.Failure);
            SaveTransaction(transaction);
            return await _rollback.RollbackAsync(transaction, "Automatic rollback after apply or verification failure.").ConfigureAwait(false);
        }
    }

    internal async Task<PlcCodeTransactionApplyResult> RecoverAsync(string transactionId)
    {
        PlcCodeTransaction transaction = LoadTransaction(transactionId, updateExpiry: false);
        if (!new[] { "Applying", "Verifying", "DisposingProbe", "RollingBack", "RecoveryRequired" }.Contains(transaction.State, StringComparer.Ordinal))
            throw new InvalidOperationException($"Transaction '{transaction.TransactionId}' is in state '{transaction.State}' and does not require recovery.");
        if (string.IsNullOrWhiteSpace(transaction.PlanHash) || !FixedEquals(transaction.PlanHash!, ComputeTransactionPlanHash(transaction)))
        {
            transaction.State = "RecoveryRequired";
            transaction.Failure = "The transaction manifest integrity check failed. Automatic recovery is unsafe; inspect the retained baseline package manually.";
            AddJournal(transaction, null, "Recovery", "IntegrityFailed", transaction.Failure);
            SaveTransaction(transaction);
            throw new InvalidOperationException(transaction.Failure);
        }
        await EnsureTransactionContextAsync(transaction).ConfigureAwait(false);
        AddJournal(transaction, null, "Recovery", "Starting", "Recovery was explicitly requested; all operations will be restored conservatively.");
        SaveTransaction(transaction);
        return await _rollback.RollbackAsync(transaction, "Explicit recovery completed.").ConfigureAwait(false);
    }

    private void VerifyTransactionPreconditions(PlcCodeTransaction transaction)
    {
        object root = RequireBlockRoot();
        string currentRoot = Path.Combine(TransactionDirectory(transaction.TransactionId), "preflight");
        Cleanup(currentRoot);
        try
        {
            foreach (PlcCodeTransactionOperation operation in transaction.Operations.OrderBy(item => item.Sequence))
            {
                string actualInputHash = operation.Kind == "DeletePlcType" ? TypeIdentityHash(operation.GroupPath, operation.SourceName!) : operation.Kind == "GenerateSclSource" || operation.Kind == "PatchSimaticMlBlock" || operation.Kind == "PatchSimaticMlType" ? HashFile(operation.SourcePath!) : HashDocumentPair(operation.ImportDirectory, operation.FileName);
                if (!FixedEquals(operation.InputHash, actualInputHash))
                    throw new InvalidOperationException($"Operation {operation.Sequence} input documents changed after planning.");
                if ((operation.Kind == "PatchSimaticMlBlock" || operation.Kind == "PatchSimaticMlType") && !FixedEquals(operation.ValidationBaselineHash!, _formats.ComputeSimaticMlContentHash(operation.BaselinePath!)))
                    throw new InvalidOperationException($"Operation {operation.Sequence} SimaticML validation baseline changed after planning.");
                if (operation.Kind == "PatchSimaticMlBlock" || operation.Kind == "PatchSimaticMlType")
                {
                    PlcCodeChangeContract currentXmlContract = _formats.AnalyzeSimaticMlChange(operation.SourcePath!, operation.BaselinePath!, operation.AllowTopologyChange, string.Join(",", operation.AllowedChangePaths));
                    if (operation.ChangeContract == null || !FixedEquals(operation.ChangeContract.Fingerprint, currentXmlContract.Fingerprint)) throw new InvalidOperationException($"Operation {operation.Sequence} SimaticML changed-path contract changed after planning.");
                }
                if (operation.TargetMode == "BaselineBound")
                {
                    if (operation.ChangeContract == null || string.IsNullOrWhiteSpace(operation.AuthoringBaselineDirectory) || string.IsNullOrWhiteSpace(operation.AuthoringBaselineFileName))
                        throw new InvalidOperationException($"Operation {operation.Sequence} baseline-bound change contract is incomplete.");
                    PlcCodeChangeContract currentContract = _formats.AnalyzeS7DclChange(operation.ImportDirectory, operation.FileName, operation.AuthoringBaselineDirectory!, operation.AuthoringBaselineFileName!, string.Join(",", operation.ChangeContract.AllowedSections));
                    if (!FixedEquals(operation.ChangeContract.Fingerprint, currentContract.Fingerprint)) throw new InvalidOperationException($"Operation {operation.Sequence} baseline-bound candidate or baseline changed after planning.");
                }
                IReadOnlyList<PlcCodeTransactionTargetSnapshot> targets = EffectiveSnapshots(operation);
                int targetIndex = 0;
                foreach (PlcCodeTransactionTargetSnapshot snapshot in targets)
                {
                    targetIndex++;
                    if (IsTypeTarget(snapshot.Target))
                    {
                        ResolvedType? currentType = ResolveExactType(RequireTypeRoot(), snapshot.Target, allowMissing: true);
                        string actualType = "missing";
                        if (currentType != null)
                        {
                            string outputPath = Path.Combine(currentRoot, operation.Sequence + "-" + targetIndex + ".xml");
                            if (!ExportExactType(currentType, outputPath)) throw new InvalidOperationException($"Operation {operation.Sequence} exact PLC type target could not be exported during preflight.");
                            actualType = _formats.ComputeSimaticMlContentHash(outputPath);
                        }
                        if (!FixedEquals(snapshot.BaselineHash, actualType)) throw new InvalidOperationException($"Operation {operation.Sequence} PLC type target '{snapshot.Target}' changed after planning (expected {snapshot.BaselineHash}, actual {actualType}).");
                        continue;
                    }
                    ResolvedBlock? current = ResolveExact(root, snapshot.Target, allowMissing: true);
                    string actual = "missing";
                    if (current != null)
                    {
                        string output = Path.Combine(currentRoot, operation.Sequence + "-" + targetIndex);
                        Directory.CreateDirectory(output);
                        if (!ExportExact(current, output)) throw new InvalidOperationException($"Operation {operation.Sequence} exact target could not be exported during preflight.");
                        actual = HashDocumentPair(output, current.Identity.Name);
                    }
                    if (!FixedEquals(snapshot.BaselineHash, actual)) throw new InvalidOperationException($"Operation {operation.Sequence} target '{snapshot.Target}' changed after planning (expected {snapshot.BaselineHash}, actual {actual}).");
                }
            }
        }
        finally { Cleanup(currentRoot); }
    }

    private void VerifyCanonicalResults(PlcCodeTransaction transaction)
    {
        string resultRoot = Path.Combine(TransactionDirectory(transaction.TransactionId), "result");
        Cleanup(resultRoot);
        foreach (PlcCodeTransactionOperation operation in transaction.Operations.OrderBy(item => item.Sequence))
        {
            int targetIndex = 0;
            foreach (PlcCodeTransactionTargetSnapshot snapshot in EffectiveSnapshots(operation))
            {
                targetIndex++;
                if (IsTypeTarget(snapshot.Target))
                {
                    if (operation.Kind == "DeletePlcType")
                    {
                        if (ResolveExactType(RequireTypeRoot(), snapshot.Target, allowMissing: true) != null) throw new InvalidOperationException($"Operation {operation.Sequence} exact PLC type deletion verification failed for '{snapshot.Target}'.");
                        snapshot.CanonicalResultHash = "missing";
                        if (operation.TargetSnapshots.Count == 0) operation.CanonicalResultHash = "missing";
                        continue;
                    }
                    ResolvedType resolvedType = ResolveExactType(RequireTypeRoot(), snapshot.Target, allowMissing: false)!;
                    string typeOutput = Path.Combine(resultRoot, operation.Sequence + "-" + targetIndex + ".xml");
                    if (!ExportExactType(resolvedType, typeOutput)) throw new InvalidOperationException($"Operation {operation.Sequence} canonical PLC type export failed for '{snapshot.Target}'.");
                    snapshot.CanonicalResultHash = _formats.ComputeSimaticMlContentHash(typeOutput);
                    if (operation.TargetSnapshots.Count == 0) operation.CanonicalResultHash = snapshot.CanonicalResultHash;
                    continue;
                }
                ResolvedBlock resolved = ResolveExact(RequireBlockRoot(), snapshot.Target, allowMissing: false)!;
                if (!IdentityMatches(snapshot.Target, resolved.Identity, requireNumber: !string.IsNullOrWhiteSpace(snapshot.Target.Number))) throw new InvalidOperationException($"Operation {operation.Sequence} canonical identity verification failed for '{snapshot.Target}'.");
                string output = Path.Combine(resultRoot, operation.Sequence + "-" + targetIndex);
                if (snapshot.BaselineHash == "missing" && resolved.Identity.BlockType.Equals("InstanceDB", StringComparison.OrdinalIgnoreCase))
                {
                    // Instance DB documents are not a supported recreate route. Use
                    // the exact Siemens XML export for canonical creation evidence.
                    string xml = output + ".xml";
                    if (!_targets.ExportExactBlockXml(resolved, xml)) throw new InvalidOperationException($"Operation {operation.Sequence} canonical instance DB XML export failed for '{snapshot.Target}'.");
                    snapshot.CanonicalResultHash = _formats.ComputeSimaticMlContentHash(xml);
                }
                else
                {
                    Directory.CreateDirectory(output);
                    if (!ExportExact(resolved, output)) throw new InvalidOperationException($"Operation {operation.Sequence} canonical export failed for '{snapshot.Target}'.");
                    snapshot.CanonicalResultHash = HashDocumentPair(output, resolved.Identity.Name);
                }
                if (operation.TargetSnapshots.Count == 0) operation.CanonicalResultHash = snapshot.CanonicalResultHash;
            }
            operation.CanonicalResultHash = operation.TargetSnapshots.Count == 1 ? operation.TargetSnapshots[0].CanonicalResultHash : operation.TargetSnapshots.Count > 1 ? Hash(System.Text.Encoding.UTF8.GetBytes(string.Join("|", operation.TargetSnapshots.Select(item => item.CanonicalResultHash)))) : operation.CanonicalResultHash;
            operation.ApplyState = "Verified";
            AddJournal(transaction, operation.Sequence, "Verify", "Verified", $"Canonical result hash {operation.CanonicalResultHash}.");
        }
    }

    private async Task EnsureTransactionContextAsync(PlcCodeTransaction transaction)
    {
        if (transaction.TiaProcessId.HasValue && _sessionContext.AttachedPortalProcessId != transaction.TiaProcessId)
            throw new InvalidOperationException($"Transaction is bound to TIA process {transaction.TiaProcessId}, but the current process is {_sessionContext.AttachedPortalProcessId?.ToString() ?? "none"}.");
        string projectName = SessionProjectName();
        if (!string.Equals(transaction.ProjectName, projectName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Transaction is bound to project '{transaction.ProjectName}', but the current project is '{projectName}'.");
        await EnsurePlcOpen(transaction.PlcName).ConfigureAwait(false);
    }

    private static PlcCodeTransactionCompileEvidence ToEvidence(CompilationResult result) => new PlcCodeTransactionCompileEvidence
    {
        Success = result.Success,
        ErrorCount = result.ErrorCount,
        WarningCount = result.WarningCount,
        Message = result.Message ?? string.Empty
    };

    private static PlcCodeTransactionApplyResult Result(PlcCodeTransaction transaction, bool applied, bool verified, bool rollbackAttempted, bool rolledBack, bool recoveryRequired, string message) => new PlcCodeTransactionApplyResult
    {
        TransactionId = transaction.TransactionId,
        State = transaction.State,
        Applied = applied,
        Verified = verified,
        RollbackAttempted = rollbackAttempted,
        RolledBackAndVerified = rolledBack,
        RecoveryRequired = recoveryRequired,
        Message = message,
        Transaction = transaction
    };

    private static void RequireState(PlcCodeTransaction transaction, string state) => PlcCodeTransactionIntegrity.RequireState(transaction, state);
    private static IReadOnlyList<int> ParseDependencySequences(string? value) => PlcCodeTransactionIntegrity.ParseDependencySequences(value);

    private string SessionProjectName() => _sessionContext.Project == null ? string.Empty : TiaReflectionHelper.GetName(_sessionContext.Project) ?? string.Empty;
    private string SessionProjectPath() => _sessionContext.Project == null ? string.Empty : TiaReflectionHelper.GetProperty(_sessionContext.Project, "Path")?.ToString() ?? string.Empty;
    private string TransactionDirectory(string id) => _store.DirectoryPath(id);
    private string TransactionBaselineRoot(string id) => _store.BaselineRoot(id);
    private string TransactionOperationBaseline(string id, int sequence) => _store.OperationBaseline(id, sequence);
    private PlcCodeTransaction LoadTransaction(string id, bool updateExpiry = true) => _store.Load(id, updateExpiry);
    private void SaveTransaction(PlcCodeTransaction transaction) => _store.Save(transaction);
    private void AddJournal(PlcCodeTransaction transaction, int? operation, string phase, string state, string message) => _store.AddJournal(transaction, operation, phase, state, message);
    private static string ComputeTransactionPlanHash(PlcCodeTransaction transaction) => PlcCodeTransactionIntegrity.ComputePlanHash(transaction);

    private void RequireCapabilityOperation(PlcCodeTransaction transaction, string operationKind, string objectKind, string? objectName, string? objectType, string groupPath) => _capabilityGate.RequireOperation(transaction, operationKind, objectKind, objectName, objectType, groupPath);

    private static string Message(Exception exception) => PlcWorkflowPrimitives.Message(exception);
    internal static IReadOnlyList<PlcBlockIdentity> OperationTargets(PlcCodeTransactionOperation operation) => PlcCodeTransactionIntegrity.OperationTargets(operation);
    internal static IReadOnlyList<PlcCodeTransactionTargetSnapshot> EffectiveSnapshots(PlcCodeTransactionOperation operation) => PlcCodeTransactionIntegrity.EffectiveSnapshots(operation);
    private string TransactionSclTargetBaseline(string id, int sequence, int targetIndex) => _store.SclTargetBaseline(id, sequence, targetIndex);
    private string TransactionSclTypeBaseline(string id, int sequence, int targetIndex) => _store.SclTypeBaseline(id, sequence, targetIndex);
    private string TransactionOperationTypeBaseline(string id, int sequence) => _store.OperationTypeBaseline(id, sequence);
    private static bool IsTypeTarget(PlcBlockIdentity target) => PlcWorkflowPrimitives.IsTypeTarget(target);
    private static string HashFile(string path) => PlcWorkflowPrimitives.HashFile(path);
    private static string TypeIdentityHash(string groupPath, string typeName) => PlcCodeTransactionIntegrity.TypeIdentityHash(groupPath, typeName);
    private static string Hash(byte[] value) => PlcWorkflowPrimitives.Hash(value);
    private static bool FixedEquals(string? left, string? right) => PlcWorkflowPrimitives.FixedEquals(left, right);
    private static string RandomToken() => PlcWorkflowPrimitives.RandomToken();
    private static string HashDocumentPair(string directory, string name) => PlcWorkflowPrimitives.HashDocumentPair(directory, name);
    private static string NormalizeGroupPath(string? path) => PlcWorkflowPrimitives.NormalizeGroupPath(path);
    private static string NormalizeTypeGroupPath(string? path) => PlcWorkflowPrimitives.NormalizeTypeGroupPath(path);
    private static string DisplayGroup(string path) => PlcWorkflowPrimitives.DisplayGroup(path);
    private static bool GroupEquals(string? left, string? right) => PlcWorkflowPrimitives.GroupEquals(left, right);
    private static void Cleanup(string path) => PlcWorkflowPrimitives.Cleanup(path);
    private object RequireBlockRoot() => _targets.RequireBlockRoot();
    private object RequireTypeRoot() => _targets.RequireTypeRoot();
    private object? ResolveGroup(object root, string path) => _targets.ResolveBlockGroup(path);
    private IReadOnlyList<ResolvedBlock> FindBlocks(object root, string name) => _targets.FindBlocks(name);
    private IReadOnlyList<ResolvedType> FindTypes(object root, string name) => _targets.FindTypes(name);
    private ResolvedBlock? ResolveExact(object root, PlcBlockIdentity identity, bool allowMissing) => _targets.ResolveExact(identity, allowMissing);
    private ResolvedType? ResolveExactType(object root, PlcBlockIdentity identity, bool allowMissing) => _targets.ResolveExactType(identity, allowMissing);
    private bool ExportExact(ResolvedBlock target, string directory) => _targets.ExportExact(target, directory);
    private bool ExportExactType(ResolvedType target, string path) => _targets.ExportExactType(target, path);
    private bool IdentityMatches(PlcBlockIdentity expected, PlcBlockIdentity actual, bool requireNumber) => _targets.IdentityMatches(expected, actual, requireNumber);
    private bool BlockTypeEquals(string left, string right) => _targets.BlockTypeEquals(left, right);
    private InvalidOperationException Ambiguous(string name, IEnumerable<ResolvedBlock> matches) => _targets.Ambiguous(name, matches);
    private async Task EnsurePlcOpen(string plcName) { if (!_plcService.IsSoftwareOpen || !string.Equals(_plcService.CurrentPlcName, plcName, StringComparison.OrdinalIgnoreCase)) await _plcService.OpenPlcAsync(plcName).ConfigureAwait(false); }
    private PlcCodeCapabilityDiscoveryService? TryCapabilityDiscovery() => _capabilityGate.Discovery;
    private PlcCodeImpactAnalyzer RequireImpactAnalyzer() => _impactAnalyzer;

}
