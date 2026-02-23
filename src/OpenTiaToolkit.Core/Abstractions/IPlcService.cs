using OpenTiaToolkit.Core.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Abstractions;

/// <summary>
/// Defines the interface for PLC software operations.
/// Provides access to PLC device software, blocks, and compilation.
/// </summary>
public interface IPlcService
{
    /// <summary>
    /// Gets whether a PLC software is currently open.
    /// </summary>
    bool IsSoftwareOpen { get; }

    /// <summary>
    /// Gets the name of the currently open PLC.
    /// </summary>
    string? CurrentPlcName { get; }

    /// <summary>
    /// Opens PLC software from a device by name.
    /// </summary>
    /// <param name="plcName">The name of the PLC device.</param>
    /// <returns>True if the PLC software was opened successfully.</returns>
    Task<bool> OpenPlcAsync(string plcName);

    /// <summary>
    /// Closes the currently open PLC software.
    /// </summary>
    /// <returns>True if the PLC software was closed successfully.</returns>
    Task<bool> ClosePlcAsync();

    /// <summary>
    /// Gets the software tree structure (blocks, types, sources).
    /// </summary>
    /// <param name="maxDepth">Maximum depth to traverse (null for unlimited).</param>
    /// <returns>The root node of the software tree.</returns>
    Task<SoftwareTreeNode?> GetSoftwareTreeAsync(int? maxDepth = null);

    /// <summary>
    /// Compiles the PLC software.
    /// </summary>
    /// <param name="password">Optional password for protected blocks.</param>
    /// <returns>Compilation result with success status and diagnostics.</returns>
    Task<CompilationResult> CompileSoftwareAsync(string? password = null);

    /// <summary>
    /// Exports all blocks from the PLC software to XML files.
    /// </summary>
    /// <param name="exportPath">The directory path where XML files will be saved.</param>
    /// <returns>True if export was successful.</returns>
    Task<bool> ExportPlcAsync(string exportPath);

    /// <summary>
    /// Gets all blocks from the PLC software.
    /// </summary>
    /// <param name="filterRegex">Optional regex filter for block names.</param>
    /// <returns>List of block information.</returns>
    Task<IReadOnlyList<BlockInfo>> GetBlocksAsync(string? filterRegex = null);

    /// <summary>
    /// Deletes blocks matching the specified regex pattern.
    /// </summary>
    /// <param name="filterRegex">Regex pattern for block names to delete.</param>
    /// <param name="confirm">Whether to confirm deletion without prompting (default: false).</param>
    /// <returns>True if blocks were deleted successfully.</returns>
    Task<bool> DeleteBlocksAsync(string filterRegex, bool confirm = false);

    /// <summary>
    /// Gets detailed information about a specific block.
    /// </summary>
    /// <param name="blockName">The name of the block.</param>
    /// <returns>Detailed block information or null if not found.</returns>
    Task<BlockInfoDetail?> GetBlockInfoAsync(string blockName);

    /// <summary>
    /// Exports a single block to XML.
    /// </summary>
    /// <param name="blockName">The name of the block to export.</param>
    /// <param name="exportPath">The full file path where the XML will be saved.</param>
    /// <returns>True if the block was exported successfully.</returns>
    Task<bool> ExportBlockAsync(string blockName, string exportPath);

    /// <summary>
    /// Searches blocks by name or comment.
    /// </summary>
    /// <param name="pattern">Search text pattern.</param>
    /// <param name="searchIn">Where to search: "name" or "comment" (default: "name").</param>
    /// <param name="blockType">Optional block type filter (e.g., "FB", "FC", "DB", "OB").</param>
    /// <returns>List of matching blocks.</returns>
    Task<IReadOnlyList<BlockInfo>> SearchBlocksAsync(string pattern, string searchIn = "name", string? blockType = null);

    /// <summary>
    /// Creates a new block in the PLC software.
    /// </summary>
    /// <param name="blockName">The name of the block to create.</param>
    /// <param name="blockType">The type of block (FB, FC, DB, OB, SFB, SFC, UDT).</param>
    /// <param name="programmingLanguage">Optional programming language (e.g., "SCL", "FBD", "LAD", "ST").</param>
    /// <returns>True if the block was created successfully.</returns>
    Task<bool> CreateBlockAsync(string blockName, string blockType, string? programmingLanguage = null);

