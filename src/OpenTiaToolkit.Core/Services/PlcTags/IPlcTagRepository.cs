using System.Collections.Generic;

namespace OpenTiaToolkit.Core.Services.PlcTags
{
    /// <summary>
    /// Narrow Siemens access boundary used by PLC-tag workflows.
    /// </summary>
    internal interface IPlcTagCatalogReader
    {
        PlcTagCatalog? ReadCatalog(object plcSoftware);
    }

    internal interface IPlcTagRepository : IPlcTagCatalogReader
    {
        IReadOnlyList<object>? GetTagTables(object plcSoftware);
        IReadOnlyList<object>? GetTags(object tagTable);
        object? FindTagTable(object plcSoftware, string name);
        string? GetName(object item);
        void SetName(object item, string value);
        string? GetDataType(object tag);
        void SetDataType(object tag, string value);
        string? GetLogicalAddress(object tag);
        void SetLogicalAddress(object tag, string value);
        string? GetTagCommentText(object tag, string culture);
        bool TrySetTagComment(object tag, string comment, string culture);
        bool TryCreateTag(object tagTable, string name, string dataType, string logicalAddress, out object? tag, out string? failureReason);
        bool TryDelete(object item);
    }
}
