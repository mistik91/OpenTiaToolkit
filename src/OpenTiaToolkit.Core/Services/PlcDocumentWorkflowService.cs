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

public sealed class PlcDocumentWorkflowService
{
    private static readonly TimeSpan PlanLifetime = TimeSpan.FromMinutes(10);
    private static readonly Regex ResourceReferenceRegex = new Regex(@"\bMLC_[A-Za-z0-9_]+\b", RegexOptions.Compiled);
    private static readonly Regex ResourceDefinitionRegex = new Regex(@"(?im)^\s*(?:-\s*)?id\s*:\s*(MLC_[A-Za-z0-9_]+)\s*", RegexOptions.Compiled);
    private static readonly Regex BlockRegex = new Regex(@"(?im)^\s*(FUNCTION_BLOCK|FUNCTION|DATA_BLOCK|ORGANIZATION_BLOCK)\s+(?:""(?<quoted>[^""]+)""|(?<plain>[^\s:]+))", RegexOptions.Compiled);
    private static readonly Regex BlockNumberRegex = new Regex(@"S7_BlockNumber\s*:=\s*""(?<value>[^""]+)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PreferredLanguageRegex = new Regex(@"S7_PreferredLanguage\s*:=\s*""(?<value>[^""]+)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private readonly IPlcService _plcService;
    private readonly Dictionary<string, StoredPlan> _plans = new Dictionary<string, StoredPlan>(StringComparer.Ordinal);
    private readonly object _sync = new object();

    public PlcDocumentWorkflowService(IPlcService plcService) => _plcService = plcService;

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
        if (!string.IsNullOrWhiteSpace(blockName) && !string.Equals(blockName, baseName, StringComparison.Ordinal))
            errors.Add($"Document block name '{blockName}' does not match file-name '{baseName}'.");
        if (blockType == "FC" && text.IndexOf("END_FUNCTION", StringComparison.OrdinalIgnoreCase) < 0) errors.Add("FUNCTION is missing END_FUNCTION.");
        if (blockType == "FB" && text.IndexOf("END_FUNCTION_BLOCK", StringComparison.OrdinalIgnoreCase) < 0) errors.Add("FUNCTION_BLOCK is missing END_FUNCTION_BLOCK.");
        if (blockType == "GlobalDB" && text.IndexOf("END_DATA_BLOCK", StringComparison.OrdinalIgnoreCase) < 0) errors.Add("DATA_BLOCK is missing END_DATA_BLOCK.");
        if (blockType == "OB" && text.IndexOf("END_ORGANIZATION_BLOCK", StringComparison.OrdinalIgnoreCase) < 0) errors.Add("ORGANIZATION_BLOCK is missing END_ORGANIZATION_BLOCK.");

        string[] references = ResourceReferenceRegex.Matches(text).Cast<Match>().Select(match => match.Value).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        bool resourceRequired = references.Length > 0;
        string? resourceHash = null;
        if (resourceRequired && !File.Exists(s7resPath)) errors.Add("The S7DCL contains MLC_* references, but the matching S7RES file is missing.");
        if (File.Exists(s7resPath))
        {
            byte[] resourceBytes = File.ReadAllBytes(s7resPath);
            string resourceText = DecodeUtf8(resourceBytes, errors, "S7RES");
            resourceHash = Hash(resourceBytes);
            var definitions = new HashSet<string>(ResourceDefinitionRegex.Matches(resourceText).Cast<Match>().Select(match => match.Groups[1].Value), StringComparer.Ordinal);
            foreach (string reference in references.Where(reference => !definitions.Contains(reference))) errors.Add($"S7RES does not define referenced resource '{reference}'.");
        }

        return new PlcDocumentValidationResult
        {
            Valid = errors.Count == 0,
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
            Errors = errors,
            Warnings = warnings
        };
    }

