using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Models;
using ResolvedBlock = OpenTiaToolkit.Core.Services.PlcCodeTargetRepository.ResolvedBlock;

namespace OpenTiaToolkit.Core.Services;

internal sealed class PlcDocumentImportWorkflow
{
    private readonly IPlcService _plc;
    private readonly PlcDocumentValidator _validator;
    private readonly PlcDocumentPlanStore _plans;
    private readonly PlcCodeTargetRepository _targets;

    internal PlcDocumentImportWorkflow(IPlcService plc, PlcDocumentValidator validator, PlcDocumentPlanStore plans, PlcCodeTargetRepository targets)
    {
        _plc = plc; _validator = validator; _plans = plans; _targets = targets;
    }

    internal PlcDocumentValidationResult Validate(string importDirectory, string fileName) => _validator.Validate(importDirectory, fileName);

    internal async Task<PlcDocumentImportPlan> PlanAsync(string plcName, string importDirectory, string fileName, string? groupPath, string importOption)
    {
        PlcDocumentValidationResult validation = Validate(importDirectory, fileName);
        if (!validation.Valid) throw new InvalidDataException("PLC documents are invalid: " + string.Join("; ", validation.Errors));
        await EnsurePlcOpen(plcName).ConfigureAwait(false);
        string group = PlcWorkflowPrimitives.NormalizeGroupPath(groupPath);
        if (_targets.ResolveBlockGroup(group) == null) throw new InvalidOperationException($"PLC block group was not found: '{PlcWorkflowPrimitives.DisplayGroup(group)}'.");
        IReadOnlyList<ResolvedBlock> named = _targets.FindBlocks(validation.BlockName!);
        IReadOnlyList<ResolvedBlock> inGroup = named.Where(item => PlcWorkflowPrimitives.GroupEquals(item.Identity.GroupPath, group)).ToArray();
        if (inGroup.Count > 1) throw _targets.Ambiguous(validation.BlockName!, inGroup);
        if (inGroup.Count == 1 && !_targets.BlockTypeEquals(inGroup[0].Identity.BlockType, validation.BlockType!)) throw new InvalidOperationException($"Target '{inGroup[0].Identity}' exists with type '{inGroup[0].Identity.BlockType}', but the document declares '{validation.BlockType}'.");
        if (string.IsNullOrEmpty(group) && inGroup.Count == 0 && named.Count > 0) throw new InvalidOperationException($"Block '{validation.BlockName}' exists outside the root group. Supply group-path explicitly. Candidates: {string.Join(", ", named.Select(item => item.Identity.ToString()))}");
        ResolvedBlock? existing = inGroup.SingleOrDefault();
        string planId = Guid.NewGuid().ToString("N"); string backup = Path.Combine(_plans.PlanRoot, planId); string baselineHash = "missing";
        var warnings = new List<string>(validation.Warnings);
        if (validation.Classification == "unknown-requires-live-probe") warnings.Add("The document contains unknown instructions or calls. Apply remains guarded and must pass Siemens import plus compile verification.");
        if (existing != null)
        {
            Directory.CreateDirectory(backup);
            if (!_targets.ExportExact(existing, backup)) throw new InvalidOperationException($"Existing block '{existing.Identity}' could not be exported; a rollback-safe plan cannot be created.");
            baselineHash = PlcWorkflowPrimitives.HashDocumentPair(backup, existing.Identity.Name);
        }
        else warnings.Add("The plan creates a new block. A failed apply will delete only the exact planned group/type/name target and recompile.");
        var target = existing?.Identity ?? new PlcBlockIdentity { PlcName = plcName, GroupPath = group, Name = validation.BlockName!, BlockType = validation.BlockType!, Number = validation.BlockNumber };
        string token = PlcWorkflowPrimitives.RandomToken();
        var plan = new PlcDocumentImportPlan { PlanId = planId, ApplyToken = token, ExpiresAtUtc = _plans.ExpiresAt(), PlcName = plcName, ImportDirectory = validation.ImportDirectory, FileName = validation.FileName, GroupPath = group, ImportOption = string.IsNullOrWhiteSpace(importOption) ? "Override" : importOption, Action = existing == null ? "Create" : "Override", InputHash = PlcWorkflowPrimitives.HashDocumentPair(validation.ImportDirectory, validation.FileName), BaselineHash = baselineHash, ExistingBlock = existing?.Info, Target = target, Validation = validation, Warnings = warnings };
        _plans.Add(token, plan, backup);
        return plan;
    }

