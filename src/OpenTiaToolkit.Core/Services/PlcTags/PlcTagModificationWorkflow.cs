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
    internal sealed class PlcTagModificationWorkflow
    {
        private readonly EngineeringSessionContext _sessionContext;
        private readonly IPlcTagRepository _repository;
        private readonly ILogger? _logger;
        private readonly PlcTagMutationBoundary _mutations;
        private readonly PlcTagModificationPlanner _planner;

        public PlcTagModificationWorkflow(EngineeringSessionContext sessionContext, IPlcTagRepository repository, ILogger? logger)
        {
            _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _logger = logger;
            _mutations = new PlcTagMutationBoundary(repository, logger);
            _planner = new PlcTagModificationPlanner();
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
                var catalog = _repository.ReadCatalog(plcSoftware);
                if (catalog == null)
                {
                    AddModificationFailure(result, 0, null, string.Empty, "PLC software does not expose accessible tag tables");
                    return result;
                }

                var tables = catalog.Tables;
                var tags = catalog.Tags;

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
            IReadOnlyList<PlcTagTableState> tables,
            List<PlcTagState> tags,
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
            IReadOnlyList<PlcTagTableState> tables,
            List<PlcTagState> tags,
            TagModificationResult result,
            bool dryRun,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _planner.RequireValue(operation.Set.HasTagTable, operation.Set.TagTable, "create requires set.tagTable");
            _planner.RequireValue(operation.Set.HasName, operation.Set.Name, "create requires set.name");
            _planner.RequireValue(operation.Set.HasDataType, operation.Set.DataType, "create requires set.dataType");

            var table = _planner.FindTable(tables, operation.Set.TagTable!);
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
                _planner.EnsureNoNameCollision(tags, snapshot.TagTable, snapshot.Name);
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
            List<PlcTagState> tags,
            TagModificationResult result,
            bool dryRun,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!operation.Set.HasAnyValue)
            {
                throw new InvalidOperationException("update requires at least one set value");
            }

            var tag = _planner.FindSingleTag(tags, operation.Match);
            var before = tag.ToSnapshot();
            if (operation.Set.HasTagTable &&
                !string.Equals(operation.Set.TagTable, tag.Table.Name, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("update cannot move a tag to another tag table; use replace");
            }

            var after = _planner.ApplyPatch(before, operation.Set);
            if (!string.Equals(before.Name, after.Name, StringComparison.OrdinalIgnoreCase))
            {
                _planner.EnsureNoNameCollision(tags, after.TagTable, after.Name, tag);
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
            List<PlcTagState> tags,
            TagModificationResult result,
            bool dryRun,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tag = _planner.FindSingleTag(tags, operation.Match);
            var before = tag.ToSnapshot();
            if (!dryRun && !_repository.TryDelete(tag.TagObject!))
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
            IReadOnlyList<PlcTagTableState> tables,
            List<PlcTagState> tags,
            TagModificationResult result,
            bool dryRun,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = _planner.FindSingleTag(tags, operation.Match);
            var before = source.ToSnapshot();
            var after = _planner.ApplyPatch(before, operation.Set);
            var targetTable = _planner.FindTable(tables, after.TagTable);
            _planner.EnsureNoNameCollision(tags, after.TagTable, after.Name, source);

            PlcTagState replacement;
            if (dryRun)
            {
                source.IsDeleted = true;
                replacement = CreateTagState(targetTable, after, dryRun: true);
            }
            else
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_repository.TryDelete(source.TagObject!))
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

        private PlcTagState CreateTagState(PlcTagTableState table, TagSnapshot snapshot, bool dryRun)
        {
            object? createdTag = null;
            if (!dryRun)
            {
                if (!_repository.TryCreateTag(
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
                    _repository.TryDelete(createdTag!);
                    throw new InvalidOperationException(
                        $"Tag '{snapshot.Name}' was created, but its comment could not be applied.");
                }
            }

            return new PlcTagState
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
            PlcTagState tag,
            TagSnapshot before,
            TagSnapshot after,
            TagValuePatch patch)
        {
            try
            {
                if (patch.HasName && !string.Equals(before.Name, after.Name, StringComparison.Ordinal))
                {
                    _repository.SetName(tag.TagObject!, after.Name);
                }

                if (patch.HasDataType && !string.Equals(before.DataType, after.DataType, StringComparison.Ordinal))
                {
                    _repository.SetDataType(tag.TagObject!, after.DataType);
                }

                if (patch.HasLogicalAddress &&
                    !string.Equals(before.LogicalAddress, after.LogicalAddress, StringComparison.Ordinal))
                {
                    _repository.SetLogicalAddress(tag.TagObject!, after.LogicalAddress);
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
                _repository.SetName(tag, snapshot.Name);
                _repository.SetDataType(tag, snapshot.DataType);
                _repository.SetLogicalAddress(tag, snapshot.LogicalAddress);
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

        private PlcTagState? TryRestoreReplacedTag(
            PlcTagTableState table,
            TagSnapshot snapshot,
            out string? failureReason)
        {
            if (_mutations.TryRestore(
                table.TableObject,
                snapshot,
                out var restoredTag,
                out var restorationMessage))
            {
                failureReason = null;
                return new PlcTagState
                {
                    TagObject = restoredTag,
                    Table = table,
                    Name = snapshot.Name,
                    DataType = snapshot.DataType,
                    LogicalAddress = snapshot.LogicalAddress,
                    Comment = snapshot.Comment
                };
            }

            failureReason = restorationMessage;
            return null;
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

        private string? GetCommentText(object tag, string culture = "en-US")
        {
            return _repository.GetTagCommentText(tag, culture);
        }

        private bool SetTagComment(object tag, string commentText, string culture = "en-US")
        {
            return _mutations.TrySetComment(tag, commentText, culture);
        }


    }
}
