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
    internal sealed class PlcTagCreationWorkflow
    {
        private readonly EngineeringSessionContext _sessionContext;
        private readonly IPlcTagRepository _repository;
        private readonly ILogger? _logger;

        public PlcTagCreationWorkflow(EngineeringSessionContext sessionContext, IPlcTagRepository repository, ILogger? logger)
        {
            _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _logger = logger;
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

                    var tagTables = _repository.GetTagTables(plcSoftware);
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
                        targetTagTable = _repository.FindTagTable(plcSoftware, requestedTagTableName);
                        actualTableName = targetTagTable == null ? null : _repository.GetName(targetTagTable);

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
                        actualTableName = targetTagTable == null ? null : _repository.GetName(targetTagTable) ?? "Default";
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

                    var tags = _repository.GetTags(targetTagTable);
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
                        var name = _repository.GetName(tag);
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
                        if (!_repository.TryCreateTag(targetTagTable, tagName, dataType, address, out var createdTag, out var failureReason))
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
                        result.TagName = _repository.GetName(createdTag!);
                        result.TagTableName = actualTableName;
                        result.DataType = _repository.GetDataType(createdTag!);
                        result.LogicalAddress = _repository.GetLogicalAddress(createdTag!);

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

        private static string BuildPath(params string?[] segments)
        {
            var validSegments = segments.Where(segment => !string.IsNullOrEmpty(segment)).ToArray();
            return string.Join(".", validSegments);
        }

    }
}
