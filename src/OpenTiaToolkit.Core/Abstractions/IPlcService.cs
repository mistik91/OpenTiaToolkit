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
    /// Logs into the offline safety program for the currently open F-PLC when the SafetyAdministration service is available.
    /// </summary>
    /// <param name="password">The safety program password.</param>
    /// <returns>Result describing whether safety access is available.</returns>
    Task<SafetyLoginResult> LoginToSafetyOfflineProgramAsync(string password);

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
    /// Compares the currently open PLC software to its online target.
    /// </summary>
    /// <returns>Structured compare result.</returns>
    Task<PlcSoftwareCompareResult> CompareSoftwareToOnlineAsync();

    /// <summary>
    /// Downloads PLC software through a guarded simulator-only path.
    /// </summary>
    /// <param name="toSimulator">Require PLCSIM Advanced as the target.</param>
    /// <param name="dryRun">Validate and return a plan without downloading.</param>
    /// <param name="confirm">Explicit confirmation for non-dry-run downloads.</param>
    /// <param name="onlyChanges">Download software changes instead of the full software load.</param>
    /// <returns>Structured download result.</returns>
    Task<PlcSoftwareDownloadResult> DownloadSoftwareAsync(bool toSimulator, bool dryRun = true, bool confirm = false, bool onlyChanges = false);

    /// <summary>
    /// Gets or establishes online status for the currently open PLC.
    /// </summary>
    /// <param name="goOnline">Attempt to go online before reading status.</param>
    /// <returns>Structured online status.</returns>
    Task<PlcOnlineStatusResult> GetOnlineStatusAsync(bool goOnline = true);

    /// <summary>
    /// Attempts to start the CPU when the selected online provider exposes a supported operation.
    /// </summary>
    /// <param name="confirm">Explicit confirmation for runtime state changes.</param>
    /// <returns>Structured CPU control result.</returns>
    Task<PlcCpuControlResult> StartCpuAsync(bool confirm = false);

    /// <summary>
    /// Attempts to stop the CPU when the selected online provider exposes a supported operation.
    /// </summary>
    /// <param name="confirm">Explicit confirmation for runtime state changes.</param>
    /// <returns>Structured CPU control result.</returns>
    Task<PlcCpuControlResult> StopCpuAsync(bool confirm = false);

    /// <summary>
    /// Exports PLC tags from all tag tables.
    /// </summary>
    /// <param name="culture">Project language culture used for tag comments.</param>
    /// <returns>Structured tag export result.</returns>
    Task<PlcTagExportResult> ExportTagsAsync(string culture = "en-US");

    /// <summary>
    /// Gets cross references for a tag, block, or other named PLC object.
    /// </summary>
    /// <param name="name">Object name to look up.</param>
    /// <returns>Structured cross-reference result.</returns>
    Task<PlcCrossReferenceResult> GetCrossReferencesAsync(string name);

    /// <summary>
    /// Lists PLC external source files.
    /// </summary>
    Task<IReadOnlyList<PlcSurfaceItem>> ListExternalSourcesAsync();

    /// <summary>
    /// Lists PLC watch and force tables.
    /// </summary>
    Task<IReadOnlyList<PlcSurfaceItem>> ListWatchTablesAsync();

    /// <summary>
    /// Lists PLC technology objects.
    /// </summary>
    Task<IReadOnlyList<PlcSurfaceItem>> ListTechnologyObjectsAsync();

    /// <summary>
    /// Lists PLC alarm text lists.
    /// </summary>
    Task<IReadOnlyList<PlcSurfaceItem>> ListPlcAlarmTextListsAsync();

    /// <summary>
    /// Imports a PLC external source file when supported by the selected PLC software.
    /// </summary>
    Task<PlcSourceOperationResult> ImportSourceAsync(string sourcePath, bool dryRun = true, bool confirm = false);

    /// <summary>
    /// Generates blocks from an imported PLC external source when supported.
    /// </summary>
    Task<PlcSourceOperationResult> GenerateBlocksFromSourceAsync(string sourceName, bool dryRun = true, bool confirm = false);

    /// <summary>
    /// Exports a PLC block as an external source file when supported.
    /// </summary>
    Task<PlcSourceOperationResult> ExportBlockAsSourceAsync(string blockName, string outputPath);

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
    /// Exports a single block as SIMATIC SD documents (V20+).
    /// Creates .s7dcl and .s7res files.
    /// </summary>
    /// <param name="blockName">The name of the block to export.</param>
    /// <param name="exportPath">The directory path where files will be saved.</param>
    /// <returns>True if the block was exported successfully.</returns>
    Task<bool> ExportBlockAsDocumentsAsync(string blockName, string exportPath);

    /// <summary>
    /// Exports multiple blocks as SIMATIC SD documents (V20+).
    /// </summary>
    /// <param name="filterRegex">Optional regex filter for block names.</param>
    /// <param name="exportPath">The directory path where files will be saved.</param>
    /// <returns>Export result with success/failure statistics.</returns>
    Task<ExportResult> ExportBlocksAsDocumentsAsync(string? filterRegex, string exportPath);

    /// <summary>
    /// Exports a single type (UDT) to XML.
    /// </summary>
    /// <param name="typeName">The name of the type to export.</param>
    /// <param name="exportPath">The full file path where the XML will be saved.</param>
    /// <returns>True if the type was exported successfully.</returns>
    Task<bool> ExportTypeAsync(string typeName, string exportPath);

    /// <summary>
    /// Exports multiple types (UDT) to XML.
    /// </summary>
    /// <param name="filterRegex">Optional regex filter for type names.</param>
    /// <param name="exportPath">The directory path where files will be saved.</param>
    /// <returns>Export result with success/failure statistics.</returns>
    Task<ExportResult> ExportTypesAsync(string? filterRegex, string exportPath);

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
    /// <param name="blockType">The type of block to create. The current validated V21 path is limited.</param>
    /// <param name="programmingLanguage">Optional programming language. The currently validated V21 path supports ProDiag FB creation.</param>
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

    /// <summary>
    /// Imports a single block from XML.
    /// </summary>
    /// <param name="importPath">The full file path of the XML file to import.</param>
    /// <param name="groupPath">Optional path within PLC program (e.g., "Program Blocks/Motors"). Empty for root.</param>
    /// <returns>True if the block was imported successfully.</returns>
    Task<bool> ImportBlockAsync(string importPath, string? groupPath = null);

    /// <summary>
    /// Imports multiple blocks from XML files in a directory.
    /// </summary>
    /// <param name="importDirectory">The directory containing XML files to import.</param>
    /// <param name="filterRegex">Optional regex filter for file names.</param>
    /// <param name="groupPath">Optional path within PLC program.</param>
    /// <returns>Import result with success/failure statistics.</returns>
    Task<ImportResult> ImportBlocksAsync(string importDirectory, string? filterRegex = null, string? groupPath = null);

    /// <summary>
    /// Imports a single type (UDT) from XML.
    /// </summary>
    /// <param name="importPath">The full file path of the XML file to import.</param>
    /// <param name="groupPath">Optional path within type groups (e.g., "Types/Subgroups"). Empty for root.</param>
    /// <returns>True if the type was imported successfully.</returns>
    Task<bool> ImportTypeAsync(string importPath, string? groupPath = null);

    /// <summary>
    /// Imports multiple types (UDT) from XML files in a directory.
    /// </summary>
    /// <param name="importDirectory">The directory containing XML files to import.</param>
    /// <param name="filterRegex">Optional regex filter for file names.</param>
    /// <param name="groupPath">Optional path within type groups.</param>
    /// <returns>Import result with success/failure statistics.</returns>
    Task<ImportResult> ImportTypesAsync(string importDirectory, string? filterRegex = null, string? groupPath = null);

    /// <summary>
    /// Imports a single block from SIMATIC SD documents (.s7dcl/.s7res) - V20+ only.
    /// </summary>
    /// <param name="importDirectory">The directory containing .s7dcl and .s7res files.</param>
    /// <param name="fileNameWithoutExtension">Block name without extension.</param>
    /// <param name="groupPath">Optional path within PLC program.</param>
    /// <param name="importOption">Import behavior: "Override", "None", "SkipInactiveCultures", "ActivateInactiveCultures".</param>
    /// <returns>True if the block was imported successfully.</returns>
    Task<bool> ImportBlockFromDocumentsAsync(string importDirectory, string fileNameWithoutExtension, string? groupPath = null, string importOption = "Override");

    /// <summary>
    /// Imports multiple blocks from SIMATIC SD documents (.s7dcl/.s7res) - V20+ only.
    /// </summary>
    /// <param name="importDirectory">The directory containing document files.</param>
    /// <param name="filterRegex">Optional regex filter for block names.</param>
    /// <param name="groupPath">Optional path within PLC program.</param>
    /// <param name="importOption">Import behavior option.</param>
    /// <returns>Import result with success/failure statistics.</returns>
    Task<ImportResult> ImportBlocksFromDocumentsAsync(string importDirectory, string? filterRegex = null, string? groupPath = null, string importOption = "Override");
}