    public async Task<PlcDocumentImportPlan> PlanAsync(string plcName, string importDirectory, string fileName, string? groupPath, string importOption)
    {
        PlcDocumentValidationResult validation = Validate(importDirectory, fileName);
        if (!validation.Valid) throw new InvalidDataException("PLC documents are invalid: " + string.Join("; ", validation.Errors));
        await EnsurePlcOpen(plcName).ConfigureAwait(false);
        BlockInfoDetail? existing = await _plcService.GetBlockInfoAsync(validation.BlockName!).ConfigureAwait(false);
        string planId = Guid.NewGuid().ToString("N");
        string backupDirectory = Path.Combine(Path.GetTempPath(), "OpenTiaToolkit", "plc-document-plans", planId);
        string baselineHash = "missing";
        var warnings = new List<string>(validation.Warnings);
        if (existing != null)
        {
            Directory.CreateDirectory(backupDirectory);
            if (!await _plcService.ExportBlockAsDocumentsAsync(existing.Name, backupDirectory).ConfigureAwait(false))
                throw new InvalidOperationException($"Existing block '{existing.Name}' could not be exported as SIMATIC SD documents; a rollback-safe plan cannot be created.");
            baselineHash = HashDocumentPair(backupDirectory, existing.Name);
        }
        else
        {
            warnings.Add("The plan creates a new block. A failed apply will delete the new block and recompile the PLC.");
        }

        string token = RandomToken();
        var plan = new PlcDocumentImportPlan
        {
            PlanId = planId,
            ApplyToken = token,
            ExpiresAtUtc = DateTimeOffset.UtcNow.Add(PlanLifetime),
            PlcName = plcName,
            ImportDirectory = validation.ImportDirectory,
            FileName = validation.FileName,
            GroupPath = groupPath,
            ImportOption = string.IsNullOrWhiteSpace(importOption) ? "Override" : importOption,
            Action = existing == null ? "Create" : "Override",
            InputHash = HashDocumentPair(validation.ImportDirectory, validation.FileName),
            BaselineHash = baselineHash,
            ExistingBlock = existing,
            Validation = validation,
            Warnings = warnings
        };
        lock (_sync)
        {
            PruneExpired();
            _plans[token] = new StoredPlan(plan, backupDirectory);
        }
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
            throw new InvalidOperationException("Input documents changed after planning. Create and review a new import plan.");
        BlockInfoDetail? current = await _plcService.GetBlockInfoAsync(plan.Validation.BlockName!).ConfigureAwait(false);
        string currentBaseline = "missing";
        string currentExport = Path.Combine(Path.GetTempPath(), "OpenTiaToolkit", "plc-document-current", Guid.NewGuid().ToString("N"));
        try
        {
            if (current != null)
            {
                Directory.CreateDirectory(currentExport);
                if (!await _plcService.ExportBlockAsDocumentsAsync(current.Name, currentExport).ConfigureAwait(false))
                    throw new InvalidOperationException("The current target block could not be exported for stale-state verification.");
                currentBaseline = HashDocumentPair(currentExport, current.Name);
            }
            if (!FixedEquals(plan.BaselineHash, currentBaseline)) throw new InvalidOperationException("The target block changed after planning. Create and review a new import plan.");

            bool imported = await _plcService.ImportBlockFromDocumentsAsync(plan.ImportDirectory, plan.FileName, plan.GroupPath, plan.ImportOption).ConfigureAwait(false);
            BlockInfoDetail? verified = imported ? await _plcService.GetBlockInfoAsync(plan.Validation.BlockName!).ConfigureAwait(false) : null;
            CompilationResult? compilation = verified != null ? await _plcService.CompileSoftwareAsync().ConfigureAwait(false) : null;
            bool succeeded = imported && verified != null && compilation != null && compilation.Success && compilation.ErrorCount == 0;
            if (succeeded)
            {
                Cleanup(stored.BackupDirectory);
                return new PlcDocumentImportApplyResult { PlanId = plan.PlanId, Applied = true, StateMatched = true, Verified = true, CompileSucceeded = true, Message = "PLC documents imported, read back, and compiled successfully.", ImportedBlock = verified, Compilation = compilation };
            }

            bool rolledBack = await RollbackAsync(plan, stored.BackupDirectory).ConfigureAwait(false);
            CompilationResult rollbackCompilation = await _plcService.CompileSoftwareAsync().ConfigureAwait(false);
            Cleanup(stored.BackupDirectory);
            return new PlcDocumentImportApplyResult
            {
                PlanId = plan.PlanId,
                Applied = false,
                StateMatched = true,
                Verified = verified != null,
                CompileSucceeded = compilation != null && compilation.Success && compilation.ErrorCount == 0,
                RollbackAttempted = true,
                RolledBack = rolledBack && rollbackCompilation.Success && rollbackCompilation.ErrorCount == 0,
                Message = "Import or compile verification failed; rollback was attempted.",
                ImportedBlock = verified,
                Compilation = compilation,
                RollbackCompilation = rollbackCompilation
            };
        }
        finally { Cleanup(currentExport); }
    }

