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
    /// Options for browsing Unified HMI tags without forcing a complete tag-tree scan.
    /// </summary>
    public sealed class HmiTagBrowseOptions
    {
        public string? Filter { get; set; }

        public string? RootPath { get; set; }

        public int? MaxResults { get; set; }

        public int? MaxDepth { get; set; }

        public bool IncludeDetails { get; set; } = true;

        public bool IncludeLoggingCounts { get; set; } = true;
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

    public sealed class HmiTrendCreateRequest
    {
        public string TagPath { get; set; } = string.Empty;
        public string DataLogName { get; set; } = string.Empty;
        public bool Cyclic { get; set; }
        public string? Cycle { get; set; }
        public string StorageFolder { get; set; } = "/Trends";
        public int LogMaxSize { get; set; } = 2000;
        public int DurationDays { get; set; } = 365;
        public int SegmentMaxSize { get; set; } = 500;
    }

    public sealed class HmiLoggingTagRemoveRequest
    {
        public string TagPath { get; set; } = string.Empty;
        public string DataLogName { get; set; } = string.Empty;
        public string? LoggingTagName { get; set; }
    }

    public sealed class HmiDataLogDeleteRequest
    {
        public string DataLogName { get; set; } = string.Empty;
    }

    public sealed class HmiTrendBulkItemResult
    {
        public string? TagPath { get; set; }
        public string DataLogName { get; set; } = string.Empty;
        public string? LoggingTagName { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? Message { get; set; }
        public HmiTrendInfo? Trend { get; set; }
        public System.Collections.Generic.List<HmiLoggingTagReferenceInfo> References { get; set; } =
            new System.Collections.Generic.List<HmiLoggingTagReferenceInfo>();
    }

    public sealed class HmiTrendBulkResult
    {
        public string Operation { get; set; } = string.Empty;
        public bool DryRun { get; set; }
        public int RequestedCount { get; set; }
        public int AppliedCount { get; set; }
        public int PlannedCount { get; set; }
        public int BlockedCount { get; set; }
        public int NotFoundCount { get; set; }
        public int FailedCount { get; set; }
        public System.Collections.Generic.List<HmiTrendBulkItemResult> Results { get; set; } =
            new System.Collections.Generic.List<HmiTrendBulkItemResult>();
    }
}
