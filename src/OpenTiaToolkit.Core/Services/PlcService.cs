using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace OpenTiaToolkit.Core.Services
{
    /// <summary>
    /// Provides PLC software operations, wrapping the TIA Openness API.
    /// </summary>
    public class PlcService : IPlcService
    {
        private readonly IPortalService _portalService;
        private readonly ILogger<PlcService>? _logger;
        private object? _plcSoftware;  // Will hold PlcSoftware object
        private Device? _currentDevice;

        /// <summary>
        /// Initializes a new instance of the <see cref="PlcService"/> class.
        /// </summary>
        /// <param name="portalService">The portal service instance.</param>
        /// <param name="logger">Optional logger for diagnostic output.</param>
        public PlcService(IPortalService portalService, ILogger<PlcService>? logger = null)
        {
            _portalService = portalService;
            _logger = logger;
        }

        /// <inheritdoc/>
        public bool IsSoftwareOpen => _plcSoftware != null;

        /// <inheritdoc/>
        public string? CurrentPlcName { get; private set; }

        /// <inheritdoc/>
        public async Task<bool> OpenPlcAsync(string plcName)
        {
            return await Task.Run(() =>
            {
                _logger?.LogInformation("Opening PLC software: {PlcName}", plcName);

                try
                {
                    var project = _portalService.Project;
                    if (project == null)
                    {
                        _logger?.LogError("No project is currently open");
                        return false;
                    }

                    // First, try to find a Device with the exact name
                    var device = project.Devices.FirstOrDefault(d =>
                        d.Name.Equals(plcName, StringComparison.OrdinalIgnoreCase));

                    // If not found, search all DeviceItems for matching PLC name
                    DeviceItem? foundDeviceItem = null;
                    if (device == null)
                    {
                        foreach (var dev in project.Devices)
                        {
                            foreach (DeviceItem deviceItem in dev.DeviceItems)
                            {
                                if (deviceItem.Name.Equals(plcName, StringComparison.OrdinalIgnoreCase))
                                {
                                    foundDeviceItem = deviceItem;
                                    device = dev;
                                    break;
                                }
                            }
                            if (device != null) break;
                        }
                    }

                    if (device == null)
                    {
                        _logger?.LogError("Device/DeviceItem not found: {PlcName}", plcName);
                        return false;
                    }

                    // Use BoemaCLITool pattern to get PlcSoftware
                    object? plcSoftware = null;

                    // If we found a DeviceItem directly, use its parent Device
                    var deviceItemsToSearch = foundDeviceItem != null
                        ? new[] { foundDeviceItem }
                        : (System.Collections.IEnumerable)device.DeviceItems;

                    foreach (DeviceItem deviceItem in deviceItemsToSearch)
                    {
                        try
                        {
                            // Try to get SoftwareContainer
                            var getMethod = typeof(DeviceItem).GetMethod("GetService");
                            if (getMethod != null)
                            {
                                // Find SoftwareContainer type
                                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                                Type? softwareContainerType = null;
                                foreach (var assembly in assemblies)
                                {
                                    try
                                    {
                                        if (assembly.FullName != null && assembly.FullName.StartsWith("Siemens.Engineering"))
                                        {
                                            var types = assembly.GetTypes();
                                            var foundType = types.FirstOrDefault(t => t.Name == "SoftwareContainer");
                                            if (foundType != null)
                                            {
                                                softwareContainerType = foundType;
                                                break;
                                            }
                                        }
                                    }
                                    catch
                                    {
                                        // Skip assemblies that can't be loaded
                                    }
                                }

                                if (softwareContainerType != null)
                                {
                                    var genericMethod = getMethod.MakeGenericMethod(softwareContainerType);
                                    var softwareContainer = genericMethod.Invoke(deviceItem, null);

                                    if (softwareContainer != null)
                                    {
                                        // Get Software property
                                        var softwareProperty = softwareContainerType.GetProperty("Software");
                                        if (softwareProperty != null)
                                        {
                                            var softwareBase = softwareProperty.GetValue(softwareContainer);

                                            // Find PlcSoftware type
                                            Type? plcSoftwareType = null;
                                            foreach (var assembly in assemblies)
                                            {
                                                try
                                                {
                                                    if (assembly.FullName != null && assembly.FullName.StartsWith("Siemens.Engineering"))
                                                    {
                                                        var types = assembly.GetTypes();
                                                        var foundType = types.FirstOrDefault(t => t.Name == "PlcSoftware");
                                                        if (foundType != null)
                                                        {
                                                            plcSoftwareType = foundType;
                                                            break;
                                                        }
                                                    }
                                                }
                                                catch
                                                {
                                                    // Skip assemblies that can't be loaded
                                                }
                                            }

                                            if (plcSoftwareType != null && softwareBase != null)
                                            {
                                                // Check if softwareBase is of type PlcSoftware
                                                if (plcSoftwareType.IsInstanceOfType(softwareBase))
                                                {
                                                    plcSoftware = softwareBase;
                                                    _currentDevice = device;  // Store parent Device
                                                    break;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogDebug(ex, "Failed to get software from device item: {DeviceItemName}", deviceItem.Name);
                        }
                    }

                    if (plcSoftware == null)
                    {
                        _logger?.LogError("Could not find PLC software for device: {PlcName}", plcName);
                        return false;
                    }

                    _plcSoftware = plcSoftware;
                    CurrentPlcName = plcName;

                    _logger?.LogInformation("PLC software opened successfully: {PlcName}", plcName);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to open PLC software: {PlcName}", plcName);
                    return false;
                }
            });
        }

        /// <inheritdoc/>
        public async Task<bool> ClosePlcAsync()
        {
            return await Task.Run(() =>
            {
                _logger?.LogInformation("Closing PLC software");

                _plcSoftware = null;
                _currentDevice = null;
                CurrentPlcName = null;

                return true;
            });
        }

        /// <inheritdoc/>
        public async Task<SoftwareTreeNode?> GetSoftwareTreeAsync(int? maxDepth = null)
        {
            return await Task.Run(() =>
            {
                if (_plcSoftware == null || string.IsNullOrEmpty(CurrentPlcName))
                {
                    _logger?.LogWarning("No PLC software is currently open");
                    return null;
                }

                var depthStr = maxDepth.HasValue ? maxDepth.Value.ToString() : "unlimited";
                _logger?.LogInformation("Getting PLC software tree (max depth: {MaxDepth})", depthStr);

                try
                {
                    // Create a basic tree structure with placeholder nodes
                    // In a real implementation, this would use the TIA Openness API to traverse the software
                    var children = new List<SoftwareTreeNode>
                    {
                        new SoftwareTreeNode("Blocks", "BlockContainer", $"{CurrentPlcName}/Blocks", new List<SoftwareTreeNode>()),
                        new SoftwareTreeNode("Types", "TypeContainer", $"{CurrentPlcName}/Types", new List<SoftwareTreeNode>()),
                        new SoftwareTreeNode("Sources", "SourceContainer", $"{CurrentPlcName}/Sources", new List<SoftwareTreeNode>())
                    };

                    return new SoftwareTreeNode(CurrentPlcName ?? "PLC", "PlcSoftware", CurrentPlcName ?? "PLC", children);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to get PLC software tree");
                    return null;
                }
            });
        }

        /// <inheritdoc/>
        public async Task<CompilationResult> CompileSoftwareAsync(string? password = null)
        {
            return await Task.Run(() =>
            {
                if (_plcSoftware == null || string.IsNullOrEmpty(CurrentPlcName))
                {
                    return new CompilationResult(
                        false,
                        0,
                        0,
                        "No PLC software is currently open"
                    );
                }

                _logger?.LogInformation("Compiling PLC software: {PlcName}", CurrentPlcName);

                try
                {
                    // In a real implementation, this would use the TIA Openness API to compile
                    // For now, return a placeholder result
                    return new CompilationResult(
                        true,
                        0,
                        0,
                        $"Compilation initiated for {CurrentPlcName}"
                    );
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to compile PLC software");
                    return new CompilationResult(
                        false,
                        1,
                        0,
                        $"Compilation failed: {ex.Message}"
                    );
                }
            });
        }

        /// <inheritdoc/>
        public async Task<bool> ExportPlcAsync(string exportPath)
        {
            return await Task.Run(() =>
            {
                if (_plcSoftware == null || string.IsNullOrEmpty(CurrentPlcName))
                {
                    _logger?.LogWarning("No PLC software is currently open");
                    return false;
                }

                _logger?.LogInformation("Exporting PLC blocks to: {ExportPath}", exportPath);

                try
                {
                    // Create export directory if it doesn't exist
                    if (!Directory.Exists(exportPath))
                    {
                        Directory.CreateDirectory(exportPath);
                    }

                    // Get PlcSoftware type
                    var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                    Type? plcSoftwareType = null;
                    foreach (var assembly in assemblies)
                    {
                        try
                        {
                            if (assembly.FullName != null && assembly.FullName.StartsWith("Siemens.Engineering"))
                            {
                                var types = assembly.GetTypes();
                                var foundType = types.FirstOrDefault(t => t.Name == "PlcSoftware");
                                if (foundType != null)
                                {
                                    plcSoftwareType = foundType;
                                    break;
                                }
                            }
                        }
                        catch
                        {
                            // Skip assemblies that can't be loaded
                        }
                    }

                    if (plcSoftwareType == null)
                    {
                        _logger?.LogError("Could not find PlcSoftware type");
                        return false;
                    }

                    // Get BlockGroup property
                    var blockGroupProperty = plcSoftwareType.GetProperty("BlockGroup");
                    if (blockGroupProperty == null)
                    {
                        _logger?.LogError("Could not find BlockGroup property");
                        return false;
                    }

                    var blockGroup = blockGroupProperty.GetValue(_plcSoftware);
                    if (blockGroup == null)
                    {
                        _logger?.LogError("BlockGroup is null");
                        return false;
                    }

                    // Get Blocks collection
                    var blocksProperty = blockGroup.GetType().GetProperty("Blocks");
                    if (blocksProperty == null)
                    {
                        _logger?.LogError("Could not find Blocks property");
                        return false;
                    }

                    var blocksCollection = blocksProperty.GetValue(blockGroup) as System.Collections.IEnumerable;
                    if (blocksCollection == null)
                    {
                        _logger?.LogError("Blocks collection is null");
                        return false;
                    }

                    // Find ExportOptions type and values
                    Type? exportOptionsType = null;
                    object? withDefaults = null;
                    object? withReadOnly = null;
                    object? withOriginalStructure = null;

                    foreach (var assembly in assemblies)
                    {
                        try
                        {
                            if (assembly.FullName != null && assembly.FullName.StartsWith("Siemens.Engineering"))
                            {
                                var types = assembly.GetTypes();
                                var foundType = types.FirstOrDefault(t => t.Name == "ExportOptions");
                                if (foundType != null)
                                {
                                    exportOptionsType = foundType;
                                    break;
                                }
                            }
                        }
                        catch
                        {
                            // Skip assemblies that can't be loaded
                        }
                    }

                    if (exportOptionsType != null)
                    {
                        // Get WithDefaults field
                        var withDefaultsField = exportOptionsType.GetField("WithDefaults");
                        if (withDefaultsField != null)
                        {
                            withDefaults = withDefaultsField.GetValue(null);
                        }

                        // Get WithReadOnly field
                        var withReadOnlyField = exportOptionsType.GetField("WithReadOnly");
                        if (withReadOnlyField != null)
                        {
                            withReadOnly = withReadOnlyField.GetValue(null);
                        }

                        // Get WithOriginalStructure field (may be needed for some block types)
                        var withOriginalStructureField = exportOptionsType.GetField("WithOriginalStructure");
                        if (withOriginalStructureField != null)
                        {
                            withOriginalStructure = withOriginalStructureField.GetValue(null);
                        }
                    }

                    int successCount = 0;
                    int failureCount = 0;
                    int skippedCount = 0;

                    // Iterate through blocks and export
                    foreach (var block in blocksCollection)
                    {
                        try
                        {
                            // Get block properties
                            var nameProperty = block.GetType().GetProperty("Name");
                            var typeProperty = block.GetType().GetProperty("Type");

                            string name = nameProperty?.GetValue(block)?.ToString() ?? "Unknown";
                            string? type = typeProperty?.GetValue(block)?.ToString();

                            // Create export file path
                            string fileName = !string.IsNullOrEmpty(type) ? $"{name}.{type}.xml" : $"{name}.xml";
                            string filePath = Path.Combine(exportPath, fileName);

                            bool exported = false;
                            Exception? lastException = null;

                            // Strategy 1: Try Export with ExportOptions.WithDefaults
                            if (!exported && exportOptionsType != null && withDefaults != null)
                            {
                                try
                                {
                                    var exportMethod = block.GetType().GetMethod("Export", new[] { typeof(FileInfo), exportOptionsType });
                                    if (exportMethod != null)
                                    {
                                        exportMethod.Invoke(block, new object[] { new FileInfo(filePath), withDefaults });
                                        exported = true;
                                        _logger?.LogInformation("Exported block with WithDefaults: {BlockName}", name);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger?.LogDebug(ex, "Export with WithDefaults failed for block: {BlockName}", name);
                                    lastException = ex;
                                }
                            }

                            // Strategy 2: Try Export with just FileInfo (no options)
                            if (!exported)
                            {
                                try
                                {
                                    var simpleExportMethod = block.GetType().GetMethod("Export", new[] { typeof(FileInfo) });
                                    if (simpleExportMethod != null)
                                    {
                                        simpleExportMethod.Invoke(block, new object[] { new FileInfo(filePath) });
                                        exported = true;
                                        _logger?.LogInformation("Exported block without options: {BlockName}", name);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger?.LogDebug(ex, "Export without options failed for block: {BlockName}", name);
                                    lastException = ex;
                                }
                            }

                            // Strategy 3: Try Export with string path instead of FileInfo
                            if (!exported)
                            {
                                try
                                {
                                    var stringExportMethod = block.GetType().GetMethod("Export", new[] { typeof(string) });
                                    if (stringExportMethod != null)
                                    {
                                        stringExportMethod.Invoke(block, new object[] { filePath });
                                        exported = true;
                                        _logger?.LogInformation("Exported block with string path: {BlockName}", name);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger?.LogDebug(ex, "Export with string path failed for block: {BlockName}", name);
                                    lastException = ex;
                                }
                            }

                            if (exported)
                            {
                                successCount++;
                            }
                            else
                            {
                                // Log detailed error information
                                if (lastException != null)
                                {
                                    _logger?.LogWarning(lastException, "Could not export block: {BlockName} (type: {Type})", name, type);
                                }
                                else
                                {
                                    _logger?.LogWarning("Could not export block: {BlockName} (type: {Type}) - No export method found", name, type);
                                }
                                failureCount++;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogWarning(ex, "Failed to export block");
                            failureCount++;
                        }
                    }

                    _logger?.LogInformation("Export complete: {SuccessCount} succeeded, {FailureCount} failed, {SkippedCount} skipped", successCount, failureCount, skippedCount);
                    return successCount > 0;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to export PLC blocks");
                    return false;
                }
            });
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<BlockInfo>> GetBlocksAsync(string? filterRegex = null)
        {
            return await Task.Run(() =>
            {
                var blocks = new List<BlockInfo>();

                if (_plcSoftware == null || string.IsNullOrEmpty(CurrentPlcName))
                {
                    _logger?.LogWarning("No PLC software is currently open");
                    return blocks;
                }

                _logger?.LogInformation("Getting PLC blocks (filter: {Filter})", filterRegex ?? "none");

                try
                {
                    // Get PlcSoftware type
                    var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                    Type? plcSoftwareType = null;
                    foreach (var assembly in assemblies)
                    {
                        try
                        {
                            if (assembly.FullName != null && assembly.FullName.StartsWith("Siemens.Engineering"))
                            {
                                var types = assembly.GetTypes();
                                var foundType = types.FirstOrDefault(t => t.Name == "PlcSoftware");
                                if (foundType != null)
                                {
                                    plcSoftwareType = foundType;
                                    break;
                                }
                            }
                        }
                        catch
                        {
                            // Skip assemblies that can't be loaded
                        }
                    }

                    if (plcSoftwareType == null)
                    {
                        _logger?.LogError("Could not find PlcSoftware type");
                        return blocks;
                    }

                    // Get BlockGroup property
                    var blockGroupProperty = plcSoftwareType.GetProperty("BlockGroup");
                    if (blockGroupProperty == null)
                    {
                        _logger?.LogError("Could not find BlockGroup property");
                        return blocks;
                    }

                    var blockGroup = blockGroupProperty.GetValue(_plcSoftware);
                    if (blockGroup == null)
                    {
                        _logger?.LogError("BlockGroup is null");
                        return blocks;
                    }

                    // Get Blocks collection
                    var blocksProperty = blockGroup.GetType().GetProperty("Blocks");
                    if (blocksProperty == null)
                    {
                        _logger?.LogError("Could not find Blocks property");
                        return blocks;
                    }

                    var blocksCollection = blocksProperty.GetValue(blockGroup) as System.Collections.IEnumerable;
                    if (blocksCollection == null)
                    {
                        _logger?.LogError("Blocks collection is null");
                        return blocks;
                    }

                    // Iterate through blocks
                    foreach (var block in blocksCollection)
                    {
                        try
                        {
                            // Get block properties
                            var nameProperty = block.GetType().GetProperty("Name");
                            var typeProperty = block.GetType().GetProperty("Type");
                            var numberProperty = block.GetType().GetProperty("Number");
                            var languageProperty = block.GetType().GetProperty("ProgrammingLanguage");

                            string name = nameProperty?.GetValue(block)?.ToString() ?? "Unknown";
                            string? type = typeProperty?.GetValue(block)?.ToString();
                            string? number = numberProperty?.GetValue(block)?.ToString();
                            string? language = languageProperty?.GetValue(block)?.ToString();

                            // Get ReadOnly property
                            bool isReadOnly = false;
                            var isReadOnlyProperty = block.GetType().GetProperty("IsReadOnly");
                            if (isReadOnlyProperty != null)
                            {
                                var isReadOnlyValue = isReadOnlyProperty.GetValue(block);
                                if (isReadOnlyValue != null)
                                {
                                    isReadOnly = (bool)isReadOnlyValue;
                                }
                            }

                            // Get Comment property
                            string? comment = null;
                            var commentProperty = block.GetType().GetProperty("Comment");
                            if (commentProperty != null)
                            {
                                var commentObj = commentProperty.GetValue(block);
                                if (commentObj != null)
                                {
                                    // Comment might be a special object, try to get its text
                                    var textProperty = commentObj.GetType().GetProperty("Text");
                                    if (textProperty != null)
                                    {
                                        comment = textProperty.GetValue(commentObj)?.ToString();
                                    }
                                    else
                                    {
                                        comment = commentObj.ToString();
                                    }
                                }
                            }

                            if (type == null) type = "Unknown";

                            var blockInfo = new BlockInfo(name, type, language, number, isReadOnly, comment);
                            blocks.Add(blockInfo);
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogDebug(ex, "Failed to get info for block");
                        }
                    }

                    // Apply regex filter if provided
                    if (!string.IsNullOrEmpty(filterRegex))
                    {
                        try
                        {
                            var regex = new Regex(filterRegex, RegexOptions.IgnoreCase);
                            blocks = blocks.Where(b => regex.IsMatch(b.Name)).ToList();
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogWarning(ex, "Invalid regex filter: {FilterRegex}", filterRegex);
                        }
                    }

                    _logger?.LogInformation("Found {BlockCount} blocks", blocks.Count);
                    return blocks;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to get PLC blocks");
                    return blocks;
                }
            });
        }

        /// <inheritdoc/>
        public async Task<bool> DeleteBlocksAsync(string filterRegex, bool confirm = false)
        {
            return await Task.Run(() =>
            {
                if (_plcSoftware == null || string.IsNullOrEmpty(CurrentPlcName))
                {
                    _logger?.LogWarning("No PLC software is currently open");
                    return false;
                }

                _logger?.LogInformation("Deleting blocks matching filter: {FilterRegex} (confirm: {Confirm})", filterRegex, confirm);

                try
                {
                    // Get blocks matching the filter
                    var blocksTask = GetBlocksAsync(null);
                    var blocks = blocksTask.GetAwaiter().GetResult();
                    var regex = new Regex(filterRegex, RegexOptions.IgnoreCase);
                    var blocksToDelete = blocks.Where(b => regex.IsMatch(b.Name)).ToList();

                    if (blocksToDelete.Count == 0)
                    {
                        _logger?.LogInformation("No blocks matched the filter");
                        return false;
                    }

                    // Show preview of blocks to delete
                    _logger?.LogInformation("Found {Count} blocks to delete:", blocksToDelete.Count);
                    foreach (var block in blocksToDelete)
                    {
                        _logger?.LogInformation("  - {BlockName} ({Type}){ReadOnly}", block.Name, block.Type, block.IsReadOnly ? " [Read-Only]" : "");
                    }

                    // Require confirmation unless confirm=true
                    if (!confirm)
                    {
                        _logger?.LogWarning("Deletion requires confirmation. Use --confirm=true to proceed.");
                        return false;
                    }

                    // Get PlcSoftware type and blocks
                    var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                    Type? plcSoftwareType = null;
                    foreach (var assembly in assemblies)
                    {
                        try
                        {
                            if (assembly.FullName != null && assembly.FullName.StartsWith("Siemens.Engineering"))
                            {
                                var types = assembly.GetTypes();
                                var foundType = types.FirstOrDefault(t => t.Name == "PlcSoftware");
                                if (foundType != null)
                                {
                                    plcSoftwareType = foundType;
                                    break;
                                }
                            }
                        }
                        catch
                        {
                            // Skip assemblies that can't be loaded
                        }
                    }

                    if (plcSoftwareType == null)
                    {
                        _logger?.LogError("Could not find PlcSoftware type");
                        return false;
                    }

                    // Get BlockGroup property
                    var blockGroupProperty = plcSoftwareType.GetProperty("BlockGroup");
                    if (blockGroupProperty == null)
                    {
                        _logger?.LogError("Could not find BlockGroup property");
                        return false;
                    }

                    var blockGroup = blockGroupProperty.GetValue(_plcSoftware);
                    if (blockGroup == null)
                    {
                        _logger?.LogError("BlockGroup is null");
                        return false;
                    }

                    // Get Blocks collection
                    var blocksProperty = blockGroup.GetType().GetProperty("Blocks");
                    if (blocksProperty == null)
                    {
                        _logger?.LogError("Could not find Blocks property");
                        return false;
                    }

                    var blocksCollection = blocksProperty.GetValue(blockGroup) as System.Collections.IEnumerable;
                    if (blocksCollection == null)
                    {
                        _logger?.LogError("Blocks collection is null");
                        return false;
                    }

                    int deletedCount = 0;
                    int skippedCount = 0;
                    int errorCount = 0;

                    // Iterate through blocks and delete matching ones
                    foreach (var block in blocksCollection)
                    {
                        try
                        {
                            // Get block name
                            var nameProperty = block.GetType().GetProperty("Name");
                            var isReadOnlyProperty = block.GetType().GetProperty("IsReadOnly");

                            string name = nameProperty?.GetValue(block)?.ToString() ?? "";

                            // Check if this block matches our filter
                            if (!regex.IsMatch(name))
                            {
                                continue;
                            }

                            // Check if block is read-only
                            bool isReadOnly = false;
                            if (isReadOnlyProperty != null)
                            {
                                var isReadOnlyValue = isReadOnlyProperty.GetValue(block);
                                if (isReadOnlyValue != null)
                                {
                                    isReadOnly = (bool)isReadOnlyValue;
                                }
                            }

                            if (isReadOnly)
                            {
                                _logger?.LogWarning("Skipping read-only block: {BlockName}", name);
                                skippedCount++;
                                continue;
                            }

                            // Call Delete method
                            var deleteMethod = block.GetType().GetMethod("Delete");
                            if (deleteMethod != null)
                            {
                                deleteMethod.Invoke(block, null);
                                deletedCount++;
                                _logger?.LogDebug("Deleted block: {BlockName}", name);
                            }
                            else
                            {
                                _logger?.LogWarning("Could not find Delete method for block: {BlockName}", name);
                                errorCount++;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogWarning(ex, "Failed to delete block");
                            errorCount++;
                        }
                    }

                    _logger?.LogInformation("Deletion complete: {DeletedCount} deleted, {SkippedCount} skipped (read-only), {ErrorCount} errors", deletedCount, skippedCount, errorCount);
                    return deletedCount > 0;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to delete blocks");
                    return false;
                }
            });
        }

        /// <inheritdoc/>
        public async Task<BlockInfoDetail?> GetBlockInfoAsync(string blockName)
        {
            return await Task.Run(() =>
            {
                if (_plcSoftware == null || string.IsNullOrEmpty(CurrentPlcName))
                {
                    _logger?.LogWarning("No PLC software is currently open");
                    return null;
                }

                _logger?.LogInformation("Getting block info: {BlockName}", blockName);

                try
                {
                    // Get PlcSoftware type
                    var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                    Type? plcSoftwareType = null;
                    foreach (var assembly in assemblies)
                    {
                        try
                        {
                            if (assembly.FullName != null && assembly.FullName.StartsWith("Siemens.Engineering"))
                            {
                                var types = assembly.GetTypes();
                                var foundType = types.FirstOrDefault(t => t.Name == "PlcSoftware");
                                if (foundType != null)
                                {
                                    plcSoftwareType = foundType;
                                    break;
                                }
                            }
                        }
                        catch
                        {
                            // Skip assemblies that can't be loaded
                        }
                    }

                    if (plcSoftwareType == null)
                    {
                        _logger?.LogError("Could not find PlcSoftware type");
                        return null;
                    }

                    // Get BlockGroup property
                    var blockGroupProperty = plcSoftwareType.GetProperty("BlockGroup");
                    if (blockGroupProperty == null)
                    {
                        _logger?.LogError("Could not find BlockGroup property");
                        return null;
                    }

                    var blockGroup = blockGroupProperty.GetValue(_plcSoftware);
                    if (blockGroup == null)
                    {
                        _logger?.LogError("BlockGroup is null");
                        return null;
                    }

                    // Get Blocks collection
                    var blocksProperty = blockGroup.GetType().GetProperty("Blocks");
                    if (blocksProperty == null)
                    {
                        _logger?.LogError("Could not find Blocks property");
                        return null;
                    }

                    var blocksCollection = blocksProperty.GetValue(blockGroup) as System.Collections.IEnumerable;
                    if (blocksCollection == null)
                    {
                        _logger?.LogError("Blocks collection is null");
                        return null;
                    }

                    // Find the block by name
                    foreach (var block in blocksCollection)
                    {
                        try
                        {
                            var nameProperty = block.GetType().GetProperty("Name");
                            string name = nameProperty?.GetValue(block)?.ToString() ?? "";

                            if (name.Equals(blockName, StringComparison.OrdinalIgnoreCase))
                            {
                                // Get all properties
                                var typeProperty = block.GetType().GetProperty("Type");
                                var numberProperty = block.GetType().GetProperty("Number");
                                var languageProperty = block.GetType().GetProperty("ProgrammingLanguage");
                                var isReadOnlyProperty = block.GetType().GetProperty("IsReadOnly");
                                var commentProperty = block.GetType().GetProperty("Comment");

                                string? type = typeProperty?.GetValue(block)?.ToString();
                                string? number = numberProperty?.GetValue(block)?.ToString();
                                string? language = languageProperty?.GetValue(block)?.ToString();
                                bool isReadOnly = false;
                                string? comment = null;

                                if (isReadOnlyProperty != null)
                                {
                                    var isReadOnlyValue = isReadOnlyProperty.GetValue(block);
                                    if (isReadOnlyValue != null)
                                    {
                                        isReadOnly = (bool)isReadOnlyValue;
                                    }
                                }

                                if (commentProperty != null)
                                {
                                    var commentObj = commentProperty.GetValue(block);
                                    if (commentObj != null)
                                    {
                                        var textProperty = commentObj.GetType().GetProperty("Text");
                                        if (textProperty != null)
                                        {
                                            comment = textProperty.GetValue(commentObj)?.ToString();
                                        }
                                        else
                                        {
                                            comment = commentObj.ToString();
                                        }
                                    }
                                }

                                // Try to get additional properties if available
                                string? author = null;
                                string? created = null;
                                string? modified = null;
                                int memorySize = 0;
                                string? programmingLanguage = language;

                                // Try to get Author property
                                var authorProperty = block.GetType().GetProperty("Author");
                                if (authorProperty != null)
                                {
                                    author = authorProperty.GetValue(block)?.ToString();
                                }

                                // Try to memory-related properties
                                var memorySizeProperty = block.GetType().GetProperty("MemorySize");
                                if (memorySizeProperty != null)
                                {
                                    var memorySizeValue = memorySizeProperty.GetValue(block);
                                    if (memorySizeValue != null)
                                    {
                                        int.TryParse(memorySizeValue.ToString(), out memorySize);
                                    }
                                }

                                return new BlockInfoDetail(
                                    name,
                                    type ?? "Unknown",
                                    language,
                                    number,
                                    isReadOnly,
                                    comment,
                                    author,
                                    created,
                                    modified,
                                    memorySize,
                                    programmingLanguage
                                );
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogDebug(ex, "Failed to get info for block");
                        }
                    }

                    _logger?.LogWarning("Block not found: {BlockName}", blockName);
                    return null;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to get block info: {BlockName}", blockName);
                    return null;
                }
            });
        }

        /// <inheritdoc/>
        public async Task<bool> ExportBlockAsync(string blockName, string exportPath)
        {
            return await Task.Run(() =>
            {
                if (_plcSoftware == null || string.IsNullOrEmpty(CurrentPlcName))
                {
                    _logger?.LogWarning("No PLC software is currently open");
                    return false;
                }

                _logger?.LogInformation("Exporting block: {BlockName} to {ExportPath}", blockName, exportPath);

                try
                {
                    // Get PlcSoftware type
                    var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                    Type? plcSoftwareType = null;
                    foreach (var assembly in assemblies)
                    {
                        try
                        {
                            if (assembly.FullName != null && assembly.FullName.StartsWith("Siemens.Engineering"))
                            {
                                var types = assembly.GetTypes();
                                var foundType = types.FirstOrDefault(t => t.Name == "PlcSoftware");
                                if (foundType != null)
                                {
                                    plcSoftwareType = foundType;
                                    break;
                                }
                            }
                        }
                        catch
                        {
                            // Skip assemblies that can't be loaded
                        }
                    }

                    if (plcSoftwareType == null)
                    {
                        _logger?.LogError("Could not find PlcSoftware type");
                        return false;
                    }

                    // Get BlockGroup property
                    var blockGroupProperty = plcSoftwareType.GetProperty("BlockGroup");
                    if (blockGroupProperty == null)
                    {
                        _logger?.LogError("Could not find BlockGroup property");
                        return false;
                    }

                    var blockGroup = blockGroupProperty.GetValue(_plcSoftware);
                    if (blockGroup == null)
                    {
                        _logger?.LogError("BlockGroup is null");
                        return false;
                    }

                    // Get Blocks collection
                    var blocksProperty = blockGroup.GetType().GetProperty("Blocks");
                    if (blocksProperty == null)
                    {
                        _logger?.LogError("Could not find Blocks property");
                        return false;
                    }

                    var blocksCollection = blocksProperty.GetValue(blockGroup) as System.Collections.IEnumerable;
                    if (blocksCollection == null)
                    {
                        _logger?.LogError("Blocks collection is null");
                        return false;
                    }

                    // Find ExportOptions type
                    Type? exportOptionsType = null;
                    object? withDefaults = null;

                    foreach (var assembly in assemblies)
                    {
                        try
                        {
                            if (assembly.FullName != null && assembly.FullName.StartsWith("Siemens.Engineering"))
                            {
                                var types = assembly.GetTypes();
                                var foundType = types.FirstOrDefault(t => t.Name == "ExportOptions");
                                if (foundType != null)
                                {
                                    exportOptionsType = foundType;
                                    break;
                                }
                            }
                        }
                        catch
                        {
                            // Skip assemblies that can't be loaded
                        }
                    }

                    if (exportOptionsType != null)
                    {
                        var withDefaultsField = exportOptionsType.GetField("WithDefaults");
                        if (withDefaultsField != null)
                        {
                            withDefaults = withDefaultsField.GetValue(null);
                        }
                    }

                    // Find the block by name and export it
                    foreach (var block in blocksCollection)
                    {
                        try
                        {
                            var nameProperty = block.GetType().GetProperty("Name");
                            var typeProperty = block.GetType().GetProperty("Type");

                            string name = nameProperty?.GetValue(block)?.ToString() ?? "";
                            string? type = typeProperty?.GetValue(block)?.ToString();

                            if (name.Equals(blockName, StringComparison.OrdinalIgnoreCase))
                            {
                                // Ensure directory exists
                                var directory = Path.GetDirectoryName(exportPath);
                                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                                {
                                    Directory.CreateDirectory(directory);
                                }

                                bool exported = false;
                                Exception? lastException = null;

                                // Strategy 1: Try Export with ExportOptions.WithDefaults
                                if (!exported && exportOptionsType != null && withDefaults != null)
                                {
                                    try
                                    {
                                        var exportMethod = block.GetType().GetMethod("Export", new[] { typeof(FileInfo), exportOptionsType });
                                        if (exportMethod != null)
                                        {
                                            exportMethod.Invoke(block, new object[] { new FileInfo(exportPath), withDefaults });
                                            exported = true;
                                            _logger?.LogInformation("Successfully exported block with WithDefaults: {BlockName} to {ExportPath}", blockName, exportPath);
                                            return true;
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger?.LogDebug(ex, "Export with WithDefaults failed for block: {BlockName}", blockName);
                                        lastException = ex;
                                    }
                                }

                                // Strategy 2: Try Export with just FileInfo (no options)
                                if (!exported)
                                {
                                    try
                                    {
                                        var simpleExportMethod = block.GetType().GetMethod("Export", new[] { typeof(FileInfo) });
                                        if (simpleExportMethod != null)
                                        {
                                            simpleExportMethod.Invoke(block, new object[] { new FileInfo(exportPath) });
                                            exported = true;
                                            _logger?.LogInformation("Successfully exported block without options: {BlockName} to {ExportPath}", blockName, exportPath);
                                            return true;
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger?.LogDebug(ex, "Export without options failed for block: {BlockName}", blockName);
                                        lastException = ex;
                                    }
                                }

                                // Strategy 3: Try Export with string path instead of FileInfo
                                if (!exported)
                                {
                                    try
                                    {
                                        var stringExportMethod = block.GetType().GetMethod("Export", new[] { typeof(string) });
                                        if (stringExportMethod != null)
                                        {
                                            stringExportMethod.Invoke(block, new object[] { exportPath });
                                            exported = true;
                                            _logger?.LogInformation("Successfully exported block with string path: {BlockName} to {ExportPath}", blockName, exportPath);
                                            return true;
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger?.LogDebug(ex, "Export with string path failed for block: {BlockName}", blockName);
                                        lastException = ex;
                                    }
                                }

                                // All strategies failed
                                _logger?.LogError(lastException, "Failed to export block: {BlockName} (type: {Type})", blockName, type);
                                return false;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogWarning(ex, "Failed to export block: {BlockName}", blockName);
                        }
                    }

                    _logger?.LogWarning("Block not found: {BlockName}", blockName);
                    return false;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to export block: {BlockName}", blockName);
                    return false;
                }
            });
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<BlockInfo>> SearchBlocksAsync(string pattern, string searchIn = "name", string? blockType = null)
        {
            return await Task.Run(() =>
            {
                if (_plcSoftware == null || string.IsNullOrEmpty(CurrentPlcName))
                {
                    _logger?.LogWarning("No PLC software is currently open");
                    return new List<BlockInfo>();
                }

                _logger?.LogInformation("Searching blocks: pattern='{Pattern}', searchIn='{SearchIn}', blockType='{BlockType}'", pattern, searchIn, blockType);

                try
                {
                    // Get all blocks first
                    var allBlocksTask = GetBlocksAsync(null);
                    var allBlocks = allBlocksTask.GetAwaiter().GetResult();
                    var results = new List<BlockInfo>();

                    // Normalize search parameter
                    bool searchInName = searchIn.Equals("name", StringComparison.OrdinalIgnoreCase);
                    bool searchInComment = searchIn.Equals("comment", StringComparison.OrdinalIgnoreCase);
                    bool searchInBoth = !searchInName && !searchInComment;

                    // Perform search
                    foreach (var block in allBlocks)
                    {
                        bool matches = false;

                        // Search in name
                        if (searchInName || searchInBoth)
                        {
                            if (block.Name.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                matches = true;
                            }
                        }

                        // Search in comment
                        if ((searchInComment || searchInBoth) && !matches)
                        {
                            if (!string.IsNullOrEmpty(block.Comment) &&
                                block.Comment.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                matches = true;
                            }
                        }

                        // Apply type filter if specified
                        if (matches && !string.IsNullOrEmpty(blockType))
                        {
                            matches = block.Type.Equals(blockType, StringComparison.OrdinalIgnoreCase);
                        }

                        if (matches)
                        {
                            results.Add(block);
                        }
                    }

                    _logger?.LogInformation("Search found {Count} matching blocks", results.Count);
                    return results;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to search blocks");
                    return new List<BlockInfo>();
                }
            });
        }

        /// <inheritdoc/>
        public async Task<bool> CreateBlockAsync(string blockName, string blockType, string? programmingLanguage = null)
        {
            return await Task.Run(() =>
            {
                if (_plcSoftware == null || string.IsNullOrEmpty(CurrentPlcName))
                {
                    _logger?.LogWarning("No PLC software is currently open");
                    return false;
                }

                _logger?.LogInformation("Creating block: {BlockName} of type {BlockType} (language: {Language})", blockName, blockType, programmingLanguage ?? "default");

                try
                {
                    // Validate block name
                    if (string.IsNullOrWhiteSpace(blockName))
                    {
                        _logger?.LogError("Block name cannot be empty");
                        return false;
                    }

                    // Check if block already exists
                    var existingBlocksTask = GetBlocksAsync(null);
                    var existingBlocks = existingBlocksTask.GetAwaiter().GetResult();
                    if (existingBlocks.Any(b => b.Name.Equals(blockName, StringComparison.OrdinalIgnoreCase)))
                    {
                        _logger?.LogError("Block already exists: {BlockName}", blockName);
                        return false;
                    }

                    // Get PlcSoftware type
                    var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                    Type? plcSoftwareType = null;
                    foreach (var assembly in assemblies)
                    {
                        try
                        {
                            if (assembly.FullName != null && assembly.FullName.StartsWith("Siemens.Engineering"))
                            {
                                var types = assembly.GetTypes();
                                var foundType = types.FirstOrDefault(t => t.Name == "PlcSoftware");
                                if (foundType != null)
                                {
                                    plcSoftwareType = foundType;
                                    break;
                                }
                            }
                        }
                        catch
                        {
                            // Skip assemblies that can't be loaded
                        }
                    }

                    if (plcSoftwareType == null)
                    {
                        _logger?.LogError("Could not find PlcSoftware type");
                        return false;
                    }

                    // Get BlockGroup property
                    var blockGroupProperty = plcSoftwareType.GetProperty("BlockGroup");
                    if (blockGroupProperty == null)
                    {
                        _logger?.LogError("Could not find BlockGroup property");
                        return false;
                    }

                    var blockGroup = blockGroupProperty.GetValue(_plcSoftware);
                    if (blockGroup == null)
                    {
                        _logger?.LogError("BlockGroup is null");
                        return false;
                    }

                    // Get Blocks collection
                    var blocksProperty = blockGroup.GetType().GetProperty("Blocks");
                    if (blocksProperty == null)
                    {
                        _logger?.LogError("Could not find Blocks property");
                        return false;
                    }

                    var blocksCollection = blocksProperty.GetValue(blockGroup);
                    if (blocksCollection == null)
                    {
                        _logger?.LogError("Blocks collection is null");
                        return false;
                    }

                    // Find PlcBlockType enum
                    Type? plcBlockTypeEnum = null;
                    object? blockTypeEnumValue = null;

                    foreach (var assembly in assemblies)
                    {
                        try
                        {
                            if (assembly.FullName != null && assembly.FullName.StartsWith("Siemens.Engineering"))
                            {
                                var types = assembly.GetTypes();
                                var foundType = types.FirstOrDefault(t => t.Name == "BlockType");
                                if (foundType != null && foundType.IsEnum)
                                {
                                    plcBlockTypeEnum = foundType;

                                    // Map common block type abbreviations - identity mapping since BlockType uses abbreviations directly
                                    // TIA Portal BlockType enum uses abbreviations: FB, FC, DB, OB, SFB, SFC, UDT
                                    var blockTypeMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                                    {
                                        { "FB", "FB" },
                                        { "FC", "FC" },
                                        { "DB", "DB" },
                                        { "OB", "OB" },
                                        { "SFB", "SFB" },
                                        { "SFC", "SFC" },
                                        { "UDT", "UDT" }
                                    };

                                    string targetName = blockTypeMapping.TryGetValue(blockType, out var fullName) ? fullName : blockType;

                                    // Log all available enum values for debugging
                                    var enumValues = Enum.GetValues(foundType);
                                    _logger?.LogError("DEBUG: Available PlcBlockType enum values:");
                                    foreach (var val in enumValues)
                                    {
                                        _logger?.LogError("DEBUG:   - {EnumValue}", val.ToString());
                                    }

                                    // Try to find enum value by full name
                                    foreach (var val in enumValues)
                                    {
                                        if (val.ToString().Equals(targetName, StringComparison.OrdinalIgnoreCase))
                                        {
                                            blockTypeEnumValue = val;
                                            _logger?.LogError("DEBUG: Found enum value by full name: {EnumValue}", val.ToString());
                                            break;
                                        }
                                    }

                                    // If still not found, try partial matching
                                    if (blockTypeEnumValue == null)
                                    {
                                        foreach (var val in enumValues)
                                        {
                                            var enumName = val.ToString();
                                            if (enumName.IndexOf(blockType, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                blockType.IndexOf(enumName, StringComparison.OrdinalIgnoreCase) >= 0)
                                            {
                                                blockTypeEnumValue = val;
                                                _logger?.LogError("DEBUG: Found enum value by partial match: {EnumName} -> {BlockType}", enumName, blockType);
                                                break;
                                            }
                                        }
                                    }

                                    if (blockTypeEnumValue != null)
                                    {
                                        _logger?.LogError("DEBUG: blockTypeEnumValue found: {EnumValue}", blockTypeEnumValue.ToString());
                                    }
                                    else
                                    {
                                        _logger?.LogError("DEBUG: blockTypeEnumValue is NULL! targetName={TargetName}, blockType={BlockType}", targetName, blockType);
                                    }
                                }
                            }
                        }
                        catch
                        {
                            // Skip assemblies that can't be loaded
                        }
                    }

                    if (plcBlockTypeEnum == null || blockTypeEnumValue == null)
                    {
                        _logger?.LogError("Could not find BlockType enum or invalid block type: {BlockType}", blockType);
                        return false;
                    }

                    var blocksCollectionType = blocksCollection.GetType();
                    _logger?.LogInformation("Blocks collection type: {BlocksCollectionType}", blocksCollectionType.FullName);

                    // Try TIA Portal V21+ approach: Generic Create method (for forward compatibility)
                    _logger?.LogInformation("Attempting TIA Portal V21+ API (for forward compatibility): Generic Create method");
                    var createMethod = blocksCollectionType.GetMethod("Create",
                        new[] { typeof(string), plcBlockTypeEnum });

                    if (createMethod == null && !string.IsNullOrEmpty(programmingLanguage))
                    {
                        // Try the overload with programming language
                        createMethod = blocksCollection.GetType().GetMethod("Create",
                            new[] { typeof(string), plcBlockTypeEnum, typeof(string) });
                    }

                    object? createdBlock = null;

                    // If V21+ generic Create method exists, use it (forward compatibility)
                    if (createMethod != null)
                    {
                        _logger?.LogInformation("Using TIA Portal V21+ API (forward compatibility): Create method found");

                        // Invoke Create method
                        if (createMethod.GetParameters().Length == 2)
                        {
                            createdBlock = createMethod.Invoke(blocksCollection, new[] { blockName, blockTypeEnumValue });
                        }
                        else if (createMethod.GetParameters().Length == 3)
                        {
                            if (string.IsNullOrEmpty(programmingLanguage))
                            {
                                _logger?.LogError("Programming language is required for block creation");
                                return false;
                            }
                            createdBlock = createMethod.Invoke(blocksCollection, new[] { blockName, blockTypeEnumValue, programmingLanguage });
                        }
                    }
                    else
                    {
                        // Fall back to TIA Portal V19 approach: Type-specific Create methods (primary target)
                        _logger?.LogInformation("Using TIA Portal V19 API (primary): Type-specific Create methods");

                        var blockTypeUpper = blockType.ToUpperInvariant();
                        if (blockTypeUpper == "FB" || blockTypeUpper == "SFB")
                        {
                            createdBlock = CreateFBV19(blocksCollection, blockName, programmingLanguage);
                        }
                        else if (blockTypeUpper == "DB")
                        {
                            createdBlock = CreateGlobalDBV19(blocksCollection, blockName, programmingLanguage);
                        }
                        else
                        {
                            _logger?.LogError("Block type '{BlockType}' is not supported in TIA Portal V19. " +
                                "Supported types in V19: FB, SFB, InstanceDB. " +
                                "For FC, DB (global), OB support, upgrade to TIA Portal V21 or later.", blockType);
                            createdBlock = null;
                        }
                    }

                    if (createdBlock != null)
                    {
                        _logger?.LogInformation("Block created successfully: {BlockName}", blockName);
                        return true;
                    }
                    else
                    {
                        _logger?.LogError("Failed to create block: {BlockName}", blockName);
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to create block: {BlockName}", blockName);
                    return false;
                }
            });
        }

        /// <summary>
        /// Creates a Function Block (FB) in TIA Portal V19 using the CreateFB method
        /// </summary>
        private object? CreateFBV19(object blocksCollection, string blockName, string? programmingLanguage)
        {
            try
            {
                _logger?.LogInformation("Creating FB using V19 API: CreateFB");

                // Get ProgrammingLanguage enum value
                var programmingLanguageValue = GetProgrammingLanguageEnum(programmingLanguage ?? "SCL");
                if (programmingLanguageValue == null)
                {
                    _logger?.LogError("Invalid programming language: {Language}", programmingLanguage);
                    return null;
                }

                // Get CreateFB method: CreateFB(string name, bool isAutoNumbered, int number, ProgrammingLanguage language)
                var createFBMethod = blocksCollection.GetType().GetMethod("CreateFB",
                    new[] { typeof(string), typeof(bool), typeof(int), programmingLanguageValue.GetType() });

                if (createFBMethod == null)
                {
                    _logger?.LogError("CreateFB method not found on blocks collection");
                    return null;
                }

                // Invoke CreateFB with default values: autoNumbered=true, number=0
                var createdBlock = createFBMethod.Invoke(blocksCollection, new[] { blockName, true, 0, programmingLanguageValue });
                _logger?.LogInformation("FB created successfully using V19 API");
                return createdBlock;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to create FB using V19 API");
                return null;
            }
        }

        /// <summary>
        /// Creates a Global Data Block (DB) in TIA Portal V19 (primary target)
        /// Note: In V19, only InstanceDBs can be created programmatically. Global DBs require V21+.
        /// </summary>
        private object? CreateGlobalDBV19(object blocksCollection, string blockName, string? programmingLanguage)
        {
            _logger?.LogWarning("Global DB creation is not supported in TIA Portal V19 API.");
            _logger?.LogWarning("Only Instance DBs (attached to FBs) can be created in V19.");
            _logger?.LogWarning("For global DB creation, upgrade to TIA Portal V21 or later.");
            _logger?.LogInformation("To create an Instance DB in V19, specify the FB name it should be attached to.");
            return null;
        }

        /// <summary>
        /// Gets the ProgrammingLanguage enum value from a string
        /// </summary>
        private object? GetProgrammingLanguageEnum(string language)
        {
            try
            {
                // Load assemblies to find ProgrammingLanguage enum
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();

                foreach (var assembly in assemblies)
                {
                    try
                    {
                        if (assembly.FullName != null && assembly.FullName.StartsWith("Siemens.Engineering"))
                        {
                            var types = assembly.GetTypes();
                            var programmingLanguageType = types.FirstOrDefault(t => t.Name == "ProgrammingLanguage");

                            if (programmingLanguageType != null && programmingLanguageType.IsEnum)
                            {
                                // Map common language names to enum values
                                var languageMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                                {
                                    { "SCL", "SCL" },
                                    { "FBD", "FBD" },
                                    { "LAD", "LD" },
                                    { "STL", "STL" },
                                    { "GRAPH", "GRAPH" }
                                };

                                string targetName = languageMapping.TryGetValue(language, out var fullName) ? fullName : language;

                                foreach (var val in Enum.GetValues(programmingLanguageType))
                                {
                                    if (val.ToString()!.Equals(targetName, StringComparison.OrdinalIgnoreCase))
                                    {
                                        return val;
                                    }
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Skip assemblies that can't be loaded
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}
