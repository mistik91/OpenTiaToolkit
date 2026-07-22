using System;
using System.Collections.Generic;
using System.Linq;
using OpenTiaToolkit.Core.Utilities;
using OpenTiaToolkit.Core.Services.V21;

namespace OpenTiaToolkit.Core.Services.PlcTags
{
    internal sealed class V21PlcTagRepository : IPlcTagRepository
    {
        private readonly V21PlcTagStructureAccess _tags;
        private readonly V21EngineeringObjectAccess _objects;

        public V21PlcTagRepository(V21OpennessAdapter adapter)
        {
            if (adapter == null) throw new ArgumentNullException(nameof(adapter));
            _tags = adapter.Tags;
            _objects = adapter.Objects;
        }

        public IReadOnlyList<object>? GetTagTables(object plcSoftware) => _tags.GetTagTables(plcSoftware);
        public IReadOnlyList<object>? GetTags(object tagTable) => _tags.GetTags(tagTable);
        public PlcTagCatalog? ReadCatalog(object plcSoftware)
        {
            var tableObjects = GetTagTables(plcSoftware);
            if (tableObjects == null)
            {
                return null;
            }

            var tables = tableObjects.Select(table => new PlcTagTableState
            {
                TableObject = table,
                Name = GetName(table) ?? "Unknown"
            }).ToList();
            var tags = new List<PlcTagState>();
            foreach (var table in tables)
            {
                var tableTags = GetTags(table.TableObject);
                if (tableTags == null)
                {
                    continue;
                }

                tags.AddRange(tableTags.Select(tag =>
                {
                    var rawLogicalAddress = GetLogicalAddress(tag) ?? string.Empty;
                    return new PlcTagState
                    {
                        TagObject = tag,
                        Table = table,
                        Name = GetName(tag) ?? string.Empty,
                        DataType = GetDataType(tag) ?? string.Empty,
                        RawLogicalAddress = rawLogicalAddress,
                        LogicalAddress = TagUpdateCsvParser.NormalizeLogicalAddress(rawLogicalAddress),
                        Comment = GetTagCommentText(tag, "en-US")
                    };
                }));
            }

            return new PlcTagCatalog(tables, tags);
        }
        public object? FindTagTable(object plcSoftware, string name) => _tags.FindTagTable(plcSoftware, name);
        public string? GetName(object item) => _objects.GetName(item);
        public void SetName(object item, string value) => _objects.SetName(item, value);
        public string? GetDataType(object tag) => _objects.GetDataType(tag);
        public void SetDataType(object tag, string value) => _objects.SetDataType(tag, value);
        public string? GetLogicalAddress(object tag) => _objects.GetLogicalAddress(tag);
        public void SetLogicalAddress(object tag, string value) => _objects.SetLogicalAddress(tag, value);
        public string? GetTagCommentText(object tag, string culture) => _objects.GetTagCommentText(tag, culture);
        public bool TrySetTagComment(object tag, string comment, string culture) =>
            _objects.TrySetTagComment(tag, comment, culture);
        public bool TryCreateTag(object tagTable, string name, string dataType, string logicalAddress, out object? tag, out string? failureReason) =>
            _tags.TryCreateTag(tagTable, name, dataType, logicalAddress, out tag, out failureReason);
        public bool TryDelete(object item) => _objects.TryDelete(item);
    }
}