    private async Task<bool> RollbackAsync(PlcDocumentImportPlan plan, string backupDirectory)
    {
        if (plan.ExistingBlock != null)
            return await _plcService.ImportBlockFromDocumentsAsync(backupDirectory, plan.ExistingBlock.Name, plan.GroupPath, "Override").ConfigureAwait(false);
        return await _plcService.DeleteBlocksAsync("^" + Regex.Escape(plan.Validation.BlockName!) + "$", true).ConfigureAwait(false);
    }

    private async Task EnsurePlcOpen(string plcName)
    {
        if (!_plcService.IsSoftwareOpen || !string.Equals(_plcService.CurrentPlcName, plcName, StringComparison.OrdinalIgnoreCase))
            await _plcService.OpenPlcAsync(plcName).ConfigureAwait(false);
    }

    private void PruneExpired()
    {
        foreach (string token in _plans.Where(pair => pair.Value.Plan.ExpiresAtUtc <= DateTimeOffset.UtcNow).Select(pair => pair.Key).ToArray())
        {
            Cleanup(_plans[token].BackupDirectory);
            _plans.Remove(token);
        }
    }

    private static string HashDocumentPair(string directory, string baseName)
    {
        byte[] dcl = File.ReadAllBytes(Path.Combine(directory, baseName + ".s7dcl"));
        string resourcePath = Path.Combine(directory, baseName + ".s7res");
        byte[] res = File.Exists(resourcePath) ? File.ReadAllBytes(resourcePath) : Array.Empty<byte>();
        return Hash(dcl.Concat(new byte[] { 0 }).Concat(res).ToArray());
    }

    private static string DecodeUtf8(byte[] bytes, List<string> errors, string label)
    {
        if (bytes.Length == 0) return string.Empty;
        try { return new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); }
        catch (DecoderFallbackException ex) { errors.Add($"{label} is not valid UTF-8: {ex.Message}"); return string.Empty; }
    }

    private static string Hash(byte[] bytes)
    {
        using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
    }

    private static string RandomToken()
    {
        var bytes = new byte[32];
        using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static bool FixedEquals(string left, string right)
    {
        byte[] a = Encoding.UTF8.GetBytes(left); byte[] b = Encoding.UTF8.GetBytes(right);
        if (a.Length != b.Length) return false;
        int difference = 0; for (int index = 0; index < a.Length; index++) difference |= a[index] ^ b[index];
        return difference == 0;
    }

    private static string? Capture(Regex regex, string text) { Match match = regex.Match(text); return match.Success ? match.Groups["value"].Value : null; }
    private static string? FirstNonEmpty(string first, string second) => !string.IsNullOrWhiteSpace(first) ? first : (!string.IsNullOrWhiteSpace(second) ? second : null);
    private static string NormalizeBlockType(string value) => value.ToUpperInvariant() == "FUNCTION" ? "FC" : value.ToUpperInvariant() == "FUNCTION_BLOCK" ? "FB" : value.ToUpperInvariant() == "DATA_BLOCK" ? "GlobalDB" : "OB";
    private static void Cleanup(string directory) { try { if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory)) Directory.Delete(directory, true); } catch { } }

    private sealed class StoredPlan
    {
        public StoredPlan(PlcDocumentImportPlan plan, string backupDirectory) { Plan = plan; BackupDirectory = backupDirectory; }
        public PlcDocumentImportPlan Plan { get; }
        public string BackupDirectory { get; }
    }
}
