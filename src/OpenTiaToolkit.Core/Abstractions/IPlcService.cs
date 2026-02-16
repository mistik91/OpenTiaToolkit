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
