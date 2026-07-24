using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Models;
using ResolvedBlock = OpenTiaToolkit.Core.Services.PlcCodeTargetRepository.ResolvedBlock;
using ResolvedType = OpenTiaToolkit.Core.Services.PlcCodeTargetRepository.ResolvedType;

namespace OpenTiaToolkit.Core.Services;

internal sealed class PlcCodeRollbackCoordinator
{
    private readonly IPlcService _plc;
    private readonly PlcCodeFormatService _formats;
    private readonly PlcCodeTargetRepository _targets;
    private readonly PlcCodeTransactionStore _store;
    internal PlcCodeRollbackCoordinator(IPlcService plc, PlcCodeFormatService formats, PlcCodeTargetRepository targets, PlcCodeTransactionStore store) { _plc = plc; _formats = formats; _targets = targets; _store = store; }

    internal async Task<PlcCodeTransactionApplyResult> RollbackAsync(PlcCodeTransaction transaction, string reason)
    {
        transaction.State = "RollingBack"; Journal(transaction, null, "Rollback", "Starting", reason); Save(transaction); bool allRestored = true;
        foreach (PlcCodeTransactionOperation operation in transaction.Operations.OrderByDescending(item => item.ExecutionOrder).ThenByDescending(item => item.Sequence))
        {
            try
            {
                bool operationRestored = true; int targetIndex = PlcCodeTransactionIntegrity.EffectiveSnapshots(operation).Count;
                foreach (PlcCodeTransactionTargetSnapshot snapshot in PlcCodeTransactionIntegrity.EffectiveSnapshots(operation).Reverse())
                {
                    if (PlcWorkflowPrimitives.IsTypeTarget(snapshot.Target))
                    {
                        string path = operation.Kind == "GenerateSclSource" ? _store.SclTypeBaseline(transaction.TransactionId, operation.Sequence, targetIndex) : _store.OperationTypeBaseline(transaction.TransactionId, operation.Sequence); targetIndex--;
                        PlcRollbackEvidence evidence = await RollbackTypeAsync(snapshot, path).ConfigureAwait(false); snapshot.RestorationVerified = evidence.OperationSucceeded && evidence.HashMatches; snapshot.ActualRestoredHash = evidence.ActualHash; operationRestored &= snapshot.RestorationVerified; continue;
                    }
                    string backup = operation.Kind == "GenerateSclSource" ? _store.SclTargetBaseline(transaction.TransactionId, operation.Sequence, targetIndex) : _store.OperationBaseline(transaction.TransactionId, operation.Sequence); targetIndex--;
                    var plan = new PlcDocumentImportPlan { PlcName = transaction.PlcName, Target = snapshot.Target, FileName = snapshot.Target.Name, BaselineHash = snapshot.BaselineHash };
                    PlcRollbackEvidence blockEvidence = await RollbackBlockAsync(plan, backup).ConfigureAwait(false); snapshot.RestorationVerified = blockEvidence.OperationSucceeded && blockEvidence.HashMatches; snapshot.ActualRestoredHash = blockEvidence.ActualHash; operationRestored &= snapshot.RestorationVerified;
                }
                if (operation.Kind == "GenerateSclSource")
                {
                    PlcSourceOperationResult deleted = await _plc.DeleteExternalSourceAsync(operation.SourceName!, confirm: true).ConfigureAwait(false); var sources = await _plc.ListExternalSourcesAsync().ConfigureAwait(false);
                    operationRestored &= (deleted.Success || !sources.Any(item => string.Equals(item.Name, operation.SourceName, StringComparison.OrdinalIgnoreCase))) && !sources.Any(item => string.Equals(item.Name, operation.SourceName, StringComparison.OrdinalIgnoreCase));
                }
                operation.RestorationVerified = operationRestored; operation.ActualRestoredHash = operationRestored ? "all-targets-restored" : "unverified"; operation.ApplyState = operationRestored ? "RolledBack" : "RecoveryRequired"; allRestored &= operationRestored;
                Journal(transaction, operation.Sequence, "Rollback", operation.ApplyState, operation.RestorationVerified ? "Every generated target and the external source were restored or removed with proof." : "One or more generated targets or the external source could not be proven restored."); Save(transaction);
            }
            catch (Exception ex) { allRestored = false; operation.ApplyState = "RecoveryRequired"; Journal(transaction, operation.Sequence, "Rollback", "Failed", PlcWorkflowPrimitives.Message(ex)); Save(transaction); }
        }
        bool compileRestored = false;
        try { CompilationResult compilation = await _plc.CompileSoftwareAsync().ConfigureAwait(false); transaction.RollbackCompilation = Evidence(compilation); int baselineErrors = transaction.BaselineCompilation?.ErrorCount ?? 0; compileRestored = compilation.ErrorCount <= baselineErrors && (baselineErrors > 0 || compilation.Success); }
        catch (Exception ex) { Journal(transaction, null, "RollbackCompile", "Failed", PlcWorkflowPrimitives.Message(ex)); }
        bool proven = allRestored && compileRestored; transaction.State = proven ? "RolledBackAndVerified" : "RecoveryRequired"; transaction.RecoveryPackageRetained = !proven;
        Journal(transaction, null, "Rollback", transaction.State, proven ? "All original hashes or missing targets were restored and the baseline compile condition was recovered." : "Rollback remains unverified. The baseline package is retained for explicit recovery or manual inspection."); Save(transaction);
        if (proven) PlcWorkflowPrimitives.Cleanup(_store.BaselineRoot(transaction.TransactionId));
        return Result(transaction, proven ? "Transaction failed and was rolled back with full restoration proof." : "Transaction failed and automatic rollback could not be fully proven; recovery is required.");
    }

