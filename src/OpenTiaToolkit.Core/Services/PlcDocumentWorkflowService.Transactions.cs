using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using OpenTiaToolkit.Core.Models;

namespace OpenTiaToolkit.Core.Services;

public sealed partial class PlcDocumentWorkflowService
{
    private static readonly TimeSpan TransactionLifetime = TimeSpan.FromMinutes(30);
    private static readonly JsonSerializerOptions TransactionJsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public async Task<PlcCodeTransaction> BeginTransactionAsync(string plcName, string? description, string? compilePolicy = null, int? maximumNewWarnings = null, string? capabilityId = null, string? capabilityIds = null)
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
        if (_capabilityDiscovery != null && requestedCapabilities.Length == 0)
            throw new InvalidOperationException("At least one capability-id is required. Run discover-plc-code-capabilities for every intended transaction target.");
        var capabilities = new List<PlcCodeCapabilityDiscovery>();
        foreach (string requestedCapability in requestedCapabilities)
        {
            if (_capabilityDiscovery == null) throw new InvalidOperationException("Capability discovery is unavailable in this host.");
            PlcCodeCapabilityDiscovery capability = await _capabilityDiscovery.ValidateCurrentAsync(requestedCapability, plcName).ConfigureAwait(false);
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
        AddJournal(transaction, null, "Lifecycle", "Draft", "Transaction created and bound to the current TIA project and PLC.");
        SaveTransaction(transaction);
        return transaction;
    }

