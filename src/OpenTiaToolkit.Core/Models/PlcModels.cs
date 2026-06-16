using System.Collections.Generic;
using System.Linq;

namespace OpenTiaToolkit.Core.Models;

/// <summary>
/// Represents a node in the PLC software tree structure.
/// </summary>
public class SoftwareTreeNode
{
    public SoftwareTreeNode(string name, string type, string path, IEnumerable<SoftwareTreeNode> children)
    {
        Name = name;
        Type = type;
        Path = path;
        Children = new List<SoftwareTreeNode>(children);
    }

    /// <summary>Gets the node name.</summary>
    public string Name { get; }

    /// <summary>Gets the node type (e.g., "Blocks", "Types", "Sources").</summary>
    public string Type { get; }

    /// <summary>Gets the full path to the node.</summary>
    public string Path { get; }

    /// <summary>Gets the child nodes.</summary>
    public IReadOnlyList<SoftwareTreeNode> Children { get; }
}

/// <summary>
/// Represents the result of a PLC software compilation.
/// </summary>
public class CompilationResult
{
    public CompilationResult(bool success, int errorCount, int warningCount, string? message, IReadOnlyList<CompilationDiagnostic>? diagnostics = null)
    {
        Success = success;
        ErrorCount = errorCount;
        WarningCount = warningCount;
        Message = message;
        Diagnostics = diagnostics ?? System.Array.Empty<CompilationDiagnostic>();
    }

    /// <summary>Gets whether the compilation was successful.</summary>
    public bool Success { get; }

    /// <summary>Gets the number of errors found.</summary>
    public int ErrorCount { get; }

    /// <summary>Gets the number of warnings found.</summary>
    public int WarningCount { get; }

    /// <summary>Gets a descriptive message about the compilation result.</summary>
    public string? Message { get; }

    /// <summary>Gets detailed compiler diagnostics when exposed by TIA Openness.</summary>
    public IReadOnlyList<CompilationDiagnostic> Diagnostics { get; }
}

/// <summary>
/// Represents the result of attempting to log into an offline safety program.
/// </summary>
public class SafetyLoginResult
{
    public SafetyLoginResult(bool success, bool serviceAvailable, bool passwordRequired, bool alreadyLoggedIn, string message)
    {
        Success = success;
        ServiceAvailable = serviceAvailable;
        PasswordRequired = passwordRequired;
        AlreadyLoggedIn = alreadyLoggedIn;
        Message = message;
    }

    public bool Success { get; }

    public bool ServiceAvailable { get; }

    public bool PasswordRequired { get; }

    public bool AlreadyLoggedIn { get; }

    public string Message { get; }
}

/// <summary>
/// Represents one compiler diagnostic message.
/// </summary>
public class CompilationDiagnostic
{
    public CompilationDiagnostic(string severity, string? path, string? name, string? description, string? errorCode)
    {
        Severity = severity;
        Path = path;
        Name = name;
        Description = description;
        ErrorCode = errorCode;
    }

    public string Severity { get; }

    public string? Path { get; }

    public string? Name { get; }

    public string? Description { get; }

    public string? ErrorCode { get; }
}

/// <summary>
/// Represents a PLC software compare result.
/// </summary>
public class PlcSoftwareCompareResult
{
    public PlcSoftwareCompareResult(bool success, bool comparedToOnline, string message, IReadOnlyList<PlcSoftwareCompareEntry>? entries = null)
    {
        Success = success;
        ComparedToOnline = comparedToOnline;
        Message = message;
        Entries = entries ?? System.Array.Empty<PlcSoftwareCompareEntry>();
        DifferenceCount = Entries.Count(entry => entry.HasDifference);
    }

    public bool Success { get; }

    public bool ComparedToOnline { get; }

    public string Message { get; }

    public int DifferenceCount { get; }

    public IReadOnlyList<PlcSoftwareCompareEntry> Entries { get; }
}

/// <summary>
/// Represents one flattened entry from a PLC software compare tree.
/// </summary>
public class PlcSoftwareCompareEntry
{
    public PlcSoftwareCompareEntry(string path, string state, string? leftName, string? rightName, string? detail, bool hasDifference)
    {
        Path = path;
        State = state;
        LeftName = leftName;
        RightName = rightName;
        Detail = detail;
        HasDifference = hasDifference;
    }

    public string Path { get; }

    public string State { get; }

    public string? LeftName { get; }

    public string? RightName { get; }

    public string? Detail { get; }

