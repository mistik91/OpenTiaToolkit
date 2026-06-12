using OpenTiaToolkit.Core.Models;
using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Services
{
    /// <summary>
    /// Read-only PLC browse and inspection operations extracted from PlcService.
    /// </summary>
    internal sealed class PlcBrowseService
    {
        private readonly EngineeringSessionContext _sessionContext;
        private readonly ILogger? _logger;

        public PlcBrowseService(EngineeringSessionContext sessionContext, ILogger? logger)
        {
            _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
            _logger = logger;
        }

        public async Task<SoftwareTreeNode?> GetSoftwareTreeAsync(int? maxDepth = null)
        {
            return await Task.Run(() =>
            {
                if (!TryGetOpenSoftware(out var plcSoftware, out var plcName))
                {
                    _logger?.LogWarning("No PLC software is currently open");
                    return null;
                }

                var depthStr = maxDepth.HasValue ? maxDepth.Value.ToString() : "unlimited";
                _logger?.LogInformation("Getting PLC software tree (max depth: {MaxDepth})", depthStr);

                try
                {
                    var builder = new SoftwareTreeBuilder();
                    return builder.Build(plcSoftware!, plcName, maxDepth);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to get PLC software tree");
                    return null;
                }
            });
        }

        public async Task<IReadOnlyList<BlockInfo>> GetBlocksAsync(string? filterRegex = null)
        {
            return await Task.Run(() =>
            {
                var blocks = new List<BlockInfo>();

                if (!TryGetOpenSoftware(out var plcSoftware, out _))
                {
                    _logger?.LogWarning("No PLC software is currently open");
                    return (IReadOnlyList<BlockInfo>)blocks;
                }

                _logger?.LogInformation("Getting PLC blocks (filter: {Filter})", filterRegex ?? "none");

                try
                {
                    var nonNullPlcSoftware = plcSoftware!;
                    var blocksCollection = GetBlocksCollection(nonNullPlcSoftware);
                    if (blocksCollection == null)
                    {
                        _logger?.LogError("Blocks collection is null");
                        return (IReadOnlyList<BlockInfo>)blocks;
                    }

                    foreach (var block in blocksCollection)
                    {
                        try
                        {
                            var name = TiaReflectionHelper.GetName(block) ?? "Unknown";
                            var type = TiaReflectionHelper.GetProperty(block, "Type")?.ToString();
                            var number = TiaReflectionHelper.GetProperty(block, "Number")?.ToString();
                            var language = TiaReflectionHelper.GetProperty(block, "ProgrammingLanguage")?.ToString();
                            var isReadOnly = (bool?)TiaReflectionHelper.GetProperty(block, "IsReadOnly") ?? false;

                            string? comment = null;
                            var commentObject = TiaReflectionHelper.GetProperty(block, "Comment");
                            if (commentObject != null)
                            {
                                comment = TiaReflectionHelper.GetProperty(commentObject, "Text")?.ToString() ?? commentObject.ToString();
                            }

                            blocks.Add(new BlockInfo(
                                name,
                                InferBlockTypeName(block, type),
                                language,
                                number,
                                isReadOnly,
                                comment));
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogDebug(ex, "Failed to get info for block");
                        }
                    }

                    if (!string.IsNullOrEmpty(filterRegex))
                    {
                        try
                        {
                            var regex = new Regex(filterRegex, RegexOptions.IgnoreCase);
                            blocks = blocks.Where(block => regex.IsMatch(block.Name)).ToList();
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogWarning(ex, "Invalid regex filter: {FilterRegex}", filterRegex);
                        }
                    }

                    _logger?.LogInformation("Found {BlockCount} blocks", blocks.Count);
                    return (IReadOnlyList<BlockInfo>)blocks;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to get PLC blocks");
                    return (IReadOnlyList<BlockInfo>)blocks;
                }
            });
        }

        public async Task<BlockInfoDetail?> GetBlockInfoAsync(string blockName)
        {
            return await Task.Run(() =>
            {
                if (!TryGetOpenSoftware(out var plcSoftware, out _))
                {
                    _logger?.LogWarning("No PLC software is currently open");
                    return null;
                }

                _logger?.LogInformation("Getting block info: {BlockName}", blockName);

                try
                {
                    var nonNullPlcSoftware = plcSoftware!;
                    var blocksCollection = GetBlocksCollection(nonNullPlcSoftware);
                    if (blocksCollection == null)
                    {
                        _logger?.LogError("Blocks collection is null");
                        return null;
                    }

                    foreach (var block in blocksCollection)
                    {
                        try
                        {
                            var name = TiaReflectionHelper.GetName(block) ?? string.Empty;
                            if (!name.Equals(blockName, StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            var type = TiaReflectionHelper.GetProperty(block, "Type")?.ToString();
                            var number = TiaReflectionHelper.GetProperty(block, "Number")?.ToString();
                            var language = TiaReflectionHelper.GetProperty(block, "ProgrammingLanguage")?.ToString();
                            var isReadOnly = (bool?)TiaReflectionHelper.GetProperty(block, "IsReadOnly") ?? false;

                            string? comment = null;
                            var commentObject = TiaReflectionHelper.GetProperty(block, "Comment");
                            if (commentObject != null)
                            {
                                comment = TiaReflectionHelper.GetProperty(commentObject, "Text")?.ToString() ?? commentObject.ToString();
                            }

                            var author = TiaReflectionHelper.GetProperty(block, "Author")?.ToString();
                            int memorySize = 0;
                            var memorySizeValue = TiaReflectionHelper.GetProperty(block, "MemorySize");
                            if (memorySizeValue != null)
                            {
                                int.TryParse(memorySizeValue.ToString(), out memorySize);
                            }

                            return new BlockInfoDetail(
                                name,
                                InferBlockTypeName(block, type),
                                language,
                                number,
                                isReadOnly,
                                comment,
                                author,
                                null,
                                null,
                                memorySize,
                                language);
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogDebug(ex, "Failed to get info for block");
                        }
                    }

                    _logger?.LogWarning("Block not found: {BlockName}", blockName);
                    return null;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to get block info: {BlockName}", blockName);
                    return null;
                }
            });
        }

        public async Task<IReadOnlyList<BlockInfo>> SearchBlocksAsync(string pattern, string searchIn = "name", string? blockType = null)
        {
            if (!TryGetOpenSoftware(out _, out _))
            {
                _logger?.LogWarning("No PLC software is currently open");
                return new List<BlockInfo>();
            }

            _logger?.LogInformation("Searching blocks: pattern='{Pattern}', searchIn='{SearchIn}', blockType='{BlockType}'", pattern, searchIn, blockType);

            try
            {
                var allBlocks = await GetBlocksAsync(null);
                var results = new List<BlockInfo>();

                bool searchInName = searchIn.Equals("name", StringComparison.OrdinalIgnoreCase);
                bool searchInComment = searchIn.Equals("comment", StringComparison.OrdinalIgnoreCase);
                bool searchInBoth = !searchInName && !searchInComment;

                foreach (var block in allBlocks)
                {
                    bool matches = false;

                    if ((searchInName || searchInBoth) &&
                        block.Name.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        matches = true;
                    }

                    if (!matches &&
                        (searchInComment || searchInBoth) &&
                        block.Comment is string comment &&
                        comment.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        matches = true;
                    }

                    if (matches && !string.IsNullOrEmpty(blockType))
                    {
                        matches = block.Type.Equals(blockType, StringComparison.OrdinalIgnoreCase);
                    }

                    if (matches)
                    {
                        results.Add(block);
                    }
                }

                _logger?.LogInformation("Search found {Count} matching blocks", results.Count);
                return results;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to search blocks");
                return new List<BlockInfo>();
            }
        }

        private bool TryGetOpenSoftware(out object? plcSoftware, out string? plcName)
        {
            plcSoftware = _sessionContext.PlcSoftware;
            plcName = _sessionContext.CurrentPlcName;
            return plcSoftware != null && !string.IsNullOrEmpty(plcName);
        }

        private static IEnumerable<object>? GetBlocksCollection(object plcSoftware)
        {
            var blockGroup = TiaReflectionHelper.GetBlockGroup(plcSoftware);
            return TiaReflectionHelper.GetBlocks(blockGroup);
        }

        private static string InferBlockTypeName(object block, string? explicitType)
        {
            if (!string.IsNullOrWhiteSpace(explicitType))
            {
                return explicitType!;
            }

            return block.GetType().Name switch
            {
                "FB" => "FB",
                "FC" => "FC",
                "OB" => "OB",
                "GlobalDB" => "DB",
                "InstanceDB" => "InstanceDB",
                "CodeBlock" => "CodeBlock",
                _ => "Unknown"
            };
        }
    }
}
