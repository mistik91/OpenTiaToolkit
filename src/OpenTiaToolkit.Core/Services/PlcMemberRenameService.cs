using OpenTiaToolkit.Core.Models;
using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Services
{
    /// <summary>
    /// PLC GlobalDB and UDT member rename operations extracted from PlcService.
    /// </summary>
    internal sealed class PlcMemberRenameService
    {
        private readonly EngineeringSessionContext _sessionContext;
        private readonly V21OpennessAdapter _v21Adapter;
        private readonly ILogger? _logger;

        public PlcMemberRenameService(EngineeringSessionContext sessionContext, ILogger? logger, V21OpennessAdapter? v21Adapter = null)
        {
            _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
            _v21Adapter = v21Adapter ?? new V21OpennessAdapter();
            _logger = logger;
        }

        public async Task<FindReplaceResult> FindAndReplaceDbMembersAsync(
            Dictionary<string, string> renameMap,
            bool caseInsensitive = false,
            bool dryRun = true,
            string[]? targetBlocks = null)
        {
            var result = new FindReplaceResult();

            return await Task.Run(() =>
            {
                var plcSoftware = _sessionContext.PlcSoftware;
                var plcName = _sessionContext.CurrentPlcName;
                if (plcSoftware == null || string.IsNullOrEmpty(plcName))
                {
                    result.Summary.Errors = 1;
                    result.Errors.Add(new ErrorEntry
                    {
                        ItemType = "System",
                        Error = "No PLC software is currently open"
                    });
                    return result;
                }

                _logger?.LogInformation(
                    "FindAndReplaceDbMembersAsync called with {Count} mappings, caseInsensitive={CaseInsensitive}, dryRun={DryRun}, targetBlocks={TargetBlocks}",
                    renameMap.Count,
                    caseInsensitive,
                    dryRun,
                    targetBlocks?.Length ?? 0);

                try
                {
                    var targetBlockList = _v21Adapter.FindGlobalDbs(plcSoftware, targetBlocks);
                    if (targetBlockList == null)
                    {
                        result.Summary.Errors = 1;
                        result.Errors.Add(new ErrorEntry
                        {
                            ItemType = "System",
                            Error = "PLC software does not expose accessible block groups"
                        });
                        return result;
                    }

                    foreach (var block in targetBlockList)
                    {
                        var blockName = _v21Adapter.GetName(block) ?? "Unknown";
                        _logger?.LogInformation("Processing GlobalDB: {BlockName}", blockName);

                        try
                        {
                            var members = _v21Adapter.GetDbMembers(block);
                            if (members == null)
                            {
                                continue;
                            }

                            ProcessMembers(members, renameMap, caseInsensitive, dryRun, blockName, "DB Member", result);
                        }
                        catch (Exception ex)
                        {
                            result.Errors.Add(new ErrorEntry
                            {
                                ItemType = "GlobalDB",
                                Path = blockName,
                                Name = blockName,
                                Error = "Failed to process block",
                                Details = ex.Message
                            });
                            result.Summary.Errors++;
                        }
                    }

                    _logger?.LogInformation(
                        "FindAndReplaceDbMembersAsync completed: {TotalProcessed} processed, {MatchesFound} matches, {ChangesApplied} changes, {Skipped} skipped, {Warnings} warnings, {Errors} errors",
                        result.Summary.TotalProcessed,
                        result.Summary.MatchesFound,
                        result.Summary.ChangesApplied,
                        result.Summary.Skipped,
                        result.Summary.Warnings,
                        result.Summary.Errors);

                    return result;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error during FindAndReplaceDbMembersAsync operation");
                    result.Summary.Errors = 1;
                    result.Errors.Add(new ErrorEntry
                    {
                        ItemType = "System",
                        Error = "Error during FindAndReplaceDbMembersAsync operation",
                        Details = ex.Message
                    });
                    return result;
                }
            });
        }

        public async Task<FindReplaceResult> FindAndReplaceUdtMembersAsync(
            Dictionary<string, string> renameMap,
            bool caseInsensitive = false,
            bool dryRun = true,
            string[]? targetTypes = null)
        {
            var result = new FindReplaceResult();

            return await Task.Run(() =>
            {
                var plcSoftware = _sessionContext.PlcSoftware;
                var plcName = _sessionContext.CurrentPlcName;
                if (plcSoftware == null || string.IsNullOrEmpty(plcName))
                {
                    result.Summary.Errors = 1;
                    result.Errors.Add(new ErrorEntry
                    {
                        ItemType = "System",
                        Error = "No PLC software is currently open"
                    });
                    return result;
                }

                _logger?.LogInformation(
                    "FindAndReplaceUdtMembersAsync called with {Count} mappings, caseInsensitive={CaseInsensitive}, dryRun={DryRun}, targetTypes={TargetTypes}",
                    renameMap.Count,
                    caseInsensitive,
                    dryRun,
                    targetTypes?.Length ?? 0);

                try
                {
                    var targetTypeList = _v21Adapter.FindUdts(plcSoftware, targetTypes);
                    if (targetTypeList == null)
                    {
                        result.Summary.Errors = 1;
                        result.Errors.Add(new ErrorEntry
                        {
                            ItemType = "System",
                            Error = "PLC software does not expose accessible type groups"
                        });
                        return result;
                    }

                    foreach (var type in targetTypeList)
                    {
                        var typeName = _v21Adapter.GetName(type) ?? "Unknown";
                        _logger?.LogInformation("Processing UDT: {TypeName}", typeName);

                        try
                        {
                            var members = _v21Adapter.GetUdtMembers(type);
                            if (members == null)
                            {
                                continue;
                            }

                            ProcessMembers(members, renameMap, caseInsensitive, dryRun, typeName, "UDT Member", result);
                        }
                        catch (Exception ex)
                        {
                            result.Errors.Add(new ErrorEntry
                            {
                                ItemType = "UDT",
                                Path = typeName,
                                Name = typeName,
                                Error = "Failed to process type",
                                Details = ex.Message
                            });
                            result.Summary.Errors++;
                        }
                    }

                    _logger?.LogInformation(
                        "FindAndReplaceUdtMembersAsync completed: {TotalProcessed} processed, {MatchesFound} matches, {ChangesApplied} changes, {Skipped} skipped, {Warnings} warnings, {Errors} errors",
                        result.Summary.TotalProcessed,
                        result.Summary.MatchesFound,
                        result.Summary.ChangesApplied,
                        result.Summary.Skipped,
                        result.Summary.Warnings,
                        result.Summary.Errors);

                    return result;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error during FindAndReplaceUdtMembersAsync operation");
                    result.Summary.Errors = 1;
                    result.Errors.Add(new ErrorEntry
                    {
                        ItemType = "System",
                        Error = "Error during FindAndReplaceUdtMembersAsync operation",
                        Details = ex.Message
                    });
                    return result;
                }
            });
        }

        private void ProcessMembers(
            IEnumerable<object> members,
            Dictionary<string, string> renameMap,
            bool caseInsensitive,
            bool dryRun,
            string parentPath,
            string itemType,
            FindReplaceResult result)
        {
            foreach (var member in members)
            {
                ProcessMember(member, renameMap, caseInsensitive, dryRun, parentPath, itemType, result);
            }
        }

        private void ProcessMember(
            object member,
            Dictionary<string, string> renameMap,
            bool caseInsensitive,
            bool dryRun,
            string parentPath,
            string itemType,
            FindReplaceResult result)
        {
            result.Summary.TotalProcessed++;

            var memberName = _v21Adapter.GetName(member) ?? "Unknown";
            var memberPath = BuildPath(parentPath, memberName);

            try
            {
                string oldName = memberName;
                string? newName = null;

                if (caseInsensitive)
                {
                    foreach (var kvp in renameMap)
                    {
                        if (string.Equals(oldName, kvp.Key, StringComparison.OrdinalIgnoreCase))
                        {
                            newName = kvp.Value;
                            break;
                        }
                    }
                }
                else if (renameMap.ContainsKey(oldName))
                {
                    newName = renameMap[oldName];
                }

                if (newName != null)
                {
                    result.Summary.MatchesFound++;

                    if (!dryRun)
                    {
                        _v21Adapter.SetName(member, newName);
                        result.Summary.ChangesApplied++;
                    }

                    result.Changes.Add(new ChangeEntry
                    {
                        ItemType = itemType,
                        Path = memberPath,
                        Before = oldName,
                        After = newName
                    });
                }
                else
                {
                    result.Skipped.Add(new SkippedEntry
                    {
                        ItemType = itemType,
                        Path = memberPath,
                        Name = memberName,
                        Reason = "No match in renameMap"
                    });
                }

                var nestedMembers = _v21Adapter.GetNestedMembers(member);
                if (nestedMembers != null)
                {
                    ProcessMembers(nestedMembers, renameMap, caseInsensitive, dryRun, memberPath, itemType, result);
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add(new ErrorEntry
                {
                    ItemType = itemType,
                    Path = memberPath,
                    Name = memberName,
                    Error = "Failed to process member",
                    Details = ex.Message
                });
                result.Summary.Errors++;
            }
        }

        private static string BuildPath(params string?[] segments)
        {
            var validSegments = segments.Where(segment => !string.IsNullOrEmpty(segment)).ToArray();
            return string.Join(".", validSegments);
        }
    }
}