    public bool HasDifference { get; }
}

/// <summary>
/// Represents a guarded PLC software download result.
/// </summary>
public class PlcSoftwareDownloadResult
{
    public PlcSoftwareDownloadResult(
        bool success,
        bool dryRun,
        bool toSimulator,
        bool applied,
        string message,
        IReadOnlyList<string>? warnings = null,
        string? state = null,
        bool onlyChanges = false)
    {
        Success = success;
        DryRun = dryRun;
        ToSimulator = toSimulator;
        Applied = applied;
        Message = message;
        Warnings = warnings ?? System.Array.Empty<string>();
        State = state;
        OnlyChanges = onlyChanges;
    }

    public bool Success { get; }

    public bool DryRun { get; }

    public bool ToSimulator { get; }

    public bool Applied { get; }

    public bool OnlyChanges { get; }

    public string Message { get; }

    public IReadOnlyList<string> Warnings { get; }

    public string? State { get; }
}

/// <summary>
/// Represents PLC online connection status.
/// </summary>
public class PlcOnlineStatusResult
{
    public PlcOnlineStatusResult(bool success, string onlineState, bool isOnline, string message)
    {
        Success = success;
        OnlineState = onlineState;
        IsOnline = isOnline;
        Message = message;
    }

    public bool Success { get; }

    public string OnlineState { get; }

    public bool IsOnline { get; }

    public string Message { get; }
}

/// <summary>
/// Represents the result of an attempted PLC runtime control operation.
/// </summary>
public class PlcCpuControlResult
{
    public PlcCpuControlResult(bool success, string action, bool applied, string onlineState, string message)
    {
        Success = success;
        Action = action;
        Applied = applied;
        OnlineState = onlineState;
        Message = message;
    }

    public bool Success { get; }

    public string Action { get; }

    public bool Applied { get; }

    public string OnlineState { get; }

    public string Message { get; }
}

/// <summary>
/// Represents one exported PLC tag row.
/// </summary>
public class PlcTagExportRow
{
    public PlcTagExportRow(string tagTableName, string name, string? dataType, string? logicalAddress, string? comment)
    {
        TagTableName = tagTableName;
        Name = name;
        DataType = dataType;
        LogicalAddress = logicalAddress;
        Comment = comment;
    }

    public string TagTableName { get; }

    public string Name { get; }

    public string? DataType { get; }

    public string? LogicalAddress { get; }

    public string? Comment { get; }
}

/// <summary>
/// Represents exported PLC tags.
/// </summary>
public class PlcTagExportResult
{
    public PlcTagExportResult(bool success, string message, IReadOnlyList<PlcTagExportRow>? tags = null)
    {
        Success = success;
        Message = message;
        Tags = tags ?? System.Array.Empty<PlcTagExportRow>();
    }

    public bool Success { get; }

    public string Message { get; }

    public int TagCount => Tags.Count;

    public IReadOnlyList<PlcTagExportRow> Tags { get; }
}

/// <summary>
/// Represents a named PLC surface item such as a source file, watch table, technology object, or alarm text list.
/// </summary>
public class PlcSurfaceItem
{
    public PlcSurfaceItem(string name, string itemType, string path, string? detail = null)
    {
        Name = name;
        ItemType = itemType;
        Path = path;
        Detail = detail;
    }

    public string Name { get; }

    public string ItemType { get; }

    public string Path { get; }

    public string? Detail { get; }
}

/// <summary>
/// Represents one PLC cross-reference entry.
/// </summary>
public class PlcCrossReferenceEntry
{
    public PlcCrossReferenceEntry(string name, string? path, string? usage, string? itemType, string? detail)
    {
        Name = name;
        Path = path;
        Usage = usage;
        ItemType = itemType;
        Detail = detail;
    }

    public string Name { get; }

    public string? Path { get; }

    public string? Usage { get; }

    public string? ItemType { get; }

    public string? Detail { get; }
}

/// <summary>
/// Represents PLC cross-reference lookup output.
/// </summary>
public class PlcCrossReferenceResult
{
    public PlcCrossReferenceResult(bool success, string query, string message, IReadOnlyList<PlcCrossReferenceEntry>? entries = null)
    {
        Success = success;
        Query = query;
        Message = message;
        Entries = entries ?? System.Array.Empty<PlcCrossReferenceEntry>();
    }

    public bool Success { get; }

    public string Query { get; }

    public string Message { get; }

    public int ReferenceCount => Entries.Count;

