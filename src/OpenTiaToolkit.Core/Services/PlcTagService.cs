using OpenTiaToolkit.Core.Models;
using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
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
                    var tagsOverPage100 = new Dictionary<string, List<TagInfo>>(StringComparer.OrdinalIgnoreCase);
                    var tagsUnderPage100 = new Dictionary<string, List<TagInfo>>(StringComparer.OrdinalIgnoreCase);
                    var tagsByFullName = new Dictionary<string, List<TagInfo>>(StringComparer.OrdinalIgnoreCase);
                    var tagsByAddress = new Dictionary<string, List<TagInfo>>(StringComparer.OrdinalIgnoreCase);
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
                            var tagInfo = new TagInfo
                            {
                                TagObject = tag,
                                TagTableName = tagTableName,
                                FullName = nonNullTagName,
                                NormalizedAddress = TagUpdateCsvParser.NormalizeLogicalAddress(_v21Adapter.GetLogicalAddress(tag) ?? string.Empty),
                                BaseName = string.Empty,
                                PageNumber = -1
                            };

                            AddTagIndex(tagsByFullName, nonNullTagName, tagInfo);
                            if (!string.IsNullOrEmpty(tagInfo.NormalizedAddress))
                            {
                                AddTagIndex(tagsByAddress, tagInfo.NormalizedAddress, tagInfo);
                            }

                            tagsByTable[tagTableName].Add(tagInfo);
                            result.Summary.ExistingTagsScanned++;

                            var match = tagPattern.Match(nonNullTagName);
                            if (!match.Success)
                            {
                                continue;
                            }

                            int pageNumber = int.Parse(match.Groups[1].Value);
                            string baseName = match.Groups[2].Value;
                            tagInfo.BaseName = baseName;
                            tagInfo.PageNumber = pageNumber;

                            if (pageNumber >= 100)
                            {
                                AddTagIndex(tagsOverPage100, baseName, tagInfo);
                            }
                            else
                            {
                                AddTagIndex(tagsUnderPage100, nonNullTagName, tagInfo);
                            }
                        }
                    }

                    _logger?.LogInformation(
                        "Scanned {ExistingTags} existing tags ({OverPage} page>=100, {UnderPage} page<100)",
                        result.Summary.ExistingTagsScanned,
                        tagsOverPage100.Count,
                        tagsUnderPage100.Count);

                    var csvNameCounts = csvRows
                        .GroupBy(row => row.TagName, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
                    var csvTagNames = new HashSet<string>(
                        csvRows.Select(row => row.TagName),
                        StringComparer.OrdinalIgnoreCase);
                    var reservedTagNames = new HashSet<string>(tagsByFullName.Keys, StringComparer.OrdinalIgnoreCase);
                    var seenCsvAddresses = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    foreach (var csvRow in csvRows)
                    {
                        if (!string.IsNullOrEmpty(csvRow.Address))
                        {
                            if (seenCsvAddresses.TryGetValue(csvRow.Address, out var firstAddressLineNumber))
                            {
                                result.Errors.Add(new TagUpdateError
                                {
                                    Item = csvRow.TagName,
                                    Error = $"Duplicate CSV address '{csvRow.Address}'. First occurrence was on line {firstAddressLineNumber}.",
                                    LineNumber = csvRow.LineNumber
                                });
                                result.Summary.TagsSkipped++;
                                continue;
                            }

                            seenCsvAddresses[csvRow.Address] = csvRow.LineNumber;

                            if (tagsByAddress.TryGetValue(csvRow.Address, out var addressMatches) && addressMatches.Count > 0)
                            {
                                if (addressMatches.Count > 1)
                                {
                                    var exactAddressMatches = addressMatches
                                        .Where(match => string.Equals(match.FullName, csvRow.TagName, StringComparison.OrdinalIgnoreCase))
                                        .ToList();
                                    if (exactAddressMatches.Count == 1)
                                    {
                                        var exactAddressMatch = exactAddressMatches[0];
                                        var obsoleteAddressMatches = addressMatches
                                            .Where(match => !ReferenceEquals(match.TagObject, exactAddressMatch.TagObject)
                                                && !csvTagNames.Contains(match.FullName))
                                            .ToList();
                                        var stillAmbiguousMatches = addressMatches
                                            .Where(match => !ReferenceEquals(match.TagObject, exactAddressMatch.TagObject)
                                                && csvTagNames.Contains(match.FullName))
                                            .ToList();

                                        if (stillAmbiguousMatches.Count == 0
                                            && DeleteTags(obsoleteAddressMatches, $"Duplicate address reused by CSV tag '{csvRow.TagName}'", result, dryRun, csvRow.LineNumber))
                                        {
                                            UpdateMatchedTagFromCsvRow(
                                                csvRow,
                                                exactAddressMatch,
                                                result,
                                                dryRun,
                                                matchedBy: "Address",
                                                allowRename: false);
                                            continue;
                                        }
                                    }

                                    AddAmbiguousMatchError(csvRow, $"logical address '{csvRow.Address}'", addressMatches, result);
                                    continue;
                                }

                                var addressMatch = addressMatches[0];
                                if (string.Equals(addressMatch.FullName, csvRow.TagName, StringComparison.OrdinalIgnoreCase))
                                {
                                    UpdateMatchedTagFromCsvRow(
                                        csvRow,
                                        addressMatch,
                                        result,
                                        dryRun,
                                        matchedBy: "Address",
                                        allowRename: false);
                                    continue;
                                }

                                if (!csvTagNames.Contains(addressMatch.FullName))
                                {
                                    ReplaceTagAtAddressFromCsvRow(csvRow, addressMatch, tagTables, reservedTagNames, result, dryRun);
                                    continue;
                                }

                                if (!tagsByFullName.ContainsKey(csvRow.TagName))
                                {
                                    CreateTagFromCsvRow(csvRow, tagTables, tagsByTable, reservedTagNames, result, dryRun, csvPageNumber: null);
                                    continue;
                                }
                            }
                        }

                        var match = tagPattern.Match(csvRow.TagName);
                        if (!match.Success)
                        {
                            CreateTagFromCsvRow(csvRow, tagTables, tagsByTable, reservedTagNames, result, dryRun, csvPageNumber: null);
                            continue;
                        }

                        int csvPageNumber = int.Parse(match.Groups[1].Value);
                        string csvBaseName = match.Groups[2].Value;

                        if (csvPageNumber >= 100)
                        {
                            ProcessPage100OrAboveRow(csvRow, tagsOverPage100, tagsByFullName, tagsByTable, tagTables, reservedTagNames, result, dryRun, csvNameCounts[csvRow.TagName], csvTagNames);
                        }
                        else
                        {
                            ProcessPageBelow100Row(csvRow, tagsUnderPage100, tagsByFullName, tagsByTable, tagTables, reservedTagNames, result, dryRun, csvPageNumber, csvNameCounts[csvRow.TagName]);
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
            Dictionary<string, List<TagInfo>> tagsOverPage100,
            Dictionary<string, List<TagInfo>> tagsByFullName,
            Dictionary<string, List<TagInfo>> tagsByTable,
            IEnumerable<object> tagTables,
            HashSet<string> reservedTagNames,
            TagUpdateResult result,
            bool dryRun,
            int csvNameCount,
            HashSet<string> csvTagNames)
        {
            var match = Regex.Match(csvRow.TagName, @"^(\d+)([a-zA-Z0-9_]+)$");
            int csvPageNumber = int.Parse(match.Groups[1].Value);
            string csvBaseName = match.Groups[2].Value;

            if (csvNameCount == 1 && tagsByFullName.TryGetValue(csvRow.TagName, out var exactNameMatches) && exactNameMatches.Count > 0)
            {
                if (exactNameMatches.Count > 1)
                {
                    AddAmbiguousMatchError(csvRow, $"full tag name '{csvRow.TagName}'", exactNameMatches, result);
                    return;
                }

                UpdateMatchedTagFromCsvRow(
                    csvRow,
                    exactNameMatches[0],
                    result,
                    dryRun,
                    matchedBy: "FullName",
                    allowRename: false);
                return;
            }

            if (csvNameCount == 1 && tagsOverPage100.TryGetValue(csvBaseName, out var matchingTags))
            {
                if (matchingTags.Count > 1)
                {
                    AddAmbiguousMatchError(csvRow, $"base name '{csvBaseName}'", matchingTags, result);
                    return;
                }

                var tagInfo = matchingTags[0];
                if (csvTagNames.Contains(tagInfo.FullName))
                {
                    CreateTagFromCsvRow(csvRow, tagTables, tagsByTable, reservedTagNames, result, dryRun, csvPageNumber);
                    return;
                }

                if (HasExactNameConflict(csvRow.TagName, tagInfo, tagsByFullName))
                {
                    AddExactNameConflictError(csvRow, tagsByFullName[csvRow.TagName], result);
                    return;
                }

                UpdateMatchedTagFromCsvRow(
                    csvRow,
                    tagInfo,
                    result,
                    dryRun,
                    matchedBy: "BaseName",
                    allowRename: true);
                return;
            }

            CreateTagFromCsvRow(csvRow, tagTables, tagsByTable, reservedTagNames, result, dryRun, csvPageNumber);
        }

        private void ProcessPageBelow100Row(
            TagUpdateRow csvRow,
            Dictionary<string, List<TagInfo>> tagsUnderPage100,
            Dictionary<string, List<TagInfo>> tagsByFullName,
            Dictionary<string, List<TagInfo>> tagsByTable,
            IEnumerable<object> tagTables,
            HashSet<string> reservedTagNames,
            TagUpdateResult result,
            bool dryRun,
            int csvPageNumber,
            int csvNameCount)
        {
            if (csvNameCount == 1 && tagsUnderPage100.TryGetValue(csvRow.TagName, out var matchingTags))
            {
                if (matchingTags.Count > 1)
                {
                    AddAmbiguousMatchError(csvRow, $"full tag name '{csvRow.TagName}'", matchingTags, result);
                    return;
                }

                var tagInfo = matchingTags[0];
                UpdateMatchedTagFromCsvRow(
                    csvRow,
                    tagInfo,
                    result,
                    dryRun,
                    matchedBy: "FullName",
                    allowRename: false);
                return;
            }

            CreateTagFromCsvRow(csvRow, tagTables, tagsByTable, reservedTagNames, result, dryRun, csvPageNumber);
        }

        private void UpdateMatchedTagFromCsvRow(
            TagUpdateRow csvRow,
            TagInfo tagInfo,
            TagUpdateResult result,
            bool dryRun,
            string matchedBy,
            bool allowRename)
        {
            var oldAddress = _v21Adapter.GetLogicalAddress(tagInfo.TagObject);
            var oldComment = GetCommentText(tagInfo.TagObject);
            var newName = allowRename ? csvRow.TagName : tagInfo.FullName;
            var newAddress = string.IsNullOrEmpty(csvRow.Address) ? oldAddress ?? string.Empty : csvRow.Address;
            var newComment = csvRow.Comment ?? oldComment;

            if (!dryRun)
            {
                try
                {
                    if (allowRename && !string.Equals(tagInfo.FullName, csvRow.TagName, StringComparison.Ordinal))
                    {
                        _v21Adapter.SetName(tagInfo.TagObject, csvRow.TagName);
                    }

                    if (!string.IsNullOrEmpty(csvRow.Address))
                    {
                        _v21Adapter.SetLogicalAddress(tagInfo.TagObject, csvRow.Address);
                    }

                    if (csvRow.Comment != null)
                    {
                        if (SetTagComment(tagInfo.TagObject, csvRow.Comment))
                        {
                            newComment = csvRow.Comment;
                        }
                        else
                        {
                            result.Warnings.Add(new TagUpdateWarning
                            {
                                Item = csvRow.TagName,
                                Reason = $"Tag was updated, but failed to apply comment for {csvRow.TagName}.",
                                LineNumber = csvRow.LineNumber
                            });
                            newComment = oldComment;
                        }
                    }
                }
                catch (Exception ex)
                {
                    result.Errors.Add(new TagUpdateError
                    {
                        Item = csvRow.TagName,
                        Error = "Failed to update tag",
                        Details = AddSafetyLoginReminder(ex.Message),
                        LineNumber = csvRow.LineNumber
                    });
                    result.Summary.TagsSkipped++;
                    return;
                }
            }

            result.Updated.Add(new TagUpdateEntry
            {
                TagTableName = tagInfo.TagTableName,
                MatchedBy = matchedBy,
                OldName = tagInfo.FullName,
                NewName = newName,
                OldAddress = oldAddress,
                NewAddress = newAddress,
                OldComment = oldComment,
                NewComment = newComment
            });
            result.Summary.TagsUpdated++;
        }

        private void CreateTagFromCsvRow(
            TagUpdateRow csvRow,
            IEnumerable<object> tagTables,
            Dictionary<string, List<TagInfo>> tagsByTable,
            HashSet<string> reservedTagNames,
            TagUpdateResult result,
            bool dryRun,
            int? csvPageNumber)
        {
            var dataType = string.IsNullOrEmpty(csvRow.DataType)
                ? InferDataTypeFromAddress(csvRow.Address)
                : csvRow.DataType;

            if (string.IsNullOrEmpty(dataType))
            {
                result.Errors.Add(new TagUpdateError
                {
                    Item = csvRow.TagName,
                    Error = "DataType is required for new tags when it cannot be inferred from the address",
                    LineNumber = csvRow.LineNumber
                });
                result.Summary.TagsSkipped++;
                return;
            }

            var targetTagTable = FindBestTagTableForAddress(tagTables, csvRow.Address, tagsByTable)
                ?? (csvPageNumber.HasValue ? FindBestTagTableForPage(tagTables, csvPageNumber.Value, tagsByTable) : null);
            if (targetTagTable == null)
            {
                result.Errors.Add(new TagUpdateError
                {
                    Item = csvRow.TagName,
                    Error = csvPageNumber.HasValue
                        ? "No tag table found for creating new tags"
                        : "No tag table could be inferred from the address for creating a new non-page tag",
                    LineNumber = csvRow.LineNumber
                });
                result.Summary.TagsSkipped++;
                return;
            }

            var targetTableName = _v21Adapter.GetName(targetTagTable) ?? "Default";
            var tagName = AllocateTagName(csvRow.TagName, reservedTagNames);

            if (!dryRun)
            {
                try
                {
                    var address = string.IsNullOrEmpty(csvRow.Address) ? string.Empty : csvRow.Address;
                    if (!_v21Adapter.TryCreateTag(targetTagTable, tagName, dataType, address, out var createdTag, out var failureReason))
                    {
                        result.Errors.Add(new TagUpdateError
                        {
                            Item = csvRow.TagName,
                            Error = AddSafetyLoginReminder(failureReason ?? "Tag creation failed"),
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
                        Details = AddSafetyLoginReminder($"{ex.GetType().Name}: {ex.Message}"),
                        LineNumber = csvRow.LineNumber
                    });
                    result.Summary.TagsSkipped++;
                    return;
                }
            }

            result.Created.Add(new TagCreateEntry
            {
                TagTableName = targetTableName,
                TagName = tagName,
                DataType = dataType,
                Address = csvRow.Address,
                Comment = csvRow.Comment
            });
            result.Summary.TagsCreated++;
        }

        private void ReplaceTagAtAddressFromCsvRow(
            TagUpdateRow csvRow,
            TagInfo tagInfo,
            IEnumerable<object> tagTables,
            HashSet<string> reservedTagNames,
            TagUpdateResult result,
            bool dryRun)
        {
            var dataType = string.IsNullOrEmpty(csvRow.DataType)
                ? InferDataTypeFromAddress(csvRow.Address)
                : csvRow.DataType;

            if (string.IsNullOrEmpty(dataType))
            {
                result.Errors.Add(new TagUpdateError
                {
                    Item = csvRow.TagName,
                    Error = "DataType is required for replacement tags when it cannot be inferred from the address",
                    LineNumber = csvRow.LineNumber
                });
                result.Summary.TagsSkipped++;
                return;
            }

            var targetTagTable = tagTables.FirstOrDefault(table =>
                _v21Adapter.GetName(table)?.Equals(tagInfo.TagTableName, StringComparison.OrdinalIgnoreCase) == true);
            if (targetTagTable == null)
            {
                result.Errors.Add(new TagUpdateError
                {
                    Item = csvRow.TagName,
                    Error = $"Could not find tag table '{tagInfo.TagTableName}' for replacement",
                    LineNumber = csvRow.LineNumber
                });
                result.Summary.TagsSkipped++;
                return;
            }

            var oldAddress = _v21Adapter.GetLogicalAddress(tagInfo.TagObject);
            reservedTagNames.Remove(tagInfo.FullName);
            var tagName = AllocateTagName(csvRow.TagName, reservedTagNames);
            if (!dryRun)
            {
                try
                {
                    if (!_v21Adapter.TryDelete(tagInfo.TagObject))
                    {
                        result.Errors.Add(new TagUpdateError
                        {
                            Item = csvRow.TagName,
                            Error = $"Could not delete old tag '{tagInfo.FullName}' before replacement",
                            LineNumber = csvRow.LineNumber
                        });
                        result.Summary.TagsSkipped++;
                        return;
                    }

                    var address = string.IsNullOrEmpty(csvRow.Address) ? string.Empty : csvRow.Address;
                    if (!_v21Adapter.TryCreateTag(targetTagTable, tagName, dataType, address, out var createdTag, out var failureReason))
                    {
                        result.Errors.Add(new TagUpdateError
                        {
                            Item = csvRow.TagName,
                            Error = AddSafetyLoginReminder(failureReason ?? "Replacement tag creation failed"),
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
                                Reason = $"Tag was replaced, but failed to apply comment for {csvRow.TagName}.",
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
                        Error = "Failed to replace tag",
                        Details = AddSafetyLoginReminder($"{ex.GetType().Name}: {ex.Message}"),
                        LineNumber = csvRow.LineNumber
                    });
                    result.Summary.TagsSkipped++;
                    return;
                }
            }

            result.Deleted.Add(new TagDeleteEntry
            {
                TagTableName = tagInfo.TagTableName,
                TagName = tagInfo.FullName,
                Address = oldAddress,
                Reason = $"Address reused by CSV tag '{csvRow.TagName}'"
            });
            result.Summary.TagsDeleted++;

            result.Created.Add(new TagCreateEntry
            {
                TagTableName = tagInfo.TagTableName,
                TagName = tagName,
                DataType = dataType,
                Address = csvRow.Address,
                Comment = csvRow.Comment
            });
            result.Summary.TagsCreated++;
        }

        private static void AddTagIndex(Dictionary<string, List<TagInfo>> index, string key, TagInfo tagInfo)
        {
            if (!index.TryGetValue(key, out var matches))
            {
                matches = new List<TagInfo>();
                index[key] = matches;
            }

            matches.Add(tagInfo);
        }

        private static string AllocateTagName(string requestedName, HashSet<string> reservedTagNames)
        {
            if (reservedTagNames.Add(requestedName))
            {
                return requestedName;
            }

            var suffix = 1;
            string candidate;
            do
            {
                candidate = $"{requestedName}({suffix})";
                suffix++;
            }
            while (!reservedTagNames.Add(candidate));

            return candidate;
        }

        private static bool HasExactNameConflict(
            string tagName,
            TagInfo allowedTag,
            Dictionary<string, List<TagInfo>> tagsByFullName)
        {
            return tagsByFullName.TryGetValue(tagName, out var exactNameMatches)
                && exactNameMatches.Any(match => !ReferenceEquals(match.TagObject, allowedTag.TagObject));
        }

        private static void AddAmbiguousMatchError(
            TagUpdateRow csvRow,
            string matchDescription,
            List<TagInfo> matches,
            TagUpdateResult result)
        {
            result.Errors.Add(new TagUpdateError
            {
                Item = csvRow.TagName,
                Error = $"Multiple existing tags match {matchDescription}. Update is ambiguous.",
                Details = FormatTagLocations(matches),
                LineNumber = csvRow.LineNumber
            });
            result.Summary.TagsSkipped++;
        }

        private static void AddExactNameConflictError(
            TagUpdateRow csvRow,
            List<TagInfo> matches,
            TagUpdateResult result)
        {
            result.Errors.Add(new TagUpdateError
            {
                Item = csvRow.TagName,
                Error = $"A tag named '{csvRow.TagName}' already exists. Creation or rename would create a duplicate.",
                Details = FormatTagLocations(matches),
                LineNumber = csvRow.LineNumber
            });
            result.Summary.TagsSkipped++;
        }

        private static string FormatTagLocations(IEnumerable<TagInfo> matches)
        {
            return string.Join(
                "; ",
                matches.Select(match => $"[{match.TagTableName}] {match.FullName}"));
        }

        private static string AddSafetyLoginReminder(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return message;
            }

            return message.IndexOf("permission to modify the safety program", StringComparison.OrdinalIgnoreCase) >= 0
                ? message + " Log into the safety program in TIA Portal, or rerun with --safety-password when Openness safety login is available."
                : message;
        }

        private bool DeleteTags(
            IEnumerable<TagInfo> tags,
            string reason,
            TagUpdateResult result,
            bool dryRun,
            int? lineNumber)
        {
            foreach (var tag in tags)
            {
                var oldAddress = _v21Adapter.GetLogicalAddress(tag.TagObject);
                if (!dryRun)
                {
                    try
                    {
                        if (!_v21Adapter.TryDelete(tag.TagObject))
                        {
                            result.Errors.Add(new TagUpdateError
                            {
                                Item = tag.FullName,
                                Error = AddSafetyLoginReminder($"Could not delete obsolete tag '{tag.FullName}'"),
                                LineNumber = lineNumber
                            });
                            result.Summary.TagsSkipped++;
                            return false;
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add(new TagUpdateError
                        {
                            Item = tag.FullName,
                            Error = "Failed to delete obsolete tag",
                            Details = AddSafetyLoginReminder($"{ex.GetType().Name}: {ex.Message}"),
                            LineNumber = lineNumber
                        });
                        result.Summary.TagsSkipped++;
                        return false;
                    }
                }

                result.Deleted.Add(new TagDeleteEntry
                {
                    TagTableName = tag.TagTableName,
                    TagName = tag.FullName,
                    Address = oldAddress,
                    Reason = reason
                });
                result.Summary.TagsDeleted++;
            }

            return true;
        }

        private static string InferDataTypeFromAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                return string.Empty;
            }

            var normalized = TagUpdateCsvParser.NormalizeLogicalAddress(address);
            if (Regex.IsMatch(normalized, @"^%[IQM]\d+\.\d+$", RegexOptions.IgnoreCase))
            {
                return "Bool";
            }

            if (Regex.IsMatch(normalized, @"^%[IQM]B\d+$", RegexOptions.IgnoreCase))
            {
                return "Byte";
            }

            if (Regex.IsMatch(normalized, @"^%[IQM]W\d+$", RegexOptions.IgnoreCase))
            {
                return "Word";
            }

            if (Regex.IsMatch(normalized, @"^%[IQM]D\d+$", RegexOptions.IgnoreCase))
            {
                return "DWord";
            }

            return string.Empty;
        }

        private object? FindBestTagTableForAddress(
            IEnumerable<object> tagTables,
            string address,
            Dictionary<string, List<TagInfo>> tagsByTable)
        {
            var addressInfo = TryParseAddressLocation(address);
            if (addressInfo == null)
            {
                return null;
            }

            var candidateTables = tagsByTable
                .Where(kvp => kvp.Value.Any(tag =>
                {
                    var tagAddressInfo = TryParseAddressLocation(tag.NormalizedAddress);
                    return tagAddressInfo != null
                        && tagAddressInfo.Value.Area == addressInfo.Value.Area
                        && tagAddressInfo.Value.ByteNumber == addressInfo.Value.ByteNumber;
                }))
                .Select(kvp => kvp.Key)
                .ToList();

            if (candidateTables.Count == 0)
            {
                candidateTables = tagsByTable
                    .Where(kvp => kvp.Value.Any(tag =>
                    {
                        var tagAddressInfo = TryParseAddressLocation(tag.NormalizedAddress);
                        return tagAddressInfo != null
                            && tagAddressInfo.Value.Area == addressInfo.Value.Area;
                    }))
                    .OrderBy(kvp => kvp.Value
                        .Select(tag => TryParseAddressLocation(tag.NormalizedAddress))
                        .Where(parsed => parsed != null && parsed.Value.Area == addressInfo.Value.Area)
                        .Min(parsed => Math.Abs(parsed!.Value.ByteNumber - addressInfo.Value.ByteNumber)))
                    .Select(kvp => kvp.Key)
                    .Take(1)
                    .ToList();
            }

            if (candidateTables.Count == 0)
            {
                return null;
            }

            var bestTableName = candidateTables
                .OrderByDescending(tableName => tagsByTable[tableName].Count(tag =>
                {
                    var tagAddressInfo = TryParseAddressLocation(tag.NormalizedAddress);
                    return tagAddressInfo != null
                        && tagAddressInfo.Value.Area == addressInfo.Value.Area
                        && tagAddressInfo.Value.ByteNumber == addressInfo.Value.ByteNumber;
                }))
                .First();

            return tagTables.FirstOrDefault(table =>
                _v21Adapter.GetName(table)?.Equals(bestTableName, StringComparison.OrdinalIgnoreCase) == true);
        }

        private static AddressLocation? TryParseAddressLocation(string address)
        {
            var normalized = TagUpdateCsvParser.NormalizeLogicalAddress(address);
            var match = Regex.Match(normalized, @"^%(?<area>[IQM])(?:(?<size>[BWD]))?(?<byte>\d+)(?:\.(?<bit>\d+))?$", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return null;
            }

            return new AddressLocation(
                match.Groups["area"].Value.ToUpperInvariant(),
                int.Parse(match.Groups["byte"].Value));
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

            public string NormalizedAddress { get; set; } = string.Empty;

            public string BaseName { get; set; } = string.Empty;

            public int PageNumber { get; set; }
        }

        private struct AddressLocation
        {
            public AddressLocation(string area, int byteNumber)
            {
                Area = area;
                ByteNumber = byteNumber;
            }

            public string Area { get; }

            public int ByteNumber { get; }
        }
    }
}
