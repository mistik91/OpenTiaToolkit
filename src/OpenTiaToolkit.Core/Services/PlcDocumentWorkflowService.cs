using System.Collections.Generic;
using System.Threading.Tasks;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Models;

namespace OpenTiaToolkit.Core.Services;

public sealed class PlcDocumentWorkflowService
{
    private readonly PlcDocumentImportWorkflow _documents;
    private readonly PlcCodeTransactionCoordinator _transactions;
    private readonly PlcCodeProbeWorkflow _probes;
    private readonly PlcCodeBehaviorProbeWorkflow _behavior;

    public PlcDocumentWorkflowService(IPlcService plcService) : this(plcService, new EngineeringSessionContext(), null, new PlcCodeEvidenceRegistry(), new PlcSimulationRuntime()) { }
    internal PlcDocumentWorkflowService(IPlcService plcService, EngineeringSessionContext sessionContext) : this(plcService, sessionContext, null, new PlcCodeEvidenceRegistry(), new PlcSimulationRuntime()) { }
    internal PlcDocumentWorkflowService(IPlcService plcService, EngineeringSessionContext sessionContext, PlcCodeCapabilityDiscoveryService? capabilityDiscovery, PlcCodeEvidenceRegistry? evidenceRegistry = null, IPlcSimulationRuntime? simulationRuntime = null)
    {
        var validator = new PlcDocumentValidator();
        var formats = new PlcCodeFormatService(validator);
        var targets = new PlcCodeTargetRepository(sessionContext);
        var planStore = new PlcDocumentPlanStore();
        var transactionStore = new PlcCodeTransactionStore();
        var capabilities = new PlcCodeCapabilityGate(capabilityDiscovery);
        PlcCodeEvidenceRegistry evidence = evidenceRegistry ?? new PlcCodeEvidenceRegistry();
        IPlcSimulationRuntime simulator = simulationRuntime ?? new PlcSimulationRuntime();
        var promotion = new PlcCodePromotionGate(capabilities, evidence, formats);
        var rollback = new PlcCodeRollbackCoordinator(plcService, formats, targets, transactionStore);
        var impact = new PlcCodeImpactAnalyzer(plcService, targets, transactionStore);
        _documents = new PlcDocumentImportWorkflow(plcService, validator, planStore, targets);
        _transactions = new PlcCodeTransactionCoordinator(plcService, sessionContext, validator, formats, targets, transactionStore, capabilities, promotion, rollback, impact);
        _probes = new PlcCodeProbeWorkflow(_transactions, capabilities, promotion, evidence);
        _behavior = new PlcCodeBehaviorProbeWorkflow(plcService, simulator, evidence, _probes, _transactions);
    }

