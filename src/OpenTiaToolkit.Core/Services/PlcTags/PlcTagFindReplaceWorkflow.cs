using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Services.PlcTags
{
    internal sealed class PlcTagFindReplaceWorkflow
    {
        private readonly EngineeringSessionContext _sessionContext;
        private readonly IPlcTagRepository _repository;
        private readonly ILogger? _logger;

        public PlcTagFindReplaceWorkflow(EngineeringSessionContext sessionContext, IPlcTagRepository repository, ILogger? logger)
        {
            _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _logger = logger;
        }

        public async Task<FindReplaceResult> FindAndReplaceTagsAsync(
            Dictionary<string, string> renameMap,
            bool caseInsensitive = false,
            bool dryRun = true,
            bool substringMatch = false,
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
                    "FindAndReplaceTagsAsync called with {Count} mappings, caseInsensitive={CaseInsensitive}, dryRun={DryRun}, substringMatch={SubstringMatch}",
                    renameMap.Count,
                    caseInsensitive,
                    dryRun,
                    substringMatch);

                try
                {
                    ReportProgress(progress, "Reading PLC tag tables.", 0, null, "read-tag-tables");
                    var catalog = _repository.ReadCatalog(plcSoftware);
                    if (catalog == null)
                    {
                        result.Summary.Errors = 1;
                        result.Errors.Add(new ErrorEntry
                        {
                            ItemType = "System",
                            Error = "PLC software does not expose accessible tag tables"
                        });
                        return result;
                    }

                    var totalTags = catalog.Tags.Count;
                    var existingTagNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var tagTable in catalog.Tables)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var tagTableName = tagTable.Name;
                        _logger?.LogDebug("Processing tag table: {TagTableName}", tagTableName);

                        foreach (var tag in catalog.Tags.Where(item => ReferenceEquals(item.Table, tagTable)))
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            result.Summary.TotalProcessed++;

                            var oldName = tag.Name;
                            var tagPath = BuildPath(tagTableName, oldName);
                            existingTagNames.Add(oldName);

                            try
                            {
                                string? newName = null;

                                if (substringMatch)
                                {
                                    foreach (var kvp in renameMap)
                                    {
                                        bool matchFound = caseInsensitive
                                            ? oldName.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase) >= 0
                                            : oldName.Contains(kvp.Key);

                                        if (matchFound)
                                        {
                                            if (caseInsensitive)
                                            {
                                                var index = oldName.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase);
                                                newName = oldName.Substring(0, index) + kvp.Value + oldName.Substring(index + kvp.Key.Length);
                                            }
                                            else
                                            {
                                                newName = oldName.Replace(kvp.Key, kvp.Value);
                                            }
                                            break;
                                        }
                                    }
                                }
                                else if (caseInsensitive)
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

                                if (newName == null)
                                {
                                    result.Skipped.Add(new SkippedEntry
                                    {
                                        ItemType = "Tag",
                                        Path = tagPath,
                                        Name = oldName,
                                        Reason = substringMatch ? "No substring match in renameMap" : "No match in renameMap"
                                    });
                                    result.Summary.Skipped++;
                                    continue;
                                }

                                if (string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase))
                                {
                                    result.Skipped.Add(new SkippedEntry
                                    {
                                        ItemType = "Tag",
                                        Path = tagPath,
                                        Name = oldName,
                                        Reason = "New name is identical to old name"
                                    });
                                    result.Summary.Skipped++;
                                    continue;
                                }

                                result.Summary.MatchesFound++;

                                if (!dryRun)
                                {
                                    if (existingTagNames.Contains(newName))
                                    {
                                        result.Warnings.Add(new WarningEntry
                                        {
                                            Code = "COLLISION",
                                            Message = $"Name '{newName}' already exists in tag tables",
                                            Path = tagPath
                                        });
                                        result.Summary.Warnings++;
                                        continue;
                                    }

                                    try
                                    {
                                        cancellationToken.ThrowIfCancellationRequested();
                                        _repository.SetName(tag.TagObject!, newName);
                                        tag.Name = newName;
                                        existingTagNames.Add(newName);
                                        result.Summary.ChangesApplied++;
                                    }
                                    catch (OperationCanceledException)
                                    {
                                        throw;
                                    }
                                    catch (Exception ex)
                                    {
                                        result.Errors.Add(new ErrorEntry
                                        {
                                            ItemType = "Tag",
                                            Path = tagPath,
                                            Name = oldName,
                                            Error = "Failed to rename tag",
                                            Details = ex.Message
                                        });
                                        result.Summary.Errors++;
                                        continue;
                                    }
                                }

                                result.Changes.Add(new ChangeEntry
                                {
                                    ItemType = "Tag",
                                    Path = tagPath,
                                    Before = oldName,
                                    After = newName
                                });
                            }
                            catch (OperationCanceledException)
                            {
                                throw;
                            }
                            catch (Exception ex)
                            {
                                result.Errors.Add(new ErrorEntry
                                {
                                    ItemType = "Tag",
                                    Path = tagPath,
                                    Name = oldName,
                                    Error = "Failed to rename tag",
                                    Details = ex.Message
                                });
                                result.Summary.Errors++;
                            }

                            ReportProgress(
                                progress,
                                $"Processed PLC tag {tagPath}.",
                                result.Summary.TotalProcessed,
                                totalTags,
                                "find-replace-tags");
                        }
                    }

                    if (dryRun)
                    {
                        var collisionWarnings = DetectCollisions(result.Changes, existingTagNames);
                        result.Warnings.AddRange(collisionWarnings);
                        result.Summary.Warnings += collisionWarnings.Count;
                    }

                    _logger?.LogInformation(
                        "FindAndReplaceTagsAsync completed: {TotalProcessed} processed, {MatchesFound} matches, {ChangesApplied} changes, {Skipped} skipped, {Warnings} warnings, {Errors} errors",
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
                    _logger?.LogError(ex, "Error during FindAndReplaceTagsAsync operation");
                    result.Summary.Errors = 1;
                    result.Errors.Add(new ErrorEntry
                    {
                        ItemType = "System",
                        Error = "Error during FindAndReplaceTagsAsync operation",
                        Details = ex.Message
                    });
                    return result;
                }
            }, cancellationToken);
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


        private static List<WarningEntry> DetectCollisions(List<ChangeEntry> changes, HashSet<string> existingNames)
        {
            var warnings = new List<WarningEntry>();
            var newNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var change in changes)
            {
                if (change.After == null)
                {
                    continue;
                }

                if (existingNames.Contains(change.After) || newNames.Contains(change.After))
                {
                    warnings.Add(new WarningEntry
                    {
                        Code = "COLLISION",
                        Message = $"Name '{change.After}' already exists or will be created multiple times",
                        Path = change.Path
                    });
                }

                newNames.Add(change.After);
            }

            return warnings;
        }
    }
}
