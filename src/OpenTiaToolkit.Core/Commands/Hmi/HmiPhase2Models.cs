namespace OpenTiaToolkit.Core.Commands.Hmi
{
    /// <summary>
    /// Summary information about a Unified HMI tag.
    /// </summary>
    public sealed class HmiTagInfo
    {
        public string Path { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string? DisplayName { get; set; }

        public string? DataType { get; set; }

        public string? HmiDataType { get; set; }

        public string? TagTableName { get; set; }

        public bool HasMembers { get; set; }

        public int MemberCount { get; set; }

        public int LoggingTagCount { get; set; }
    }

    /// <summary>
    /// Result of copying a Unified HMI screen from a template.
    /// </summary>
    public sealed class HmiScreenCopyResult
    {
        public string TemplatePath { get; set; } = string.Empty;

        public string TargetPath { get; set; } = string.Empty;

        public bool ReplacementApplied { get; set; }

        public int CopiedItemCount { get; set; }

        public int CopiedScreenEventHandlerCount { get; set; }

        public int CopiedItemEventHandlerCount { get; set; }

        public HmiScreenInfo Screen { get; set; } = new HmiScreenInfo();
    }

    /// <summary>
    /// Result of creating or updating Unified HMI trend logging infrastructure.
    /// </summary>
    public sealed class HmiTrendInfo
    {
        public string TagPath { get; set; } = string.Empty;

        public string TagName { get; set; } = string.Empty;

        public string LoggingTagName { get; set; } = string.Empty;

        public string DataLogName { get; set; } = string.Empty;

        public string LoggingMode { get; set; } = string.Empty;

        public string? Cycle { get; set; }

        public string StorageFolder { get; set; } = string.Empty;

        public int LogMaxSize { get; set; }

        public int DurationDays { get; set; }

        public int SegmentMaxSize { get; set; }

        public bool DataLogCreated { get; set; }

        public bool LoggingTagCreated { get; set; }
    }

    /// <summary>
    /// One logging tag that references a Unified HMI data log.
    /// </summary>
    public sealed class HmiLoggingTagReferenceInfo
    {
        public string TagPath { get; set; } = string.Empty;

        public string LoggingTagName { get; set; } = string.Empty;

        public string DataLogName { get; set; } = string.Empty;
    }

    /// <summary>
    /// Result of removing one logging tag from a Unified HMI tag.
    /// </summary>
    public sealed class HmiLoggingTagDeleteResult
    {
        public string TagPath { get; set; } = string.Empty;

        public string LoggingTagName { get; set; } = string.Empty;

        public string DataLogName { get; set; } = string.Empty;

        public bool Deleted { get; set; }

        public string? Error { get; set; }
    }

    /// <summary>
    /// Result of deleting one Unified HMI data log archive.
    /// </summary>
    public sealed class HmiDataLogDeleteResult
    {
        public string DataLogName { get; set; } = string.Empty;

        public bool Deleted { get; set; }

        public string? Error { get; set; }

        public System.Collections.Generic.List<HmiLoggingTagReferenceInfo> References { get; set; } =
            new System.Collections.Generic.List<HmiLoggingTagReferenceInfo>();
    }
}
