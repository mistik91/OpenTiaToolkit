using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Models;

namespace OpenTiaToolkit.Core.Services;

public sealed partial class PlcDocumentWorkflowService
{
    private static readonly TimeSpan PlanLifetime = TimeSpan.FromMinutes(10);
    private static readonly Regex ResourceReferenceRegex = new Regex(@"\bMLC_[A-Za-z0-9_]+\b", RegexOptions.Compiled);
    private static readonly Regex ResourceDefinitionRegex = new Regex(@"(?im)^\s*(?:-\s*)?id\s*:\s*(MLC_[A-Za-z0-9_]+)\s*", RegexOptions.Compiled);
    private static readonly Regex BlockRegex = new Regex(@"(?im)^\s*(FUNCTION_BLOCK|FUNCTION|DATA_BLOCK|ORGANIZATION_BLOCK)\s+(?:""(?<quoted>[^""]+)""|(?<plain>[^\s:]+))", RegexOptions.Compiled);
    private static readonly Regex BlockNumberRegex = new Regex(@"S7_BlockNumber\s*:=\s*""(?<value>[^""]+)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PreferredLanguageRegex = new Regex(@"S7_PreferredLanguage\s*:=\s*""(?<value>[^""]+)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CallRegex = new Regex(@"(?<![\w""#])(?:(?:#[A-Za-z_][\w]*|""[^""]+""(?:\.[A-Za-z_][\w]*)*)\.)?(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?:\{[^{}]*\})?\s*\(", RegexOptions.Compiled);
    private static readonly Regex QuotedBlockCallRegex = new Regex(@"""(?<name>[^""]+)""\s*\(", RegexOptions.Compiled);
    private static readonly Regex UnsupportedDecoratedMoveRegex = new Regex(@"\bMove\s*\{", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex WireRegex = new Regex(@"\bwire#(?<name>[A-Za-z0-9_]+)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly HashSet<string> VerifiedInstructions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Contact", "I_Contact", "Coil", "I_Coil", "S_Coil", "R_Coil", "Not", "P_Trig",
        "GT_Contact", "LT_Contact", "EQ_Contact", "NE_Contact", "GE_Contact", "LE_Contact",
        "P_Contact", "N_Contact", "Move", "Add", "Sub", "Mul", "Div", "Normalize", "Scale",
        "TON", "TOF", "TP", "TON_Coil", "TOF_Coil", "TP_Coil", "TONR_Coil", "RT_Coil", "ATTR_DB"
    };
    private static readonly HashSet<string> KnownUnsupportedInstructions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Gt", "Lt", "Eq", "Ne", "Ge", "Le", "PBox", "NBox"
    };
    private static readonly HashSet<string> TypedInstructions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "GT_Contact", "LT_Contact", "EQ_Contact", "NE_Contact", "GE_Contact", "LE_Contact", "Add", "Sub", "Mul", "Div", "Normalize", "Scale"
    };
    private static readonly HashSet<string> DualTypedInstructions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Normalize", "Scale"
    };
    private static readonly HashSet<string> StatefulInstructions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "TON", "TOF", "TP", "CTU", "CTD", "CTUD"
    };
    private readonly IPlcService _plcService;
    private readonly EngineeringSessionContext _sessionContext;
    private readonly V21OpennessAdapter _v21Adapter = new V21OpennessAdapter();
    private readonly Dictionary<string, StoredPlan> _plans = new Dictionary<string, StoredPlan>(StringComparer.Ordinal);
    private readonly object _sync = new object();

    public PlcDocumentWorkflowService(IPlcService plcService) : this(plcService, new EngineeringSessionContext()) { }

    internal PlcDocumentWorkflowService(IPlcService plcService, EngineeringSessionContext sessionContext)
    {
        _plcService = plcService;
        _sessionContext = sessionContext;
        CleanupOrphanedPlanDirectories();
    }

    public PlcDocumentValidationResult Validate(string importDirectory, string fileName)
    {
        string directory = Path.GetFullPath(importDirectory ?? string.Empty);
        string baseName = Path.GetFileNameWithoutExtension(fileName ?? string.Empty);
        var errors = new List<string>();
        var warnings = new List<string>();
        string s7dclPath = Path.Combine(directory, baseName + ".s7dcl");
        string s7resPath = Path.Combine(directory, baseName + ".s7res");
        if (!Directory.Exists(directory)) errors.Add($"Import directory was not found: {directory}");
        if (string.IsNullOrWhiteSpace(baseName)) errors.Add("file-name is required.");
        if (!File.Exists(s7dclPath)) errors.Add($"S7DCL file was not found: {s7dclPath}");

        byte[] documentBytes = File.Exists(s7dclPath) ? File.ReadAllBytes(s7dclPath) : Array.Empty<byte>();
        bool hasBom = documentBytes.Length >= 3 && documentBytes[0] == 0xEF && documentBytes[1] == 0xBB && documentBytes[2] == 0xBF;
        string text = DecodeUtf8(documentBytes, errors, "S7DCL");
        if (documentBytes.Length > 0 && !hasBom) warnings.Add("S7DCL is UTF-8 without BOM. UTF-8 BOM is recommended for Siemens imports and required for reliable non-ASCII text.");

        Match blockMatch = BlockRegex.Match(text);
        if (!blockMatch.Success && documentBytes.Length > 0) errors.Add("No supported FUNCTION, FUNCTION_BLOCK, DATA_BLOCK, or ORGANIZATION_BLOCK declaration was found.");
        string? blockName = blockMatch.Success ? FirstNonEmpty(blockMatch.Groups["quoted"].Value, blockMatch.Groups["plain"].Value) : null;
        string? blockType = blockMatch.Success ? NormalizeBlockType(blockMatch.Groups[1].Value) : null;
        if (!string.IsNullOrWhiteSpace(blockName) && !string.Equals(blockName, baseName, StringComparison.Ordinal)) errors.Add($"Document block name '{blockName}' does not match file-name '{baseName}'.");
        ValidateBlockBoundaries(text, blockType, errors);
        if (string.Equals(blockType, "GlobalDB", StringComparison.OrdinalIgnoreCase)) ValidateDataBlockDocument(text, errors, warnings);
        if (Regex.IsMatch(text, @"S7_PreferredLanguage\s*:=\s*""F_", RegexOptions.IgnoreCase) || Regex.IsMatch(text, @"\bS7_F", RegexOptions.IgnoreCase))
            errors.Add("Safety-language S7DCL authoring is known-unsupported by this standard-block workflow. Use a separately validated safety workflow.");

        string[] references = ResourceReferenceRegex.Matches(text).Cast<Match>().Select(match => match.Value).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        bool resourceRequired = references.Length > 0;
        string? resourceHash = null;
        if (resourceRequired && !File.Exists(s7resPath)) errors.Add("The S7DCL contains MLC_* references, but the matching S7RES file is missing.");
        if (File.Exists(s7resPath))
        {
            byte[] resourceBytes = File.ReadAllBytes(s7resPath);
            string resourceText = DecodeUtf8(resourceBytes, errors, "S7RES");
            resourceHash = Hash(resourceBytes);
            string[] definitionRows = ResourceDefinitionRegex.Matches(resourceText).Cast<Match>().Select(match => match.Groups[1].Value).ToArray();
            foreach (IGrouping<string, string> duplicate in definitionRows.GroupBy(value => value, StringComparer.Ordinal).Where(group => group.Count() > 1)) errors.Add($"S7RES defines resource '{duplicate.Key}' more than once.");
            var definitions = new HashSet<string>(definitionRows, StringComparer.Ordinal);
            foreach (string reference in references.Where(reference => !definitions.Contains(reference))) errors.Add($"S7RES does not define referenced resource '{reference}'.");
            foreach (string unused in definitions.Where(definition => !references.Contains(definition, StringComparer.Ordinal))) warnings.Add($"S7RES resource '{unused}' is not referenced by the S7DCL document.");
            if (resourceRequired && !Regex.IsMatch(resourceText, @"(?im)^\s+[A-Za-z]{2}-[A-Za-z]{2}\s*:")) errors.Add("S7RES resources do not contain any culture text rows.");
            if (resourceRequired && !Regex.IsMatch(resourceText, @"(?im)^\s+en-US\s*:")) warnings.Add("S7RES has no en-US text. Siemens LAD imports can reject resources that only contain another culture.");
        }

        IReadOnlyList<PlcDocumentInstructionValidation> instructions = ValidateInstructions(text, blockType, errors, warnings);
        string[] dependencies = instructions
            .Where(item => item.Classification == "unknown-requires-live-probe" && item.Guidance != null && item.Guidance.IndexOf("Block calls", StringComparison.OrdinalIgnoreCase) >= 0)
            .Select(item => item.Instruction)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string classification = errors.Any(error => error.IndexOf("known-unsupported", StringComparison.OrdinalIgnoreCase) >= 0)
            ? "known-unsupported"
            : instructions.Any(item => item.Classification == "unknown-requires-live-probe") ? "unknown-requires-live-probe" : "verified";
        return new PlcDocumentValidationResult
        {
            Valid = errors.Count == 0,
            Classification = classification,
            ImportDirectory = directory,
            FileName = baseName,
            S7dclPath = s7dclPath,
            S7resPath = File.Exists(s7resPath) ? s7resPath : null,
            DocumentHash = Hash(documentBytes),
            ResourceHash = resourceHash,
            BlockName = blockName,
            BlockType = blockType,
            PreferredLanguage = Capture(PreferredLanguageRegex, text),
            BlockNumber = Capture(BlockNumberRegex, text),
            HasUtf8Bom = hasBom,
            ResourceRequired = resourceRequired,
            ResourceReferences = references,
            Instructions = instructions,
            Dependencies = dependencies,
            Errors = errors,
            Warnings = warnings
        };
    }

    public async Task<PlcDocumentImportPlan> PlanAsync(string plcName, string importDirectory, string fileName, string? groupPath, string importOption)
    {
        PlcDocumentValidationResult validation = Validate(importDirectory, fileName);
        if (!validation.Valid) throw new InvalidDataException("PLC documents are invalid: " + string.Join("; ", validation.Errors));
        await EnsurePlcOpen(plcName).ConfigureAwait(false);
        string normalizedGroup = NormalizeGroupPath(groupPath);
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
        string planId = Guid.NewGuid().ToString("N");
        string backupDirectory = Path.Combine(PlanRoot, planId);
        string baselineHash = "missing";
        var warnings = new List<string>(validation.Warnings);
        if (validation.Classification == "unknown-requires-live-probe") warnings.Add("The document contains unknown instructions or calls. Apply remains guarded and must pass Siemens import plus compile verification.");
        if (existing != null)
        {
            Directory.CreateDirectory(backupDirectory);
            if (!ExportExact(existing, backupDirectory)) throw new InvalidOperationException($"Existing block '{existing.Identity}' could not be exported; a rollback-safe plan cannot be created.");
            baselineHash = HashDocumentPair(backupDirectory, existing.Identity.Name);
        }
        else warnings.Add("The plan creates a new block. A failed apply will delete only the exact planned group/type/name target and recompile.");

        var target = existing?.Identity ?? new PlcBlockIdentity { PlcName = plcName, GroupPath = normalizedGroup, Name = validation.BlockName!, BlockType = validation.BlockType!, Number = validation.BlockNumber };
        string token = RandomToken();
        var plan = new PlcDocumentImportPlan
        {
            PlanId = planId,
            ApplyToken = token,
            ExpiresAtUtc = DateTimeOffset.UtcNow.Add(PlanLifetime),
            PlcName = plcName,
            ImportDirectory = validation.ImportDirectory,
            FileName = validation.FileName,
            GroupPath = normalizedGroup,
            ImportOption = string.IsNullOrWhiteSpace(importOption) ? "Override" : importOption,
            Action = existing == null ? "Create" : "Override",
            InputHash = HashDocumentPair(validation.ImportDirectory, validation.FileName),
            BaselineHash = baselineHash,
            ExistingBlock = existing?.Info,
            Target = target,
            Validation = validation,
            Warnings = warnings
        };
        lock (_sync) { PruneExpired(); _plans[token] = new StoredPlan(plan, backupDirectory); }
        return plan;
    }

    public async Task<PlcDocumentImportApplyResult> ApplyAsync(string token)
    {
        StoredPlan stored;
        lock (_sync)
        {
            PruneExpired();
            if (!_plans.TryGetValue(token, out stored!)) throw new InvalidOperationException("The apply token is invalid, expired, or already consumed. Create a new import plan.");
            _plans.Remove(token);
        }

        PlcDocumentImportPlan plan = stored.Plan;
        await EnsurePlcOpen(plan.PlcName).ConfigureAwait(false);
        if (!FixedEquals(plan.InputHash, HashDocumentPair(plan.ImportDirectory, plan.FileName)))
        {
            Cleanup(stored.BackupDirectory);
            throw new InvalidOperationException("Input documents changed after planning. Create and review a new import plan.");
        }
        object root = RequireBlockRoot();
        ResolvedBlock? current = ResolveExact(root, plan.Target, allowMissing: true);
        string currentBaseline = "missing";
        string currentExport = Path.Combine(CurrentRoot, Guid.NewGuid().ToString("N"));
        try
        {
            if (current != null)
            {
                Directory.CreateDirectory(currentExport);
                if (!ExportExact(current, currentExport)) throw new InvalidOperationException("The exact target block could not be exported for stale-state verification.");
                currentBaseline = HashDocumentPair(currentExport, current.Identity.Name);
            }
            if (!FixedEquals(plan.BaselineHash, currentBaseline)) throw new InvalidOperationException("The exact target block changed after planning. The token is stale; create and review a new plan.");

            bool imported = await _plcService.ImportBlockFromDocumentsAsync(plan.ImportDirectory, plan.FileName, plan.Target.GroupPath, plan.ImportOption).ConfigureAwait(false);
            ResolvedBlock? verified = imported ? ResolveExact(RequireBlockRoot(), plan.Target, allowMissing: true) : null;
            bool identityVerified = verified != null && IdentityMatches(plan.Target, verified.Identity, requireNumber: !string.IsNullOrWhiteSpace(plan.Target.Number));
            CompilationResult? compilation = identityVerified ? await _plcService.CompileSoftwareAsync().ConfigureAwait(false) : null;
            bool succeeded = imported && identityVerified && compilation != null && compilation.Success && compilation.ErrorCount == 0;
            if (succeeded)
            {
                Cleanup(stored.BackupDirectory);
                return new PlcDocumentImportApplyResult { PlanId = plan.PlanId, Target = plan.Target, Applied = true, StateMatched = true, Verified = true, CompileSucceeded = true, Message = "Exact PLC block imported, read back, and compiled successfully.", ImportedBlock = verified!.Info, Compilation = compilation };
            }

            RollbackEvidence rollback = await RollbackAsync(plan, stored.BackupDirectory).ConfigureAwait(false);
            CompilationResult rollbackCompilation = await _plcService.CompileSoftwareAsync().ConfigureAwait(false);
            Cleanup(stored.BackupDirectory);
            bool rollbackProven = rollback.OperationSucceeded && rollback.HashMatches && rollbackCompilation.Success && rollbackCompilation.ErrorCount == 0;
            return new PlcDocumentImportApplyResult
            {
                PlanId = plan.PlanId,
                Target = plan.Target,
                Applied = false,
                StateMatched = true,
                Verified = identityVerified,
                CompileSucceeded = compilation != null && compilation.Success && compilation.ErrorCount == 0,
                RollbackAttempted = true,
                RolledBack = rollbackProven,
                RestoredHashMatches = rollback.HashMatches,
                ExpectedRestoredHash = rollback.ExpectedHash,
                ActualRestoredHash = rollback.ActualHash,
                Message = rollbackProven ? "Import or compile verification failed; the exact baseline was restored and hash-verified." : "Import or compile verification failed and exact rollback could not be proven. Inspect the target before continuing.",
                ImportedBlock = verified?.Info,
                Compilation = compilation,
                RollbackCompilation = rollbackCompilation
            };
        }
        catch
        {
            Cleanup(stored.BackupDirectory);
            throw;
        }
        finally { Cleanup(currentExport); }
    }

    private async Task<RollbackEvidence> RollbackAsync(PlcDocumentImportPlan plan, string backupDirectory)
    {
        if (plan.ExistingBlock != null || !string.Equals(plan.BaselineHash, "missing", StringComparison.Ordinal))
        {
            bool restored = await _plcService.ImportBlockFromDocumentsAsync(backupDirectory, plan.Target.Name, plan.Target.GroupPath, "Override").ConfigureAwait(false);
            if (!restored) return new RollbackEvidence(false, false, plan.BaselineHash, null);
            ResolvedBlock? restoredBlock = ResolveExact(RequireBlockRoot(), plan.Target, allowMissing: true);
            if (restoredBlock == null) return new RollbackEvidence(true, false, plan.BaselineHash, null);
            string verifyDirectory = Path.Combine(RollbackRoot, Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(verifyDirectory);
                if (!ExportExact(restoredBlock, verifyDirectory)) return new RollbackEvidence(true, false, plan.BaselineHash, null);
                string actual = HashDocumentPair(verifyDirectory, plan.Target.Name);
                return new RollbackEvidence(true, FixedEquals(plan.BaselineHash, actual), plan.BaselineHash, actual);
            }
            finally { Cleanup(verifyDirectory); }
        }

        ResolvedBlock? created = ResolveExact(RequireBlockRoot(), plan.Target, allowMissing: true);
        bool deleted = created == null || _v21Adapter.TryDelete(created.EngineeringObject);
        bool absent = ResolveExact(RequireBlockRoot(), plan.Target, allowMissing: true) == null;
        return new RollbackEvidence(deleted && absent, deleted && absent, "missing", absent ? "missing" : "present");
    }

    private IReadOnlyList<PlcDocumentInstructionValidation> ValidateInstructions(string text, string? blockType, List<string> errors, List<string> warnings)
    {
        var results = new List<PlcDocumentInstructionValidation>();
        foreach (Match match in CallRegex.Matches(text))
        {
            string name = match.Groups["name"].Value;
            int line = 1 + text.Take(match.Index).Count(character => character == '\n');
            if (KnownUnsupportedInstructions.Contains(name) || (name.Equals("Move", StringComparison.OrdinalIgnoreCase) && UnsupportedDecoratedMoveRegex.IsMatch(match.Value)))
            {
                results.Add(new PlcDocumentInstructionValidation { Instruction = name, Classification = "known-unsupported", Line = line, Guidance = "Use the import-verified S7DCL form from the LAD grammar reference." });
                errors.Add($"Line {line}: known-unsupported S7DCL instruction form '{name}'.");
                continue;
            }
            if (VerifiedInstructions.Contains(name))
            {
                bool inferredType = TypedInstructions.Contains(name) && !HasSourceTypeTemplateBeforeCall(text, match.Index, DualTypedInstructions.Contains(name));
                string classification = inferredType ? "unknown-requires-live-probe" : "verified";
                results.Add(new PlcDocumentInstructionValidation { Instruction = name, Classification = classification, Line = line, Guidance = inferredType ? "Siemens may infer the operand type from connected values, but agent-authored calls should emit an explicit S7_Templates SrcType and require a live compile when it is absent." : null });
                if (inferredType) warnings.Add($"Typed instruction {name} at line {line} has no preceding S7_Templates SrcType in its network. Siemens-exported inferred forms are accepted only with guarded live compile verification.");
                ValidateInstructionArguments(text, match, name, blockType, errors);
            }
            else
            {
                bool projectCall = !StatefulInstructions.Contains(name) && !name.Equals("N_Trig", StringComparison.OrdinalIgnoreCase);
                results.Add(new PlcDocumentInstructionValidation { Instruction = name, Classification = "unknown-requires-live-probe", Line = line, Guidance = projectCall ? "Block calls are project-specific and require Siemens import plus compile verification." : "Keep the guarded apply and require Siemens import plus zero-error compile verification." });
                warnings.Add($"Line {line}: instruction or block call '{name}' is not in the import-verified registry and requires a live probe.");
                if (StatefulInstructions.Contains(name) || name.Equals("N_Trig", StringComparison.OrdinalIgnoreCase)) ValidateInstructionArguments(text, match, name, blockType, errors);
            }
        }
        foreach (Match match in QuotedBlockCallRegex.Matches(text))
        {
            int line = LineOf(text, match.Index);
            string name = match.Groups["name"].Value;
            results.Add(new PlcDocumentInstructionValidation { Instruction = name, Classification = "unknown-requires-live-probe", Line = line, Guidance = "Block calls are project-specific and require Siemens import plus compile verification." });
            warnings.Add($"Line {line}: block call '{name}' requires a live probe against the target project.");
        }
        foreach (IGrouping<string, Match> wire in WireRegex.Matches(text).Cast<Match>().Where(match => !match.Groups["name"].Value.Equals("powerrail", StringComparison.OrdinalIgnoreCase)).GroupBy(match => match.Groups["name"].Value, StringComparer.OrdinalIgnoreCase))
            if (wire.Count() < 2) errors.Add($"Wire junction 'wire#{wire.Key}' is referenced only once; every branch junction must have a tap and a join.");
        return results;
    }

    private static void ValidateInstructionArguments(string text, Match call, string name, string? blockType, List<string> errors)
    {
        string arguments = ExtractParenthesized(text, call.Index + call.Length - 1);
        bool Has(string parameter) => Regex.IsMatch(arguments, @"(?i)\b" + Regex.Escape(parameter) + @"\s*(?::=|=>)");
        if (name.Equals("Move", StringComparison.OrdinalIgnoreCase) && (!Has("in") || !Has("out1"))) errors.Add($"Instruction Move at line {LineOf(text, call.Index)} requires named 'in' and 'out1' parameters.");
        if (new[] { "Add", "Sub", "Mul", "Div" }.Contains(name, StringComparer.OrdinalIgnoreCase) && (!Has("in1") || !Has("in2") || !Has("out"))) errors.Add($"Instruction {name} at line {LineOf(text, call.Index)} requires named 'in1', 'in2', and 'out' parameters.");
        if (new[] { "Normalize", "Scale" }.Contains(name, StringComparer.OrdinalIgnoreCase))
            foreach (string parameter in new[] { "min", "value", "max", "out" })
                if (!Has(parameter)) errors.Add($"Instruction {name} at line {LineOf(text, call.Index)} requires named '{parameter}'.");
        if (name.Equals("ATTR_DB", StringComparison.OrdinalIgnoreCase))
            foreach (string parameter in new[] { "REQ", "DB_NUMBER", "RET_VAL", "DB_LENGTH", "ATTRIB" })
                if (!Has(parameter)) errors.Add($"Instruction ATTR_DB at line {LineOf(text, call.Index)} requires named '{parameter}'.");
        if (TypedInstructions.Contains(name) && name.EndsWith("_Contact", StringComparison.OrdinalIgnoreCase) && (!Has("in1") || !Has("in2"))) errors.Add($"Instruction {name} at line {LineOf(text, call.Index)} requires named 'in1' and 'in2' parameters.");
        if (new[] { "TON", "TOF", "TP", "TON_Coil", "TOF_Coil", "TP_Coil", "TONR_Coil" }.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            if (!Has("pt")) errors.Add($"Timer instruction {name} at line {LineOf(text, call.Index)} requires named 'pt'.");
            if (name.EndsWith("_Coil", StringComparison.OrdinalIgnoreCase) && !Has("timer")) errors.Add($"Timer coil {name} at line {LineOf(text, call.Index)} requires named 'timer'.");
            string receiver = call.Value.Substring(0, call.Value.LastIndexOf(name, StringComparison.OrdinalIgnoreCase));
            if (string.Equals(blockType, "FC", StringComparison.OrdinalIgnoreCase) && receiver.TrimStart().StartsWith("#", StringComparison.Ordinal)) errors.Add($"Timer instance {receiver.Trim().TrimEnd('.')} at line {LineOf(text, call.Index)} is local to an FC. Use an FB static or a global DB timer instance.");
        }
        if (new[] { "P_Contact", "N_Contact" }.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            foreach (string parameter in new[] { "operand", "bit" })
                if (!Has(parameter)) errors.Add($"Edge contact {name} at line {LineOf(text, call.Index)} requires named '{parameter}'.");
            Match bit = Regex.Match(arguments, @"(?i)\bbit\s*:=\s*#(?<name>[A-Za-z_][A-Za-z0-9_]*)");
            if (bit.Success && !IsDeclaredInRetainedEdgeSection(text, bit.Groups["name"].Value)) errors.Add($"Edge contact {name} memory '#{bit.Groups["name"].Value}' at line {LineOf(text, call.Index)} must be Bool in FB static VAR or VAR_IN_OUT, not VAR_TEMP.");
        }
        if (new[] { "CTU", "CTD", "CTUD" }.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            string[] required = name.Equals("CTU", StringComparison.OrdinalIgnoreCase) ? new[] { "CU", "R", "PV" } : name.Equals("CTD", StringComparison.OrdinalIgnoreCase) ? new[] { "CD", "LD", "PV" } : new[] { "CU", "CD", "R", "LD", "PV" };
            foreach (string parameter in required) if (!Has(parameter)) errors.Add($"Counter instruction {name} at line {LineOf(text, call.Index)} requires named '{parameter}'.");
        }
        Match localReceiver = Regex.Match(call.Value, @"#(?<name>[A-Za-z_][A-Za-z0-9_]*)\.");
        if (localReceiver.Success && StatefulInstructions.Contains(name) && string.Equals(blockType, "FB", StringComparison.OrdinalIgnoreCase))
        {
            string instance = localReceiver.Groups["name"].Value;
            if (!IsDeclaredInStaticSection(text, instance)) errors.Add($"Stateful instruction instance '#{instance}' at line {LineOf(text, call.Index)} must be declared in the FB static VAR section, not VAR_TEMP or an interface section.");
        }
        if (name.Equals("P_Trig", StringComparison.OrdinalIgnoreCase) || name.Equals("N_Trig", StringComparison.OrdinalIgnoreCase))
        {
            Match operand = Regex.Match(arguments, @"#(?<name>[A-Za-z_][A-Za-z0-9_]*)");
            if (!operand.Success) errors.Add($"{name} at line {LineOf(text, call.Index)} requires a local edge-memory operand.");
            else if (!IsDeclaredInLocalOrStaticSection(text, operand.Groups["name"].Value, "Bool")) errors.Add($"Edge-memory operand '#{operand.Groups["name"].Value}' at line {LineOf(text, call.Index)} must be declared as Bool in FB static or temporary data.");
        }
    }

    private static bool HasSourceTypeTemplateBeforeCall(string text, int callIndex, bool requireDestinationType)
    {
        int networkStart = text.LastIndexOf("NETWORK", callIndex, StringComparison.OrdinalIgnoreCase);
        string prefix = text.Substring(Math.Max(0, networkStart), callIndex - Math.Max(0, networkStart));
        int templateIndex = prefix.LastIndexOf("S7_Templates", StringComparison.OrdinalIgnoreCase);
        if (templateIndex < 0 || templateIndex < prefix.LastIndexOf(')')) return false;
        string nearestTemplate = prefix.Substring(templateIndex);
        return nearestTemplate.IndexOf("SrcType", StringComparison.OrdinalIgnoreCase) >= 0 && (!requireDestinationType || nearestTemplate.IndexOf("DestType", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static void ValidateBlockBoundaries(string text, string? blockType, List<string> errors)
    {
        if (blockType == "FC" && !Regex.IsMatch(text, @"(?im)^\s*END_FUNCTION\s*$")) errors.Add("FUNCTION is missing END_FUNCTION.");
        if (blockType == "FB" && !Regex.IsMatch(text, @"(?im)^\s*END_FUNCTION_BLOCK\s*$")) errors.Add("FUNCTION_BLOCK is missing END_FUNCTION_BLOCK.");
        if (blockType == "GlobalDB" && !Regex.IsMatch(text, @"(?im)^\s*END_DATA_BLOCK\s*$")) errors.Add("DATA_BLOCK is missing END_DATA_BLOCK.");
        if (blockType == "OB" && !Regex.IsMatch(text, @"(?im)^\s*END_ORGANIZATION_BLOCK\s*$")) errors.Add("ORGANIZATION_BLOCK is missing END_ORGANIZATION_BLOCK.");
        if (Regex.Matches(text, @"(?i)(?<!END_)\bNETWORK\b").Count != Regex.Matches(text, @"(?i)\bEND_NETWORK\b").Count) errors.Add("NETWORK and END_NETWORK counts do not match.");
        if (Regex.Matches(text, @"(?i)(?<!END_)\bRUNG\b").Count != Regex.Matches(text, @"(?i)\bEND_RUNG\b").Count) errors.Add("RUNG and END_RUNG counts do not match.");
    }

    private static void ValidateDataBlockDocument(string text, List<string> errors, List<string> warnings)
    {
        Match variableSection = Regex.Match(text, @"(?is)\bVAR\b(?<body>.*?)\bEND_VAR\b");
        if (!variableSection.Success)
        {
            errors.Add("Global DATA_BLOCK must contain a VAR/END_VAR declaration section.");
            return;
        }
        string body = variableSection.Groups["body"].Value;
        int structStarts = Regex.Matches(body, @"(?i)\bSTRUCT\b").Count;
        int structEnds = Regex.Matches(body, @"(?i)\bEND_STRUCT\b").Count;
        if (structStarts != structEnds) errors.Add($"Global DB STRUCT/END_STRUCT counts do not match ({structStarts}/{structEnds}).");
        var declarations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match declaration in Regex.Matches(body, @"(?im)^\s*(?:\{[^\r\n]*\}\s*)?(?:""(?<quoted>[^""]+)""|(?<plain>[A-Za-z_][A-Za-z0-9_]*))\s*:\s*(?<type>[^;]+);"))
        {
            string name = FirstNonEmpty(declaration.Groups["quoted"].Value, declaration.Groups["plain"].Value)!;
            string type = declaration.Groups["type"].Value.Trim();
            if (declarations.ContainsKey(name)) errors.Add($"Global DB member '{name}' is declared more than once.");
            else declarations.Add(name, type);
            foreach (Match array in Regex.Matches(type, @"(?i)Array\s*\[\s*(?<lower>-?\d+)\s*\.\.\s*(?<upper>-?\d+)\s*\]"))
            {
                long lower = long.Parse(array.Groups["lower"].Value, System.Globalization.CultureInfo.InvariantCulture);
                long upper = long.Parse(array.Groups["upper"].Value, System.Globalization.CultureInfo.InvariantCulture);
                if (upper < lower) errors.Add($"Global DB member '{name}' has descending array bounds {lower}..{upper}.");
                else if (upper - lower > 1000000) errors.Add($"Global DB member '{name}' declares an implausibly large array; review the bounds explicitly.");
            }
            Match stringLength = Regex.Match(type, @"(?i)^(?<kind>W?String)\s*\[\s*(?<length>\d+)\s*\]$");
            if (stringLength.Success)
            {
                int length = int.Parse(stringLength.Groups["length"].Value, System.Globalization.CultureInfo.InvariantCulture);
                int maximum = stringLength.Groups["kind"].Value.Equals("WString", StringComparison.OrdinalIgnoreCase) ? 16382 : 254;
                if (length < 1 || length > maximum) errors.Add($"Global DB member '{name}' has invalid {stringLength.Groups["kind"].Value} length {length}; expected 1..{maximum}.");
            }
            if (!Regex.IsMatch(type, @"(?i)^(Bool|Byte|Word|DWord|LWord|SInt|USInt|Int|UInt|DInt|UDInt|LInt|ULInt|Real|LReal|Time|LTime|Date|Time_Of_Day|TOD|String|WString|Char|WChar|Array|Struct)\b"))
                warnings.Add($"Global DB member '{name}' references project type '{type}'. Confirm that the PLC data type exists before apply.");
        }
        if (declarations.Count == 0) warnings.Add("Global DB declares no members.");
        string initialization = text.Substring(variableSection.Index + variableSection.Length);
        var assigned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match assignment in Regex.Matches(initialization, @"(?im)^\s*(?:""(?<quoted>[^""]+)""|(?<plain>[A-Za-z_][A-Za-z0-9_]*))\s*:=\s*(?<value>[^;]+);"))
        {
            string name = FirstNonEmpty(assignment.Groups["quoted"].Value, assignment.Groups["plain"].Value)!;
            if (!assigned.Add(name)) errors.Add($"Global DB member '{name}' has more than one start-value assignment.");
            if (!declarations.TryGetValue(name, out string? type)) errors.Add($"Start-value assignment targets undeclared global DB member '{name}'.");
            else if (type.Trim().Equals("Bool", StringComparison.OrdinalIgnoreCase) && !Regex.IsMatch(assignment.Groups["value"].Value.Trim(), @"^(TRUE|FALSE)$", RegexOptions.IgnoreCase)) errors.Add($"Bool member '{name}' requires TRUE or FALSE start value.");
        }
        if (!Regex.IsMatch(text, @"(?i)S7_Optimized\s*:=")) warnings.Add("Global DB does not declare S7_Optimized explicitly; preserve the exported access mode before applying a modification.");
        if (Regex.IsMatch(text, @"(?i)S7_Optimized\s*:=\s*""FALSE""")) warnings.Add("Non-optimized DB layout is address-sensitive; run cross-reference and HMI impact checks before changing members or order.");
        if (Regex.IsMatch(text, @"(?i)InstanceDB|InstanceOf")) errors.Add("Instance DB documents are not supported by the global DB authoring workflow.");
    }

    private object RequireBlockRoot() => TiaReflectionHelper.GetBlockGroup(_sessionContext.PlcSoftware) ?? throw new InvalidOperationException("PLC block root is unavailable.");
    private object RequireTypeRoot() => TiaReflectionHelper.GetTypeGroup(_sessionContext.PlcSoftware) ?? throw new InvalidOperationException("PLC type root is unavailable.");

    private IReadOnlyList<ResolvedBlock> FindBlocks(object root, string name)
    {
        var results = new List<ResolvedBlock>();
        CollectBlocks(root, string.Empty, name, results);
        return results;
    }

    private void CollectBlocks(object group, string path, string name, List<ResolvedBlock> results)
    {
        IEnumerable<object>? blocks = TiaReflectionHelper.GetBlocks(group);
        if (blocks != null)
            foreach (object block in blocks)
                if (string.Equals(TiaReflectionHelper.GetName(block), name, StringComparison.OrdinalIgnoreCase)) results.Add(ToResolved(block, path));
        IEnumerable<object>? groups = TiaReflectionHelper.GetGroups(group);
        if (groups == null) return;
        foreach (object child in groups)
        {
            string childName = TiaReflectionHelper.GetName(child) ?? "<unnamed>";
            CollectBlocks(child, CombinePath(path, childName), name, results);
        }
    }

    private IReadOnlyList<ResolvedType> FindTypes(object root, string name)
    {
        var results = new List<ResolvedType>();
        CollectTypes(root, string.Empty, name, results);
        return results;
    }

    private void CollectTypes(object group, string path, string name, List<ResolvedType> results)
    {
        IEnumerable<object>? types = TiaReflectionHelper.GetTypes(group);
        if (types != null)
            foreach (object type in types)
                if (string.Equals(TiaReflectionHelper.GetName(type), name, StringComparison.OrdinalIgnoreCase))
                    results.Add(new ResolvedType(type, new PlcBlockIdentity { PlcName = _sessionContext.CurrentPlcName ?? string.Empty, GroupPath = path, Name = name, BlockType = "TYPE" }));
        IEnumerable<object>? groups = TiaReflectionHelper.GetGroups(group);
        if (groups == null) return;
        foreach (object child in groups)
        {
            string childName = TiaReflectionHelper.GetName(child) ?? "<unnamed>";
            CollectTypes(child, CombinePath(path, childName), name, results);
        }
    }

    private ResolvedBlock? ResolveExact(object root, PlcBlockIdentity identity, bool allowMissing)
    {
        IReadOnlyList<ResolvedBlock> matches = FindBlocks(root, identity.Name).Where(item => GroupEquals(item.Identity.GroupPath, identity.GroupPath) && BlockTypeEquals(item.Identity.BlockType, identity.BlockType)).ToArray();
        if (matches.Count > 1) throw Ambiguous(identity.Name, matches);
        if (matches.Count == 0) { if (allowMissing) return null; throw new InvalidOperationException($"Exact target '{identity}' was not found."); }
        if (!string.IsNullOrWhiteSpace(identity.Number) && !string.Equals(identity.Number, matches[0].Identity.Number, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Exact target '{identity}' resolved with block number '{matches[0].Identity.Number ?? "<none>"}' instead of planned number '{identity.Number}'.");
        return matches[0];
    }

    private ResolvedType? ResolveExactType(object root, PlcBlockIdentity identity, bool allowMissing)
    {
        IReadOnlyList<ResolvedType> matches = FindTypes(root, identity.Name).Where(item => GroupEquals(item.Identity.GroupPath, identity.GroupPath)).ToArray();
        if (matches.Count > 1) throw new InvalidOperationException($"PLC type target '{identity.Name}' is ambiguous in group '{DisplayGroup(identity.GroupPath)}'.");
        if (matches.Count == 0) { if (allowMissing) return null; throw new InvalidOperationException($"Exact PLC type target '{identity}' was not found."); }
        return matches[0];
    }

    private ResolvedBlock ToResolved(object block, string groupPath)
    {
        string name = TiaReflectionHelper.GetName(block) ?? string.Empty;
        string type = NormalizeRuntimeBlockType(TiaReflectionHelper.GetProperty(block, "Type")?.ToString(), block.GetType().Name);
        string? number = TiaReflectionHelper.GetProperty(block, "Number")?.ToString();
        string? language = TiaReflectionHelper.GetProperty(block, "ProgrammingLanguage")?.ToString();
        bool readOnly = (bool?)TiaReflectionHelper.GetProperty(block, "IsReadOnly") ?? false;
        var info = new BlockInfoDetail(name, type, language, number, readOnly, TiaReflectionHelper.GetMultilingualText(TiaReflectionHelper.GetProperty(block, "Comment")), TiaReflectionHelper.GetProperty(block, "Author")?.ToString(), null, null, 0, language);
        return new ResolvedBlock(block, new PlcBlockIdentity { PlcName = _sessionContext.CurrentPlcName ?? string.Empty, GroupPath = groupPath, Name = name, BlockType = type, Number = number }, info);
    }

    private bool ExportExact(ResolvedBlock block, string directory)
    {
        Directory.CreateDirectory(directory);
        return _v21Adapter.TryExportAsDocuments(block.EngineeringObject, new DirectoryInfo(directory), block.Identity.Name, out string? state) && string.Equals(state, "Success", StringComparison.OrdinalIgnoreCase);
    }

    private bool ExportExactType(ResolvedType type, string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        return _v21Adapter.TryExportEngineeringObject(type.EngineeringObject, path, "None", out _);
    }

    private static object? ResolveGroup(object root, string path) => TiaReflectionHelper.GetBlockGroupByPath(root, path);
    private static bool IdentityMatches(PlcBlockIdentity expected, PlcBlockIdentity actual, bool requireNumber) => GroupEquals(expected.GroupPath, actual.GroupPath) && BlockTypeEquals(expected.BlockType, actual.BlockType) && string.Equals(expected.Name, actual.Name, StringComparison.OrdinalIgnoreCase) && (!requireNumber || string.Equals(expected.Number, actual.Number, StringComparison.OrdinalIgnoreCase));
    private static bool GroupEquals(string? left, string? right) => string.Equals(NormalizeGroupPath(left), NormalizeGroupPath(right), StringComparison.OrdinalIgnoreCase);
    private static bool BlockTypeEquals(string left, string right) => string.Equals(NormalizeRuntimeBlockType(left, left), NormalizeRuntimeBlockType(right, right), StringComparison.OrdinalIgnoreCase);
    private static string NormalizeRuntimeBlockType(string? type, string runtimeType)
    {
        string value = (type ?? runtimeType).Replace("PlcBlock", string.Empty);
        if (value.IndexOf("Instance", StringComparison.OrdinalIgnoreCase) >= 0 && value.IndexOf("DB", StringComparison.OrdinalIgnoreCase) >= 0) return "InstanceDB";
        if (value.Equals("DB", StringComparison.OrdinalIgnoreCase) || value.IndexOf("GlobalDB", StringComparison.OrdinalIgnoreCase) >= 0 || value.IndexOf("DataBlock", StringComparison.OrdinalIgnoreCase) >= 0) return "GlobalDB";
        if (value.IndexOf("FunctionBlock", StringComparison.OrdinalIgnoreCase) >= 0 || value.Equals("FB", StringComparison.OrdinalIgnoreCase)) return "FB";
        if (value.IndexOf("Function", StringComparison.OrdinalIgnoreCase) >= 0 || value.Equals("FC", StringComparison.OrdinalIgnoreCase)) return "FC";
        if (value.IndexOf("Organization", StringComparison.OrdinalIgnoreCase) >= 0 || value.Equals("OB", StringComparison.OrdinalIgnoreCase)) return "OB";
        return value;
    }

    private static InvalidOperationException Ambiguous(string name, IEnumerable<ResolvedBlock> matches) => new InvalidOperationException($"Block target '{name}' is ambiguous. Supply an exact group-path and block type. Candidates: {string.Join(", ", matches.Select(item => item.Identity.ToString()))}");
    private static string NormalizeGroupPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        string normalized = string.Join("/", path!.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries).Select(segment => segment.Trim()));
        if (normalized.Equals("Program Blocks", StringComparison.OrdinalIgnoreCase)) return string.Empty;
        if (normalized.StartsWith("Program Blocks/", StringComparison.OrdinalIgnoreCase)) return normalized.Substring("Program Blocks/".Length);
        return normalized;
    }
    private static string NormalizeTypeGroupPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        string normalized = string.Join("/", path!.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries).Select(segment => segment.Trim()));
        foreach (string root in new[] { "PLC data types", "PLC types", "Types" })
        {
            if (normalized.Equals(root, StringComparison.OrdinalIgnoreCase)) return string.Empty;
            if (normalized.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase)) return normalized.Substring(root.Length + 1);
        }
        return normalized;
    }
    private static string DisplayGroup(string path) => string.IsNullOrEmpty(path) ? "<root>" : path;
    private static string CombinePath(string path, string child) => string.IsNullOrEmpty(path) ? child : path + "/" + child;

    private async Task EnsurePlcOpen(string plcName)
    {
        if (!_plcService.IsSoftwareOpen || !string.Equals(_plcService.CurrentPlcName, plcName, StringComparison.OrdinalIgnoreCase)) await _plcService.OpenPlcAsync(plcName).ConfigureAwait(false);
    }

    private void PruneExpired()
    {
        foreach (string token in _plans.Where(pair => pair.Value.Plan.ExpiresAtUtc <= DateTimeOffset.UtcNow).Select(pair => pair.Key).ToArray()) { Cleanup(_plans[token].BackupDirectory); _plans.Remove(token); }
    }

    private static void CleanupOrphanedPlanDirectories()
    {
        try
        {
            if (!Directory.Exists(PlanRoot)) return;
            foreach (string directory in Directory.GetDirectories(PlanRoot))
                if (Directory.GetLastWriteTimeUtc(directory) < DateTime.UtcNow.Subtract(PlanLifetime)) Cleanup(directory);
        }
        catch { }
    }

    private static string PlanRoot => Path.Combine(Path.GetTempPath(), "OpenTiaToolkit", "plc-document-plans");
    private static string CurrentRoot => Path.Combine(Path.GetTempPath(), "OpenTiaToolkit", "plc-document-current");
    private static string RollbackRoot => Path.Combine(Path.GetTempPath(), "OpenTiaToolkit", "plc-document-rollback");
    private static string HashDocumentPair(string directory, string baseName)
    {
        byte[] dcl = File.ReadAllBytes(Path.Combine(directory, baseName + ".s7dcl"));
        string resourcePath = Path.Combine(directory, baseName + ".s7res");
        byte[] res = File.Exists(resourcePath) ? File.ReadAllBytes(resourcePath) : Array.Empty<byte>();
        return Hash(dcl.Concat(new byte[] { 0 }).Concat(res).ToArray());
    }
    private static string DecodeUtf8(byte[] bytes, List<string> errors, string label) { if (bytes.Length == 0) return string.Empty; try { return new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); } catch (DecoderFallbackException ex) { errors.Add($"{label} is not valid UTF-8: {ex.Message}"); return string.Empty; } }
    private static string Hash(byte[] bytes) { using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant(); }
    private static string RandomToken() { var bytes = new byte[32]; using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes); return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_'); }
    private static bool FixedEquals(string left, string right) { byte[] a = Encoding.UTF8.GetBytes(left); byte[] b = Encoding.UTF8.GetBytes(right); if (a.Length != b.Length) return false; int difference = 0; for (int index = 0; index < a.Length; index++) difference |= a[index] ^ b[index]; return difference == 0; }
    private static string? Capture(Regex regex, string text) { Match match = regex.Match(text); return match.Success ? match.Groups["value"].Value : null; }
    private static string? FirstNonEmpty(string first, string second) => !string.IsNullOrWhiteSpace(first) ? first : (!string.IsNullOrWhiteSpace(second) ? second : null);
    private static string NormalizeBlockType(string value) => value.ToUpperInvariant() == "FUNCTION" ? "FC" : value.ToUpperInvariant() == "FUNCTION_BLOCK" ? "FB" : value.ToUpperInvariant() == "DATA_BLOCK" ? "GlobalDB" : "OB";
    private static int LineOf(string text, int index) => 1 + text.Take(index).Count(character => character == '\n');
    private static string ExtractParenthesized(string text, int openIndex) { int depth = 0; for (int index = openIndex; index < text.Length; index++) { if (text[index] == '(') depth++; else if (text[index] == ')' && --depth == 0) return text.Substring(openIndex + 1, index - openIndex - 1); } return text.Substring(Math.Min(openIndex + 1, text.Length)); }
    private static bool IsDeclaredInStaticSection(string text, string instance)
    {
        foreach (Match section in Regex.Matches(text, @"(?is)\bVAR\b(?<body>.*?)\bEND_VAR\b"))
            if (Regex.IsMatch(section.Groups["body"].Value, @"(?im)^\s*" + Regex.Escape(instance) + @"\s*:")) return true;
        return false;
    }
    private static bool IsDeclaredInLocalOrStaticSection(string text, string name, string type)
    {
        foreach (Match section in Regex.Matches(text, @"(?is)\b(?:VAR|VAR_TEMP)\b(?<body>.*?)\bEND_VAR\b"))
            if (Regex.IsMatch(section.Groups["body"].Value, @"(?im)(?:^|;)\s*" + Regex.Escape(name) + @"\s*:\s*" + Regex.Escape(type) + @"\s*;")) return true;
        return false;
    }
    private static bool IsDeclaredInRetainedEdgeSection(string text, string name)
    {
        foreach (Match section in Regex.Matches(text, @"(?is)\b(?:VAR|VAR_IN_OUT)\b(?<body>.*?)\bEND_VAR\b"))
            if (Regex.IsMatch(section.Groups["body"].Value, @"(?im)(?:^|;)\s*" + Regex.Escape(name) + @"\s*:\s*Bool\s*;")) return true;
        return false;
    }
    private static void Cleanup(string directory) { try { if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory)) Directory.Delete(directory, true); } catch { } }

    private sealed class ResolvedBlock
    {
        public ResolvedBlock(object engineeringObject, PlcBlockIdentity identity, BlockInfoDetail info) { EngineeringObject = engineeringObject; Identity = identity; Info = info; }
        public object EngineeringObject { get; }
        public PlcBlockIdentity Identity { get; }
        public BlockInfoDetail Info { get; }
    }
    private sealed class ResolvedType
    {
        public ResolvedType(object engineeringObject, PlcBlockIdentity identity) { EngineeringObject = engineeringObject; Identity = identity; }
        public object EngineeringObject { get; }
        public PlcBlockIdentity Identity { get; }
    }
    private sealed class RollbackEvidence
    {
        public RollbackEvidence(bool operationSucceeded, bool hashMatches, string? expectedHash, string? actualHash) { OperationSucceeded = operationSucceeded; HashMatches = hashMatches; ExpectedHash = expectedHash; ActualHash = actualHash; }
        public bool OperationSucceeded { get; }
        public bool HashMatches { get; }
        public string? ExpectedHash { get; }
        public string? ActualHash { get; }
    }
    private sealed class StoredPlan
    {
        public StoredPlan(PlcDocumentImportPlan plan, string backupDirectory) { Plan = plan; BackupDirectory = backupDirectory; }
        public PlcDocumentImportPlan Plan { get; }
        public string BackupDirectory { get; }
    }
}
