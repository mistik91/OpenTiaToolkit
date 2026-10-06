using System.Collections.Generic;

namespace OpenTiaToolkit.Core.Models
{
    public sealed class TagModificationOperation
    {
        public string Action { get; set; } = string.Empty;

        public TagMatchCriteria Match { get; set; } = new TagMatchCriteria();

        public TagValuePatch Set { get; set; } = new TagValuePatch();

        public int? SourceLine { get; set; }
    }

    public sealed class TagMatchCriteria
    {
        public string? TagTable { get; set; }

        public string? Name { get; set; }

        public string? LogicalAddress { get; set; }
    }

    public sealed class TagValuePatch
    {
        public string? TagTable { get; set; }

        public string? Name { get; set; }

        public string? DataType { get; set; }

        public string? LogicalAddress { get; set; }

        public string? Comment { get; set; }

        public bool HasTagTable { get; set; }

        public bool HasName { get; set; }

        public bool HasDataType { get; set; }

        public bool HasLogicalAddress { get; set; }

        public bool HasComment { get; set; }

        public bool HasAnyValue =>
            HasTagTable ||
            HasName ||
            HasDataType ||
            HasLogicalAddress ||
            HasComment;
    }

    public sealed class TagModificationResult
    {
        public bool DryRun { get; set; }

        public TagModificationSummary Summary { get; set; } = new TagModificationSummary();

        public List<TagModificationEntry> Operations { get; set; } = new List<TagModificationEntry>();
    }

    public sealed class TagModificationSummary
    {
        public int Total { get; set; }

        public int Planned { get; set; }

        public int Applied { get; set; }

        public int Failed { get; set; }
    }

    public sealed class TagModificationEntry
    {
        public int Index { get; set; }

        public int? SourceLine { get; set; }

        public string Action { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public TagSnapshot? Before { get; set; }

        public TagSnapshot? After { get; set; }

        public string? Message { get; set; }
    }

    public sealed class TagSnapshot
    {
        public string TagTable { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string DataType { get; set; } = string.Empty;

        public string LogicalAddress { get; set; } = string.Empty;

        public string? Comment { get; set; }
    }
}
