using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Services.V21;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Services
{
    internal sealed class PlcTagExportService
    {
        private readonly EngineeringSessionContext _sessionContext;
        private readonly V21PlcTagStructureAccess _tags;
        private readonly V21EngineeringObjectAccess _objects;

        public PlcTagExportService(
            EngineeringSessionContext sessionContext,
            V21PlcTagStructureAccess tags,
            V21EngineeringObjectAccess objects)
        {
            _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
            _tags = tags ?? throw new ArgumentNullException(nameof(tags));
            _objects = objects ?? throw new ArgumentNullException(nameof(objects));
        }

        public Task<PlcTagExportResult> ExportAsync(string culture) =>
            Task.Run(() =>
            {
                var plcSoftware = _sessionContext.PlcSoftware;
                if (plcSoftware == null)
                    return new PlcTagExportResult(false, "No PLC software is currently open");

                var tagTables = _tags.GetTagTables(plcSoftware);
                if (tagTables == null)
                    return new PlcTagExportResult(false, "PLC software does not expose accessible tag tables");

                var rows = new List<PlcTagExportRow>();
                foreach (var tagTable in tagTables)
                {
                    var tagTableName = _objects.GetName(tagTable) ?? string.Empty;
                    var tags = _tags.GetTags(tagTable);
                    if (tags == null) continue;
                    foreach (var tag in tags)
                    {
                        rows.Add(new PlcTagExportRow(
                            tagTableName,
                            _objects.GetName(tag) ?? string.Empty,
                            _objects.GetDataType(tag),
                            _objects.GetLogicalAddress(tag),
                            _objects.GetTagCommentText(tag, culture)));
                    }
                }

                return new PlcTagExportResult(true, $"Exported {rows.Count} PLC tag(s).", rows);
            });
    }
}