    private async Task<PlcRollbackEvidence> RollbackBlockAsync(PlcDocumentImportPlan plan, string backup)
    {
        if (plan.BaselineHash != "missing")
        {
            bool imported = await _plc.ImportBlockFromDocumentsAsync(backup, plan.Target.Name, plan.Target.GroupPath, "Override").ConfigureAwait(false); if (!imported) return new PlcRollbackEvidence(false, false, plan.BaselineHash, null);
            ResolvedBlock? restored = _targets.ResolveExact(plan.Target, true); if (restored == null) return new PlcRollbackEvidence(true, false, plan.BaselineHash, null);
            string verify = Path.Combine(Path.GetTempPath(), "OpenTiaToolkit", "plc-document-rollback", Guid.NewGuid().ToString("N"));
            try { if (!_targets.ExportExact(restored, verify)) return new PlcRollbackEvidence(true, false, plan.BaselineHash, null); string actual = PlcWorkflowPrimitives.HashDocumentPair(verify, plan.Target.Name); return new PlcRollbackEvidence(true, PlcWorkflowPrimitives.FixedEquals(plan.BaselineHash, actual), plan.BaselineHash, actual); }
            finally { PlcWorkflowPrimitives.Cleanup(verify); }
        }
        ResolvedBlock? created = _targets.ResolveExact(plan.Target, true); bool deleted = created == null || _targets.Delete(created); bool absent = _targets.ResolveExact(plan.Target, true) == null; return new PlcRollbackEvidence(deleted && absent, deleted && absent, "missing", absent ? "missing" : "present");
    }

    private async Task<PlcRollbackEvidence> RollbackTypeAsync(PlcCodeTransactionTargetSnapshot snapshot, string backup)
    {
        if (snapshot.BaselineHash == "missing") { ResolvedType? created = _targets.ResolveExactType(snapshot.Target, true); bool deleted = created == null || _targets.Delete(created); bool absent = _targets.ResolveExactType(snapshot.Target, true) == null; return new PlcRollbackEvidence(deleted && absent, deleted && absent, "missing", absent ? "missing" : "present"); }
        bool imported = await _plc.ImportTypeAsync(backup, snapshot.Target.GroupPath).ConfigureAwait(false); ResolvedType? restored = imported ? _targets.ResolveExactType(snapshot.Target, true) : null; string? actual = null;
        if (restored != null) { string verify = Path.Combine(Path.GetTempPath(), "OpenTiaToolkit", "plc-document-rollback", Guid.NewGuid().ToString("N") + ".xml"); try { if (_targets.ExportExactType(restored, verify)) actual = _formats.ComputeSimaticMlContentHash(verify); } finally { if (File.Exists(verify)) File.Delete(verify); } }
        return new PlcRollbackEvidence(imported && restored != null, actual != null && PlcWorkflowPrimitives.FixedEquals(snapshot.BaselineHash, actual), snapshot.BaselineHash, actual);
    }

    private void Save(PlcCodeTransaction transaction) => _store.Save(transaction);
    private void Journal(PlcCodeTransaction transaction, int? operation, string phase, string state, string message) => _store.AddJournal(transaction, operation, phase, state, message);
    private static PlcCodeTransactionCompileEvidence Evidence(CompilationResult value) => new PlcCodeTransactionCompileEvidence { Success = value.Success, ErrorCount = value.ErrorCount, WarningCount = value.WarningCount, Message = value.Message ?? string.Empty };
    private static PlcCodeTransactionApplyResult Result(PlcCodeTransaction transaction, string message) => new PlcCodeTransactionApplyResult { TransactionId = transaction.TransactionId, State = transaction.State, RollbackAttempted = true, RolledBackAndVerified = transaction.State == "RolledBackAndVerified", RecoveryRequired = transaction.State == "RecoveryRequired", Message = message, Transaction = transaction };
}