    internal async Task<PlcDocumentImportApplyResult> ApplyAsync(string token)
    {
        PlcDocumentPlanStore.StoredPlan stored = _plans.Consume(token); PlcDocumentImportPlan plan = stored.Plan;
        await EnsurePlcOpen(plan.PlcName).ConfigureAwait(false);
        if (!PlcWorkflowPrimitives.FixedEquals(plan.InputHash, PlcWorkflowPrimitives.HashDocumentPair(plan.ImportDirectory, plan.FileName))) { PlcWorkflowPrimitives.Cleanup(stored.BackupDirectory); throw new InvalidOperationException("Input documents changed after planning. Create and review a new import plan."); }
        ResolvedBlock? current = _targets.ResolveExact(plan.Target, true); string currentBaseline = "missing"; string currentExport = Path.Combine(_plans.CurrentRoot, Guid.NewGuid().ToString("N"));
        try
        {
            if (current != null) { Directory.CreateDirectory(currentExport); if (!_targets.ExportExact(current, currentExport)) throw new InvalidOperationException("The exact target block could not be exported for stale-state verification."); currentBaseline = PlcWorkflowPrimitives.HashDocumentPair(currentExport, current.Identity.Name); }
            if (!PlcWorkflowPrimitives.FixedEquals(plan.BaselineHash, currentBaseline)) throw new InvalidOperationException("The exact target block changed after planning. The token is stale; create and review a new plan.");
            bool imported = await _plc.ImportBlockFromDocumentsAsync(plan.ImportDirectory, plan.FileName, plan.Target.GroupPath, plan.ImportOption).ConfigureAwait(false);
            ResolvedBlock? verified = imported ? _targets.ResolveExact(plan.Target, true) : null;
            bool identityVerified = verified != null && _targets.IdentityMatches(plan.Target, verified.Identity, !string.IsNullOrWhiteSpace(plan.Target.Number));
            CompilationResult? compilation = identityVerified ? await _plc.CompileSoftwareAsync().ConfigureAwait(false) : null;
            bool succeeded = imported && identityVerified && compilation != null && compilation.Success && compilation.ErrorCount == 0;
            if (succeeded) { PlcWorkflowPrimitives.Cleanup(stored.BackupDirectory); return new PlcDocumentImportApplyResult { PlanId = plan.PlanId, Target = plan.Target, Applied = true, StateMatched = true, Verified = true, CompileSucceeded = true, Message = "Exact PLC block imported, read back, and compiled successfully.", ImportedBlock = verified!.Info, Compilation = compilation }; }
            PlcRollbackEvidence rollback = await RollbackAsync(plan, stored.BackupDirectory).ConfigureAwait(false); CompilationResult rollbackCompilation = await _plc.CompileSoftwareAsync().ConfigureAwait(false); PlcWorkflowPrimitives.Cleanup(stored.BackupDirectory);
            bool rollbackProven = rollback.OperationSucceeded && rollback.HashMatches && rollbackCompilation.Success && rollbackCompilation.ErrorCount == 0;
            return new PlcDocumentImportApplyResult { PlanId = plan.PlanId, Target = plan.Target, Applied = false, StateMatched = true, Verified = identityVerified, CompileSucceeded = compilation != null && compilation.Success && compilation.ErrorCount == 0, RollbackAttempted = true, RolledBack = rollbackProven, RestoredHashMatches = rollback.HashMatches, ExpectedRestoredHash = rollback.ExpectedHash, ActualRestoredHash = rollback.ActualHash, Message = rollbackProven ? "Import or compile verification failed; the exact baseline was restored and hash-verified." : "Import or compile verification failed and exact rollback could not be proven. Inspect the target before continuing.", ImportedBlock = verified?.Info, Compilation = compilation, RollbackCompilation = rollbackCompilation };
        }
        catch { PlcWorkflowPrimitives.Cleanup(stored.BackupDirectory); throw; }
        finally { PlcWorkflowPrimitives.Cleanup(currentExport); }
    }

    internal async Task<PlcRollbackEvidence> RollbackAsync(PlcDocumentImportPlan plan, string backupDirectory)
    {
        if (plan.ExistingBlock != null || !string.Equals(plan.BaselineHash, "missing", StringComparison.Ordinal))
        {
            bool restored = await _plc.ImportBlockFromDocumentsAsync(backupDirectory, plan.Target.Name, plan.Target.GroupPath, "Override").ConfigureAwait(false);
            if (!restored) return new PlcRollbackEvidence(false, false, plan.BaselineHash, null);
            ResolvedBlock? block = _targets.ResolveExact(plan.Target, true); if (block == null) return new PlcRollbackEvidence(true, false, plan.BaselineHash, null);
            string verify = Path.Combine(_plans.RollbackRoot, Guid.NewGuid().ToString("N"));
            try { Directory.CreateDirectory(verify); if (!_targets.ExportExact(block, verify)) return new PlcRollbackEvidence(true, false, plan.BaselineHash, null); string actual = PlcWorkflowPrimitives.HashDocumentPair(verify, plan.Target.Name); return new PlcRollbackEvidence(true, PlcWorkflowPrimitives.FixedEquals(plan.BaselineHash, actual), plan.BaselineHash, actual); }
            finally { PlcWorkflowPrimitives.Cleanup(verify); }
        }
        ResolvedBlock? created = _targets.ResolveExact(plan.Target, true); bool deleted = created == null || _targets.Delete(created); bool absent = _targets.ResolveExact(plan.Target, true) == null;
        return new PlcRollbackEvidence(deleted && absent, deleted && absent, "missing", absent ? "missing" : "present");
    }

    private async Task EnsurePlcOpen(string plcName) { if (!_plc.IsSoftwareOpen || !string.Equals(_plc.CurrentPlcName, plcName, StringComparison.OrdinalIgnoreCase)) await _plc.OpenPlcAsync(plcName).ConfigureAwait(false); }
}
