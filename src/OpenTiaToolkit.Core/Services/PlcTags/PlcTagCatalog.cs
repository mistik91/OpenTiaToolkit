using OpenTiaToolkit.Core.Models;
using System.Collections.Generic;
using System.Linq;

namespace OpenTiaToolkit.Core.Services.PlcTags
{
    /// <summary>
    /// Normalized, mutable view of PLC tag state. Planners mutate this view for ordered dry-runs.
    /// </summary>
    internal sealed class PlcTagCatalog
    {
        public PlcTagCatalog(List<PlcTagTableState> tables, List<PlcTagState> tags)
        {
            Tables = tables;
            Tags = tags;
        }

        public List<PlcTagTableState> Tables { get; }
        public List<PlcTagState> Tags { get; }

        public IEnumerable<PlcTagState> ActiveTags => Tags.Where(tag => !tag.IsDeleted);
    }

    internal sealed class PlcTagTableState
    {
        public object TableObject { get; set; } = null!;
        public string Name { get; set; } = string.Empty;
    }

    internal sealed class PlcTagState
    {
        public object? TagObject { get; set; }
        public PlcTagTableState Table { get; set; } = null!;
        public string Name { get; set; } = string.Empty;
        public string DataType { get; set; } = string.Empty;
        public string LogicalAddress { get; set; } = string.Empty;
        public string? Comment { get; set; }
        public bool IsDeleted { get; set; }
        public bool IsVirtual { get; set; }

        public TagSnapshot ToSnapshot() => new TagSnapshot
        {
            TagTable = Table.Name,
            Name = Name,
            DataType = DataType,
            LogicalAddress = LogicalAddress,
            Comment = Comment
        };

        public void Apply(TagSnapshot snapshot)
        {
            Name = snapshot.Name;
            DataType = snapshot.DataType;
            LogicalAddress = snapshot.LogicalAddress;
            Comment = snapshot.Comment;
        }
    }
}
