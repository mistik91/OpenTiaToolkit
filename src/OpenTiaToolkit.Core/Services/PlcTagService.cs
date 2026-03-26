using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Services
{
    /// <summary>
    /// PLC tag operations extracted from PlcService.
    /// </summary>
    internal sealed class PlcTagService
    {
        private readonly EngineeringSessionContext _sessionContext;
        private readonly V21OpennessAdapter _v21Adapter;
        private readonly ILogger? _logger;

        public PlcTagService(EngineeringSessionContext sessionContext, ILogger? logger, V21OpennessAdapter? v21Adapter = null)
        {
            _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
            _v21Adapter = v21Adapter ?? new V21OpennessAdapter();
            _logger = logger;
        }

        public async Task<FindReplaceResult> FindAndReplaceTagsAsync(
            Dictionary<string, string> renameMap,
            bool caseInsensitive = false,
            bool dryRun = true,
            bool substringMatch = false)
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
                    "FindAndReplaceTagsAsync called with {Count} mappings, caseInsensitive={CaseInsensitive}, dryRun={DryRun}, substringMatch={SubstringMatch}",
                    renameMap.Count,
                    caseInsensitive,
                    dryRun,
                    substringMatch);

                try
                {
                    var tagTables = _v21Adapter.GetTagTables(plcSoftware);
                    if (tagTables == null)
                    {
                        result.Summary.Errors = 1;
                        result.Errors.Add(new ErrorEntry
                        {
                            ItemType = "System",
                            Error = "PLC software does not expose accessible tag tables"
                        });
                        return result;
                    }

                    var existingTagNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var tagTable in tagTables)
                    {
                        var tagTableName = _v21Adapter.GetName(tagTable) ?? "Unknown";
                        _logger?.LogDebug("Processing tag table: {TagTableName}", tagTableName);

                        var tags = _v21Adapter.GetTags(tagTable);
                        if (tags == null)
                        {
                            continue;
                        }

                        foreach (var tag in tags)
                        {
                            result.Summary.TotalProcessed++;

                            var oldName = _v21Adapter.GetName(tag) ?? "Unknown";
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
                                        _v21Adapter.SetName(tag, newName);
                                        existingTagNames.Add(newName);
                                        result.Summary.ChangesApplied++;
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
            });
        }

        public async Task<CreateTagResult> CreateTagAsync(
            string tagName,
            string dataType,
            string? logicalAddress = null,
            string? tagTableName = null)
        {
            var result = new CreateTagResult();

            return await Task.Run(() =>
            {
                var plcSoftware = _sessionContext.PlcSoftware;
                var plcName = _sessionContext.CurrentPlcName;
                if (plcSoftware == null || string.IsNullOrEmpty(plcName))
                {
                    result.Error = new ErrorEntry
                    {
                        ItemType = "System",
                        Error = "No PLC software is currently open"
                    };
                    return result;
                }

                _logger?.LogInformation(
                    "CreateTagAsync called: TagName={TagName}, DataType={DataType}, Address={Address}, TagTable={TagTable}",
                    tagName,
                    dataType,
                    logicalAddress ?? string.Empty,
                    tagTableName ?? "default");

                try
                {
                    if (string.IsNullOrWhiteSpace(tagName))
                    {
                        result.Error = new ErrorEntry
                        {
                            ItemType = "Tag",
                            Error = "Tag name cannot be empty"
                        };
                        return result;
                    }

                    if (string.IsNullOrWhiteSpace(dataType))
                    {
                        result.Error = new ErrorEntry
                        {
                            ItemType = "Tag",
                            Name = tagName,
                            Error = "Data type cannot be empty"
                        };
                        return result;
                    }

                    var tagTables = _v21Adapter.GetTagTables(plcSoftware);
                    if (tagTables == null)
                    {
                        result.Error = new ErrorEntry
                        {
                            ItemType = "System",
                            Error = "PLC software does not expose accessible tag tables"
                        };
                        return result;
                    }

                    object? targetTagTable = null;
                    string? actualTableName = null;

                    if (!string.IsNullOrEmpty(tagTableName))
                    {
                        var requestedTagTableName = tagTableName!;
                        targetTagTable = _v21Adapter.FindTagTable(plcSoftware, requestedTagTableName);
                        actualTableName = targetTagTable == null ? null : _v21Adapter.GetName(targetTagTable);

                        if (targetTagTable == null)
                        {
                            result.Error = new ErrorEntry
                            {
                                ItemType = "TagTable",
                                Error = $"Tag table '{tagTableName}' not found"
                            };
                            return result;
                        }
                    }
                    else
                    {
                        targetTagTable = tagTables.FirstOrDefault();
                        actualTableName = targetTagTable == null ? null : _v21Adapter.GetName(targetTagTable) ?? "Default";
                    }

                    if (targetTagTable == null)
                    {
                        result.Error = new ErrorEntry
                        {
                            ItemType = "System",
                            Error = "No tag table found in PLC software"
                        };
                        return result;
                    }

                    var tags = _v21Adapter.GetTags(targetTagTable);
                    if (tags == null)
                    {
                        result.Error = new ErrorEntry
                        {
                            ItemType = "TagTable",
                            Error = "Tags collection is null or not accessible"
                        };
                        return result;
                    }

                    foreach (var tag in tags)
                    {
                        var name = _v21Adapter.GetName(tag);
                        if (string.Equals(name, tagName, StringComparison.OrdinalIgnoreCase))
                        {
                            result.Error = new ErrorEntry
                            {
                                ItemType = "Tag",
                                Name = tagName,
                                Path = BuildPath(actualTableName, tagName),
                                Error = "Tag already exists",
                                Details = $"A tag with the name '{tagName}' already exists in tag table '{actualTableName}'"
                            };
                            return result;
                        }
                    }

                    try
                    {
                        var address = logicalAddress ?? string.Empty;
                        if (!_v21Adapter.TryCreateTag(targetTagTable, tagName, dataType, address, out var createdTag, out var failureReason))
                        {
                            result.Error = new ErrorEntry
                            {
                                ItemType = "System",
                                Error = failureReason ?? "Tag creation failed",
                                Details = "The TIA Portal API version may not support direct tag creation"
                            };
                            return result;
                        }

                        result.Success = true;
                        result.TagName = _v21Adapter.GetName(createdTag!);
                        result.TagTableName = actualTableName;
                        result.DataType = _v21Adapter.GetDataType(createdTag!);
                        result.LogicalAddress = _v21Adapter.GetLogicalAddress(createdTag!);

                        _logger?.LogInformation("CreateTagAsync succeeded: {TagName} created in {TableName}", tagName, actualTableName);
                    }
                    catch (Exception ex)
                    {
                        result.Error = new ErrorEntry
                        {
                            ItemType = "Tag",
                            Name = tagName,
                            Error = "Exception during tag creation",
                            Details = $"{ex.GetType().Name}: {ex.Message}"
                        };
                        _logger?.LogError(ex, "CreateTagAsync failed for tag {TagName}", tagName);
                    }

                    return result;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error during CreateTagAsync operation");
                    result.Error = new ErrorEntry
                    {
                        ItemType = "System",
                        Error = "Error during CreateTagAsync operation",
                        Details = ex.Message
                    };
                    return result;
                }
            });
        }

        public async Task<TagUpdateResult> UpdateTagsFromCsvAsync(string csvFilePath, bool dryRun = true)
        {
            var result = new TagUpdateResult();

            return await Task.Run(() =>
            {
                var plcSoftware = _sessionContext.PlcSoftware;
                var plcName = _sessionContext.CurrentPlcName;
                if (plcSoftware == null || string.IsNullOrEmpty(plcName))
                {
                    result.Errors.Add(new TagUpdateError
                    {
                        Item = "System",
                        Error = "No PLC software is currently open"
                    });
                    return result;
                }

                _logger?.LogInformation("UpdateTagsFromCsvAsync called: CsvPath={CsvPath}, DryRun={DryRun}", csvFilePath, dryRun);

                try
                {
                    List<TagUpdateRow> csvRows;
                    try
                    {
                        csvRows = TagUpdateCsvParser.Parse(csvFilePath);
                        result.Summary.TotalCsvRows = csvRows.Count;
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add(new TagUpdateError
                        {
                            Item = csvFilePath,
                            Error = "Failed to parse CSV file",
                            Details = ex.Message
                        });
                        return result;
                    }

                    var tagPattern = new Regex(@"^(\d+)([a-zA-Z0-9_]+)$");
                    var tagsOverPage100 = new Dictionary<string, TagInfo>(StringComparer.OrdinalIgnoreCase);
                    var tagsUnderPage100 = new Dictionary<string, TagInfo>(StringComparer.OrdinalIgnoreCase);
                    var tagsByTable = new Dictionary<string, List<TagInfo>>(StringComparer.OrdinalIgnoreCase);

                    var tagTables = _v21Adapter.GetTagTables(plcSoftware);
                    if (tagTables == null)
                    {
                        result.Errors.Add(new TagUpdateError
                        {
                            Item = "System",
                            Error = "PLC software does not expose accessible tag tables"
                        });
                        return result;
                    }

                    foreach (var tagTable in tagTables)
                    {
                        var tagTableName = _v21Adapter.GetName(tagTable) ?? "Unknown";
                        var tags = _v21Adapter.GetTags(tagTable);
                        if (tags == null)
                        {
                            continue;
                        }

                        if (!tagsByTable.ContainsKey(tagTableName))
                        {
                            tagsByTable[tagTableName] = new List<TagInfo>();
                        }

                        foreach (var tag in tags)
                        {
                            var tagName = _v21Adapter.GetName(tag);
                            if (string.IsNullOrEmpty(tagName))
                            {
                                continue;
                            }

                            var nonNullTagName = tagName!;
                            var match = tagPattern.Match(nonNullTagName);
                            if (!match.Success)
                            {
                                continue;
                            }

                            int pageNumber = int.Parse(match.Groups[1].Value);
                            string baseName = match.Groups[2].Value;

                            var tagInfo = new TagInfo
                            {
                                TagObject = tag,
                                TagTableName = tagTableName,
                                FullName = nonNullTagName,
                                BaseName = baseName,
                                PageNumber = pageNumber
                            };

                            if (pageNumber >= 100)
                            {
                                if (!tagsOverPage100.ContainsKey(baseName))
                                {
                                    tagsOverPage100[baseName] = tagInfo;
                                }
                            }
                            else if (!tagsUnderPage100.ContainsKey(nonNullTagName))
                            {
                                tagsUnderPage100[nonNullTagName] = tagInfo;
                            }

                            tagsByTable[tagTableName].Add(tagInfo);
                            result.Summary.ExistingTagsScanned++;
                        }
                    }

                    _logger?.LogInformation(
                        "Scanned {ExistingTags} existing tags ({OverPage} page>=100, {UnderPage} page<100)",
                        result.Summary.ExistingTagsScanned,
                        tagsOverPage100.Count,
                        tagsUnderPage100.Count);

                    foreach (var csvRow in csvRows)
                    {
                        var match = tagPattern.Match(csvRow.TagName);
                        if (!match.Success)
                        {
                            result.Warnings.Add(new TagUpdateWarning
                            {
                                Item = csvRow.TagName,
                                Reason = "Tag name does not match pattern ^\\d+[a-zA-Z0-9_]+$ - skipping",
                                LineNumber = csvRow.LineNumber
                            });
                            result.Summary.TagsSkipped++;
                            continue;
                        }

                        int csvPageNumber = int.Parse(match.Groups[1].Value);
                        string csvBaseName = match.Groups[2].Value;

                        if (csvPageNumber >= 100)
                        {
                            ProcessPage100OrAboveRow(csvRow, tagsOverPage100, tagsByTable, tagTables, result, dryRun);
                        }
                        else
                        {
                            ProcessPageBelow100Row(csvRow, tagsUnderPage100, tagsByTable, tagTables, result, dryRun, csvPageNumber);
                        }
                    }

                    _logger?.LogInformation(
                        "UpdateTagsFromCsvAsync completed: {TotalCsvRows} CSV rows, {ExistingTags} scanned, {Updated} updated, {Created} created, {Skipped} skipped, {Warnings} warnings, {Errors} errors",
                        result.Summary.TotalCsvRows,
                        result.Summary.ExistingTagsScanned,
                        result.Summary.TagsUpdated,
                        result.Summary.TagsCreated,
                        result.Summary.TagsSkipped,
                        result.Warnings.Count,
                        result.Errors.Count);

                    return result;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error during UpdateTagsFromCsvAsync operation");
                    result.Errors.Add(new TagUpdateError
                    {
                        Item = "System",
                        Error = "Error during UpdateTagsFromCsvAsync operation",
                        Details = ex.Message
                    });
                    return result;
                }
            });
        }

        private void ProcessPage100OrAboveRow(
            TagUpdateRow csvRow,
            Dictionary<string, TagInfo> tagsOverPage100,
            Dictionary<string, List<TagInfo>> tagsByTable,
            IEnumerable<object> tagTables,
            TagUpdateResult result,
            bool dryRun)
        {
            var match = Regex.Match(csvRow.TagName, @"^(\d+)([a-zA-Z0-9_]+)$");
            int csvPageNumber = int.Parse(match.Groups[1].Value);
            string csvBaseName = match.Groups[2].Value;

            if (tagsOverPage100.TryGetValue(csvBaseName, out var tagInfo))
            {
                var oldAddress = _v21Adapter.GetLogicalAddress(tagInfo.TagObject);
                var oldComment = GetCommentText(tagInfo.TagObject);

                if (!dryRun)
                {
                    try
                    {
                        if (!string.Equals(tagInfo.FullName, csvRow.TagName, StringComparison.Ordinal))
                        {
                            _v21Adapter.SetName(tagInfo.TagObject, csvRow.TagName);
                        }

                        if (!string.IsNullOrEmpty(csvRow.Address))
                        {
                            _v21Adapter.SetLogicalAddress(tagInfo.TagObject, csvRow.Address);
                        }

                        if (csvRow.Comment != null)
                        {
                            SetTagComment(tagInfo.TagObject, csvRow.Comment);
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add(new TagUpdateError
                        {
                            Item = csvRow.TagName,
                            Error = "Failed to update tag",
                            Details = ex.Message,
                            LineNumber = csvRow.LineNumber
                        });
                        result.Summary.TagsSkipped++;
                        return;
                    }
                }

                result.Updated.Add(new TagUpdateEntry
                {
                    TagTableName = tagInfo.TagTableName,
                    MatchedBy = "BaseName",
                    OldName = tagInfo.FullName,
                    NewName = csvRow.TagName,
                    OldAddress = oldAddress,
                    NewAddress = csvRow.Address,
                    OldComment = oldComment,
                    NewComment = csvRow.Comment
                });
                result.Summary.TagsUpdated++;
                return;
            }

            CreateTagFromCsvRow(csvRow, tagTables, tagsByTable, result, dryRun, csvPageNumber);
        }

        private void ProcessPageBelow100Row(
            TagUpdateRow csvRow,
            Dictionary<string, TagInfo> tagsUnderPage100,
            Dictionary<string, List<TagInfo>> tagsByTable,
            IEnumerable<object> tagTables,
            TagUpdateResult result,
            bool dryRun,
            int csvPageNumber)
        {
            if (tagsUnderPage100.TryGetValue(csvRow.TagName, out var tagInfo))
            {
                var oldAddress = _v21Adapter.GetLogicalAddress(tagInfo.TagObject);
                var oldComment = GetCommentText(tagInfo.TagObject);

                if (!dryRun)
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(csvRow.Address))
                        {
                            _v21Adapter.SetLogicalAddress(tagInfo.TagObject, csvRow.Address);
                        }

                        if (csvRow.Comment != null)
                        {
                            SetTagComment(tagInfo.TagObject, csvRow.Comment);
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add(new TagUpdateError
                        {
                            Item = csvRow.TagName,
                            Error = "Failed to update tag",
                            Details = ex.Message,
                            LineNumber = csvRow.LineNumber
                        });
                        result.Summary.TagsSkipped++;
                        return;
                    }
                }

                result.Updated.Add(new TagUpdateEntry
                {
                    TagTableName = tagInfo.TagTableName,
                    MatchedBy = "FullName",
                    OldName = tagInfo.FullName,
                    NewName = csvRow.TagName,
                    OldAddress = oldAddress,
                    NewAddress = csvRow.Address,
                    OldComment = oldComment,
                    NewComment = csvRow.Comment ?? oldComment
                });
                result.Summary.TagsUpdated++;
                return;
            }

            CreateTagFromCsvRow(csvRow, tagTables, tagsByTable, result, dryRun, csvPageNumber);
        }

        private void CreateTagFromCsvRow(
            TagUpdateRow csvRow,
            IEnumerable<object> tagTables,
            Dictionary<string, List<TagInfo>> tagsByTable,
            TagUpdateResult result,
            bool dryRun,
            int csvPageNumber)
        {
            if (string.IsNullOrEmpty(csvRow.DataType))
            {
                result.Errors.Add(new TagUpdateError
                {
                    Item = csvRow.TagName,
                    Error = "DataType is required for new tags",
                    LineNumber = csvRow.LineNumber
                });
                result.Summary.TagsSkipped++;
                return;
            }

            var targetTagTable = FindBestTagTableForPage(tagTables, csvPageNumber, tagsByTable);
            if (targetTagTable == null)
            {
                result.Errors.Add(new TagUpdateError
                {
                    Item = csvRow.TagName,
                    Error = "No tag table found for creating new tags"
                });
                result.Summary.TagsSkipped++;
                return;
            }

            var targetTableName = _v21Adapter.GetName(targetTagTable) ?? "Default";

            if (!dryRun)
            {
                try
                {
                    var address = string.IsNullOrEmpty(csvRow.Address) ? string.Empty : csvRow.Address;
                    if (!_v21Adapter.TryCreateTag(targetTagTable, csvRow.TagName, csvRow.DataType, address, out var createdTag, out var failureReason))
                    {
                        result.Errors.Add(new TagUpdateError
                        {
                            Item = csvRow.TagName,
                            Error = failureReason ?? "Tag creation failed",
                            LineNumber = csvRow.LineNumber
                        });
                        result.Summary.TagsSkipped++;
                        return;
                    }

                    if (csvRow.Comment != null)
                    {
                        bool commentSuccess = SetTagComment(createdTag!, csvRow.Comment);
                        if (!commentSuccess)
                        {
                            result.Warnings.Add(new TagUpdateWarning
                            {
                                Item = csvRow.TagName,
                                Reason = $"Tag was created/updated, but failed to apply comment for {csvRow.TagName}.",
                                LineNumber = csvRow.LineNumber
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    result.Errors.Add(new TagUpdateError
                    {
                        Item = csvRow.TagName,
                        Error = "Failed to create tag",
                        Details = $"{ex.GetType().Name}: {ex.Message}",
                        LineNumber = csvRow.LineNumber
                    });
                    result.Summary.TagsSkipped++;
                    return;
                }
            }

            result.Created.Add(new TagCreateEntry
            {
                TagTableName = targetTableName,
                TagName = csvRow.TagName,
                DataType = csvRow.DataType,
                Address = csvRow.Address,
                Comment = csvRow.Comment
            });
            result.Summary.TagsCreated++;
        }

        private object? FindBestTagTableForPage(
            IEnumerable<object> tagTables,
            int pageNumber,
            Dictionary<string, List<TagInfo>> tagsByTable)
        {
            var tablesWithSamePage = tagsByTable
                .Where(kvp => kvp.Value.Any(tag => tag.PageNumber == pageNumber))
                .Select(kvp => kvp.Key)
                .ToList();

            if (tablesWithSamePage.Count == 1)
            {
                var tableName = tablesWithSamePage.First();
                return tagTables.FirstOrDefault(table =>
                    _v21Adapter.GetName(table)?.Equals(tableName, StringComparison.OrdinalIgnoreCase) == true);
            }

            if (tablesWithSamePage.Count > 1)
            {
                var bestTableName = tagsByTable
                    .Where(kvp => tablesWithSamePage.Contains(kvp.Key))
                    .OrderByDescending(kvp => kvp.Value.Count(tag => tag.PageNumber == pageNumber))
                    .First().Key;

                return tagTables.FirstOrDefault(table =>
                    _v21Adapter.GetName(table)?.Equals(bestTableName, StringComparison.OrdinalIgnoreCase) == true);
            }

            return tagTables.FirstOrDefault();
        }

        private string? GetCommentText(object tag, string culture = "en-US")
        {
            return _v21Adapter.GetTagCommentText(tag, culture);
        }

        private bool SetTagComment(object tag, string commentText, string culture = "en-US")
        {
            var success = _v21Adapter.TrySetTagComment(tag, commentText, culture);
            if (success)
            {
                _logger?.LogDebug("Successfully set comment for culture: {Culture}", culture);
                return true;
            }

            _logger?.LogWarning(
                "Culture '{Culture}' is not enabled in the TIA Portal Project Languages. Cannot set comment.",
                culture);
            return false;
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

        private sealed class TagInfo
        {
            public object TagObject { get; set; } = null!;

            public string TagTableName { get; set; } = string.Empty;

            public string FullName { get; set; } = string.Empty;

            public string BaseName { get; set; } = string.Empty;

            public int PageNumber { get; set; }
        }
    }
}
