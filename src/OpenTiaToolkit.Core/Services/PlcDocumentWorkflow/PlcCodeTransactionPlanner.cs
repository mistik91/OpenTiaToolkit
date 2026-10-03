using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Models;
using ResolvedBlock = OpenTiaToolkit.Core.Services.PlcCodeTargetRepository.ResolvedBlock;
using ResolvedType = OpenTiaToolkit.Core.Services.PlcCodeTargetRepository.ResolvedType;

namespace OpenTiaToolkit.Core.Services;

internal sealed class PlcCodeTransactionPlanner
{
    private static readonly TimeSpan TransactionLifetime = TimeSpan.FromMinutes(30);
    private readonly IPlcService _plcService;
    private readonly EngineeringSessionContext _sessionContext;
    private readonly PlcDocumentValidator _validator;
    private readonly PlcCodeFormatService _formats;
    private readonly PlcCodeTargetRepository _targets;
    private readonly PlcCodeTransactionStore _store;
    private readonly PlcCodeCapabilityGate _capabilityGate;
    private readonly PlcCodePromotionGate _promotionGate;
    private readonly PlcCodeImpactAnalyzer _impactAnalyzer;

    internal PlcCodeTransactionPlanner(IPlcService plcService, EngineeringSessionContext sessionContext, PlcDocumentValidator validator, PlcCodeFormatService formats, PlcCodeTargetRepository targets, PlcCodeTransactionStore store, PlcCodeCapabilityGate capabilityGate, PlcCodePromotionGate promotionGate, PlcCodeImpactAnalyzer impactAnalyzer)
    {
        _plcService = plcService;
        _sessionContext = sessionContext;
        _validator = validator;
        _formats = formats;
        _targets = targets;
        _store = store;
        _capabilityGate = capabilityGate;
        _promotionGate = promotionGate;
        _impactAnalyzer = impactAnalyzer;
    }

    internal async Task<PlcCodeTransaction> PlanTransactionAsync(string transactionId)
    {
        PlcCodeTransaction transaction = LoadTransaction(transactionId);
        RequireState(transaction, "Draft");
        if (transaction.Operations.Count == 0) throw new InvalidOperationException("The transaction has no operations.");
        await EnsureTransactionContextAsync(transaction).ConfigureAwait(false);
        await _capabilityGate.VerifyCurrentAsync(transaction).ConfigureAwait(false);
        await _promotionGate.VerifyAsync(transaction).ConfigureAwait(false);

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
                    PlcDocumentValidationResult validation = _validator.Validate(source.ImportDirectory, source.FileName);
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
            PlcCodeTransactionIntegrity.AssignDependencyOrder(prepared);
        }
        catch
        {
            Cleanup(baselineRoot);
            throw;
        }

        transaction.Operations = prepared;
        PlcCodeImpactAnalysis impact;
        try { impact = await RequireImpactAnalyzer().AnalyzeAsync(transaction).ConfigureAwait(false); }
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
        transaction.BaselineCompilation = PlcWorkflowPrimitives.CompileEvidence(baseline);
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

    private PlcCodeTransactionOperation PrepareTransactionOperation(PlcCodeTransaction transaction, PlcCodeTransactionOperation source, PlcDocumentValidationResult validation, string baselineRoot)
    {
        string normalizedGroup = PlcWorkflowPrimitives.NormalizeGroupPath(source.GroupPath, transaction.PlcName);
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
                PlcCodeChangeContract currentContract = _formats.AnalyzeS7DclChange(validation.ImportDirectory, validation.FileName, source.AuthoringBaselineDirectory!, source.AuthoringBaselineFileName!, string.Join(",", source.ChangeContract.AllowedSections));
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
        PlcCodeFormatService formatService = _formats;
        PlcCodeArtifactValidationResult validation = formatService.ValidateScl(source.SourcePath!);
        if (!validation.Valid) throw new InvalidDataException($"Operation {source.Sequence} SCL is invalid: {string.Join("; ", validation.Errors)}");
        if (!FixedEquals(source.InputHash, validation.InputHash)) throw new InvalidOperationException($"Operation {source.Sequence} SCL source changed after it was added.");
        IReadOnlyList<PlcSurfaceItem> sources = await _plcService.ListExternalSourcesAsync().ConfigureAwait(false);
        if (sources.Any(item => string.Equals(item.Name, source.SourceName, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException($"External source '{source.SourceName}' already exists and cannot be backed up through Openness. Use a unique source file name.");
        string group = PlcWorkflowPrimitives.NormalizeGroupPath(source.GroupPath, transaction.PlcName);
        if (ResolveGroup(RequireBlockRoot(), group) == null) throw new InvalidOperationException($"PLC block group was not found: '{DisplayGroup(group)}'.");
        string typeGroup = PlcWorkflowPrimitives.NormalizeTypeGroupPath(source.TypeGroupPath, transaction.PlcName);
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
                if (existing.Identity.BlockType.Equals("InstanceDB", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Existing native SCL instance DB '{existing.Identity}' cannot be replaced: lossless instance DB rollback is not supported. Use a new uniquely named instance DB.");
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

    private async Task<PlcCodeTransactionOperation> PrepareSimaticMlTransactionOperationAsync(PlcCodeTransaction transaction, PlcCodeTransactionOperation source)
    {
        PlcCodeFormatService formatService = _formats;
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
            string typeGroup = PlcWorkflowPrimitives.NormalizeTypeGroupPath(source.GroupPath, transaction.PlcName);
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
        string group = PlcWorkflowPrimitives.NormalizeGroupPath(source.GroupPath, transaction.PlcName);
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
        string group = PlcWorkflowPrimitives.NormalizeTypeGroupPath(source.GroupPath, transaction.PlcName);
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
            BaselineHash = _formats.ComputeSimaticMlContentHash(backup),
            Target = existing.Identity,
            ExplicitDependsOn = source.ExplicitDependsOn,
            Validation = new PlcDocumentValidationResult { Valid = true, Classification = "verified" },
            RollbackMethod = "RestoreTypeXmlAndVerifyHash",
            ApplyState = "Pending"
        };
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
