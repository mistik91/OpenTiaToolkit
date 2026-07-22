using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using OpenTiaToolkit.Core.Services.V21;

namespace OpenTiaToolkit.Core.Services
{
    /// <summary>
    /// PLC block mutation operations extracted from PlcService.
    /// </summary>
    internal sealed class PlcBlockMutationService
    {
        private readonly EngineeringSessionContext _sessionContext;
        private readonly PlcBrowseService _browseService;
        private readonly V21DocumentAccess _documents;
        private readonly V21EngineeringObjectAccess _objects;
        private readonly ILogger? _logger;
        private bool _diagnosticsRun;

        public PlcBlockMutationService(
            EngineeringSessionContext sessionContext,
            PlcBrowseService browseService,
            ILogger? logger,
            V21OpennessAdapter? v21Adapter = null)
        {
            _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
            _browseService = browseService ?? throw new ArgumentNullException(nameof(browseService));
            var adapter = v21Adapter ?? new V21OpennessAdapter();
            _documents = adapter.Documents;
            _objects = adapter.Objects;
            _logger = logger;
        }

        public Task<bool> DeleteBlocksAsync(string filterRegex, bool confirm = false)
        {
            return Task.Run(() =>
            {
                var plcSoftware = _sessionContext.PlcSoftware;
                var plcName = _sessionContext.CurrentPlcName;
                if (plcSoftware == null || string.IsNullOrEmpty(plcName))
                {
                    _logger?.LogWarning("No PLC software is currently open");
                    return false;
                }

                _logger?.LogInformation("Deleting blocks matching filter: {FilterRegex} (confirm: {Confirm})", filterRegex, confirm);

                try
                {
                    var regex = new Regex(filterRegex, RegexOptions.IgnoreCase);
                    var blockGroup = TiaReflectionHelper.GetBlockGroup(plcSoftware);
                    if (blockGroup == null)
                    {
                        _logger?.LogError("Could not find BlockGroup property");
                        return false;
                    }

                    var blocksToDelete = EnumerateBlocksRecursive(blockGroup)
                        .Where(block =>
                        {
                            var name = TiaReflectionHelper.GetName(block) ?? string.Empty;
                            return regex.IsMatch(name);
                        })
                        .ToList();

                    if (blocksToDelete.Count == 0)
                    {
                        _logger?.LogInformation("No blocks matched the filter");
                        return false;
                    }

                    _logger?.LogInformation("Found {Count} blocks to delete:", blocksToDelete.Count);
                    foreach (var block in blocksToDelete)
                    {
                        var name = TiaReflectionHelper.GetName(block) ?? string.Empty;
                        var type = TiaReflectionHelper.GetTypeName(block) ?? "Unknown";
                        var isReadOnly = (bool?)TiaReflectionHelper.GetProperty(block, "IsReadOnly") ?? false;
                        _logger?.LogInformation("  - {BlockName} ({Type}){ReadOnly}", name, type, isReadOnly ? " [Read-Only]" : string.Empty);
                    }

                    if (!confirm)
                    {
                        _logger?.LogWarning("Deletion requires confirmation. Use --confirm=true to proceed.");
                        return false;
                    }

                    int deletedCount = 0;
                    int skippedCount = 0;
                    int errorCount = 0;

                    foreach (var block in blocksToDelete)
                    {
                        try
                        {
                            var name = TiaReflectionHelper.GetName(block) ?? string.Empty;
                            var isReadOnly = (bool?)TiaReflectionHelper.GetProperty(block, "IsReadOnly") ?? false;
                            if (isReadOnly)
                            {
                                _logger?.LogWarning("Skipping read-only block: {BlockName}", name);
                                skippedCount++;
                                continue;
                            }

                            if (!_objects.TryDelete(block))
                            {
                                _logger?.LogWarning("Could not find a supported V21 delete path for block: {BlockName}", name);
                                errorCount++;
                                continue;
                            }

                            deletedCount++;
                            _logger?.LogDebug("Deleted block: {BlockName}", name);
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogWarning(ex, "Failed to delete block");
                            errorCount++;
                        }
                    }

                    _logger?.LogInformation(
                        "Deletion complete: {DeletedCount} deleted, {SkippedCount} skipped (read-only), {ErrorCount} errors",
                        deletedCount,
                        skippedCount,
                        errorCount);

                    return deletedCount > 0;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to delete blocks");
                    return false;
                }
            });
        }

