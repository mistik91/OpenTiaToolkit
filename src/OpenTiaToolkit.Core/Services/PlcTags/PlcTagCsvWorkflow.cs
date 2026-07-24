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
using static OpenTiaToolkit.Core.Services.PlcTags.PlcTagCsvRules;

namespace OpenTiaToolkit.Core.Services.PlcTags
{
    internal sealed class PlcTagCsvWorkflow
    {
        private readonly EngineeringSessionContext _sessionContext;
        private readonly IPlcTagRepository _repository;
        private readonly ILogger? _logger;
        private readonly IPlcTagNamingStrategy _tagNamingStrategy;
        private readonly PlcTagMutationBoundary _mutations;

        public PlcTagCsvWorkflow(
            EngineeringSessionContext sessionContext,
            IPlcTagRepository repository,
            IPlcTagNamingStrategy tagNamingStrategy,
            ILogger? logger)
        {
            _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _tagNamingStrategy = tagNamingStrategy ?? throw new ArgumentNullException(nameof(tagNamingStrategy));
            _logger = logger;
            _mutations = new PlcTagMutationBoundary(repository, logger);
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

                    var tagsOverPage100 = new Dictionary<string, List<PlcTagCsvTagState>>(StringComparer.OrdinalIgnoreCase);
                    var tagsUnderPage100 = new Dictionary<string, List<PlcTagCsvTagState>>(StringComparer.OrdinalIgnoreCase);
                    var tagsByFullName = new Dictionary<string, List<PlcTagCsvTagState>>(StringComparer.OrdinalIgnoreCase);
                    var tagsByAddress = new Dictionary<string, List<PlcTagCsvTagState>>(StringComparer.OrdinalIgnoreCase);
                    var tagsByTable = new Dictionary<string, List<PlcTagCsvTagState>>(StringComparer.OrdinalIgnoreCase);

                    var catalog = _repository.ReadCatalog(plcSoftware);
                    if (catalog == null)
                    {
                        result.Errors.Add(new TagUpdateError
                        {
                            Item = "System",
                            Error = "PLC software does not expose accessible tag tables"
                        });
                        return result;
                    }

                    var tagTables = catalog.Tables.Select(table => table.TableObject).ToList();
                    foreach (var catalogTable in catalog.Tables)
                    {
                        var tagTableName = catalogTable.Name;
                        var tags = catalog.Tags.Where(tag => ReferenceEquals(tag.Table, catalogTable));

                        if (!tagsByTable.ContainsKey(tagTableName))
                        {
                            tagsByTable[tagTableName] = new List<PlcTagCsvTagState>();
                        }

                        foreach (var tag in tags)
                        {
                            var tagName = tag.Name;
                            if (string.IsNullOrEmpty(tagName))
                            {
                                continue;
                            }

                            var nonNullTagName = tagName!;
                            var tagInfo = new PlcTagCsvTagState
                            {
                                TagObject = tag.TagObject!,
                                TagTableName = tagTableName,
                                FullName = nonNullTagName,
                                NormalizedAddress = tag.LogicalAddress,
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

                            if (!_tagNamingStrategy.TryParse(nonNullTagName, out var existingNameParts))
                            {
                                continue;
                            }

                            int pageNumber = existingNameParts.PageNumber;
                            string baseName = existingNameParts.BaseName;
                            tagInfo.BaseName = baseName;
                            tagInfo.PageNumber = pageNumber;

                            if (existingNameParts.UsesBaseNameMatching)
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

                        if (!_tagNamingStrategy.TryParse(csvRow.TagName, out var csvNameParts))
                        {
                            CreateTagFromCsvRow(csvRow, tagTables, tagsByTable, reservedTagNames, result, dryRun, csvPageNumber: null);
                            continue;
                        }

                        int csvPageNumber = csvNameParts.PageNumber;

                        if (csvNameParts.UsesBaseNameMatching)
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
            Dictionary<string, List<PlcTagCsvTagState>> tagsOverPage100,
            Dictionary<string, List<PlcTagCsvTagState>> tagsByFullName,
            Dictionary<string, List<PlcTagCsvTagState>> tagsByTable,
            IEnumerable<object> tagTables,
            HashSet<string> reservedTagNames,
            TagUpdateResult result,
            bool dryRun,
            int csvNameCount,
            HashSet<string> csvTagNames)
        {
            if (!_tagNamingStrategy.TryParse(csvRow.TagName, out var csvNameParts))
            {
                CreateTagFromCsvRow(csvRow, tagTables, tagsByTable, reservedTagNames, result, dryRun, csvPageNumber: null);
                return;
            }

            int csvPageNumber = csvNameParts.PageNumber;
            string csvBaseName = csvNameParts.BaseName;

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
            Dictionary<string, List<PlcTagCsvTagState>> tagsUnderPage100,
            Dictionary<string, List<PlcTagCsvTagState>> tagsByFullName,
            Dictionary<string, List<PlcTagCsvTagState>> tagsByTable,
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
            PlcTagCsvTagState tagInfo,
            TagUpdateResult result,
            bool dryRun,
            string matchedBy,
            bool allowRename)
        {
            var oldAddress = _repository.GetLogicalAddress(tagInfo.TagObject);
            var oldComment = _repository.GetTagCommentText(tagInfo.TagObject, "en-US");
            var newName = allowRename ? csvRow.TagName : tagInfo.FullName;
            var newAddress = string.IsNullOrEmpty(csvRow.Address) ? oldAddress ?? string.Empty : csvRow.Address;
            var newComment = csvRow.Comment ?? oldComment;

            if (!dryRun)
            {
                try
                {
                    if (allowRename && !string.Equals(tagInfo.FullName, csvRow.TagName, StringComparison.Ordinal))
                    {
                        _repository.SetName(tagInfo.TagObject, csvRow.TagName);
                    }

                    if (!string.IsNullOrEmpty(csvRow.Address))
                    {
                        _repository.SetLogicalAddress(tagInfo.TagObject, csvRow.Address);
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
            Dictionary<string, List<PlcTagCsvTagState>> tagsByTable,
            HashSet<string> reservedTagNames,
            TagUpdateResult result,
            bool dryRun,
            int? csvPageNumber)
        {
            var dataType = string.IsNullOrEmpty(csvRow.DataType)
                ? PlcTagAddressPlanner.InferDataType(csvRow.Address)
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

            var targetTableName = _repository.GetName(targetTagTable) ?? "Default";
            var tagName = AllocateTagName(csvRow.TagName, reservedTagNames);

            if (!dryRun)
            {
                try
                {
                    var address = string.IsNullOrEmpty(csvRow.Address) ? string.Empty : csvRow.Address;
                    if (!_repository.TryCreateTag(targetTagTable, tagName, dataType, address, out var createdTag, out var failureReason))
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
            PlcTagCsvTagState tagInfo,
            IEnumerable<object> tagTables,
            HashSet<string> reservedTagNames,
            TagUpdateResult result,
            bool dryRun)
        {
            var dataType = string.IsNullOrEmpty(csvRow.DataType)
                ? PlcTagAddressPlanner.InferDataType(csvRow.Address)
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
                _repository.GetName(table)?.Equals(tagInfo.TagTableName, StringComparison.OrdinalIgnoreCase) == true);
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

            var oldAddress = _repository.GetLogicalAddress(tagInfo.TagObject);
            var original = new TagSnapshot
            {
                TagTable = tagInfo.TagTableName,
                Name = tagInfo.FullName,
                DataType = _repository.GetDataType(tagInfo.TagObject) ?? string.Empty,
                LogicalAddress = oldAddress ?? string.Empty,
                Comment = _repository.GetTagCommentText(tagInfo.TagObject, "en-US")
            };
            reservedTagNames.Remove(tagInfo.FullName);
            var tagName = AllocateTagName(csvRow.TagName, reservedTagNames);
            if (!dryRun)
            {
                var sourceDeleted = false;
                try
                {
                    if (!_repository.TryDelete(tagInfo.TagObject))
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

                    sourceDeleted = true;
                    var address = string.IsNullOrEmpty(csvRow.Address) ? string.Empty : csvRow.Address;
                    if (!_repository.TryCreateTag(targetTagTable, tagName, dataType, address, out var createdTag, out var failureReason))
                    {
                        _mutations.TryRestore(targetTagTable, original, out _, out var restoration);
                        reservedTagNames.Remove(tagName);
                        reservedTagNames.Add(tagInfo.FullName);
                        result.Errors.Add(new TagUpdateError
                        {
                            Item = csvRow.TagName,
                            Error = AddSafetyLoginReminder(
                                (failureReason ?? "Replacement tag creation failed") + " " + restoration),
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
                    var restoration = "The original tag was not deleted.";
                    if (sourceDeleted)
                    {
                        _mutations.TryRestore(targetTagTable, original, out _, out restoration);
                    }
                    if (sourceDeleted)
                    {
                        reservedTagNames.Remove(tagName);
                        reservedTagNames.Add(tagInfo.FullName);
                    }

                    result.Errors.Add(new TagUpdateError
                    {
                        Item = csvRow.TagName,
                        Error = "Failed to replace tag",
                        Details = AddSafetyLoginReminder($"{ex.GetType().Name}: {ex.Message}. {restoration}"),
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

        private static string AddSafetyLoginReminder(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return message;
            }

            return message.IndexOf("permission to modify the safety program", StringComparison.OrdinalIgnoreCase) >= 0
                ? message + " Log into the safety program in TIA Portal, or provide a process environment variable through --safety-password-env when Openness safety login is available."
                : message;
        }

        private bool DeleteTags(
            IEnumerable<PlcTagCsvTagState> tags,
            string reason,
            TagUpdateResult result,
            bool dryRun,
            int? lineNumber)
        {
            foreach (var tag in tags)
            {
                var oldAddress = _repository.GetLogicalAddress(tag.TagObject);
                if (!dryRun)
                {
                    try
                    {
                        if (!_repository.TryDelete(tag.TagObject))
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

        private object? FindBestTagTableForAddress(
            IEnumerable<object> tagTables,
            string address,
            Dictionary<string, List<PlcTagCsvTagState>> tagsByTable)
        {
            var addressInfo = PlcTagAddressPlanner.TryParse(address);
            if (addressInfo == null)
            {
                return null;
            }

            var candidateTables = tagsByTable
                .Where(kvp => kvp.Value.Any(tag =>
                {
                    var tagAddressInfo = PlcTagAddressPlanner.TryParse(tag.NormalizedAddress);
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
                        var tagAddressInfo = PlcTagAddressPlanner.TryParse(tag.NormalizedAddress);
                        return tagAddressInfo != null
                            && tagAddressInfo.Value.Area == addressInfo.Value.Area;
                    }))
                    .OrderBy(kvp => kvp.Value
                        .Select(tag => PlcTagAddressPlanner.TryParse(tag.NormalizedAddress))
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
                    var tagAddressInfo = PlcTagAddressPlanner.TryParse(tag.NormalizedAddress);
                    return tagAddressInfo != null
                        && tagAddressInfo.Value.Area == addressInfo.Value.Area
                        && tagAddressInfo.Value.ByteNumber == addressInfo.Value.ByteNumber;
                }))
                .First();

            return tagTables.FirstOrDefault(table =>
                _repository.GetName(table)?.Equals(bestTableName, StringComparison.OrdinalIgnoreCase) == true);
        }

        private object? FindBestTagTableForPage(
            IEnumerable<object> tagTables,
            int pageNumber,
            Dictionary<string, List<PlcTagCsvTagState>> tagsByTable)
        {
            var tablesWithSamePage = tagsByTable
                .Where(kvp => kvp.Value.Any(tag => tag.PageNumber == pageNumber))
                .Select(kvp => kvp.Key)
                .ToList();

            if (tablesWithSamePage.Count == 1)
            {
                var tableName = tablesWithSamePage.First();
                return tagTables.FirstOrDefault(table =>
                    _repository.GetName(table)?.Equals(tableName, StringComparison.OrdinalIgnoreCase) == true);
            }

            if (tablesWithSamePage.Count > 1)
            {
                var bestTableName = tagsByTable
                    .Where(kvp => tablesWithSamePage.Contains(kvp.Key))
                    .OrderByDescending(kvp => kvp.Value.Count(tag => tag.PageNumber == pageNumber))
                    .First().Key;

                return tagTables.FirstOrDefault(table =>
                    _repository.GetName(table)?.Equals(bestTableName, StringComparison.OrdinalIgnoreCase) == true);
            }

            return tagTables.FirstOrDefault();
        }

        private bool SetTagComment(object tag, string commentText, string culture = "en-US")
        {
            return _mutations.TrySetComment(tag, commentText, culture);
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
