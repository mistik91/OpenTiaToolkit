using OpenTiaToolkit.Core.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Services
{
    /// <summary>
    /// Agent-oriented single-screen JSON edit workflow built on the import/export pipeline.
    /// </summary>
    internal sealed class HmiScreenJsonEditService
    {
        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };

        private readonly EngineeringSessionContext _sessionContext;
        private readonly HmiJsonExportService _exportService;
        private readonly HmiJsonDiffService _diffService;
        private readonly HmiJsonImportService _importService;
        private readonly ILogger? _logger;

        public HmiScreenJsonEditService(
            EngineeringSessionContext sessionContext,
            HmiJsonExportService exportService,
            HmiJsonDiffService diffService,
            HmiJsonImportService importService,
            ILogger? logger)
        {
            _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
            _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
            _diffService = diffService ?? throw new ArgumentNullException(nameof(diffService));
            _importService = importService ?? throw new ArgumentNullException(nameof(importService));
            _logger = logger;
        }

        public async Task<HmiScreenJsonSnapshotResult?> GetScreenJsonAsync(
            string screenPath,
            HmiJsonFilterOptions? filters,
            string? workingPath)
        {
            var workingDirectory = ResolveWorkingDirectory(workingPath, screenPath, "get");
            var basePath = Path.Combine(workingDirectory, "base");
            var effectiveFilters = BuildSingleScreenFilters(screenPath, filters);

            var exportResult = await _exportService.ExportAsync(basePath, effectiveFilters);
            if (exportResult == null)
            {
                _logger?.LogWarning("Failed to export Unified HMI screen JSON: {ScreenPath}", screenPath);
                return null;
            }

            var document = HmiJsonDocumentStore.LoadDocument(exportResult.OutputPath);
            var screen = document.Screens.SingleOrDefault();
            if (screen == null)
            {
                _logger?.LogWarning("Unified HMI screen was not found during JSON export: {ScreenPath}", screenPath);
                return null;
            }

            var baseHash = ComputeScreenHash(screen);
            return new HmiScreenJsonSnapshotResult
            {
                GeneratedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                HmiName = document.HmiName ?? _sessionContext.CurrentHmiName,
                ScreenPath = screen.Path,
                BaseHash = baseHash,
                ExportPath = exportResult.OutputPath,
                SnapshotFilters = effectiveFilters,
                RecommendedPatchArgs = CreateRecommendedPatchArgs(
                    document.HmiName ?? _sessionContext.CurrentHmiName,
                    screen.Path,
                    baseHash,
                    effectiveFilters),
                Capabilities = HmiScreenJsonAgentSupport.CreateCapabilities(),
                WarningCount = document.Warnings.Count,
                Warnings = document.Warnings.ToList(),
                Screen = screen
            };
        }

        public async Task<HmiScreenJsonEditResult?> ApplyScreenJsonEditAsync(
            string screenPath,
            HmiJsonScreen editedScreen,
            string? baseHash,
            HmiJsonFilterOptions? filters,
            bool dryRun,
            HmiJsonImportExecutionOptions? executionOptions,
            string? workingPath,
            bool allowStaleBase)
        {
            return await ApplyScreenJsonEditCoreAsync(
                screenPath,
                editedScreen,
                baseHash,
                filters,
                dryRun,
                executionOptions,
                workingPath,
                allowStaleBase,
                initialSnapshot: null);
        }

        private async Task<HmiScreenJsonEditResult?> ApplyScreenJsonEditCoreAsync(
            string screenPath,
            HmiJsonScreen editedScreen,
            string? baseHash,
            HmiJsonFilterOptions? filters,
            bool dryRun,
            HmiJsonImportExecutionOptions? executionOptions,
            string? workingPath,
            bool allowStaleBase,
            HmiScreenJsonSnapshotResult? initialSnapshot)
        {
            var workingDirectory = ResolveWorkingDirectory(workingPath, screenPath, dryRun ? "plan" : "apply");
            var basePath = Path.Combine(workingDirectory, "base");
            var editedPath = Path.Combine(workingDirectory, "edited");
            var patchPath = Path.Combine(workingDirectory, "patch");
            var verifyPath = Path.Combine(workingDirectory, "verify");
            var diffBasePath = basePath;
            var effectiveFilters = BuildSingleScreenFilters(screenPath, filters);
            var options = executionOptions ?? new HmiJsonImportExecutionOptions();

            var result = new HmiScreenJsonEditResult
            {
                GeneratedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                HmiName = _sessionContext.CurrentHmiName,
                ScreenPath = screenPath,
                DryRun = dryRun,
                EffectiveFilters = effectiveFilters,
                ExpectedBaseHash = NormalizeOptionalString(baseHash)
            };

            HmiJsonScreen? baseScreen;
            if (initialSnapshot?.Screen != null)
            {
                result.BaseExportPath = initialSnapshot.ExportPath;
                result.HmiName = initialSnapshot.HmiName ?? result.HmiName;
                result.ScreenPath = initialSnapshot.ScreenPath;
                baseScreen = initialSnapshot.Screen;
                diffBasePath = initialSnapshot.ExportPath;
            }
            else
            {
                var baseExport = await _exportService.ExportAsync(basePath, effectiveFilters);
                if (baseExport == null)
                {
                    result.SafetyBlocked = true;
                    result.SafetyMessage = "Current screen export could not be created.";
                    result.Warnings.Add(result.SafetyMessage);
                    result.Guidance = HmiScreenJsonAgentSupport.CreateGuidance(result);
                    return result;
                }

                result.BaseExportPath = baseExport.OutputPath;
                var baseDocument = HmiJsonDocumentStore.LoadDocument(baseExport.OutputPath);
                baseScreen = baseDocument.Screens.SingleOrDefault();
                if (baseScreen == null)
                {
                    result.SafetyBlocked = true;
                    result.SafetyMessage = "Current screen export did not contain the requested screen.";
                    result.Warnings.Add(result.SafetyMessage);
                    result.Guidance = HmiScreenJsonAgentSupport.CreateGuidance(result);
                    return result;
                }

                result.HmiName = baseDocument.HmiName ?? result.HmiName;
                result.ScreenPath = baseScreen.Path;
            }

            result.CurrentHash = ComputeScreenHash(baseScreen);

            if (!string.IsNullOrWhiteSpace(baseHash) &&
                !result.CurrentHash.Equals(baseHash!.Trim(), StringComparison.OrdinalIgnoreCase) &&
                !allowStaleBase)
            {
                result.SafetyBlocked = true;
                result.SafetyMessage = "Current screen hash does not match the supplied base hash. Re-read the screen or set allow-stale-base=true.";
                result.Warnings.Add(result.SafetyMessage);
                result.Guidance = HmiScreenJsonAgentSupport.CreateGuidance(result);
                return result;
            }

            var normalizedEditedScreen = NormalizeEditedScreen(editedScreen, baseScreen);
            result.EditedHash = ComputeScreenHash(normalizedEditedScreen);

            var editedDocument = new HmiJsonDocument
            {
                DocumentKind = "Export",
                ExportedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                HmiName = result.HmiName ?? _sessionContext.CurrentHmiName,
                Filters = effectiveFilters
            };
            editedDocument.Screens.Add(normalizedEditedScreen);

            var editedFilePath = HmiJsonDocumentStore.ResolveExportFilePath(editedPath);
            HmiJsonDocumentStore.SaveDocument(editedDocument, editedFilePath);
            result.EditedExportPath = editedFilePath;

            result.Diff = await _diffService.CreatePatchAsync(diffBasePath!, editedPath, patchPath, effectiveFilters);
            if (result.Diff == null)
            {
                result.SafetyBlocked = true;
                result.SafetyMessage = "Screen edit patch could not be created.";
                result.Warnings.Add(result.SafetyMessage);
                result.Guidance = HmiScreenJsonAgentSupport.CreateGuidance(result);
                return result;
            }

            result.PatchPath = result.Diff.OutputPath;
            result.PatchScreen = TryLoadSingleScreen(result.Diff.OutputPath);
            result.Preflight = await _importService.ImportAsync(
                result.Diff.OutputPath,
                effectiveFilters,
                dryRun: true,
                executionOptions: new HmiJsonImportExecutionOptions
                {
                    FailOnWarning = options.FailOnWarning,
                    RequireCleanPreflight = false,
                    BackupBeforeImport = false
                });

            result.SafeToApply = result.Preflight != null &&
                                 !result.Preflight.SafetyBlocked &&
                                 result.Preflight.PropertyFailedCount == 0 &&
                                 (!options.FailOnWarning || result.Preflight.WarningCount == 0);
            if (dryRun)
            {
                result.Guidance = HmiScreenJsonAgentSupport.CreateGuidance(result);
                return result;
            }

            if (!result.SafeToApply)
            {
                result.SafetyBlocked = true;
                result.SafetyMessage = result.Preflight?.SafetyMessage ?? "Preflight reported warnings or failed property changes.";
                result.BlockedReason = result.Preflight?.BlockedReason;
                result.Warnings.Add(result.SafetyMessage);
                result.Guidance = HmiScreenJsonAgentSupport.CreateGuidance(result);
                return result;
            }

            result.Import = await _importService.ImportAsync(result.Diff.OutputPath, effectiveFilters, dryRun: false, executionOptions: options);
            result.BackupPath = result.Import?.BackupPath;
            if (result.Import == null)
            {
                result.SafetyBlocked = true;
                result.SafetyMessage = "Screen edit import did not return a result.";
                result.Warnings.Add(result.SafetyMessage);
            }
            else
            {
                HmiJsonImportService.ApplySafetyPolicy(result.Import, options, result.Import.BackupPath);
                result.SafetyBlocked = result.Import.SafetyBlocked;
                result.SafetyMessage = result.Import.SafetyMessage;
                result.BlockedReason = result.Import.BlockedReason;
            }

            result.Applied = result.Import != null && !result.SafetyBlocked;

            if (result.Applied)
            {
                result.VerifyExport = await _exportService.ExportAsync(verifyPath, effectiveFilters);
                result.VerifyExportPath = result.VerifyExport?.OutputPath;
                result.VerifiedScreen = result.VerifyExportPath == null ? null : TryLoadSingleScreen(result.VerifyExportPath);
                result.NewBaseHash = result.VerifiedScreen == null ? null : ComputeScreenHash(result.VerifiedScreen);
            }

            result.Guidance = HmiScreenJsonAgentSupport.CreateGuidance(result);
            return result;
        }

        public async Task<HmiScreenJsonEditResult?> ApplyScreenJsonPatchAsync(
            string screenPath,
            IReadOnlyList<HmiScreenJsonPatchOperation> operations,
            string? baseHash,
            HmiJsonFilterOptions? filters,
            bool dryRun,
            HmiJsonImportExecutionOptions? executionOptions,
            string? workingPath,
            bool allowStaleBase)
        {
            var effectiveFilters = string.IsNullOrWhiteSpace(baseHash)
                ? HmiScreenJsonPatchApplier.MergeOperationFilters(filters, operations)
                : HmiJsonFilterMatcher.Normalize(filters);
            var snapshot = await GetScreenJsonAsync(screenPath, effectiveFilters, workingPath);
            var result = new HmiScreenJsonEditResult
            {
                GeneratedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                HmiName = snapshot?.HmiName ?? _sessionContext.CurrentHmiName,
                ScreenPath = snapshot?.ScreenPath ?? screenPath,
                DryRun = dryRun,
                EffectiveFilters = BuildSingleScreenFilters(screenPath, effectiveFilters),
                ExpectedBaseHash = NormalizeOptionalString(baseHash),
                CurrentHash = snapshot?.BaseHash ?? string.Empty,
                BaseExportPath = snapshot?.ExportPath
            };

            if (snapshot?.Screen == null)
            {
                result.SafetyBlocked = true;
                result.SafetyMessage = "Current screen export could not be created for patch operations.";
                result.Warnings.Add(result.SafetyMessage);
                result.Guidance = HmiScreenJsonAgentSupport.CreateGuidance(result);
                return result;
            }

            var filterIssues = HmiScreenJsonPatchApplier.ValidateOperationFilters(
                BuildSingleScreenFilters(screenPath, effectiveFilters),
                operations);
            if (filterIssues.Count > 0)
            {
                result.SafetyBlocked = true;
                result.SafetyMessage = "One or more patch operations are outside the current screen JSON filters.";
                result.Warnings.Add(result.SafetyMessage);
                result.PatchOperationCount = operations.Count;
                result.PatchOperationFailedCount = filterIssues.Count;
                result.PatchOperations = filterIssues;
                result.Guidance = HmiScreenJsonAgentSupport.CreateGuidance(result);
                return result;
            }

            var patchApply = HmiScreenJsonPatchApplier.Apply(snapshot.Screen, operations);
            CopyPatchOperationResults(result, patchApply, operations.Count);
            if (patchApply.FailedCount > 0)
            {
                result.SafetyBlocked = true;
                result.SafetyMessage = "One or more screen patch operations could not be applied to the exported JSON.";
                result.Warnings.Add(result.SafetyMessage);
                result.Guidance = HmiScreenJsonAgentSupport.CreateGuidance(result);
                return result;
            }

            var editResult = await ApplyScreenJsonEditCoreAsync(
                screenPath,
                patchApply.Screen,
                baseHash,
                effectiveFilters,
                dryRun,
                executionOptions,
                workingPath,
                allowStaleBase,
                snapshot);

            if (editResult == null)
            {
                result.SafetyBlocked = true;
                result.SafetyMessage = "Screen patch edit workflow did not return a result.";
                result.Warnings.Add(result.SafetyMessage);
                result.Guidance = HmiScreenJsonAgentSupport.CreateGuidance(result);
                return result;
            }

            CopyPatchOperationResults(editResult, patchApply, operations.Count);
            editResult.Guidance = HmiScreenJsonAgentSupport.CreateGuidance(editResult);
            return editResult;
        }

        private static HmiJsonFilterOptions BuildSingleScreenFilters(string screenPath, HmiJsonFilterOptions? filters)
        {
            var normalized = HmiJsonFilterMatcher.Normalize(filters);
            return new HmiJsonFilterOptions
            {
                Screens = new[] { screenPath },
                ItemNames = normalized.ItemNames,
                Properties = normalized.Properties
            };
        }

        private static HmiJsonScreen NormalizeEditedScreen(HmiJsonScreen editedScreen, HmiJsonScreen baseScreen)
        {
            var clone = JsonSerializer.Deserialize<HmiJsonScreen>(
                JsonSerializer.Serialize(editedScreen, SerializerOptions),
                SerializerOptions) ?? throw new InvalidOperationException("Unable to clone edited screen.");

            if (string.IsNullOrWhiteSpace(clone.Path))
            {
                clone.Path = baseScreen.Path;
            }

            if (string.IsNullOrWhiteSpace(clone.Name))
            {
                clone.Name = baseScreen.Name;
            }

            return clone;
        }

        private static HmiJsonScreen? TryLoadSingleScreen(string path)
        {
            try
            {
                return HmiJsonDocumentStore.LoadDocument(path).Screens.SingleOrDefault();
            }
            catch
            {
                return null;
            }
        }

        private static string ComputeScreenHash(HmiJsonScreen screen)
        {
            var json = JsonSerializer.Serialize(screen, SerializerOptions);
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(json));
            return Convert.ToBase64String(hash);
        }

        private static string ResolveWorkingDirectory(string? workingPath, string screenPath, string operation)
        {
            var basePath = string.IsNullOrWhiteSpace(workingPath)
                ? Path.Combine(Path.GetTempPath(), "OpenTiaToolkit", "HmiScreenJsonEdit")
                : Path.GetFullPath(workingPath!);

            var safeScreenName = SanitizePathSegment(screenPath);
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
            var directory = Path.Combine(basePath, safeScreenName, operation + "_" + timestamp + "_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static string SanitizePathSegment(string value)
        {
            var builder = new StringBuilder();
            foreach (var character in value)
            {
                builder.Append(Path.GetInvalidFileNameChars().Contains(character) || character == '/' || character == '\\'
                    ? '_'
                    : character);
            }

            return builder.Length == 0 ? "screen" : builder.ToString();
        }

        private static string? NormalizeOptionalString(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
        }

        private static HmiScreenJsonRecommendedPatchArgs CreateRecommendedPatchArgs(
            string? hmiName,
            string screenPath,
            string baseHash,
            HmiJsonFilterOptions filters)
        {
            return new HmiScreenJsonRecommendedPatchArgs
            {
                HmiName = hmiName,
                ScreenPath = screenPath,
                BaseHash = baseHash,
                ItemNames = filters.ItemNames.ToList(),
                Properties = filters.Properties.ToList(),
                DryRun = true,
                Backup = true
            };
        }

        private static void CopyPatchOperationResults(
            HmiScreenJsonEditResult result,
            HmiScreenJsonPatchApplyResult patchApply,
            int operationCount)
        {
            result.PatchOperationCount = operationCount;
            result.PatchOperationSucceededCount = patchApply.OperationResults.Count(operation => operation.Succeeded);
            result.PatchOperationFailedCount = patchApply.OperationResults.Count(operation => !operation.Succeeded);
            result.PatchOperations = patchApply.OperationResults;
        }
    }
}