    public PlcCodeTransaction AddTransactionOperation(string transactionId, string importDirectory, string fileName, string? groupPath, string? importOption, string? dependsOn = null, string? targetMode = null, string? baselineDirectory = null, string? baselineFileName = null, string? allowedChanges = null)
    {
        PlcCodeTransaction transaction = LoadTransaction(transactionId);
        RequireState(transaction, "Draft");
        PlcDocumentValidationResult validation = Validate(importDirectory, fileName);
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
            changeContract = new PlcCodeFormatService(this).AnalyzeS7DclChange(validation.ImportDirectory, validation.FileName, normalizedBaselineDirectory, normalizedBaselineFileName, allowedChanges);
        }
        else if (!string.IsNullOrWhiteSpace(baselineDirectory) || !string.IsNullOrWhiteSpace(allowedChanges))
            throw new InvalidOperationException("baseline-directory and allowed-changes are valid only for target-mode=baseline-bound.");
        RequireCapabilityOperation(transaction, "UpsertBlockDocuments", "Block", validation.BlockName, validation.BlockType, NormalizeGroupPath(groupPath));
        var operations = transaction.Operations.ToList();
        operations.Add(new PlcCodeTransactionOperation
        {
            Sequence = operations.Count + 1,
            ImportDirectory = validation.ImportDirectory,
            FileName = validation.FileName,
            GroupPath = NormalizeGroupPath(groupPath),
            ImportOption = string.IsNullOrWhiteSpace(importOption) ? "Override" : importOption!,
            TargetMode = mode,
            AuthoringBaselineDirectory = normalizedBaselineDirectory,
            AuthoringBaselineFileName = normalizedBaselineFileName,
            ChangeContract = changeContract,
            InputHash = HashDocumentPair(validation.ImportDirectory, validation.FileName),
            Dependencies = validation.Dependencies,
            ExplicitDependsOn = ParseDependencySequences(dependsOn),
            Validation = validation
        });
        transaction.Operations = operations;
        AddJournal(transaction, operations.Count, "Authoring", "Added", $"Added SIMATIC SD operation for '{validation.BlockType}:{validation.BlockName}'.");
        SaveTransaction(transaction);
        return transaction;
    }

    public PlcCodeTransaction AddSclTransactionOperation(string transactionId, string sourcePath, string? groupPath, string? typeGroupPath = null, string? dependsOn = null)
    {
        PlcCodeTransaction transaction = LoadTransaction(transactionId);
        RequireState(transaction, "Draft");
        string path = Path.GetFullPath(sourcePath);
        var formatService = new PlcCodeFormatService(this);
        PlcCodeArtifactValidationResult validation = formatService.ValidateScl(path);
        if (!validation.Valid) throw new InvalidDataException("SCL source is invalid: " + string.Join("; ", validation.Errors));
        foreach (PlcCodeDeclaredObject declared in validation.DeclaredObjects)
        {
            bool isType = declared.ObjectType.Equals("TYPE", StringComparison.OrdinalIgnoreCase);
            RequireCapabilityOperation(transaction, "GenerateSclSource", isType ? "Type" : "Block", declared.Name, declared.ObjectType, isType ? NormalizeTypeGroupPath(typeGroupPath) : NormalizeGroupPath(groupPath));
        }
        var operations = transaction.Operations.ToList();
        operations.Add(new PlcCodeTransactionOperation
        {
            Sequence = operations.Count + 1,
            Kind = "GenerateSclSource",
            SourcePath = path,
            SourceName = Path.GetFileNameWithoutExtension(path),
            GroupPath = NormalizeGroupPath(groupPath),
            TypeGroupPath = NormalizeTypeGroupPath(typeGroupPath),
            InputHash = validation.InputHash,
            Dependencies = validation.Dependencies,
            ExplicitDependsOn = ParseDependencySequences(dependsOn),
            Validation = new PlcDocumentValidationResult { Valid = true, Classification = validation.Classification, Warnings = validation.Warnings }
        });
        transaction.Operations = operations;
        AddJournal(transaction, operations.Count, "Authoring", "Added", $"Added guarded SCL source generation for {validation.DeclaredObjects.Count} declared object(s).");
        SaveTransaction(transaction);
        return transaction;
    }

    public PlcCodeTransaction AddSimaticMlTransactionOperation(string transactionId, string inputPath, string baselinePath, string? groupPath, bool allowTopologyChange, string? dependsOn = null, string? allowedChangePaths = null)
    {
        PlcCodeTransaction transaction = LoadTransaction(transactionId);
        RequireState(transaction, "Draft");
        string candidate = Path.GetFullPath(inputPath);
        string baseline = Path.GetFullPath(baselinePath);
        var formatService = new PlcCodeFormatService(this);
        if (string.IsNullOrWhiteSpace(allowedChangePaths)) throw new InvalidOperationException("allowed-change-paths is required for a SimaticML transaction. Validate the candidate first and authorize only the reported changedPaths.");
        PlcCodeArtifactValidationResult validation = formatService.ValidateSimaticMl(candidate, baseline, allowTopologyChange, allowedChangePaths);
        if (!validation.Valid || !string.Equals(validation.Classification, "verified", StringComparison.Ordinal))
            throw new InvalidDataException("SimaticML patch is not baseline-verified: " + string.Join("; ", validation.Errors.Concat(validation.Warnings)));
        if (validation.DeclaredObjects.Count != 1 || !new[] { "FC", "FB", "OB", "GlobalDB", "InstanceDB", "TYPE" }.Contains(validation.DeclaredObjects[0].ObjectType, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("SimaticML transactions require exactly one supported PLC block or PLC type identity.");
        bool isType = validation.DeclaredObjects[0].ObjectType.Equals("TYPE", StringComparison.OrdinalIgnoreCase);
        PlcCodeChangeContract changeContract = formatService.AnalyzeSimaticMlChange(candidate, baseline, allowTopologyChange, allowedChangePaths!);
        RequireCapabilityOperation(transaction, isType ? "PatchSimaticMlType" : "PatchSimaticMlBlock", isType ? "Type" : "Block", validation.DeclaredObjects[0].Name, validation.DeclaredObjects[0].ObjectType, isType ? NormalizeTypeGroupPath(groupPath) : NormalizeGroupPath(groupPath));
        var operations = transaction.Operations.ToList();
        operations.Add(new PlcCodeTransactionOperation
        {
            Sequence = operations.Count + 1,
            Kind = isType ? "PatchSimaticMlType" : "PatchSimaticMlBlock",
            SourcePath = candidate,
            BaselinePath = baseline,
            ValidationBaselineHash = formatService.ComputeSimaticMlContentHash(baseline),
            AllowTopologyChange = allowTopologyChange,
            AllowedChangePaths = changeContract.AllowedSections,
            ChangeContract = changeContract,
            GroupPath = isType ? NormalizeTypeGroupPath(groupPath) : NormalizeGroupPath(groupPath),
            InputHash = validation.InputHash,
            ExplicitDependsOn = ParseDependencySequences(dependsOn),
            Validation = new PlcDocumentValidationResult { Valid = true, Classification = validation.Classification, Warnings = validation.Warnings }
        });
        transaction.Operations = operations;
        AddJournal(transaction, operations.Count, "Authoring", "Added", $"Added baseline-bound SimaticML patch for '{validation.DeclaredObjects[0].ObjectType}:{validation.DeclaredObjects[0].Name}'.");
        SaveTransaction(transaction);
        return transaction;
    }

    public PlcCodeTransaction AddDeleteTypeTransactionOperation(string transactionId, string typeName, string? groupPath, string? dependsOn = null)
    {
        PlcCodeTransaction transaction = LoadTransaction(transactionId);
        RequireState(transaction, "Draft");
        if (string.IsNullOrWhiteSpace(typeName)) throw new InvalidOperationException("type-name is required.");
        string group = NormalizeTypeGroupPath(groupPath);
        RequireCapabilityOperation(transaction, "DeletePlcType", "Type", typeName.Trim(), "TYPE", group);
        var operations = transaction.Operations.ToList();
        operations.Add(new PlcCodeTransactionOperation
        {
            Sequence = operations.Count + 1,
            Kind = "DeletePlcType",
            SourceName = typeName.Trim(),
            GroupPath = group,
            InputHash = TypeIdentityHash(group, typeName.Trim()),
            ExplicitDependsOn = ParseDependencySequences(dependsOn)
        });
        transaction.Operations = operations;
        AddJournal(transaction, operations.Count, "Authoring", "Added", $"Added exact PLC type deletion for '{DisplayGroup(group)}/TYPE:{typeName.Trim()}'.");
        SaveTransaction(transaction);
        return transaction;
    }

    public async Task<PlcCodeTransaction> PlanTransactionAsync(string transactionId)
    {
        PlcCodeTransaction transaction = LoadTransaction(transactionId);
        RequireState(transaction, "Draft");
        if (transaction.Operations.Count == 0) throw new InvalidOperationException("The transaction has no operations.");
        await EnsureTransactionContextAsync(transaction).ConfigureAwait(false);
        await VerifyCapabilityCurrentAsync(transaction).ConfigureAwait(false);

        string baselineRoot = TransactionBaselineRoot(transaction.TransactionId);
        Cleanup(baselineRoot);
        Directory.CreateDirectory(baselineRoot);
        var prepared = new List<PlcCodeTransactionOperation>();
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (PlcCodeTransactionOperation source in transaction.Operations.OrderBy(item => item.Sequence))
            {
                PlcCodeTransactionOperation operation;
                if (source.Kind == "GenerateSclSource") operation = await PrepareSclTransactionOperationAsync(transaction, source).ConfigureAwait(false);
                else if (source.Kind == "PatchSimaticMlBlock" || source.Kind == "PatchSimaticMlType") operation = await PrepareSimaticMlTransactionOperationAsync(transaction, source).ConfigureAwait(false);
                else if (source.Kind == "DeletePlcType") operation = PrepareDeleteTypeTransactionOperation(transaction, source);
                else
                {
                    PlcDocumentValidationResult validation = Validate(source.ImportDirectory, source.FileName);
                    if (!validation.Valid) throw new InvalidDataException($"Operation {source.Sequence} documents are invalid: {string.Join("; ", validation.Errors)}");
                    if (!FixedEquals(source.InputHash, HashDocumentPair(source.ImportDirectory, source.FileName)))
                        throw new InvalidOperationException($"Operation {source.Sequence} input changed after it was added. Recreate the transaction.");
                    operation = PrepareTransactionOperation(transaction, source, validation, baselineRoot);
                }
                foreach (PlcBlockIdentity target in OperationTargets(operation))
                {
                    string identityKey = target.ToString();
                    if (!identities.Add(identityKey)) throw new InvalidOperationException($"The transaction targets '{identityKey}' more than once. Combine the intended change into one operation.");
                }
                prepared.Add(operation);
            }
            AssignDependencyOrder(prepared);
        }
        catch
        {
            Cleanup(baselineRoot);
            throw;
        }

        transaction.Operations = prepared;
        PlcCodeImpactAnalysis impact;
        try { impact = await AnalyzeTransactionImpactAsync(transaction).ConfigureAwait(false); }
        catch
        {
            Cleanup(baselineRoot);
            throw;
        }

        CompilationResult baseline;
        try { baseline = await _plcService.CompileSoftwareAsync().ConfigureAwait(false); }
        catch
        {
            Cleanup(baselineRoot);
            throw;
        }
        transaction.BaselineCompilation = ToEvidence(baseline);
        transaction.ImpactAnalysis = impact;
        transaction.ImpactAcknowledged = false;
        transaction.ImpactAcknowledgementFingerprint = null;
        transaction.ExpiresAtUtc = DateTimeOffset.UtcNow.Add(TransactionLifetime);
        transaction.State = impact.Status == "Safe" ? "Planned" : impact.Status == "Blocked" ? "ImpactBlocked" : "ImpactReviewRequired";
        transaction.ApplyToken = impact.Status == "Safe" ? RandomToken() : null;
        transaction.RecoveryPackageRetained = true;
        transaction.Warnings = prepared.SelectMany(item => item.Validation?.Warnings ?? Array.Empty<string>()).Concat(impact.Warnings).Distinct(StringComparer.Ordinal).ToArray();
        transaction.PlanHash = ComputeTransactionPlanHash(transaction);
        int overrides = prepared.Sum(item => item.TargetSnapshots.Count > 0 ? item.TargetSnapshots.Count(target => target.Action == "Override") : item.Action == "Override" ? 1 : 0);
        int creates = prepared.Sum(item => item.TargetSnapshots.Count > 0 ? item.TargetSnapshots.Count(target => target.Action == "Create") : item.Action == "Create" ? 1 : 0);
        AddJournal(transaction, null, "Planning", "Planned", $"Snapshotted {overrides} existing target(s); {creates} target(s) will be created.");
        AddJournal(transaction, null, "Impact", transaction.State, $"Impact analysis classified the complete change as {impact.Status} with fingerprint {impact.Fingerprint}.");
        SaveTransaction(transaction);
        return transaction;
    }

    public async Task<PlcCodeTransactionApplyResult> ApplyTransactionAsync(string transactionId, string applyToken)
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
            await VerifyCapabilityCurrentAsync(transaction).ConfigureAwait(false);
            VerifyTransactionPreconditions(transaction);
            PlcCodeImpactAnalysis currentImpact = await AnalyzeTransactionImpactAsync(transaction).ConfigureAwait(false);
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
                    ResolvedType exactType = ResolveExactType(RequireTypeRoot(), operation.Target!, allowMissing: false)!;
                    if (!_v21Adapter.TryDelete(exactType.EngineeringObject) || ResolveExactType(RequireTypeRoot(), operation.Target!, allowMissing: true) != null)
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
            return await RollbackTransactionAsync(transaction, "Automatic rollback after apply or verification failure.").ConfigureAwait(false);
        }
    }

    public async Task<PlcCodeTransactionApplyResult> RecoverTransactionAsync(string transactionId)
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
        return await RollbackTransactionAsync(transaction, "Explicit recovery completed.").ConfigureAwait(false);
    }

    public PlcCodeTransaction GetTransaction(string transactionId) => LoadTransaction(transactionId);

    public IReadOnlyList<PlcCodeTransaction> ListTransactions()
    {
        if (!Directory.Exists(TransactionRoot)) return Array.Empty<PlcCodeTransaction>();
        var transactions = new List<PlcCodeTransaction>();
        foreach (string directory in Directory.GetDirectories(TransactionRoot))
        {
            string id = Path.GetFileName(directory);
            try { transactions.Add(LoadTransaction(id)); }
            catch { }
        }
        return transactions.OrderByDescending(item => item.UpdatedAtUtc).ToArray();
    }

    public bool DiscardTransaction(string transactionId)
    {
        PlcCodeTransaction transaction = LoadTransaction(transactionId, updateExpiry: false);
        if (new[] { "Applying", "Verifying", "DisposingProbe", "RollingBack", "RecoveryRequired" }.Contains(transaction.State, StringComparer.Ordinal))
            throw new InvalidOperationException("A transaction with unverified recovery state cannot be discarded. Recover it first.");
        string directory = TransactionDirectory(transaction.TransactionId);
        Cleanup(directory);
        return !Directory.Exists(directory);
    }

    private PlcCodeTransactionOperation PrepareTransactionOperation(PlcCodeTransaction transaction, PlcCodeTransactionOperation source, PlcDocumentValidationResult validation, string baselineRoot)
    {
        string normalizedGroup = NormalizeGroupPath(source.GroupPath);
        object root = RequireBlockRoot();
        if (ResolveGroup(root, normalizedGroup) == null) throw new InvalidOperationException($"PLC block group was not found: '{DisplayGroup(normalizedGroup)}'.");
        IReadOnlyList<ResolvedBlock> named = FindBlocks(root, validation.BlockName!);
        IReadOnlyList<ResolvedBlock> inGroup = named.Where(item => GroupEquals(item.Identity.GroupPath, normalizedGroup)).ToArray();
        if (inGroup.Count > 1) throw Ambiguous(validation.BlockName!, inGroup);
        if (inGroup.Count == 1 && !BlockTypeEquals(inGroup[0].Identity.BlockType, validation.BlockType!))
            throw new InvalidOperationException($"Target '{inGroup[0].Identity}' exists with type '{inGroup[0].Identity.BlockType}', but the document declares '{validation.BlockType}'.");
        if (string.IsNullOrEmpty(normalizedGroup) && inGroup.Count == 0 && named.Count > 0)
            throw new InvalidOperationException($"Block '{validation.BlockName}' exists outside the root group. Supply group-path explicitly. Candidates: {string.Join(", ", named.Select(item => item.Identity.ToString()))}");

        ResolvedBlock? existing = inGroup.SingleOrDefault();
        if (existing != null && source.TargetMode == "CreateOnly")
            throw new InvalidOperationException($"Target '{existing.Identity}' already exists, but the operation is create-only. Export it untouched and use target-mode=baseline-bound for modification.");
        if (existing == null && source.TargetMode == "BaselineBound")
            throw new InvalidOperationException($"Baseline-bound target '{validation.BlockType}:{validation.BlockName}' does not exist in '{DisplayGroup(normalizedGroup)}'. Use target-mode=create-only for creation.");
        string baselineHash = "missing";
        if (existing != null)
        {
            string backup = TransactionOperationBaseline(transaction.TransactionId, source.Sequence);
            Directory.CreateDirectory(backup);
            if (!ExportExact(existing, backup)) throw new InvalidOperationException($"Existing block '{existing.Identity}' could not be snapshotted; the transaction cannot start safely.");
            baselineHash = HashDocumentPair(backup, existing.Identity.Name);
            if (source.TargetMode == "BaselineBound")
            {
                if (source.ChangeContract == null || string.IsNullOrWhiteSpace(source.AuthoringBaselineDirectory) || string.IsNullOrWhiteSpace(source.AuthoringBaselineFileName))
                    throw new InvalidOperationException($"Operation {source.Sequence} has an incomplete baseline-bound change contract.");
                PlcCodeChangeContract currentContract = new PlcCodeFormatService(this).AnalyzeS7DclChange(validation.ImportDirectory, validation.FileName, source.AuthoringBaselineDirectory!, source.AuthoringBaselineFileName!, string.Join(",", source.ChangeContract.AllowedSections));
                if (!FixedEquals(source.ChangeContract.Fingerprint, currentContract.Fingerprint)) throw new InvalidOperationException($"Operation {source.Sequence} change contract changed after it was added.");
                if (!FixedEquals(currentContract.BaselineHash, baselineHash)) throw new InvalidOperationException($"The untouched S7DCL baseline does not match the current TIA export for '{existing.Identity}' (expected {currentContract.BaselineHash}, actual {baselineHash}). Re-export and recreate the transaction.");
            }
        }
        return new PlcCodeTransactionOperation
        {
            Sequence = source.Sequence,
            Kind = source.Kind,
            ImportDirectory = validation.ImportDirectory,
            FileName = validation.FileName,
            GroupPath = normalizedGroup,
            ImportOption = source.ImportOption,
            TargetMode = source.TargetMode,
            AuthoringBaselineDirectory = source.AuthoringBaselineDirectory,
            AuthoringBaselineFileName = source.AuthoringBaselineFileName,
            ChangeContract = source.ChangeContract,
            Action = existing == null ? "Create" : "Override",
            InputHash = HashDocumentPair(validation.ImportDirectory, validation.FileName),
            BaselineHash = baselineHash,
            Target = existing?.Identity ?? new PlcBlockIdentity { PlcName = transaction.PlcName, GroupPath = normalizedGroup, Name = validation.BlockName!, BlockType = validation.BlockType!, Number = validation.BlockNumber },
            Dependencies = source.Dependencies,
            ExplicitDependsOn = source.ExplicitDependsOn,
            Validation = validation,
            RollbackMethod = existing == null ? "DeleteExactTarget" : "RestoreDocumentAndVerifyHash",
            ApplyState = "Pending"
        };
    }

    private async Task<PlcCodeTransactionOperation> PrepareSclTransactionOperationAsync(PlcCodeTransaction transaction, PlcCodeTransactionOperation source)
    {
        var formatService = new PlcCodeFormatService(this);
        PlcCodeArtifactValidationResult validation = formatService.ValidateScl(source.SourcePath!);
        if (!validation.Valid) throw new InvalidDataException($"Operation {source.Sequence} SCL is invalid: {string.Join("; ", validation.Errors)}");
        if (!FixedEquals(source.InputHash, validation.InputHash)) throw new InvalidOperationException($"Operation {source.Sequence} SCL source changed after it was added.");
        IReadOnlyList<PlcSurfaceItem> sources = await _plcService.ListExternalSourcesAsync().ConfigureAwait(false);
        if (sources.Any(item => string.Equals(item.Name, source.SourceName, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException($"External source '{source.SourceName}' already exists and cannot be backed up through Openness. Use a unique source file name.");
        string group = NormalizeGroupPath(source.GroupPath);
        if (ResolveGroup(RequireBlockRoot(), group) == null) throw new InvalidOperationException($"PLC block group was not found: '{DisplayGroup(group)}'.");
        string typeGroup = NormalizeTypeGroupPath(source.TypeGroupPath);
        if (TiaReflectionHelper.GetTypeGroupByPath(RequireTypeRoot(), typeGroup) == null) throw new InvalidOperationException($"PLC type group was not found: '{DisplayGroup(typeGroup)}'.");
        var snapshots = new List<PlcCodeTransactionTargetSnapshot>();
        int targetIndex = 0;
        foreach (PlcCodeDeclaredObject declared in validation.DeclaredObjects)
        {
            targetIndex++;
            if (declared.ObjectType.Equals("TYPE", StringComparison.OrdinalIgnoreCase))
            {
                IReadOnlyList<ResolvedType> typeMatches = FindTypes(RequireTypeRoot(), declared.Name).Where(item => GroupEquals(item.Identity.GroupPath, typeGroup)).ToArray();
                if (typeMatches.Count > 1) throw new InvalidOperationException($"SCL PLC type target '{declared.Name}' is ambiguous in group '{DisplayGroup(typeGroup)}'.");
                ResolvedType? existingType = typeMatches.SingleOrDefault();
                string typeBaseline = "missing";
                if (existingType != null)
                {
                    string backupPath = TransactionSclTypeBaseline(transaction.TransactionId, source.Sequence, targetIndex);
                    if (!ExportExactType(existingType, backupPath)) throw new InvalidOperationException($"Existing SCL PLC type target '{existingType.Identity}' could not be snapshotted as SimaticML.");
                    typeBaseline = formatService.ComputeSimaticMlContentHash(backupPath);
                }
                snapshots.Add(new PlcCodeTransactionTargetSnapshot
                {
                    Target = existingType?.Identity ?? new PlcBlockIdentity { PlcName = transaction.PlcName, GroupPath = typeGroup, Name = declared.Name, BlockType = "TYPE" },
                    Action = existingType == null ? "Create" : "Override",
                    BaselineHash = typeBaseline,
                    RollbackMethod = existingType == null ? "DeleteExactType" : "RestoreTypeXmlAndVerifyHash"
                });
                continue;
            }
            IReadOnlyList<ResolvedBlock> matches = FindBlocks(RequireBlockRoot(), declared.Name).Where(item => GroupEquals(item.Identity.GroupPath, group)).ToArray();
            if (matches.Count > 1) throw Ambiguous(declared.Name, matches);
            if (matches.Count == 1 && !BlockTypeEquals(matches[0].Identity.BlockType, declared.ObjectType)) throw new InvalidOperationException($"SCL target '{declared.Name}' collides with type '{matches[0].Identity.BlockType}' instead of '{declared.ObjectType}'.");
            ResolvedBlock? existing = matches.SingleOrDefault();
            string baseline = "missing";
            if (existing != null)
            {
                string backup = TransactionSclTargetBaseline(transaction.TransactionId, source.Sequence, targetIndex);
                Directory.CreateDirectory(backup);
                if (!ExportExact(existing, backup)) throw new InvalidOperationException($"Existing SCL target '{existing.Identity}' could not be snapshotted as SIMATIC SD.");
                baseline = HashDocumentPair(backup, existing.Identity.Name);
            }
            snapshots.Add(new PlcCodeTransactionTargetSnapshot
            {
                Target = existing?.Identity ?? new PlcBlockIdentity { PlcName = transaction.PlcName, GroupPath = group, Name = declared.Name, BlockType = declared.ObjectType },
                Action = existing == null ? "Create" : "Override",
                BaselineHash = baseline,
                RollbackMethod = existing == null ? "DeleteExactTarget" : "RestoreDocumentAndVerifyHash"
            });
        }
        if (snapshots.Count == 0) throw new InvalidOperationException("SCL transaction must declare at least one generated PLC block.");
        source.TargetSnapshots = snapshots;
        source.TypeGroupPath = typeGroup;
        source.Dependencies = validation.Dependencies;
        source.Action = "Generate";
        source.Validation = new PlcDocumentValidationResult { Valid = true, Classification = validation.Classification, Warnings = validation.Warnings };
        return source;
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

    private async Task<PlcCodeTransactionOperation> PrepareSimaticMlTransactionOperationAsync(PlcCodeTransaction transaction, PlcCodeTransactionOperation source)
    {
        var formatService = new PlcCodeFormatService(this);
        PlcCodeArtifactValidationResult validation = formatService.ValidateSimaticMl(source.SourcePath!, source.BaselinePath, source.AllowTopologyChange, string.Join(",", source.AllowedChangePaths));
        if (!validation.Valid || !string.Equals(validation.Classification, "verified", StringComparison.Ordinal))
            throw new InvalidDataException($"Operation {source.Sequence} SimaticML patch is not baseline-verified: {string.Join("; ", validation.Errors.Concat(validation.Warnings))}");
        if (!FixedEquals(source.InputHash, validation.InputHash) || !FixedEquals(source.ValidationBaselineHash!, formatService.ComputeSimaticMlContentHash(source.BaselinePath!)))
            throw new InvalidOperationException($"Operation {source.Sequence} SimaticML candidate or validation baseline changed after it was added.");
        PlcCodeChangeContract currentChangeContract = formatService.AnalyzeSimaticMlChange(source.SourcePath!, source.BaselinePath!, source.AllowTopologyChange, string.Join(",", source.AllowedChangePaths));
        if (source.ChangeContract == null || !FixedEquals(source.ChangeContract.Fingerprint, currentChangeContract.Fingerprint)) throw new InvalidOperationException($"Operation {source.Sequence} SimaticML changed-path contract changed after it was added.");
        if (validation.DeclaredObjects.Count != 1) throw new InvalidOperationException($"Operation {source.Sequence} must contain exactly one PLC block identity.");
        PlcCodeDeclaredObject declared = validation.DeclaredObjects[0];
        if (declared.ObjectType.Equals("TYPE", StringComparison.OrdinalIgnoreCase))
        {
            string typeGroup = NormalizeTypeGroupPath(source.GroupPath);
            if (TiaReflectionHelper.GetTypeGroupByPath(RequireTypeRoot(), typeGroup) == null) throw new InvalidOperationException($"PLC type group was not found: '{DisplayGroup(typeGroup)}'.");
            IReadOnlyList<ResolvedType> allTypeMatches = FindTypes(RequireTypeRoot(), declared.Name);
            IReadOnlyList<ResolvedType> typeMatches = allTypeMatches.Where(item => GroupEquals(item.Identity.GroupPath, typeGroup)).ToArray();
            if (string.IsNullOrEmpty(typeGroup) && typeMatches.Count == 0 && allTypeMatches.Count > 0)
                throw new InvalidOperationException($"SimaticML PLC type '{declared.Name}' exists outside the root group. Supply group-path explicitly. Candidates: {string.Join(", ", allTypeMatches.Select(item => item.Identity.ToString()))}");
            if (typeMatches.Count != 1) throw new InvalidOperationException(typeMatches.Count == 0
                ? $"SimaticML PLC type patch target '{typeGroup}/TYPE:{declared.Name}' does not exist. XML fallback is patch-only."
                : $"SimaticML PLC type patch target '{declared.Name}' is ambiguous in group '{DisplayGroup(typeGroup)}'.");
            ResolvedType existingType = typeMatches[0];
            string typeBackup = TransactionOperationTypeBaseline(transaction.TransactionId, source.Sequence);
            if (!ExportExactType(existingType, typeBackup)) throw new InvalidOperationException($"Existing SimaticML PLC type target '{existingType.Identity}' could not be snapshotted for rollback.");
            string actualTypeHash = formatService.ComputeSimaticMlContentHash(typeBackup);
            if (!FixedEquals(source.ValidationBaselineHash!, actualTypeHash)) throw new InvalidOperationException($"The untouched SimaticML baseline does not match the current TIA export for '{existingType.Identity}' (expected {source.ValidationBaselineHash}, actual {actualTypeHash}). Re-export and replan.");
            return new PlcCodeTransactionOperation
            {
                Sequence = source.Sequence,
                Kind = "PatchSimaticMlType",
                SourcePath = source.SourcePath,
                BaselinePath = source.BaselinePath,
                ValidationBaselineHash = source.ValidationBaselineHash,
                AllowTopologyChange = source.AllowTopologyChange,
                AllowedChangePaths = source.AllowedChangePaths,
                ChangeContract = source.ChangeContract,
                GroupPath = typeGroup,
                Action = "Override",
                InputHash = source.InputHash,
                BaselineHash = actualTypeHash,
                Target = existingType.Identity,
                ExplicitDependsOn = source.ExplicitDependsOn,
                Dependencies = source.Dependencies,
                Validation = new PlcDocumentValidationResult { Valid = true, Classification = validation.Classification, Warnings = validation.Warnings },
                RollbackMethod = "RestoreTypeXmlAndVerifyHash",
                ApplyState = "Pending"
            };
        }
        string group = NormalizeGroupPath(source.GroupPath);
        object root = RequireBlockRoot();
        if (ResolveGroup(root, group) == null) throw new InvalidOperationException($"PLC block group was not found: '{DisplayGroup(group)}'.");
        IReadOnlyList<ResolvedBlock> matches = FindBlocks(root, declared.Name).Where(item => GroupEquals(item.Identity.GroupPath, group) && BlockTypeEquals(item.Identity.BlockType, declared.ObjectType)).ToArray();
        if (matches.Count != 1) throw new InvalidOperationException(matches.Count == 0
            ? $"SimaticML patch target '{group}/{declared.ObjectType}:{declared.Name}' does not exist. XML fallback is patch-only."
            : $"SimaticML patch target '{declared.Name}' is ambiguous in group '{DisplayGroup(group)}'.");
        ResolvedBlock existing = matches[0];
        string backup = TransactionOperationBaseline(transaction.TransactionId, source.Sequence);
        Directory.CreateDirectory(backup);
        if (!ExportExact(existing, backup)) throw new InvalidOperationException($"Existing SimaticML target '{existing.Identity}' could not be snapshotted as rollback documents.");
        string currentXml = Path.Combine(backup, "current.xml");
        if (!await _plcService.ExportBlockAsync(existing.Identity.Name, currentXml).ConfigureAwait(false)) throw new InvalidOperationException($"Existing SimaticML target '{existing.Identity}' could not be exported for baseline binding.");
        string actualXmlHash = formatService.ComputeSimaticMlContentHash(currentXml);
        if (!FixedEquals(source.ValidationBaselineHash!, actualXmlHash)) throw new InvalidOperationException($"The untouched SimaticML baseline does not match the current TIA export for '{existing.Identity}' (expected {source.ValidationBaselineHash}, actual {actualXmlHash}). Re-export and replan.");
        return new PlcCodeTransactionOperation
        {
            Sequence = source.Sequence,
            Kind = source.Kind,
            SourcePath = source.SourcePath,
            BaselinePath = source.BaselinePath,
            ValidationBaselineHash = source.ValidationBaselineHash,
            AllowTopologyChange = source.AllowTopologyChange,
            AllowedChangePaths = source.AllowedChangePaths,
            ChangeContract = source.ChangeContract,
            GroupPath = group,
            Action = "Override",
            InputHash = source.InputHash,
            BaselineHash = HashDocumentPair(backup, existing.Identity.Name),
            Target = existing.Identity,
            ExplicitDependsOn = source.ExplicitDependsOn,
            Dependencies = source.Dependencies,
            Validation = new PlcDocumentValidationResult { Valid = true, Classification = validation.Classification, Warnings = validation.Warnings },
            RollbackMethod = "RestoreDocumentAndVerifyHash",
            ApplyState = "Pending"
        };
    }

    private PlcCodeTransactionOperation PrepareDeleteTypeTransactionOperation(PlcCodeTransaction transaction, PlcCodeTransactionOperation source)
    {
        string group = NormalizeTypeGroupPath(source.GroupPath);
        if (!FixedEquals(source.InputHash, TypeIdentityHash(group, source.SourceName!))) throw new InvalidOperationException($"Operation {source.Sequence} PLC type deletion identity changed after it was added.");
        if (TiaReflectionHelper.GetTypeGroupByPath(RequireTypeRoot(), group) == null) throw new InvalidOperationException($"PLC type group was not found: '{DisplayGroup(group)}'.");
        IReadOnlyList<ResolvedType> allMatches = FindTypes(RequireTypeRoot(), source.SourceName!);
        IReadOnlyList<ResolvedType> matches = allMatches.Where(item => GroupEquals(item.Identity.GroupPath, group)).ToArray();
        if (string.IsNullOrEmpty(group) && matches.Count == 0 && allMatches.Count > 0)
            throw new InvalidOperationException($"PLC type '{source.SourceName}' exists outside the root group. Supply group-path explicitly. Candidates: {string.Join(", ", allMatches.Select(item => item.Identity.ToString()))}");
        if (matches.Count != 1) throw new InvalidOperationException(matches.Count == 0
            ? $"Exact PLC type deletion target '{DisplayGroup(group)}/TYPE:{source.SourceName}' does not exist."
            : $"PLC type deletion target '{source.SourceName}' is ambiguous in group '{DisplayGroup(group)}'.");
        ResolvedType existing = matches[0];
        string backup = TransactionOperationTypeBaseline(transaction.TransactionId, source.Sequence);
        if (!ExportExactType(existing, backup)) throw new InvalidOperationException($"PLC type deletion target '{existing.Identity}' could not be snapshotted for rollback.");
        return new PlcCodeTransactionOperation
        {
            Sequence = source.Sequence,
            Kind = source.Kind,
            SourceName = source.SourceName,
            GroupPath = group,
            Action = "Delete",
            InputHash = source.InputHash,
            BaselineHash = new PlcCodeFormatService(this).ComputeSimaticMlContentHash(backup),
            Target = existing.Identity,
            ExplicitDependsOn = source.ExplicitDependsOn,
            Validation = new PlcDocumentValidationResult { Valid = true, Classification = "verified" },
            RollbackMethod = "RestoreTypeXmlAndVerifyHash",
            ApplyState = "Pending"
        };
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
                if ((operation.Kind == "PatchSimaticMlBlock" || operation.Kind == "PatchSimaticMlType") && !FixedEquals(operation.ValidationBaselineHash!, new PlcCodeFormatService(this).ComputeSimaticMlContentHash(operation.BaselinePath!)))
                    throw new InvalidOperationException($"Operation {operation.Sequence} SimaticML validation baseline changed after planning.");
                if (operation.Kind == "PatchSimaticMlBlock" || operation.Kind == "PatchSimaticMlType")
                {
                    PlcCodeChangeContract currentXmlContract = new PlcCodeFormatService(this).AnalyzeSimaticMlChange(operation.SourcePath!, operation.BaselinePath!, operation.AllowTopologyChange, string.Join(",", operation.AllowedChangePaths));
                    if (operation.ChangeContract == null || !FixedEquals(operation.ChangeContract.Fingerprint, currentXmlContract.Fingerprint)) throw new InvalidOperationException($"Operation {operation.Sequence} SimaticML changed-path contract changed after planning.");
                }
                if (operation.TargetMode == "BaselineBound")
                {
                    if (operation.ChangeContract == null || string.IsNullOrWhiteSpace(operation.AuthoringBaselineDirectory) || string.IsNullOrWhiteSpace(operation.AuthoringBaselineFileName))
                        throw new InvalidOperationException($"Operation {operation.Sequence} baseline-bound change contract is incomplete.");
                    PlcCodeChangeContract currentContract = new PlcCodeFormatService(this).AnalyzeS7DclChange(operation.ImportDirectory, operation.FileName, operation.AuthoringBaselineDirectory!, operation.AuthoringBaselineFileName!, string.Join(",", operation.ChangeContract.AllowedSections));
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
                            actualType = new PlcCodeFormatService(this).ComputeSimaticMlContentHash(outputPath);
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
                    snapshot.CanonicalResultHash = new PlcCodeFormatService(this).ComputeSimaticMlContentHash(typeOutput);
                    if (operation.TargetSnapshots.Count == 0) operation.CanonicalResultHash = snapshot.CanonicalResultHash;
                    continue;
                }
                ResolvedBlock resolved = ResolveExact(RequireBlockRoot(), snapshot.Target, allowMissing: false)!;
                if (!IdentityMatches(snapshot.Target, resolved.Identity, requireNumber: !string.IsNullOrWhiteSpace(snapshot.Target.Number))) throw new InvalidOperationException($"Operation {operation.Sequence} canonical identity verification failed for '{snapshot.Target}'.");
                string output = Path.Combine(resultRoot, operation.Sequence + "-" + targetIndex);
                Directory.CreateDirectory(output);
                if (!ExportExact(resolved, output)) throw new InvalidOperationException($"Operation {operation.Sequence} canonical export failed for '{snapshot.Target}'.");
                snapshot.CanonicalResultHash = HashDocumentPair(output, resolved.Identity.Name);
                if (operation.TargetSnapshots.Count == 0) operation.CanonicalResultHash = snapshot.CanonicalResultHash;
            }
            operation.CanonicalResultHash = operation.TargetSnapshots.Count == 1 ? operation.TargetSnapshots[0].CanonicalResultHash : operation.TargetSnapshots.Count > 1 ? Hash(System.Text.Encoding.UTF8.GetBytes(string.Join("|", operation.TargetSnapshots.Select(item => item.CanonicalResultHash)))) : operation.CanonicalResultHash;
            operation.ApplyState = "Verified";
            AddJournal(transaction, operation.Sequence, "Verify", "Verified", $"Canonical result hash {operation.CanonicalResultHash}.");
        }
    }

    private async Task<PlcCodeTransactionApplyResult> RollbackTransactionAsync(PlcCodeTransaction transaction, string reason)
    {
        transaction.State = "RollingBack";
        AddJournal(transaction, null, "Rollback", "Starting", reason);
        SaveTransaction(transaction);
        bool allRestored = true;
        foreach (PlcCodeTransactionOperation operation in transaction.Operations.OrderByDescending(item => item.ExecutionOrder).ThenByDescending(item => item.Sequence))
        {
            try
            {
                bool operationRestored = true;
                int targetIndex = EffectiveSnapshots(operation).Count;
                foreach (PlcCodeTransactionTargetSnapshot snapshot in EffectiveSnapshots(operation).Reverse())
                {
                    if (IsTypeTarget(snapshot.Target))
                    {
                        string typeBackup = operation.Kind == "GenerateSclSource"
                            ? TransactionSclTypeBaseline(transaction.TransactionId, operation.Sequence, targetIndex)
                            : TransactionOperationTypeBaseline(transaction.TransactionId, operation.Sequence);
                        targetIndex--;
                        RollbackEvidence typeEvidence = await RollbackTypeAsync(snapshot, typeBackup).ConfigureAwait(false);
                        snapshot.RestorationVerified = typeEvidence.OperationSucceeded && typeEvidence.HashMatches;
                        snapshot.ActualRestoredHash = typeEvidence.ActualHash;
                        operationRestored &= snapshot.RestorationVerified;
                        continue;
                    }
                    string backup = operation.Kind == "GenerateSclSource" ? TransactionSclTargetBaseline(transaction.TransactionId, operation.Sequence, targetIndex) : TransactionOperationBaseline(transaction.TransactionId, operation.Sequence);
                    targetIndex--;
                    var plan = new PlcDocumentImportPlan { PlcName = transaction.PlcName, Target = snapshot.Target, FileName = snapshot.Target.Name, BaselineHash = snapshot.BaselineHash };
                    RollbackEvidence evidence = await RollbackAsync(plan, backup).ConfigureAwait(false);
                    snapshot.RestorationVerified = evidence.OperationSucceeded && evidence.HashMatches;
                    snapshot.ActualRestoredHash = evidence.ActualHash;
                    operationRestored &= snapshot.RestorationVerified;
                }
                if (operation.Kind == "GenerateSclSource")
                {
                    PlcSourceOperationResult deleted = await _plcService.DeleteExternalSourceAsync(operation.SourceName!, confirm: true).ConfigureAwait(false);
                    IReadOnlyList<PlcSurfaceItem> sources = await _plcService.ListExternalSourcesAsync().ConfigureAwait(false);
                    operationRestored &= (deleted.Success || !sources.Any(item => string.Equals(item.Name, operation.SourceName, StringComparison.OrdinalIgnoreCase))) && !sources.Any(item => string.Equals(item.Name, operation.SourceName, StringComparison.OrdinalIgnoreCase));
                }
                operation.RestorationVerified = operationRestored;
                operation.ActualRestoredHash = operationRestored ? "all-targets-restored" : "unverified";
                operation.ApplyState = operationRestored ? "RolledBack" : "RecoveryRequired";
                allRestored &= operationRestored;
                AddJournal(transaction, operation.Sequence, "Rollback", operation.ApplyState, operation.RestorationVerified
                    ? "Every generated target and the external source were restored or removed with proof."
                    : "One or more generated targets or the external source could not be proven restored.");
                SaveTransaction(transaction);
            }
            catch (Exception ex)
            {
                allRestored = false;
                operation.ApplyState = "RecoveryRequired";
                AddJournal(transaction, operation.Sequence, "Rollback", "Failed", Message(ex));
                SaveTransaction(transaction);
            }
        }

        bool compileRestored = false;
        try
        {
            CompilationResult rollbackCompilation = await _plcService.CompileSoftwareAsync().ConfigureAwait(false);
            transaction.RollbackCompilation = ToEvidence(rollbackCompilation);
            int baselineErrors = transaction.BaselineCompilation?.ErrorCount ?? 0;
            compileRestored = rollbackCompilation.ErrorCount <= baselineErrors && (baselineErrors > 0 || rollbackCompilation.Success);
        }
        catch (Exception ex)
        {
            AddJournal(transaction, null, "RollbackCompile", "Failed", Message(ex));
        }
        bool proven = allRestored && compileRestored;
        transaction.State = proven ? "RolledBackAndVerified" : "RecoveryRequired";
        transaction.RecoveryPackageRetained = !proven;
        AddJournal(transaction, null, "Rollback", transaction.State, proven
            ? "All original hashes or missing targets were restored and the baseline compile condition was recovered."
            : "Rollback remains unverified. The baseline package is retained for explicit recovery or manual inspection.");
        SaveTransaction(transaction);
        if (proven) Cleanup(TransactionBaselineRoot(transaction.TransactionId));
        return Result(transaction, applied: false, verified: false, rollbackAttempted: true, rolledBack: proven, recoveryRequired: !proven,
            proven ? "Transaction failed and was rolled back with full restoration proof." : "Transaction failed and automatic rollback could not be fully proven; recovery is required.");
    }

    private async Task<RollbackEvidence> RollbackTypeAsync(PlcCodeTransactionTargetSnapshot snapshot, string backupPath)
    {
        if (string.Equals(snapshot.BaselineHash, "missing", StringComparison.Ordinal))
        {
            ResolvedType? created = ResolveExactType(RequireTypeRoot(), snapshot.Target, allowMissing: true);
            bool deleted = created == null || _v21Adapter.TryDelete(created.EngineeringObject);
            bool absent = ResolveExactType(RequireTypeRoot(), snapshot.Target, allowMissing: true) == null;
            return new RollbackEvidence(deleted && absent, deleted && absent, "missing", absent ? "missing" : "present");
        }
        bool imported = await _plcService.ImportTypeAsync(backupPath, snapshot.Target.GroupPath).ConfigureAwait(false);
        ResolvedType? restored = imported ? ResolveExactType(RequireTypeRoot(), snapshot.Target, allowMissing: true) : null;
        string? actual = null;
        if (restored != null)
        {
            string verifyPath = Path.Combine(RollbackRoot, Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                if (ExportExactType(restored, verifyPath)) actual = new PlcCodeFormatService(this).ComputeSimaticMlContentHash(verifyPath);
            }
            finally { if (File.Exists(verifyPath)) File.Delete(verifyPath); }
        }
        bool matches = actual != null && FixedEquals(snapshot.BaselineHash, actual);
        return new RollbackEvidence(imported && restored != null, matches, snapshot.BaselineHash, actual);
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

    private static void RequireState(PlcCodeTransaction transaction, string state)
    {
        if (!string.Equals(transaction.State, state, StringComparison.Ordinal)) throw new InvalidOperationException($"Transaction '{transaction.TransactionId}' must be in state '{state}', but is '{transaction.State}'.");
    }

    private static void AssignDependencyOrder(IReadOnlyList<PlcCodeTransactionOperation> operations)
    {
        var bySequence = operations.ToDictionary(item => item.Sequence);
        var producersByName = operations
            .SelectMany(operation => OperationTargets(operation).Select(target => new { target.Name, operation.Sequence }))
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Sequence).Distinct().OrderBy(value => value).ToArray(), StringComparer.OrdinalIgnoreCase);
        var dependencies = new Dictionary<int, HashSet<int>>();
        foreach (PlcCodeTransactionOperation operation in operations)
        {
            var resolved = new HashSet<int>();
            foreach (int sequence in operation.ExplicitDependsOn)
            {
                if (sequence == operation.Sequence) throw new InvalidOperationException($"Operation {operation.Sequence} cannot depend on itself.");
                if (!bySequence.ContainsKey(sequence)) throw new InvalidOperationException($"Operation {operation.Sequence} explicitly depends on missing operation {sequence}.");
                resolved.Add(sequence);
            }
            var unresolved = new List<string>();
            foreach (string dependency in operation.Dependencies.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!producersByName.TryGetValue(dependency, out int[]? producers)) { unresolved.Add(dependency); continue; }
                int[] otherProducers = producers.Where(sequence => sequence != operation.Sequence).ToArray();
                if (otherProducers.Length > 1) throw new InvalidOperationException($"Operation {operation.Sequence} dependency '{dependency}' is produced by multiple transaction operations ({string.Join(", ", otherProducers)}). Use depends-on with exact operation sequences.");
                if (otherProducers.Length == 1) resolved.Add(otherProducers[0]);
            }
            operation.DependsOn = resolved.OrderBy(value => value).ToArray();
            if (unresolved.Count > 0 && operation.Validation != null)
                operation.Validation.Warnings = operation.Validation.Warnings.Concat(new[] { $"External dependencies not produced by this transaction require bound-project resolution: {string.Join(", ", unresolved.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))}." }).ToArray();
            dependencies[operation.Sequence] = resolved;
        }

        var ready = new SortedSet<int>(dependencies.Where(pair => pair.Value.Count == 0).Select(pair => pair.Key));
        int order = 0;
        while (ready.Count > 0)
        {
            int sequence = ready.Min;
            ready.Remove(sequence);
            bySequence[sequence].ExecutionOrder = ++order;
            foreach (KeyValuePair<int, HashSet<int>> pair in dependencies)
                if (pair.Value.Remove(sequence) && pair.Value.Count == 0 && bySequence[pair.Key].ExecutionOrder == 0) ready.Add(pair.Key);
        }
        int[] cyclic = bySequence.Values.Where(item => item.ExecutionOrder == 0).Select(item => item.Sequence).OrderBy(value => value).ToArray();
        if (cyclic.Length > 0) throw new InvalidOperationException($"PLC code transaction dependency cycle detected among operations {string.Join(", ", cyclic)}. Combine mutually dependent blocks in one SCL source or remove the cycle.");
    }

    private static IReadOnlyList<int> ParseDependencySequences(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Array.Empty<int>();
        var sequences = new List<int>();
        foreach (string token in value!.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(token, out int sequence) || sequence < 1) throw new InvalidOperationException($"depends-on contains invalid operation sequence '{token}'. Use positive comma-separated integers.");
            if (!sequences.Contains(sequence)) sequences.Add(sequence);
        }
        return sequences.OrderBy(sequence => sequence).ToArray();
    }

    private string SessionProjectName() => _sessionContext.Project == null ? string.Empty : TiaReflectionHelper.GetName(_sessionContext.Project) ?? string.Empty;
    private string SessionProjectPath() => _sessionContext.Project == null ? string.Empty : TiaReflectionHelper.GetProperty(_sessionContext.Project, "Path")?.ToString() ?? string.Empty;
    private static string TransactionRoot => Path.Combine(Path.GetTempPath(), "OpenTiaToolkit", "plc-code-transactions");
    private static string TransactionDirectory(string id) => Path.Combine(TransactionRoot, ValidateTransactionId(id));
    private static string TransactionManifest(string id) => Path.Combine(TransactionDirectory(id), "manifest.json");
    private static string TransactionJournal(string id) => Path.Combine(TransactionDirectory(id), "journal.jsonl");
    private static string TransactionBaselineRoot(string id) => Path.Combine(TransactionDirectory(id), "baseline");
    private static string TransactionOperationBaseline(string id, int sequence) => Path.Combine(TransactionBaselineRoot(id), sequence.ToString());

    private static string ValidateTransactionId(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidOperationException("Transaction id is invalid.");
        return id;
    }

    private static PlcCodeTransaction LoadTransaction(string id, bool updateExpiry = true)
    {
        string manifest = TransactionManifest(id);
        if (!File.Exists(manifest)) throw new InvalidOperationException($"PLC code transaction '{id}' was not found.");
        PlcCodeTransaction transaction = JsonSerializer.Deserialize<PlcCodeTransaction>(File.ReadAllText(manifest), TransactionJsonOptions)
            ?? throw new InvalidDataException($"PLC code transaction '{id}' manifest is invalid.");
        if (updateExpiry && new[] { "Planned", "ImpactReviewRequired" }.Contains(transaction.State, StringComparer.Ordinal) && transaction.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            transaction.State = "Expired";
            transaction.ApplyToken = null;
            AddJournal(transaction, null, "Lifecycle", "Expired", "Transaction expired without mutation.");
            SaveTransaction(transaction);
        }
        return transaction;
    }

    private static void SaveTransaction(PlcCodeTransaction transaction)
    {
        transaction.UpdatedAtUtc = DateTimeOffset.UtcNow;
        string directory = TransactionDirectory(transaction.TransactionId);
        Directory.CreateDirectory(directory);
        string manifest = Path.Combine(directory, "manifest.json");
        string temporary = Path.Combine(directory, "manifest.tmp");
        File.WriteAllText(temporary, JsonSerializer.Serialize(transaction, TransactionJsonOptions));
        if (File.Exists(manifest)) File.Replace(temporary, manifest, null);
        else File.Move(temporary, manifest);
        string recovery = Path.Combine(directory, "recovery.json");
        if (transaction.State == "RecoveryRequired")
        {
            File.WriteAllText(recovery, JsonSerializer.Serialize(new
            {
                transaction.TransactionId,
                transaction.State,
                transaction.Failure,
                transaction.ProjectName,
                transaction.PlcName,
                transaction.Operations
            }, TransactionJsonOptions));
        }
        else if (File.Exists(recovery)) File.Delete(recovery);
    }

    private static void AddJournal(PlcCodeTransaction transaction, int? operation, string phase, string state, string message)
    {
        var journal = transaction.Journal.ToList();
        var entry = new PlcCodeTransactionJournalEntry { TimestampUtc = DateTimeOffset.UtcNow, OperationSequence = operation, Phase = phase, State = state, Message = message };
        journal.Add(entry);
        transaction.Journal = journal;
        string directory = TransactionDirectory(transaction.TransactionId);
        Directory.CreateDirectory(directory);
        File.AppendAllText(TransactionJournal(transaction.TransactionId), JsonSerializer.Serialize(entry, TransactionJsonOptions) + Environment.NewLine);
    }

    private static string ComputeTransactionPlanHash(PlcCodeTransaction transaction)
    {
        string canonical = JsonSerializer.Serialize(new
        {
            transaction.SchemaVersion,
            transaction.TransactionId,
            transaction.TiaProcessId,
            transaction.ProjectName,
            transaction.ProjectPath,
            transaction.PlcName,
            transaction.CapabilityId,
            transaction.CapabilityFingerprint,
            transaction.CapabilityRoute,
            transaction.CapabilityBindings,
            transaction.CompilePolicy,
            transaction.MaximumNewWarnings,
            transaction.ImpactAnalysis,
            transaction.ImpactAcknowledged,
            transaction.ImpactAcknowledgementFingerprint,
            Operations = transaction.Operations.OrderBy(item => item.Sequence).Select(item => new
            {
                item.Sequence,
                item.Kind,
                item.SourcePath,
                item.SourceName,
                item.TypeGroupPath,
                item.BaselinePath,
                item.ValidationBaselineHash,
                item.AllowTopologyChange,
                item.AllowedChangePaths,
                item.TargetMode,
                item.AuthoringBaselineDirectory,
                item.AuthoringBaselineFileName,
                item.ChangeContract,
                item.ImportDirectory,
                item.FileName,
                item.GroupPath,
                item.ImportOption,
                item.Action,
                item.InputHash,
                item.BaselineHash,
                item.Dependencies,
                item.ExplicitDependsOn,
                item.DependsOn,
                item.ExecutionOrder,
                item.RollbackMethod,
                item.Target,
                TargetSnapshots = item.TargetSnapshots.Select(target => new { target.Target, target.Action, target.BaselineHash, target.RollbackMethod }).ToArray()
            }).ToArray()
        });
        return Hash(System.Text.Encoding.UTF8.GetBytes(canonical));
    }

    private void RequireCapabilityOperation(PlcCodeTransaction transaction, string operationKind, string objectKind, string? objectName, string? objectType, string groupPath)
    {
        if (transaction.CapabilityBindings.Count == 0 && string.IsNullOrWhiteSpace(transaction.CapabilityId)) return;
        if (_capabilityDiscovery == null) throw new InvalidOperationException("Capability discovery is unavailable in this host.");
        PlcCodeCapabilityBinding? binding = transaction.CapabilityBindings.FirstOrDefault(item =>
            item.ObjectKind.Equals(objectKind, StringComparison.OrdinalIgnoreCase) &&
            item.ObjectName.Equals(objectName ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
            item.GroupPath.Trim().Trim('/').Equals((groupPath ?? string.Empty).Trim().Trim('/'), StringComparison.OrdinalIgnoreCase));
        PlcCodeCapabilityDiscovery capability = binding != null ? _capabilityDiscovery.Get(binding.CapabilityId) : _capabilityDiscovery.Get(transaction.CapabilityId!);
        _capabilityDiscovery.RequireOperationRoute(capability, operationKind);
        if (!capability.Request.ObjectKind.Equals(objectKind, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Capability discovery is bound to object kind '{capability.Request.ObjectKind}', but the operation targets '{objectKind}'.");
        if (!capability.Request.ObjectName.Equals(objectName ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Capability discovery is bound to '{capability.Request.ObjectName}', but the operation targets '{objectName}'.");
        if (!string.IsNullOrWhiteSpace(capability.Request.BlockType) && !BlockTypesCompatible(capability.Request.BlockType!, objectType ?? string.Empty))
            throw new InvalidOperationException($"Capability discovery is bound to object type '{capability.Request.BlockType}', but the operation declares '{objectType}'.");
        string expectedGroup = (capability.Request.GroupPath ?? string.Empty).Trim().Trim('/');
        string actualGroup = (groupPath ?? string.Empty).Trim().Trim('/');
        if (!expectedGroup.Equals(actualGroup, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Capability discovery is bound to group '{expectedGroup}', but the operation targets '{actualGroup}'.");
    }

    private static bool BlockTypesCompatible(string expected, string actual)
    {
        if (expected.Equals(actual, StringComparison.OrdinalIgnoreCase)) return true;
        return expected.Equals("DB", StringComparison.OrdinalIgnoreCase) && (actual.Equals("GlobalDB", StringComparison.OrdinalIgnoreCase) || actual.Equals("InstanceDB", StringComparison.OrdinalIgnoreCase));
    }

    private async Task VerifyCapabilityCurrentAsync(PlcCodeTransaction transaction)
    {
        if (transaction.CapabilityBindings.Count == 0 && string.IsNullOrWhiteSpace(transaction.CapabilityId)) return;
        if (_capabilityDiscovery == null) throw new InvalidOperationException("Capability discovery is unavailable in this host.");
        if (transaction.CapabilityBindings.Count > 0)
        {
            foreach (PlcCodeCapabilityBinding binding in transaction.CapabilityBindings)
            {
                PlcCodeCapabilityDiscovery current = await _capabilityDiscovery.ValidateCurrentAsync(binding.CapabilityId, transaction.PlcName).ConfigureAwait(false);
                if (!FixedEquals(binding.Fingerprint, current.Fingerprint) || !string.Equals(binding.Route, current.SelectedRoute, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Transaction capability binding '{binding.CapabilityId}' does not match the current discovery manifest.");
            }
            return;
        }
        PlcCodeCapabilityDiscovery legacy = await _capabilityDiscovery.ValidateCurrentAsync(transaction.CapabilityId!, transaction.PlcName).ConfigureAwait(false);
        if (!FixedEquals(transaction.CapabilityFingerprint ?? string.Empty, legacy.Fingerprint) || !string.Equals(transaction.CapabilityRoute, legacy.SelectedRoute, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The transaction capability binding does not match the current discovery manifest.");
    }

    private static string Message(Exception exception) => exception.InnerException?.Message ?? exception.Message;
    private static IReadOnlyList<PlcBlockIdentity> OperationTargets(PlcCodeTransactionOperation operation) => operation.TargetSnapshots.Count > 0 ? operation.TargetSnapshots.Select(item => item.Target).ToArray() : operation.Target == null ? Array.Empty<PlcBlockIdentity>() : new[] { operation.Target };
    private static IReadOnlyList<PlcCodeTransactionTargetSnapshot> EffectiveSnapshots(PlcCodeTransactionOperation operation) => operation.TargetSnapshots.Count > 0 ? operation.TargetSnapshots : operation.Target == null ? Array.Empty<PlcCodeTransactionTargetSnapshot>() : new[] { new PlcCodeTransactionTargetSnapshot { Target = operation.Target, Action = operation.Action, BaselineHash = operation.BaselineHash, RollbackMethod = operation.RollbackMethod, CanonicalResultHash = operation.CanonicalResultHash, RestorationVerified = operation.RestorationVerified, ActualRestoredHash = operation.ActualRestoredHash } };
    private static string TransactionSclTargetBaseline(string id, int sequence, int targetIndex) => Path.Combine(TransactionOperationBaseline(id, sequence), targetIndex.ToString());
    private static string TransactionSclTypeBaseline(string id, int sequence, int targetIndex) => Path.Combine(TransactionOperationBaseline(id, sequence), targetIndex.ToString(), "type.xml");
    private static string TransactionOperationTypeBaseline(string id, int sequence) => Path.Combine(TransactionOperationBaseline(id, sequence), "type.xml");
    private static bool IsTypeTarget(PlcBlockIdentity target) => target.BlockType.Equals("TYPE", StringComparison.OrdinalIgnoreCase);
    private static string HashFile(string path) => Hash(File.ReadAllBytes(path));
    private static string TypeIdentityHash(string groupPath, string typeName) => Hash(System.Text.Encoding.UTF8.GetBytes("TYPE|" + NormalizeTypeGroupPath(groupPath) + "|" + typeName.Trim()));
}