        public async Task<bool> CreateBlockAsync(string blockName, string blockType, string? programmingLanguage = null)
        {
            var plcSoftware = _sessionContext.PlcSoftware;
            var plcName = _sessionContext.CurrentPlcName;
            if (plcSoftware == null || string.IsNullOrEmpty(plcName))
            {
                _logger?.LogWarning("No PLC software is currently open");
                return false;
            }

            _logger?.LogInformation(
                "Creating block: {BlockName} of type {BlockType} (language: {Language})",
                blockName,
                blockType,
                programmingLanguage ?? "default");

            try
            {
                if (string.IsNullOrWhiteSpace(blockName))
                {
                    _logger?.LogError("Block name cannot be empty");
                    return false;
                }

                var existingBlocks = await _browseService.GetBlocksAsync(null);
                if (existingBlocks.Any(block => block.Name.Equals(blockName, StringComparison.OrdinalIgnoreCase)))
                {
                    _logger?.LogError("Block already exists: {BlockName}", blockName);
                    return false;
                }

                var blockGroup = TiaReflectionHelper.GetBlockGroup(plcSoftware);
                if (blockGroup == null)
                {
                    _logger?.LogError("Could not find BlockGroup property");
                    return false;
                }

                var blocksCollection = TiaReflectionHelper.GetProperty(blockGroup, "Blocks");
                if (blocksCollection == null)
                {
                    _logger?.LogError("Blocks collection is null");
                    return false;
                }

                var blockTypeUpper = blockType.ToUpperInvariant();
                object? programmingLanguageValue = null;
                if (!string.IsNullOrEmpty(programmingLanguage))
                {
                    programmingLanguageValue = _documents.GetProgrammingLanguageValue(programmingLanguage);
                    if (programmingLanguageValue == null)
                    {
                        _logger?.LogError("Invalid programming language: {Language}", programmingLanguage);
                        return false;
                    }
                }

                object? createdBlock = null;

                if (blockTypeUpper == "FB")
                {
                    if (string.IsNullOrWhiteSpace(programmingLanguage))
                    {
                        _logger?.LogError("Programming language is required for V21 FB creation. Supported language: ProDiag");
                        return false;
                    }

                    var requestedLanguage = programmingLanguage!;
                    if (!requestedLanguage.Equals("ProDiag", StringComparison.OrdinalIgnoreCase) &&
                        !requestedLanguage.Equals("S7_PDIAG", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger?.LogError(
                            "Unsupported programming language for V21 FB creation: {Language}. Supported language: ProDiag",
                            requestedLanguage);
                        return false;
                    }

                    if (!_diagnosticsRun)
                    {
                        PlcServiceDiagnostics.ListBlocksCollectionMethods(blocksCollection, _logger);
                        _diagnosticsRun = true;
                    }

                    createdBlock = _documents.CreateFb(blocksCollection, blockName, programmingLanguageValue);
                }
                else
                {
                    _logger?.LogError(
                        "Unsupported block type for the current validated V21 creation path: {BlockType}. Supported: FB with ProDiag",
                        blockType);
                    return false;
                }

                if (createdBlock != null)
                {
                    _logger?.LogInformation("Block created successfully: {BlockName}", blockName);
                    return true;
                }

                _logger?.LogError("Failed to create block: {BlockName}", blockName);
                return false;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to create block: {BlockName}", blockName);
                return false;
            }
        }

        private static IEnumerable<object> EnumerateBlocksRecursive(object group)
        {
            var blocks = TiaReflectionHelper.GetBlocks(group);
            if (blocks != null)
            {
                foreach (var block in blocks)
                {
                    yield return block;
                }
            }

            var groups = TiaReflectionHelper.GetGroups(group);
            if (groups == null)
            {
                yield break;
            }

            foreach (var childGroup in groups)
            {
                foreach (var block in EnumerateBlocksRecursive(childGroup))
                {
                    yield return block;
                }
            }
        }
    }
}
