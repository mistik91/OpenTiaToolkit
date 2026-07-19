using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Models;

namespace OpenTiaToolkit.Core.Services;

internal sealed class PlcCodeTransactionCoordinator
{
    private readonly PlcCodeTransactionAuthoringService _authoring;
    private readonly PlcCodeTransactionPlanner _planner;
    private readonly PlcCodeTransactionExecutor _executor;
    private readonly PlcCodeTransactionStore _store;
    private readonly PlcCodeRollbackCoordinator _rollback;

    internal PlcCodeTransactionCoordinator(IPlcService plcService, EngineeringSessionContext sessionContext, PlcDocumentValidator validator, PlcCodeFormatService formats, PlcCodeTargetRepository targets, PlcCodeTransactionStore store, PlcCodeCapabilityGate capabilityGate, PlcCodePromotionGate promotionGate, PlcCodeRollbackCoordinator rollback, PlcCodeImpactAnalyzer impactAnalyzer)
    {
        _authoring = new PlcCodeTransactionAuthoringService(plcService, sessionContext, validator, formats, store, capabilityGate);
        _planner = new PlcCodeTransactionPlanner(plcService, sessionContext, validator, formats, targets, store, capabilityGate, promotionGate, impactAnalyzer);
        _executor = new PlcCodeTransactionExecutor(plcService, sessionContext, formats, targets, store, capabilityGate, promotionGate, rollback, impactAnalyzer);
        _store = store;
        _rollback = rollback;
    }

    public Task<PlcCodeTransaction> BeginTransactionAsync(string plcName, string? description, string? compilePolicy = null, int? maximumNewWarnings = null, string? capabilityId = null, string? capabilityIds = null) => _authoring.BeginAsync(plcName, description, compilePolicy, maximumNewWarnings, capabilityId, capabilityIds);
    public PlcCodeTransaction AddTransactionOperation(string transactionId, string importDirectory, string fileName, string? groupPath, string? importOption, string? dependsOn = null, string? targetMode = null, string? baselineDirectory = null, string? baselineFileName = null, string? allowedChanges = null) => _authoring.AddDocument(transactionId, importDirectory, fileName, groupPath, importOption, dependsOn, targetMode, baselineDirectory, baselineFileName, allowedChanges);
    public PlcCodeTransaction AddSclTransactionOperation(string transactionId, string sourcePath, string? groupPath, string? typeGroupPath = null, string? dependsOn = null) => _authoring.AddScl(transactionId, sourcePath, groupPath, typeGroupPath, dependsOn);
    public PlcCodeTransaction AddSimaticMlTransactionOperation(string transactionId, string inputPath, string baselinePath, string? groupPath, bool allowTopologyChange, string? dependsOn = null, string? allowedChangePaths = null) => _authoring.AddSimaticMl(transactionId, inputPath, baselinePath, groupPath, allowTopologyChange, dependsOn, allowedChangePaths);
    public PlcCodeTransaction AddDeleteTypeTransactionOperation(string transactionId, string typeName, string? groupPath, string? dependsOn = null) => _authoring.AddDeleteType(transactionId, typeName, groupPath, dependsOn);
    public Task<PlcCodeTransaction> PlanTransactionAsync(string transactionId) => _planner.PlanTransactionAsync(transactionId);
    public Task<PlcCodeTransactionApplyResult> ApplyTransactionAsync(string transactionId, string applyToken) => _executor.ApplyAsync(transactionId, applyToken);
    public Task<PlcCodeTransactionApplyResult> RecoverTransactionAsync(string transactionId) => _executor.RecoverAsync(transactionId);
    public PlcCodeTransaction GetTransaction(string transactionId) => _store.Load(transactionId);
    public IReadOnlyList<PlcCodeTransaction> ListTransactions() => _store.List();
    public bool DiscardTransaction(string transactionId)
    {
        PlcCodeTransaction transaction = _store.Load(transactionId, updateExpiry: false);
        if (new[] { "Applying", "Verifying", "DisposingProbe", "RollingBack", "RecoveryRequired" }.Contains(transaction.State, StringComparer.Ordinal))
            throw new InvalidOperationException("A transaction with unverified recovery state cannot be discarded. Recover it first.");
        return _store.Discard(transaction.TransactionId);
    }
    public PlcCodeImpactAnalysis GetTransactionImpact(string transactionId) => _store.Load(transactionId).ImpactAnalysis ?? throw new InvalidOperationException("The transaction has not been planned, so no impact analysis exists.");
    public Task<PlcCodeTransaction> ApproveTransactionImpactAsync(string transactionId, string impactFingerprint, bool acceptUnknown) => _executor.ApproveImpactAsync(transactionId, impactFingerprint, acceptUnknown);

    internal PlcCodeTransaction LoadForRecovery(string transactionId) => _store.Load(transactionId, updateExpiry: false);
    internal void SaveState(PlcCodeTransaction transaction) => _store.Save(transaction);
    internal void Journal(PlcCodeTransaction transaction, int? operation, string phase, string state, string message) => _store.AddJournal(transaction, operation, phase, state, message);
    internal Task<PlcCodeTransactionApplyResult> RollbackAsync(PlcCodeTransaction transaction, string reason) => _rollback.RollbackAsync(transaction, reason);
}
