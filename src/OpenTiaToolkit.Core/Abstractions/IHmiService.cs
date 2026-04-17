using OpenTiaToolkit.Core.Commands.Hmi;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Abstractions;

/// <summary>
/// Defines the interface for Unified HMI operations.
/// </summary>
public interface IHmiService
{
    /// <summary>
    /// Gets whether a Unified HMI is currently open.
    /// </summary>
    bool IsHmiOpen { get; }

    /// <summary>
    /// Gets the name of the currently open Unified HMI.
    /// </summary>
    string? CurrentHmiName { get; }

    /// <summary>
    /// Opens a Unified HMI by device or device item name.
    /// </summary>
    /// <param name="hmiName">The HMI device or device item name.</param>
    /// <returns>True if the HMI was opened successfully.</returns>
    Task<bool> OpenUnifiedHmiAsync(string hmiName);

    /// <summary>
    /// Closes the currently selected Unified HMI.
    /// </summary>
    /// <returns>True if the selection was cleared successfully.</returns>
    Task<bool> CloseUnifiedHmiAsync();

    /// <summary>
    /// Lists screens in the active Unified HMI.
    /// </summary>
    /// <param name="groupPath">Optional group path to scope the listing.</param>
    /// <param name="recursive">Whether nested groups should be traversed.</param>
    /// <returns>The matching screens.</returns>
    Task<IReadOnlyList<HmiScreenInfo>> GetScreensAsync(string? groupPath = null, bool recursive = true);

    /// <summary>
    /// Gets detailed information about a screen.
    /// </summary>
    /// <param name="screenPath">The slash-delimited screen path.</param>
    /// <returns>The screen information, or null if the screen was not found.</returns>
    Task<HmiScreenInfo?> GetScreenInfoAsync(string screenPath);

    /// <summary>
    /// Creates a screen if it does not already exist.
    /// </summary>
    /// <param name="screenPath">The slash-delimited screen path.</param>
    /// <param name="width">Optional screen width override.</param>
    /// <param name="height">Optional screen height override.</param>
    /// <returns>The created or existing screen.</returns>
    Task<HmiScreenInfo?> CreateScreenAsync(string screenPath, int? width = null, int? height = null);

    /// <summary>
    /// Lists the validated HMI item types supported by this toolkit.
    /// </summary>
    /// <param name="filter">Optional text filter.</param>
    /// <returns>The supported item types.</returns>
    Task<IReadOnlyList<HmiItemTypeInfo>> GetSupportedItemTypesAsync(string? filter = null);

    /// <summary>
    /// Lists tags in the active Unified HMI.
    /// </summary>
    /// <param name="filter">Optional text filter.</param>
    /// <returns>The matching tags.</returns>
    Task<IReadOnlyList<HmiTagInfo>> GetTagsAsync(string? filter = null);

    /// <summary>
    /// Creates a screen item if it does not already exist.
    /// </summary>
    /// <param name="screenPath">The slash-delimited screen path.</param>
    /// <param name="itemType">The requested item type or alias.</param>
    /// <param name="itemName">The new item name.</param>
    /// <param name="left">Optional left position.</param>
    /// <param name="top">Optional top position.</param>
    /// <param name="width">Optional width override.</param>
    /// <param name="height">Optional height override.</param>
    /// <returns>The created or existing item.</returns>
    Task<HmiScreenItemInfo?> CreateScreenItemAsync(
        string screenPath,
        string itemType,
        string itemName,
        int? left = null,
        int? top = null,
        int? width = null,
        int? height = null);

    /// <summary>
    /// Lists readable properties for a screen item.
    /// </summary>
    /// <param name="screenPath">The slash-delimited screen path.</param>
    /// <param name="itemName">The item name.</param>
    /// <param name="propertyNames">Optional requested property names.</param>
    /// <returns>The item properties.</returns>
    Task<IReadOnlyList<HmiItemPropertyInfo>> GetItemPropertiesAsync(
        string screenPath,
        string itemName,
        string[]? propertyNames = null);

    /// <summary>
    /// Sets a property or attribute on a screen item.
    /// </summary>
    /// <param name="screenPath">The slash-delimited screen path.</param>
    /// <param name="itemName">The item name.</param>
    /// <param name="propertyName">The property or attribute name.</param>
    /// <param name="value">The new value.</param>
    /// <returns>True if the property was updated successfully.</returns>
    Task<bool> SetItemPropertyAsync(string screenPath, string itemName, string propertyName, string value);

    /// <summary>
    /// Copies a screen from a template screen.
    /// </summary>
    /// <param name="templateScreenPath">The template screen path.</param>
    /// <param name="targetScreenPath">The target screen path.</param>
    /// <param name="findText">Optional text to replace while cloning.</param>
    /// <param name="replaceText">Optional replacement text.</param>
    /// <returns>The copied screen result, or null if the copy failed.</returns>
    Task<HmiScreenCopyResult?> CopyScreenFromTemplateAsync(
        string templateScreenPath,
        string targetScreenPath,
        string? findText = null,
        string? replaceText = null);

    /// <summary>
    /// Exports the supported Unified HMI surface to JSON.
    /// </summary>
    /// <param name="outputPath">The target folder or full JSON file path.</param>
    /// <param name="filters">Optional export filters.</param>
    /// <returns>The export result, or null if export failed.</returns>
    Task<HmiJsonExportResult?> ExportJsonAsync(string outputPath, HmiJsonFilterOptions? filters = null);