    /// <summary>
    /// Finds and replaces PLC tag names across all tag tables.
    /// </summary>
    /// <param name="renameMap">Dictionary mapping old names to new names (or substrings for substring matching).</param>
    /// <param name="caseInsensitive">Enable case-insensitive matching (default: false).</param>
    /// <param name="dryRun">Preview changes without applying (default: true).</param>
    /// <param name="substringMatch">Enable substring matching instead of exact matching (default: false).</param>
    /// <returns>Result containing summary, changes, skipped items, warnings, and errors.</returns>
    Task<FindReplaceResult> FindAndReplaceTagsAsync(
        Dictionary<string, string> renameMap,
        bool caseInsensitive = false,
        bool dryRun = true,
        bool substringMatch = false);

    /// <summary>
    /// Creates a new PLC tag in the specified tag table.
    /// </summary>
    /// <param name="tagName">The name of the tag to create.</param>
    /// <param name="dataType">The data type (e.g., "Bool", "Int", "DInt").</param>
    /// <param name="logicalAddress">Optional logical address (e.g., "%M10.0", "%MW12").</param>
    /// <param name="tagTableName">Optional tag table name (default: first/default table).</param>
    /// <returns>Result containing success status, created tag info, warnings, and errors.</returns>
    Task<CreateTagResult> CreateTagAsync(
        string tagName,
        string dataType,
        string? logicalAddress = null,
        string? tagTableName = null);

    /// <summary>
    /// Updates PLC tags from a CSV file based on page-based naming rules.
    /// Tags with page &gt;= 100 are matched by base name (can change both name and address).
    /// Tags with page &lt; 100 are matched by full name (address only).
    /// </summary>
    /// <param name="csvFilePath">Path to the CSV file containing tag updates.</param>
    /// <param name="dryRun">Preview changes without applying (default: true).</param>
    /// <returns>Result containing summary, updated tags, created tags, warnings, and errors.</returns>
    Task<TagUpdateResult> UpdateTagsFromCsvAsync(
        string csvFilePath,
        bool dryRun = true);

    /// <summary>
    /// Finds and replaces GlobalDB member names with nested struct support.
    /// </summary>
    /// <param name="renameMap">Dictionary mapping old member names to new names.</param>
    /// <param name="caseInsensitive">Enable case-insensitive matching.</param>
    /// <param name="dryRun">Preview changes without applying.</param>
    /// <param name="targetBlocks">Optional list of specific GlobalDB names (null = all).</param>
    /// <returns>Result containing summary, changes, skipped items, warnings, and errors.</returns>
    Task<FindReplaceResult> FindAndReplaceDbMembersAsync(
        Dictionary<string, string> renameMap,
        bool caseInsensitive = false,
        bool dryRun = true,
        string[]? targetBlocks = null);

    /// <summary>
    /// Finds and replaces UDT member names with nested struct support.
    /// </summary>
    /// <param name="renameMap">Dictionary mapping old member names to new names.</param>
    /// <param name="caseInsensitive">Enable case-insensitive matching.</param>
    /// <param name="dryRun">Preview changes without applying.</param>
    /// <param name="targetTypes">Optional list of specific UDT names (null = all).</param>
    /// <returns>Result containing summary, changes, skipped items, warnings, and errors.</returns>
    Task<FindReplaceResult> FindAndReplaceUdtMembersAsync(
        Dictionary<string, string> renameMap,
        bool caseInsensitive = false,
        bool dryRun = true,
        string[]? targetTypes = null);
}

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
    public CompilationResult(bool success, int errorCount, int warningCount, string? message)
    {
        Success = success;
        ErrorCount = errorCount;
        WarningCount = warningCount;
        Message = message;
    }

    /// <summary>Gets whether the compilation was successful.</summary>
    public bool Success { get; }

    /// <summary>Gets the number of errors found.</summary>
    public int ErrorCount { get; }

    /// <summary>Gets the number of warnings found.</summary>
    public int WarningCount { get; }

    /// <summary>Gets a descriptive message about the compilation result.</summary>
    public string? Message { get; }
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
/// Represents detailed information about a device.
/// </summary>
public class DeviceItemInfo
{
    public DeviceItemInfo(string name, string type, string? identification)
    {
        Name = name;
        Type = type;
        Identification = identification;
    }

    /// <summary>Gets the device item name.</summary>
    public string Name { get; }

    /// <summary>Gets the device item type.</summary>
    public string Type { get; }

    /// <summary>Gets the device item identification.</summary>
    public string? Identification { get; }
}

#region Find and Replace Data Models

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

#endregion
