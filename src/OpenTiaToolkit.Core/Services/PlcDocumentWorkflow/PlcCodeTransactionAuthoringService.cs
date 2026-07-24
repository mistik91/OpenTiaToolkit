using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Models;

namespace OpenTiaToolkit.Core.Services;

internal sealed class PlcCodeTransactionAuthoringService
{
    private readonly IPlcService _plcService;
    private readonly EngineeringSessionContext _sessionContext;
    private readonly PlcDocumentValidator _validator;
    private readonly PlcCodeFormatService _formats;
    private readonly PlcCodeTransactionStore _store;
    private readonly PlcCodeCapabilityGate _capabilityGate;

    internal PlcCodeTransactionAuthoringService(IPlcService plcService, EngineeringSessionContext sessionContext, PlcDocumentValidator validator, PlcCodeFormatService formats, PlcCodeTransactionStore store, PlcCodeCapabilityGate capabilityGate)
    {
        _plcService = plcService;
        _sessionContext = sessionContext;
        _validator = validator;
        _formats = formats;
        _store = store;
        _capabilityGate = capabilityGate;
    }

    internal async Task<PlcCodeTransaction> BeginAsync(string plcName, string? description, string? compilePolicy, int? maximumNewWarnings, string? capabilityId, string? capabilityIds)
    {
        string normalizedPolicy = string.IsNullOrWhiteSpace(compilePolicy) ? "NoNewErrors" : compilePolicy!;
        if (!new[] { "NoNewErrors", "CleanCompile" }.Contains(normalizedPolicy, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("compile-policy must be NoNewErrors or CleanCompile.");
        normalizedPolicy = normalizedPolicy.Equals("CleanCompile", StringComparison.OrdinalIgnoreCase) ? "CleanCompile" : "NoNewErrors";
        if (maximumNewWarnings < 0) throw new InvalidOperationException("maximum-new-warnings cannot be negative.");
        await EnsurePlcOpen(plcName).ConfigureAwait(false);
        string[] requestedCapabilities = new[] { capabilityId, capabilityIds }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => value!.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            .Select(value => value.Trim()).Distinct(StringComparer.Ordinal).ToArray();
        PlcCodeCapabilityDiscoveryService? capabilityDiscovery = _capabilityGate.Discovery;
        if (capabilityDiscovery != null && requestedCapabilities.Length == 0)
            throw new InvalidOperationException("At least one capability-id is required. Run discover-plc-code-capabilities for every intended transaction target.");
        var capabilities = new List<PlcCodeCapabilityDiscovery>();
        foreach (string requestedCapability in requestedCapabilities)
        {
            if (capabilityDiscovery == null) throw new InvalidOperationException("Capability discovery is unavailable in this host.");
            PlcCodeCapabilityDiscovery capability = await capabilityDiscovery.ValidateCurrentAsync(requestedCapability, plcName).ConfigureAwait(false);
            if (capability.SelectedRoute == "VCI") throw new InvalidOperationException("This capability selected VCI. Use the VCI synchronization workflow; direct-import transactions cannot impersonate a VCI change.");
            if (capability.SelectedRoute == "Blocked") throw new InvalidOperationException("This capability discovery did not authorize a mutation route.");
            capabilities.Add(capability);
        }
        string id = Guid.NewGuid().ToString("N");
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var transaction = new PlcCodeTransaction
        {
            TransactionId = id,
            Description = description ?? string.Empty,
            State = "Draft",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            TiaProcessId = _sessionContext.AttachedPortalProcessId,
            ProjectName = SessionProjectName(),
            ProjectPath = SessionProjectPath(),
            PlcName = plcName,
            CapabilityId = capabilities.FirstOrDefault()?.CapabilityId,
            CapabilityFingerprint = capabilities.FirstOrDefault()?.Fingerprint,
            CapabilityRoute = capabilities.FirstOrDefault()?.SelectedRoute,
            CapabilityBindings = capabilities.Select(capability => new PlcCodeCapabilityBinding
            {
                CapabilityId = capability.CapabilityId,
                Fingerprint = capability.Fingerprint,
                Route = capability.SelectedRoute,
                ObjectKind = capability.Request.ObjectKind,
                ObjectName = capability.Request.ObjectName,
                GroupPath = capability.Request.GroupPath
            }).ToArray(),
            CompilePolicy = normalizedPolicy,
            MaximumNewWarnings = maximumNewWarnings
        };
        _store.AddJournal(transaction, null, "Lifecycle", "Draft", "Transaction created and bound to the current TIA project and PLC.");
        _store.Save(transaction);
        return transaction;
    }

    internal PlcCodeTransaction AddDocument(string transactionId, string importDirectory, string fileName, string? groupPath, string? importOption, string? dependsOn, string? targetMode, string? baselineDirectory, string? baselineFileName, string? allowedChanges)
    {
        PlcCodeTransaction transaction = LoadDraft(transactionId);
        PlcDocumentValidationResult validation = _validator.Validate(importDirectory, fileName);
        if (!validation.Valid) throw new InvalidDataException("PLC documents are invalid: " + string.Join("; ", validation.Errors));
        string mode = string.IsNullOrWhiteSpace(targetMode) ? "Auto" : targetMode!.Trim();
        if (mode.Equals("create-only", StringComparison.OrdinalIgnoreCase)) mode = "CreateOnly";
        else if (mode.Equals("baseline-bound", StringComparison.OrdinalIgnoreCase)) mode = "BaselineBound";
        else if (!mode.Equals("Auto", StringComparison.Ordinal)) throw new InvalidOperationException("target-mode must be create-only or baseline-bound.");
        PlcCodeChangeContract? changeContract = null;
        string? normalizedBaselineDirectory = null;
        string? normalizedBaselineFileName = null;
        if (mode == "BaselineBound")
        {
            if (string.IsNullOrWhiteSpace(baselineDirectory)) throw new InvalidOperationException("baseline-directory is required for target-mode=baseline-bound.");
            normalizedBaselineDirectory = Path.GetFullPath(baselineDirectory!);
            normalizedBaselineFileName = string.IsNullOrWhiteSpace(baselineFileName) ? validation.FileName : Path.GetFileNameWithoutExtension(baselineFileName!);
            changeContract = _formats.AnalyzeS7DclChange(validation.ImportDirectory, validation.FileName, normalizedBaselineDirectory, normalizedBaselineFileName, allowedChanges);
        }
        else if (!string.IsNullOrWhiteSpace(baselineDirectory) || !string.IsNullOrWhiteSpace(allowedChanges))
            throw new InvalidOperationException("baseline-directory and allowed-changes are valid only for target-mode=baseline-bound.");
        string group = PlcWorkflowPrimitives.NormalizeGroupPath(groupPath);
        _capabilityGate.RequireOperation(transaction, "UpsertBlockDocuments", "Block", validation.BlockName, validation.BlockType, group);
        var operations = transaction.Operations.ToList();
        operations.Add(new PlcCodeTransactionOperation
        {
            Sequence = operations.Count + 1,
            ImportDirectory = validation.ImportDirectory,
            FileName = validation.FileName,
            GroupPath = group,
            ImportOption = string.IsNullOrWhiteSpace(importOption) ? "Override" : importOption!,
            TargetMode = mode,
            AuthoringBaselineDirectory = normalizedBaselineDirectory,
            AuthoringBaselineFileName = normalizedBaselineFileName,
            ChangeContract = changeContract,
            InputHash = PlcWorkflowPrimitives.HashDocumentPair(validation.ImportDirectory, validation.FileName),
            Dependencies = validation.Dependencies,
            ExplicitDependsOn = PlcCodeTransactionIntegrity.ParseDependencySequences(dependsOn),
            Validation = validation
        });
        return PersistAdded(transaction, operations, $"Added SIMATIC SD operation for '{validation.BlockType}:{validation.BlockName}'.");
    }

    internal PlcCodeTransaction AddScl(string transactionId, string sourcePath, string? groupPath, string? typeGroupPath, string? dependsOn)
    {
        PlcCodeTransaction transaction = LoadDraft(transactionId);
        string path = Path.GetFullPath(sourcePath);
        PlcCodeArtifactValidationResult validation = _formats.ValidateScl(path);
        if (!validation.Valid) throw new InvalidDataException("SCL source is invalid: " + string.Join("; ", validation.Errors));
        foreach (PlcCodeDeclaredObject declared in validation.DeclaredObjects)
        {
            bool isType = declared.ObjectType.Equals("TYPE", StringComparison.OrdinalIgnoreCase);
            _capabilityGate.RequireOperation(transaction, "GenerateSclSource", isType ? "Type" : "Block", declared.Name, declared.ObjectType, isType ? PlcWorkflowPrimitives.NormalizeTypeGroupPath(typeGroupPath) : PlcWorkflowPrimitives.NormalizeGroupPath(groupPath));
        }
        var operations = transaction.Operations.ToList();
        operations.Add(new PlcCodeTransactionOperation
        {
            Sequence = operations.Count + 1,
            Kind = "GenerateSclSource",
            SourcePath = path,
            SourceName = Path.GetFileNameWithoutExtension(path),
            GroupPath = PlcWorkflowPrimitives.NormalizeGroupPath(groupPath),
            TypeGroupPath = PlcWorkflowPrimitives.NormalizeTypeGroupPath(typeGroupPath),
            InputHash = validation.InputHash,
            Dependencies = validation.Dependencies,
            ExplicitDependsOn = PlcCodeTransactionIntegrity.ParseDependencySequences(dependsOn),
            Validation = new PlcDocumentValidationResult { Valid = true, Classification = validation.Classification, Warnings = validation.Warnings }
        });
        return PersistAdded(transaction, operations, $"Added guarded SCL source generation for {validation.DeclaredObjects.Count} declared object(s).");
    }

    internal PlcCodeTransaction AddSimaticMl(string transactionId, string inputPath, string baselinePath, string? groupPath, bool allowTopologyChange, string? dependsOn, string? allowedChangePaths)
    {
        PlcCodeTransaction transaction = LoadDraft(transactionId);
        string candidate = Path.GetFullPath(inputPath);
        string baseline = Path.GetFullPath(baselinePath);
        if (string.IsNullOrWhiteSpace(allowedChangePaths)) throw new InvalidOperationException("allowed-change-paths is required for a SimaticML transaction. Validate the candidate first and authorize only the reported changedPaths.");
        PlcCodeArtifactValidationResult validation = _formats.ValidateSimaticMl(candidate, baseline, allowTopologyChange, allowedChangePaths);
        if (!validation.Valid || !string.Equals(validation.Classification, "verified", StringComparison.Ordinal))
            throw new InvalidDataException("SimaticML patch is not baseline-verified: " + string.Join("; ", validation.Errors.Concat(validation.Warnings)));
        if (validation.DeclaredObjects.Count != 1 || !new[] { "FC", "FB", "OB", "GlobalDB", "InstanceDB", "TYPE" }.Contains(validation.DeclaredObjects[0].ObjectType, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("SimaticML transactions require exactly one supported PLC block or PLC type identity.");
        bool isType = validation.DeclaredObjects[0].ObjectType.Equals("TYPE", StringComparison.OrdinalIgnoreCase);
        PlcCodeChangeContract changeContract = _formats.AnalyzeSimaticMlChange(candidate, baseline, allowTopologyChange, allowedChangePaths!);
        string group = isType ? PlcWorkflowPrimitives.NormalizeTypeGroupPath(groupPath) : PlcWorkflowPrimitives.NormalizeGroupPath(groupPath);
        _capabilityGate.RequireOperation(transaction, isType ? "PatchSimaticMlType" : "PatchSimaticMlBlock", isType ? "Type" : "Block", validation.DeclaredObjects[0].Name, validation.DeclaredObjects[0].ObjectType, group);
        var operations = transaction.Operations.ToList();
        operations.Add(new PlcCodeTransactionOperation
        {
            Sequence = operations.Count + 1,
            Kind = isType ? "PatchSimaticMlType" : "PatchSimaticMlBlock",
            SourcePath = candidate,
            BaselinePath = baseline,
            ValidationBaselineHash = _formats.ComputeSimaticMlContentHash(baseline),
            AllowTopologyChange = allowTopologyChange,
            AllowedChangePaths = changeContract.AllowedSections,
            ChangeContract = changeContract,
            GroupPath = group,
            InputHash = validation.InputHash,
            ExplicitDependsOn = PlcCodeTransactionIntegrity.ParseDependencySequences(dependsOn),
            Validation = new PlcDocumentValidationResult { Valid = true, Classification = validation.Classification, Warnings = validation.Warnings }
        });
        return PersistAdded(transaction, operations, $"Added baseline-bound SimaticML patch for '{validation.DeclaredObjects[0].ObjectType}:{validation.DeclaredObjects[0].Name}'.");
    }

    internal PlcCodeTransaction AddDeleteType(string transactionId, string typeName, string? groupPath, string? dependsOn)
    {
        PlcCodeTransaction transaction = LoadDraft(transactionId);
        if (string.IsNullOrWhiteSpace(typeName)) throw new InvalidOperationException("type-name is required.");
        string group = PlcWorkflowPrimitives.NormalizeTypeGroupPath(groupPath);
        _capabilityGate.RequireOperation(transaction, "DeletePlcType", "Type", typeName.Trim(), "TYPE", group);
        var operations = transaction.Operations.ToList();
        operations.Add(new PlcCodeTransactionOperation
        {
            Sequence = operations.Count + 1,
            Kind = "DeletePlcType",
            SourceName = typeName.Trim(),
            GroupPath = group,
            InputHash = PlcCodeTransactionIntegrity.TypeIdentityHash(group, typeName.Trim()),
            ExplicitDependsOn = PlcCodeTransactionIntegrity.ParseDependencySequences(dependsOn)
        });
        return PersistAdded(transaction, operations, $"Added exact PLC type deletion for '{PlcWorkflowPrimitives.DisplayGroup(group)}/TYPE:{typeName.Trim()}'.");
    }

    private PlcCodeTransaction LoadDraft(string id)
    {
        PlcCodeTransaction transaction = _store.Load(id);
        PlcCodeTransactionIntegrity.RequireState(transaction, "Draft");
        return transaction;
    }

    private PlcCodeTransaction PersistAdded(PlcCodeTransaction transaction, List<PlcCodeTransactionOperation> operations, string message)
    {
        transaction.Operations = operations;
        _store.AddJournal(transaction, operations.Count, "Authoring", "Added", message);
        _store.Save(transaction);
        return transaction;
    }

    private string SessionProjectName() => _sessionContext.Project == null ? string.Empty : TiaReflectionHelper.GetName(_sessionContext.Project) ?? string.Empty;
    private string SessionProjectPath() => _sessionContext.Project == null ? string.Empty : TiaReflectionHelper.GetProperty(_sessionContext.Project, "Path")?.ToString() ?? string.Empty;
    private async Task EnsurePlcOpen(string plcName) { if (!_plcService.IsSoftwareOpen || !string.Equals(_plcService.CurrentPlcName, plcName, StringComparison.OrdinalIgnoreCase)) await _plcService.OpenPlcAsync(plcName).ConfigureAwait(false); }
}