    /// <summary>
    /// Creates a minimal patch JSON by comparing two Unified HMI JSON exports.
    /// </summary>
    /// <param name="basePath">The base export path.</param>
    /// <param name="modifiedPath">The edited export path.</param>
    /// <param name="patchPath">The target patch path.</param>
    /// <param name="filters">Optional diff filters.</param>
    /// <returns>The diff result, or null if patch generation failed.</returns>
    Task<HmiJsonDiffResult?> DiffJsonAsync(
        string basePath,
        string modifiedPath,
        string patchPath,
        HmiJsonFilterOptions? filters = null);

    /// <summary>
    /// Preflights or imports a Unified HMI JSON export or patch document.
    /// </summary>
    /// <param name="inputPath">The source export or patch path.</param>
    /// <param name="filters">Optional runtime filters.</param>
    /// <param name="dryRun">Whether the run should only preflight changes.</param>
    /// <param name="executionOptions">Optional safety and backup settings.</param>
    /// <returns>The import or preflight result, or null if the run failed.</returns>
    Task<HmiJsonImportResult?> ImportJsonAsync(
        string inputPath,
        HmiJsonFilterOptions? filters = null,
        bool dryRun = false,
        HmiJsonImportExecutionOptions? executionOptions = null);

    /// <summary>
    /// Exports one Unified HMI screen as canonical JSON for agent editing.
    /// </summary>
    /// <param name="screenPath">The slash-delimited screen path.</param>
    /// <param name="filters">Optional item/property filters.</param>
    /// <param name="workingPath">Optional artifact root path.</param>
    /// <returns>The screen JSON snapshot, or null if export failed.</returns>
    Task<HmiScreenJsonSnapshotResult?> GetScreenJsonAsync(
        string screenPath,
        HmiJsonFilterOptions? filters = null,
        string? workingPath = null);

    /// <summary>
    /// Plans or applies a single-screen JSON edit using the import/export pipeline.
    /// </summary>
    /// <param name="screenPath">The slash-delimited screen path.</param>
    /// <param name="editedScreen">The edited canonical screen JSON.</param>
    /// <param name="baseHash">Optional expected base hash from a prior snapshot.</param>
    /// <param name="filters">Optional item/property filters.</param>
    /// <param name="dryRun">Whether the edit should only be preflighted.</param>
    /// <param name="executionOptions">Optional safety and backup settings.</param>
    /// <param name="workingPath">Optional artifact root path.</param>
    /// <param name="allowStaleBase">Whether a base hash mismatch should be allowed.</param>
    /// <returns>The edit result, or null if planning failed before a result could be created.</returns>
    Task<HmiScreenJsonEditResult?> ApplyScreenJsonEditAsync(
        string screenPath,
        HmiJsonScreen editedScreen,
        string? baseHash = null,
        HmiJsonFilterOptions? filters = null,
        bool dryRun = true,
        HmiJsonImportExecutionOptions? executionOptions = null,
        string? workingPath = null,
        bool allowStaleBase = false);

    /// <summary>
    /// Plans or applies small agent-oriented patch operations against a single screen.
    /// </summary>
    /// <param name="screenPath">The slash-delimited screen path.</param>
    /// <param name="operations">The patch operations to apply to the exported screen JSON.</param>
    /// <param name="baseHash">Optional expected base hash from a prior snapshot.</param>
    /// <param name="filters">Optional item/property filters.</param>
    /// <param name="dryRun">Whether the edit should only be preflighted.</param>
    /// <param name="executionOptions">Optional safety and backup settings.</param>
    /// <param name="workingPath">Optional artifact root path.</param>
    /// <param name="allowStaleBase">Whether a base hash mismatch should be allowed.</param>
    /// <returns>The edit result with patch operation details, or null if planning failed before a result could be created.</returns>
    Task<HmiScreenJsonEditResult?> ApplyScreenJsonPatchAsync(
        string screenPath,
        IReadOnlyList<HmiScreenJsonPatchOperation> operations,
        string? baseHash = null,
        HmiJsonFilterOptions? filters = null,
        bool dryRun = true,
        HmiJsonImportExecutionOptions? executionOptions = null,
        string? workingPath = null,
        bool allowStaleBase = false);

    /// <summary>
    /// Creates or updates the logging infrastructure for a Unified HMI trend tag.
    /// </summary>
    /// <param name="tagPath">The HMI tag path, including nested members if needed.</param>
    /// <param name="dataLogName">The target data log name.</param>
    /// <param name="cyclic">Whether cyclic logging should be used.</param>
    /// <param name="cycle">Optional cycle value. Defaults to T5s for cyclic logging.</param>
    /// <param name="storageFolder">Optional storage folder. Defaults to /Trends.</param>
    /// <param name="logMaxSize">Optional log max size. Defaults to 2000.</param>
    /// <param name="durationDays">Optional log duration in days. Defaults to 365.</param>
    /// <param name="segmentMaxSize">Optional segment max size. Defaults to 500.</param>
    /// <returns>The configured trend information, or null if the tag was not found.</returns>
    Task<HmiTrendInfo?> CreateTrendAsync(
        string tagPath,
        string dataLogName,
        bool cyclic = false,
        string? cycle = null,
        string storageFolder = "/Trends",
        int logMaxSize = 2000,
        int durationDays = 365,
        int segmentMaxSize = 500);
}