    public IReadOnlyList<PlcCrossReferenceEntry> Entries { get; }
}

/// <summary>
/// Represents a PLC source-file operation result.
/// </summary>
public class PlcSourceOperationResult
{
    public PlcSourceOperationResult(bool success, bool applied, string message, string? sourceName = null, string? outputPath = null)
    {
        Success = success;
        Applied = applied;
        Message = message;
        SourceName = sourceName;
        OutputPath = outputPath;
    }

    public bool Success { get; }

    public bool Applied { get; }

    public string Message { get; }

    public string? SourceName { get; }

    public string? OutputPath { get; }
}

/// <summary>
/// Represents information about a PLC block.
/// </summary>
public class BlockInfo
{
    public BlockInfo(string name, string type, string? language, string? number, bool isReadOnly = false, string? comment = null)
    {
        Name = name;
        Type = type;
        Language = language;
        Number = number;
        IsReadOnly = isReadOnly;
        Comment = comment;
    }

    /// <summary>Gets the block name.</summary>
    public string Name { get; }

    /// <summary>Gets the block type (e.g., "FB", "FC", "DB").</summary>
    public string Type { get; }

    /// <summary>Gets the programming language (e.g., "SCL", "FBD", "LAD").</summary>
    public string? Language { get; }

    /// <summary>Gets the block number.</summary>
    public string? Number { get; }

    /// <summary>Gets whether the block is read-only (protected).</summary>
    public bool IsReadOnly { get; }

    /// <summary>Gets the block comment/description.</summary>
    public string? Comment { get; }
}

/// <summary>
/// Represents detailed information about a specific PLC block.
/// </summary>
public class BlockInfoDetail
{
    public BlockInfoDetail(
        string name,
        string type,
        string? language,
        string? number,
        bool isReadOnly,
        string? comment,
        string? author,
        string? created,
        string? modified,
        int memorySize,
        string? programmingLanguage)
    {
        Name = name;
        Type = type;
        Language = language;
        Number = number;
        IsReadOnly = isReadOnly;
        Comment = comment;
        Author = author;
        Created = created;
        Modified = modified;
        MemorySize = memorySize;
        ProgrammingLanguage = programmingLanguage;
    }

    /// <summary>Gets the block name.</summary>
    public string Name { get; }

    /// <summary>Gets the block type (e.g., "FB", "FC", "DB").</summary>
    public string Type { get; }

    /// <summary>Gets the programming language (e.g., "SCL", "FBD", "LAD").</summary>
    public string? Language { get; }

    /// <summary>Gets the block number.</summary>
    public string? Number { get; }

    /// <summary>Gets whether the block is read-only (protected).</summary>
    public bool IsReadOnly { get; }

    /// <summary>Gets the block comment/description.</summary>
    public string? Comment { get; }

    /// <summary>Gets the block author.</summary>
    public string? Author { get; }

    /// <summary>Gets when the block was created.</summary>
    public string? Created { get; }

    /// <summary>Gets when the block was last modified.</summary>
    public string? Modified { get; }

    /// <summary>Gets the memory size in bytes.</summary>
    public int MemorySize { get; }

    /// <summary>Gets the detailed programming language information.</summary>
    public string? ProgrammingLanguage { get; }
}

/// <summary>
/// Summary statistics for find and replace operations.
/// </summary>
public class FindReplaceSummary
{
    /// <summary>Gets or sets the total number of items processed.</summary>
    public int TotalProcessed { get; set; }

    /// <summary>Gets or sets the number of matches found.</summary>
    public int MatchesFound { get; set; }

    /// <summary>Gets or sets the number of changes applied.</summary>
    public int ChangesApplied { get; set; }

    /// <summary>Gets or sets the number of items skipped.</summary>
    public int Skipped { get; set; }

    /// <summary>Gets or sets the number of warnings generated.</summary>
    public int Warnings { get; set; }

    /// <summary>Gets or sets the number of errors encountered.</summary>
    public int Errors { get; set; }
}

/// <summary>
/// Represents a single change entry in a find and replace operation.
/// </summary>
public class ChangeEntry
{
    /// <summary>Gets or sets the item type (e.g., "Tag", "DB Member", "UDT Member").</summary>
    public string? ItemType { get; set; }

    /// <summary>Gets or sets the location path (e.g., "TagTable.PLC tags", "GlobalDB.ProcessData").</summary>
    public string? Path { get; set; }

    /// <summary>Gets or sets the old value before the change.</summary>
    public string? Before { get; set; }

