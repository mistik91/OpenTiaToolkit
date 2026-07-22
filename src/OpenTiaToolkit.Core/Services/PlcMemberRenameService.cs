using OpenTiaToolkit.Core.Models;
using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenTiaToolkit.Core.Services.V21;

namespace OpenTiaToolkit.Core.Services
{
    /// <summary>
    /// PLC GlobalDB and UDT member rename operations extracted from PlcService.
    /// </summary>
    internal sealed class PlcMemberRenameService
    {
        private readonly EngineeringSessionContext _sessionContext;
        private readonly V21PlcTagStructureAccess _structures;
        private readonly V21EngineeringObjectAccess _objects;
        private readonly ILogger? _logger;

        public PlcMemberRenameService(EngineeringSessionContext sessionContext, ILogger? logger, V21OpennessAdapter? v21Adapter = null)
        {
            _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
            var adapter = v21Adapter ?? new V21OpennessAdapter();
            _structures = adapter.Tags;
            _objects = adapter.Objects;
            _logger = logger;
        }

        public async Task<FindReplaceResult> FindAndReplaceDbMembersAsync(
            Dictionary<string, string> renameMap,
            bool caseInsensitive = false,
            bool dryRun = true,
            string[]? targetBlocks = null,
            CancellationToken cancellationToken = default,
            IProgress<BatchOperationProgress>? progress = null)
        {
            var result = new FindReplaceResult();

            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
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
                    ReportProgress(progress, "Finding target GlobalDB blocks.", 0, null, "find-global-dbs");
                    var targetBlockList = _structures.FindGlobalDbs(plcSoftware, targetBlocks);
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

                    var processedBlocks = 0;
                    foreach (var block in targetBlockList)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        processedBlocks++;
                        var blockName = _objects.GetName(block) ?? "Unknown";
                        _logger?.LogInformation("Processing GlobalDB: {BlockName}", blockName);
                        ReportProgress(
                            progress,
                            $"Processing GlobalDB {processedBlocks} of {targetBlockList.Count}: {blockName}.",
                            processedBlocks - 1,
                            targetBlockList.Count,
                            "find-replace-db-members");

                        try
                        {
                            var members = _structures.GetDbMembers(block);
                            if (members == null)
                            {
                                continue;
                            }

                            ProcessMembers(members, renameMap, caseInsensitive, dryRun, blockName, "DB Member", result, cancellationToken, progress);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
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

                        ReportProgress(
                            progress,
                            $"Processed GlobalDB {processedBlocks} of {targetBlockList.Count}: {blockName}.",
                            processedBlocks,
                            targetBlockList.Count,
                            "find-replace-db-members");
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
                catch (OperationCanceledException)
                {
                    throw;
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
            }, cancellationToken);
        }

        public async Task<FindReplaceResult> FindAndReplaceUdtMembersAsync(
            Dictionary<string, string> renameMap,
            bool caseInsensitive = false,
            bool dryRun = true,
            string[]? targetTypes = null,
            CancellationToken cancellationToken = default,
            IProgress<BatchOperationProgress>? progress = null)
        {
            var result = new FindReplaceResult();

            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
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
                    ReportProgress(progress, "Finding target UDT types.", 0, null, "find-udts");
                    var targetTypeList = _structures.FindUdts(plcSoftware, targetTypes);
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

                    var processedTypes = 0;
                    foreach (var type in targetTypeList)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        processedTypes++;
                        var typeName = _objects.GetName(type) ?? "Unknown";
                        _logger?.LogInformation("Processing UDT: {TypeName}", typeName);
                        ReportProgress(
                            progress,
                            $"Processing UDT {processedTypes} of {targetTypeList.Count}: {typeName}.",
                            processedTypes - 1,
                            targetTypeList.Count,
                            "find-replace-udt-members");

                        try
                        {
                            var members = _structures.GetUdtMembers(type);
                            if (members == null)
                            {
                                continue;
                            }

                            ProcessMembers(members, renameMap, caseInsensitive, dryRun, typeName, "UDT Member", result, cancellationToken, progress);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
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

                        ReportProgress(
                            progress,
                            $"Processed UDT {processedTypes} of {targetTypeList.Count}: {typeName}.",
                            processedTypes,
                            targetTypeList.Count,
                            "find-replace-udt-members");
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
                catch (OperationCanceledException)
                {
                    throw;
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
            }, cancellationToken);
        }

        private void ProcessMembers(
            IEnumerable<object> members,
            Dictionary<string, string> renameMap,
            bool caseInsensitive,
            bool dryRun,
            string parentPath,
            string itemType,
            FindReplaceResult result,
            CancellationToken cancellationToken,
            IProgress<BatchOperationProgress>? progress)
        {
            foreach (var member in members)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ProcessMember(member, renameMap, caseInsensitive, dryRun, parentPath, itemType, result, cancellationToken, progress);
            }
        }

        private void ProcessMember(
            object member,
            Dictionary<string, string> renameMap,
            bool caseInsensitive,
            bool dryRun,
            string parentPath,
            string itemType,
            FindReplaceResult result,
            CancellationToken cancellationToken,
            IProgress<BatchOperationProgress>? progress)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Summary.TotalProcessed++;

            var memberName = _objects.GetName(member) ?? "Unknown";
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
                        cancellationToken.ThrowIfCancellationRequested();
                        _objects.SetName(member, newName);
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

                var nestedMembers = _structures.GetNestedMembers(member);
                if (nestedMembers != null)
                {
                    ProcessMembers(nestedMembers, renameMap, caseInsensitive, dryRun, memberPath, itemType, result, cancellationToken, progress);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
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

            ReportProgress(
                progress,
                $"Processed {itemType.ToLowerInvariant()} {memberPath}.",
                result.Summary.TotalProcessed,
                null,
                itemType.Equals("DB Member", StringComparison.OrdinalIgnoreCase)
                    ? "find-replace-db-members"
                    : "find-replace-udt-members");
        }

        private static void ReportProgress(
            IProgress<BatchOperationProgress>? progress,
            string message,
            int completed,
            int? total,
            string stage)
        {
            progress?.Report(new BatchOperationProgress(message, completed, total, stage));
        }

        private static string BuildPath(params string?[] segments)
        {
            var validSegments = segments.Where(segment => !string.IsNullOrEmpty(segment)).ToArray();
            return string.Join(".", validSegments);
        }
    }
}