    public PlcDocumentValidationResult Validate(string importDirectory, string fileName) => _documents.Validate(importDirectory, fileName);
    public Task<PlcDocumentImportPlan> PlanAsync(string plcName, string importDirectory, string fileName, string? groupPath, string importOption) => _documents.PlanAsync(plcName, importDirectory, fileName, groupPath, importOption);
    public Task<PlcDocumentImportApplyResult> ApplyAsync(string token) => _documents.ApplyAsync(token);
    public Task<PlcCodeTransaction> BeginTransactionAsync(string plcName, string? description, string? compilePolicy = null, int? maximumNewWarnings = null, string? capabilityId = null, string? capabilityIds = null) => _transactions.BeginTransactionAsync(plcName, description, compilePolicy, maximumNewWarnings, capabilityId, capabilityIds);
    public PlcCodeTransaction AddTransactionOperation(string transactionId, string importDirectory, string fileName, string? groupPath, string? importOption, string? dependsOn = null, string? targetMode = null, string? baselineDirectory = null, string? baselineFileName = null, string? allowedChanges = null) => _transactions.AddTransactionOperation(transactionId, importDirectory, fileName, groupPath, importOption, dependsOn, targetMode, baselineDirectory, baselineFileName, allowedChanges);
    public PlcCodeTransaction AddSclTransactionOperation(string transactionId, string sourcePath, string? groupPath, string? typeGroupPath = null, string? dependsOn = null) => _transactions.AddSclTransactionOperation(transactionId, sourcePath, groupPath, typeGroupPath, dependsOn);
    public PlcCodeTransaction AddSimaticMlTransactionOperation(string transactionId, string inputPath, string baselinePath, string? groupPath, bool allowTopologyChange, string? dependsOn = null, string? allowedChangePaths = null) => _transactions.AddSimaticMlTransactionOperation(transactionId, inputPath, baselinePath, groupPath, allowTopologyChange, dependsOn, allowedChangePaths);
    public PlcCodeTransaction AddDeleteTypeTransactionOperation(string transactionId, string typeName, string? groupPath, string? dependsOn = null) => _transactions.AddDeleteTypeTransactionOperation(transactionId, typeName, groupPath, dependsOn);
    public Task<PlcCodeTransaction> PlanTransactionAsync(string transactionId) => _transactions.PlanTransactionAsync(transactionId);
    public Task<PlcCodeTransactionApplyResult> ApplyTransactionAsync(string transactionId, string applyToken) => _transactions.ApplyTransactionAsync(transactionId, applyToken);
    public Task<PlcCodeTransactionApplyResult> RecoverTransactionAsync(string transactionId) => _transactions.RecoverTransactionAsync(transactionId);
    public PlcCodeTransaction GetTransaction(string transactionId) => _transactions.GetTransaction(transactionId);
    public IReadOnlyList<PlcCodeTransaction> ListTransactions() => _transactions.ListTransactions();
    public bool DiscardTransaction(string transactionId) => _transactions.DiscardTransaction(transactionId);
    public PlcCodeImpactAnalysis GetTransactionImpact(string transactionId) => _transactions.GetTransactionImpact(transactionId);
    public Task<PlcCodeTransaction> ApproveTransactionImpactAsync(string transactionId, string impactFingerprint, bool acceptUnknown) => _transactions.ApproveTransactionImpactAsync(transactionId, impactFingerprint, acceptUnknown);
    public Task<PlcCodeTransaction> BeginPromotedTransactionAsync(string plcName, string evidenceId, string capabilityIds, string codeFormat, string inputPath, string? description, string? compilePolicy, int? maximumNewWarnings, string saveDecision, string sourceControlDecision) => _probes.BeginPromotedTransactionAsync(plcName, evidenceId, capabilityIds, codeFormat, inputPath, description, compilePolicy, maximumNewWarnings, saveDecision, sourceControlDecision);
    public Task<PlcCodeEvidenceQueryResult> QueryProbeEvidenceAsync(string capabilityIds, string codeFormat, string inputPath) => _probes.QueryProbeEvidenceAsync(capabilityIds, codeFormat, inputPath);
    public Task<PlcCodeProbeRunResult> RunProbeAsync(string plcName, string capabilityIds, string codeFormat, string inputPath, string? groupPath, string? typeGroupPath, string? compilePolicy, int? maximumNewWarnings, bool acceptUnknownImpact, bool force) => _probes.RunProbeAsync(plcName, capabilityIds, codeFormat, inputPath, groupPath, typeGroupPath, compilePolicy, maximumNewWarnings, acceptUnknownImpact, force);
    public PlcBehaviorVectorValidationResult ValidateBehaviorVector(string vectorPath) => _behavior.ValidateBehaviorVector(vectorPath);
    public PlcCodeArtifactValidationResult ValidateBehaviorPackageArtifact(string packagePath) => _behavior.ValidateBehaviorPackageArtifact(packagePath);
    public PlcSimulatorCapabilityDiscovery DiscoverSimulatorCapabilities(string plcName, string? instanceName = null, bool refreshTags = false) => _behavior.DiscoverSimulatorCapabilities(plcName, instanceName, refreshTags);
    public Task<PlcCodeEvidenceQueryResult> QueryBehaviorEvidenceAsync(string plcName, string capabilityIds, string packagePath, string vectorPath, string? instanceName) => _behavior.QueryBehaviorEvidenceAsync(plcName, capabilityIds, packagePath, vectorPath, instanceName);
    public Task<PlcCodeBehaviorProbeRunResult> RunBehaviorProbeAsync(string plcName, string capabilityIds, string packagePath, string vectorPath, string? instanceName, string? compilePolicy, int? maximumNewWarnings, bool acceptUnknownImpact, bool onlyChanges, bool force) => _behavior.RunBehaviorProbeAsync(plcName, capabilityIds, packagePath, vectorPath, instanceName, compilePolicy, maximumNewWarnings, acceptUnknownImpact, onlyChanges, force);
    public Task<PlcCodeBehaviorProbeRunResult> RecoverBehaviorProbeAsync(string evidenceId, bool onlyChanges) => _behavior.RecoverBehaviorProbeAsync(evidenceId, onlyChanges);
    internal static bool HasNoNewCompareDifferences(PlcSoftwareCompareResult baseline, PlcSoftwareCompareResult current) => PlcBehaviorCompareAnalyzer.HasNoNewCompareDifferences(baseline, current);
}
