using OpenTiaToolkit.Core.Models;
using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
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
        private readonly IPlcTagNamingStrategy _tagNamingStrategy;
        private readonly ILogger? _logger;

        public PlcTagService(
            EngineeringSessionContext sessionContext,
            ILogger? logger,
            V21OpennessAdapter? v21Adapter = null,
            IPlcTagNamingStrategy? tagNamingStrategy = null)
        {
            _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
            _v21Adapter = v21Adapter ?? new V21OpennessAdapter();
            _tagNamingStrategy = tagNamingStrategy ?? new PageBasedPlcTagNamingStrategy();
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

                    var tagBatches = new List<Tuple<string, IReadOnlyList<object>>>();
                    foreach (var tagTable in tagTables)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var tagTableName = _v21Adapter.GetName(tagTable) ?? "Unknown";
                        var tableTags = _v21Adapter.GetTags(tagTable);
                        if (tableTags != null)
                        {
                            tagBatches.Add(Tuple.Create(tagTableName, tableTags));
                        }
                    }

                    var totalTags = tagBatches.Sum(batch => batch.Item2.Count);
                    var existingTagNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var tagBatch in tagBatches)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var tagTableName = tagBatch.Item1;
                        _logger?.LogDebug("Processing tag table: {TagTableName}", tagTableName);

                        foreach (var tag in tagBatch.Item2)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
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
                                        cancellationToken.ThrowIfCancellationRequested();
                                        _v21Adapter.SetName(tag, newName);
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

        public async Task<TagModificationResult> ModifyTagsAsync(
            IReadOnlyList<TagModificationOperation> operations,
            bool dryRun = true,
            CancellationToken cancellationToken = default,
            IProgress<BatchOperationProgress>? progress = null)
        {
            if (operations == null)
            {
                throw new ArgumentNullException(nameof(operations));
            }

            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = new TagModificationResult
                {
                    DryRun = dryRun
                };
                result.Summary.Total = operations.Count;

                var plcSoftware = _sessionContext.PlcSoftware;
                if (plcSoftware == null || string.IsNullOrEmpty(_sessionContext.CurrentPlcName))
                {
                    AddModificationFailure(result, 0, null, string.Empty, "No PLC software is currently open");
                    return result;
                }

                ReportProgress(progress, "Reading PLC tag tables.", 0, operations.Count, "read-tag-tables");
                var tagTables = _v21Adapter.GetTagTables(plcSoftware);
                if (tagTables == null)
                {
                    AddModificationFailure(result, 0, null, string.Empty, "PLC software does not expose accessible tag tables");
                    return result;
                }

                var tables = tagTables
                    .Select(table => new TagTableState
                    {
                        TableObject = table,
                        Name = _v21Adapter.GetName(table) ?? "Unknown"
                    })
                    .ToList();
                var tags = new List<MutableTagState>();
                foreach (var table in tables)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var tableTags = _v21Adapter.GetTags(table.TableObject);
                    if (tableTags == null)
                    {
                        continue;
                    }

                    tags.AddRange(tableTags
                        .Select(tag => new MutableTagState
                        {
                            TagObject = tag,
                            Table = table,
                            Name = _v21Adapter.GetName(tag) ?? string.Empty,
                            DataType = _v21Adapter.GetDataType(tag) ?? string.Empty,
                            LogicalAddress = TagUpdateCsvParser.NormalizeLogicalAddress(
                                _v21Adapter.GetLogicalAddress(tag) ?? string.Empty),
                            Comment = GetCommentText(tag)
                        })
                        .Where(tag => tag.Name.Length > 0));
                }

                for (var index = 0; index < operations.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var operation = operations[index];
                    ReportProgress(
                        progress,
                        $"Processing tag operation {index + 1} of {operations.Count}: {operation.Action}.",
                        index,
                        operations.Count,
                        dryRun ? "plan-tag-modifications" : "apply-tag-modifications");
                    try
                    {
                        ExecuteTagModification(operation, index + 1, tables, tags, result, dryRun, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        AddModificationFailure(
                            result,
                            index + 1,
                            operation.SourceLine,
                            operation.Action,
                            AddSafetyLoginReminder(ex.Message));
                    }

                    ReportProgress(
                        progress,
                        $"Processed tag operation {index + 1} of {operations.Count}: {operation.Action}.",
                        index + 1,
                        operations.Count,
                        dryRun ? "plan-tag-modifications" : "apply-tag-modifications");
                }

                return result;
            }, cancellationToken);
        }

        private void ExecuteTagModification(
            TagModificationOperation operation,
            int index,
            IReadOnlyList<TagTableState> tables,
            List<MutableTagState> tags,
            TagModificationResult result,
            bool dryRun,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var action = (operation.Action ?? string.Empty).Trim().ToLowerInvariant();
            switch (action)
            {
                case "create":
                    ExecuteCreateOperation(operation, index, tables, tags, result, dryRun, cancellationToken);
                    return;
                case "update":
                    ExecuteUpdateOperation(operation, index, tags, result, dryRun, cancellationToken);
                    return;
                case "delete":
                    ExecuteDeleteOperation(operation, index, tags, result, dryRun, cancellationToken);
                    return;
                case "replace":
                    ExecuteReplaceOperation(operation, index, tables, tags, result, dryRun, cancellationToken);
                    return;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported tag modification action '{operation.Action}'. Expected create, update, delete, or replace.");
            }
        }

        private void ExecuteCreateOperation(
            TagModificationOperation operation,
            int index,
            IReadOnlyList<TagTableState> tables,
            List<MutableTagState> tags,
            TagModificationResult result,
            bool dryRun,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequirePatchValue(operation.Set.HasTagTable, operation.Set.TagTable, "create requires set.tagTable");
            RequirePatchValue(operation.Set.HasName, operation.Set.Name, "create requires set.name");
            RequirePatchValue(operation.Set.HasDataType, operation.Set.DataType, "create requires set.dataType");

            var table = FindTable(tables, operation.Set.TagTable!);
            var snapshot = new TagSnapshot
            {
                TagTable = table.Name,
                Name = operation.Set.Name!,
                DataType = operation.Set.DataType!,
                LogicalAddress = operation.Set.HasLogicalAddress
                    ? TagUpdateCsvParser.NormalizeLogicalAddress(operation.Set.LogicalAddress ?? string.Empty)
                    : string.Empty,
                Comment = operation.Set.HasComment ? operation.Set.Comment : null
            };

            try
            {
                EnsureNoNameCollision(tags, snapshot.TagTable, snapshot.Name);
                cancellationToken.ThrowIfCancellationRequested();
                var created = CreateTagState(table, snapshot, dryRun);
                tags.Add(created);
                AddModificationSuccess(result, index, operation, before: null, snapshot, dryRun);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AddModificationFailure(
                    result,
                    index,
                    operation.SourceLine,
                    operation.Action,
                    AddSafetyLoginReminder(ex.Message),
                    before: null,
                    after: snapshot);
            }
        }

        private void ExecuteUpdateOperation(
            TagModificationOperation operation,
            int index,
            List<MutableTagState> tags,
            TagModificationResult result,
            bool dryRun,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!operation.Set.HasAnyValue)
            {
                throw new InvalidOperationException("update requires at least one set value");
            }

            var tag = FindSingleTag(tags, operation.Match);
            var before = tag.ToSnapshot();
            if (operation.Set.HasTagTable &&
                !string.Equals(operation.Set.TagTable, tag.Table.Name, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("update cannot move a tag to another tag table; use replace");
            }

            var after = ApplyPatch(before, operation.Set);
            if (!string.Equals(before.Name, after.Name, StringComparison.OrdinalIgnoreCase))
            {
                EnsureNoNameCollision(tags, after.TagTable, after.Name, tag);
            }

            if (!dryRun)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ApplyUpdate(tag, before, after, operation.Set);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    AddModificationFailure(
                        result,
                        index,
                        operation.SourceLine,
                        operation.Action,
                        AddSafetyLoginReminder(ex.Message),
                        before,
                        after);
                    return;
                }
            }

            tag.Apply(after);
            AddModificationSuccess(result, index, operation, before, after, dryRun);
        }

        private void ExecuteDeleteOperation(
            TagModificationOperation operation,
            int index,
            List<MutableTagState> tags,
            TagModificationResult result,
            bool dryRun,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tag = FindSingleTag(tags, operation.Match);
            var before = tag.ToSnapshot();
            if (!dryRun && !_v21Adapter.TryDelete(tag.TagObject!))
            {
                AddModificationFailure(
                    result,
                    index,
                    operation.SourceLine,
                    operation.Action,
                    $"Could not delete tag '{before.Name}'",
                    before,
                    after: null);
                return;
            }

            tag.IsDeleted = true;
            AddModificationSuccess(result, index, operation, before, after: null, dryRun);
        }

        private void ExecuteReplaceOperation(
            TagModificationOperation operation,
            int index,
            IReadOnlyList<TagTableState> tables,
            List<MutableTagState> tags,
            TagModificationResult result,
            bool dryRun,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = FindSingleTag(tags, operation.Match);
            var before = source.ToSnapshot();
            var after = ApplyPatch(before, operation.Set);
            var targetTable = FindTable(tables, after.TagTable);
            EnsureNoNameCollision(tags, after.TagTable, after.Name, source);

            MutableTagState replacement;
            if (dryRun)
            {
                source.IsDeleted = true;
                replacement = CreateTagState(targetTable, after, dryRun: true);
            }
            else
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_v21Adapter.TryDelete(source.TagObject!))
                {
                    AddModificationFailure(
                        result,
                        index,
                        operation.SourceLine,
                        operation.Action,
                        $"Could not delete tag '{before.Name}' before replacement",
                        before,
                        after);
                    return;
                }

                source.IsDeleted = true;
                try
                {
                    replacement = CreateTagState(targetTable, after, dryRun: false);
                }
                catch (Exception createException)
                {
                    var restored = TryRestoreReplacedTag(source.Table, before, out var rollbackFailure);
                    if (restored != null)
                    {
                        tags.Add(restored);
                    }

                    var rollbackMessage = restored != null
                        ? "The original tag was restored and verified in the in-memory operation state."
                        : $"CRITICAL: restoring the original tag failed: {rollbackFailure}";
                    AddModificationFailure(
                        result,
                        index,
                        operation.SourceLine,
                        operation.Action,
                        AddSafetyLoginReminder(
                            $"Replacement creation failed after deleting '{before.Name}'. {rollbackMessage} {createException.Message}"),
                        before,
                        after);
                    return;
                }
            }

            tags.Add(replacement);
            AddModificationSuccess(result, index, operation, before, after, dryRun);
        }

        private MutableTagState CreateTagState(TagTableState table, TagSnapshot snapshot, bool dryRun)
        {
            object? createdTag = null;
            if (!dryRun)
            {
                if (!_v21Adapter.TryCreateTag(
                    table.TableObject,
                    snapshot.Name,
                    snapshot.DataType,
                    snapshot.LogicalAddress,
                    out createdTag,
                    out var failureReason))
                {
                    throw new InvalidOperationException(failureReason ?? "Tag creation failed");
                }

                if (snapshot.Comment != null && !SetTagComment(createdTag!, snapshot.Comment))
                {
                    _v21Adapter.TryDelete(createdTag!);
                    throw new InvalidOperationException(
                        $"Tag '{snapshot.Name}' was created, but its comment could not be applied.");
                }
            }

            return new MutableTagState
            {
                TagObject = createdTag,
                Table = table,
                Name = snapshot.Name,
                DataType = snapshot.DataType,
                LogicalAddress = snapshot.LogicalAddress,
                Comment = snapshot.Comment,
                IsVirtual = dryRun
            };
        }

        private void ApplyUpdate(
            MutableTagState tag,
            TagSnapshot before,
            TagSnapshot after,
            TagValuePatch patch)
        {
            try
            {
                if (patch.HasName && !string.Equals(before.Name, after.Name, StringComparison.Ordinal))
                {
                    _v21Adapter.SetName(tag.TagObject!, after.Name);
                }

                if (patch.HasDataType && !string.Equals(before.DataType, after.DataType, StringComparison.Ordinal))
                {
                    _v21Adapter.SetDataType(tag.TagObject!, after.DataType);
                }

                if (patch.HasLogicalAddress &&
                    !string.Equals(before.LogicalAddress, after.LogicalAddress, StringComparison.Ordinal))
                {
                    _v21Adapter.SetLogicalAddress(tag.TagObject!, after.LogicalAddress);
                }

                if (patch.HasComment && !SetTagComment(tag.TagObject!, after.Comment ?? string.Empty))
                {
                    throw new InvalidOperationException($"Could not apply comment for tag '{after.Name}'");
                }
            }
            catch
            {
                TryRestoreUpdatedTag(tag.TagObject!, before);
                throw;
            }
        }

        private void TryRestoreUpdatedTag(object tag, TagSnapshot snapshot)
        {
            try
            {
                _v21Adapter.SetName(tag, snapshot.Name);
                _v21Adapter.SetDataType(tag, snapshot.DataType);
                _v21Adapter.SetLogicalAddress(tag, snapshot.LogicalAddress);
                if (snapshot.Comment != null)
                {
                    SetTagComment(tag, snapshot.Comment);
                }
            }
            catch
            {
                // Best-effort rollback; the original failure remains the primary error.
            }
        }

        private MutableTagState? TryRestoreReplacedTag(
            TagTableState table,
            TagSnapshot snapshot,
            out string? failureReason)
        {
            try
            {
                var restored = CreateTagState(table, snapshot, dryRun: false);
                failureReason = null;
                return restored;
            }
            catch (Exception ex)
            {
                failureReason = ex.Message;
                return null;
            }
        }

        private static TagSnapshot ApplyPatch(TagSnapshot source, TagValuePatch patch)
        {
            var snapshot = new TagSnapshot
            {
                TagTable = patch.HasTagTable ? patch.TagTable ?? string.Empty : source.TagTable,
                Name = patch.HasName ? patch.Name ?? string.Empty : source.Name,
                DataType = patch.HasDataType ? patch.DataType ?? string.Empty : source.DataType,
                LogicalAddress = patch.HasLogicalAddress
                    ? TagUpdateCsvParser.NormalizeLogicalAddress(patch.LogicalAddress ?? string.Empty)
                    : source.LogicalAddress,
                Comment = patch.HasComment ? patch.Comment ?? string.Empty : source.Comment
            };

            if (string.IsNullOrWhiteSpace(snapshot.TagTable))
            {
                throw new InvalidOperationException("Resulting tag table cannot be empty");
            }

            if (string.IsNullOrWhiteSpace(snapshot.Name))
            {
                throw new InvalidOperationException("Resulting tag name cannot be empty");
            }

            if (string.IsNullOrWhiteSpace(snapshot.DataType))
            {
                throw new InvalidOperationException("Resulting data type cannot be empty");
            }

            return snapshot;
        }

        private static MutableTagState FindSingleTag(
            IEnumerable<MutableTagState> tags,
            TagMatchCriteria match)
        {
            if (string.IsNullOrWhiteSpace(match.TagTable) &&
                string.IsNullOrWhiteSpace(match.Name) &&
                string.IsNullOrWhiteSpace(match.LogicalAddress))
            {
                throw new InvalidOperationException(
                    "Tag match must specify at least one of tagTable, name, or logicalAddress");
            }

            var normalizedAddress = match.LogicalAddress == null
                ? null
                : TagUpdateCsvParser.NormalizeLogicalAddress(match.LogicalAddress);
            var matches = tags
                .Where(tag => !tag.IsDeleted)
                .Where(tag => match.TagTable == null ||
                              string.Equals(tag.Table.Name, match.TagTable, StringComparison.OrdinalIgnoreCase))
                .Where(tag => match.Name == null ||
                              string.Equals(tag.Name, match.Name, StringComparison.OrdinalIgnoreCase))
                .Where(tag => normalizedAddress == null ||
                              string.Equals(tag.LogicalAddress, normalizedAddress, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matches.Count == 0)
            {
                throw new InvalidOperationException("No existing tag matches the supplied criteria");
            }

            if (matches.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Tag match is ambiguous. Matched: {string.Join("; ", matches.Select(tag => $"[{tag.Table.Name}] {tag.Name}"))}");
            }

            return matches[0];
        }

        private static TagTableState FindTable(IEnumerable<TagTableState> tables, string name)
        {
            var matches = tables
                .Where(table => string.Equals(table.Name, name, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (matches.Count == 0)
            {
                throw new InvalidOperationException($"Tag table '{name}' was not found");
            }

            if (matches.Count > 1)
            {
                throw new InvalidOperationException($"Tag table name '{name}' is ambiguous");
            }

            return matches[0];
        }

        private static void EnsureNoNameCollision(
            IEnumerable<MutableTagState> tags,
            string tableName,
            string tagName,
            MutableTagState? allowed = null)
        {
            var collision = tags.Any(tag =>
                !tag.IsDeleted &&
                !ReferenceEquals(tag, allowed) &&
                string.Equals(tag.Table.Name, tableName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(tag.Name, tagName, StringComparison.OrdinalIgnoreCase));
            if (collision)
            {
                throw new InvalidOperationException(
                    $"Tag '{tagName}' already exists in tag table '{tableName}'");
            }
        }

        private static void RequirePatchValue(bool isPresent, string? value, string message)
        {
            if (!isPresent || string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(message);
            }
        }

        private static void AddModificationSuccess(
            TagModificationResult result,
            int index,
            TagModificationOperation operation,
            TagSnapshot? before,
            TagSnapshot? after,
            bool dryRun)
        {
            result.Operations.Add(new TagModificationEntry
            {
                Index = index,
                SourceLine = operation.SourceLine,
                Action = operation.Action,
                Status = dryRun ? "Planned" : "Applied",
                Before = before,
                After = after
            });

            if (dryRun)
            {
                result.Summary.Planned++;
            }
            else
            {
                result.Summary.Applied++;
            }
        }

        private static void AddModificationFailure(
            TagModificationResult result,
            int index,
            int? sourceLine,
            string action,
            string message,
            TagSnapshot? before = null,
            TagSnapshot? after = null)
        {
            result.Operations.Add(new TagModificationEntry
            {
                Index = index,
                SourceLine = sourceLine,
                Action = action,
                Status = "Failed",
                Before = before,
                After = after,
                Message = message
            });
            result.Summary.Failed++;
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
                ? message + " Log into the safety program in TIA Portal, or provide a process environment variable through --safety-password-env when Openness safety login is available."
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

        private sealed class TagInfo
        {
            public object TagObject { get; set; } = null!;

            public string TagTableName { get; set; } = string.Empty;

            public string FullName { get; set; } = string.Empty;

            public string NormalizedAddress { get; set; } = string.Empty;

            public string BaseName { get; set; } = string.Empty;

            public int PageNumber { get; set; }
        }

        private sealed class TagTableState
        {
            public object TableObject { get; set; } = null!;

            public string Name { get; set; } = string.Empty;
        }

        private sealed class MutableTagState
        {
            public object? TagObject { get; set; }

            public TagTableState Table { get; set; } = null!;

            public string Name { get; set; } = string.Empty;

            public string DataType { get; set; } = string.Empty;

            public string LogicalAddress { get; set; } = string.Empty;

            public string? Comment { get; set; }

            public bool IsDeleted { get; set; }

            public bool IsVirtual { get; set; }

            public TagSnapshot ToSnapshot()
            {
                return new TagSnapshot
                {
                    TagTable = Table.Name,
                    Name = Name,
                    DataType = DataType,
                    LogicalAddress = LogicalAddress,
                    Comment = Comment
                };
            }

            public void Apply(TagSnapshot snapshot)
            {
                Name = snapshot.Name;
                DataType = snapshot.DataType;
                LogicalAddress = snapshot.LogicalAddress;
                Comment = snapshot.Comment;
            }
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
