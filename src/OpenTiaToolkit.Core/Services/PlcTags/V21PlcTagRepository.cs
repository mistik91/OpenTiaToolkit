using System;
using System.Collections.Generic;
using System.Linq;
using OpenTiaToolkit.Core.Utilities;

namespace OpenTiaToolkit.Core.Services.PlcTags
{
    internal sealed class V21PlcTagRepository : IPlcTagRepository
    {
        private readonly V21OpennessAdapter _adapter;

        public V21PlcTagRepository(V21OpennessAdapter adapter)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        }

        public IReadOnlyList<object>? GetTagTables(object plcSoftware) => _adapter.GetTagTables(plcSoftware);
        public IReadOnlyList<object>? GetTags(object tagTable) => _adapter.GetTags(tagTable);
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
        public object? FindTagTable(object plcSoftware, string name) => _adapter.FindTagTable(plcSoftware, name);
        public string? GetName(object item) => _adapter.GetName(item);
        public void SetName(object item, string value) => _adapter.SetName(item, value);
        public string? GetDataType(object tag) => _adapter.GetDataType(tag);
        public void SetDataType(object tag, string value) => _adapter.SetDataType(tag, value);
        public string? GetLogicalAddress(object tag) => _adapter.GetLogicalAddress(tag);
        public void SetLogicalAddress(object tag, string value) => _adapter.SetLogicalAddress(tag, value);
        public string? GetTagCommentText(object tag, string culture) => _adapter.GetTagCommentText(tag, culture);
        public bool TrySetTagComment(object tag, string comment, string culture) =>
            _adapter.TrySetTagComment(tag, comment, culture);
        public bool TryCreateTag(object tagTable, string name, string dataType, string logicalAddress, out object? tag, out string? failureReason) =>
            _adapter.TryCreateTag(tagTable, name, dataType, logicalAddress, out tag, out failureReason);
        public bool TryDelete(object item) => _adapter.TryDelete(item);
    }
}