    /// <summary>Gets or sets the new value after the change.</summary>
    public string? After { get; set; }
}

/// <summary>
/// Represents an item that was skipped during a find and replace operation.
/// </summary>
public class SkippedEntry
{
    /// <summary>Gets or sets the item type.</summary>
    public string? ItemType { get; set; }

    /// <summary>Gets or sets the location path.</summary>
    public string? Path { get; set; }

    /// <summary>Gets or sets the item name.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets the reason for skipping.</summary>
    public string? Reason { get; set; }
}

/// <summary>
/// Represents a warning generated during a find and replace operation.
/// </summary>
public class WarningEntry
{
    /// <summary>Gets or sets the warning code.</summary>
    public string? Code { get; set; }

    /// <summary>Gets or sets the warning message.</summary>
    public string? Message { get; set; }

    /// <summary>Gets or sets the location path.</summary>
    public string? Path { get; set; }
}

/// <summary>
/// Represents an error encountered during a find and replace operation.
/// </summary>
public class ErrorEntry
{
    /// <summary>Gets or sets the item type.</summary>
    public string? ItemType { get; set; }

    /// <summary>Gets or sets the location path.</summary>
    public string? Path { get; set; }

    /// <summary>Gets or sets the item name.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets the error message.</summary>
    public string? Error { get; set; }

    /// <summary>Gets or sets detailed error information.</summary>
    public string? Details { get; set; }
}

/// <summary>
/// Represents the result of a find and replace operation.
/// </summary>
public class FindReplaceResult
{
    /// <summary>Gets or sets the summary statistics.</summary>
    public FindReplaceSummary Summary { get; set; } = new FindReplaceSummary();

    /// <summary>Gets or sets the list of changes made.</summary>
    public List<ChangeEntry> Changes { get; set; } = new List<ChangeEntry>();

    /// <summary>Gets or sets the list of skipped items.</summary>
    public List<SkippedEntry> Skipped { get; set; } = new List<SkippedEntry>();

    /// <summary>Gets or sets the list of warnings.</summary>
    public List<WarningEntry> Warnings { get; set; } = new List<WarningEntry>();

    /// <summary>Gets or sets the list of errors.</summary>
    public List<ErrorEntry> Errors { get; set; } = new List<ErrorEntry>();
}

/// <summary>
/// Typed command payload for PLC find and replace operations.
/// </summary>
public class FindReplaceCommandResult
{
    /// <summary>Gets or sets the summary statistics.</summary>
    public FindReplaceSummary Summary { get; set; } = new FindReplaceSummary();

    /// <summary>Gets or sets the list of changes made.</summary>
    public List<ChangeEntry> Changes { get; set; } = new List<ChangeEntry>();

    /// <summary>Gets or sets the list of skipped items.</summary>
    public List<SkippedEntry> Skipped { get; set; } = new List<SkippedEntry>();

    /// <summary>Gets or sets the list of warnings.</summary>
    public List<WarningEntry> Warnings { get; set; } = new List<WarningEntry>();

    /// <summary>Gets or sets the list of errors.</summary>
    public List<ErrorEntry> Errors { get; set; } = new List<ErrorEntry>();

    /// <summary>
    /// Creates a command payload from the service result without anonymous wrappers.
    /// </summary>
    public static FindReplaceCommandResult From(FindReplaceResult result)
    {
        return new FindReplaceCommandResult
        {
            Summary = result.Summary,
            Changes = result.Changes,
            Skipped = result.Skipped,
            Warnings = result.Warnings,
            Errors = result.Errors
        };
    }
}

/// <summary>
/// Represents the result of a tag creation operation.
/// </summary>
public class CreateTagResult
{
    /// <summary>Gets or sets whether the tag was created successfully.</summary>
    public bool Success { get; set; }

    /// <summary>Gets or sets the name of the created tag.</summary>
    public string? TagName { get; set; }

    /// <summary>Gets or sets the tag table where the tag was created.</summary>
    public string? TagTableName { get; set; }

    /// <summary>Gets or sets the data type of the created tag.</summary>
    public string? DataType { get; set; }

    /// <summary>Gets or sets the logical address of the created tag.</summary>
    public string? LogicalAddress { get; set; }

    /// <summary>Gets or sets the list of warnings.</summary>
    public List<WarningEntry> Warnings { get; set; } = new List<WarningEntry>();

    /// <summary>Gets or sets the error information (if creation failed).</summary>
    public ErrorEntry? Error { get; set; }
}
