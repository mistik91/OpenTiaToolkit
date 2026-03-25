using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Utilities;
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
        private bool _diagnosticsRun = false;

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
                    var rootName = TiaReflectionHelper.GetName(_plcSoftware) ?? CurrentPlcName ?? "PLC";
                    var rootPath = rootName;
                    var children = new List<SoftwareTreeNode>();

                    AddSoftwareContainerNode(
                        children,
                        "Blocks",
                        "BlockContainer",
                        rootPath,
                        TiaReflectionHelper.GetBlockGroup(_plcSoftware),
                        maxDepth,
                        1,
                        new[] { "Blocks" });

                    AddSoftwareContainerNode(
                        children,
                        "Types",
                        "TypeContainer",
                        rootPath,
                        TiaReflectionHelper.GetTypeGroup(_plcSoftware),
                        maxDepth,
                        1,
                        new[] { "Types", "Documents" });

                    AddSoftwareContainerNode(
                        children,
                        "Tag Tables",
                        "TagTableContainer",
                        rootPath,
                        TiaReflectionHelper.GetTagTableGroup(_plcSoftware),
                        maxDepth,
                        1,
                        new[] { "TagTables" });

                    AddSoftwareContainerNode(
                        children,
                        "Source Files",
                        "ExternalSourceContainer",
                        rootPath,
                        TiaReflectionHelper.GetExternalSourceGroup(_plcSoftware),
                        maxDepth,
                        1,
                        new[] { "ExternalSources" });

                    return new SoftwareTreeNode(rootName, "PlcSoftware", rootPath, children);
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
                    if (!string.IsNullOrWhiteSpace(password))
                    {
                        _logger?.LogWarning("Password parameter is currently ignored by the V21 compile service");
                    }

                    var compilableType = FindSiemensType("Siemens.Engineering.Compiler.ICompilable");
                    if (compilableType == null)
                    {
                        return new CompilationResult(
                            false,
                            0,
                            0,
                            "Could not locate Siemens.Engineering.Compiler.ICompilable"
                        );
                    }

                    var compileService = GetEngineeringService(_plcSoftware, compilableType);
                    if (compileService == null)
                    {
                        return new CompilationResult(
                            false,
                            0,
                            0,
                            "PLC software does not expose the ICompilable service"
                        );
                    }

                    var compileMethod = compileService.GetType().GetMethod("Compile", Type.EmptyTypes);
                    if (compileMethod == null)
                    {
                        return new CompilationResult(
                            false,
                            0,
                            0,
                            "ICompilable service does not provide a Compile() method"
                        );
                    }

                    var rawResult = compileMethod.Invoke(compileService, null);
                    if (rawResult == null)
                    {
                        return new CompilationResult(
                            false,
                            0,
                            0,
                            "Compile() returned no result"
                        );
                    }

                    var errorCount = GetIntProperty(rawResult, "ErrorCount");
                    var warningCount = GetIntProperty(rawResult, "WarningCount");
                    var state = TiaReflectionHelper.GetProperty(rawResult, "State")?.ToString() ?? "Unknown";
                    var success = !string.Equals(state, "Error", StringComparison.OrdinalIgnoreCase) && errorCount == 0;
                    var message = BuildCompilationMessage(rawResult, state, errorCount, warningCount);

                    return new CompilationResult(
                        success,
                        errorCount,
                        warningCount,
                        message
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

                            type = InferBlockTypeName(block, type);

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

                                type = InferBlockTypeName(block, type);

                                return new BlockInfoDetail(
                                    name,
                                    type,
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
                            var blockComment = block.Comment;
                            if (blockComment is string nonNullBlockComment &&
                                nonNullBlockComment.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
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

                    // Normalize block type to uppercase for comparison
                    string blockTypeUpper = blockType.ToUpperInvariant();

                    // Get ProgrammingLanguage enum value if needed
                    object? programmingLanguageValue = null;
                    if (!string.IsNullOrEmpty(programmingLanguage))
                    {
                        programmingLanguageValue = GetProgrammingLanguageEnum(programmingLanguage);
                        if (programmingLanguageValue == null)
                        {
                            _logger?.LogError("Invalid programming language: {Language}", programmingLanguage);
                            return false;
                        }
                    }

                    object? createdBlock = null;

                    // Current validated V21 support is intentionally conservative.
                    // The runtime block composition on this project exposes CreateFB and
                    // CreateInstanceDB, but CreateFB accepts only ProDiag in this context,
                    // and the current service surface has no "instance-of" parameter for
                    // Instance DB creation yet.
                    if (blockTypeUpper == "FB")
                    {
                        if (string.IsNullOrWhiteSpace(programmingLanguage))
                        {
                            _logger?.LogError("Programming language is required for V21 FB creation. Supported language: ProDiag");
                            return false;
                        }

                        var requestedLanguage = programmingLanguage!;
                        if (!requestedLanguage.Equals("ProDiag", StringComparison.OrdinalIgnoreCase) &&
                            !requestedLanguage.Equals("S7_PDIAG", StringComparison.OrdinalIgnoreCase))
                        {
                            _logger?.LogError(
                                "Unsupported programming language for V21 FB creation: {Language}. Supported language: ProDiag",
                                requestedLanguage);
                            return false;
                        }

                        createdBlock = CreateBlockV21(blocksCollection, "CreateFB", blockName, programmingLanguageValue, requireLanguage: true);
                    }
                    else
                    {
                        _logger?.LogError(
                            "Unsupported block type for the current validated V21 creation path: {BlockType}. Supported: FB with ProDiag",
                            blockType);
                        return false;
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
        /// Creates a block in TIA Portal V21 using specific creation methods (CreateFB, CreateFC, CreateGlobalDB)
        /// </summary>
        private object? CreateBlockV21(object blocksCollection, string methodName, string blockName, object? programmingLanguageValue, bool requireLanguage)
        {
            try
            {
                _logger?.LogInformation("Creating block using V21 API: {MethodName}", methodName);

                // Validate programming language if required
                if (requireLanguage && programmingLanguageValue == null)
                {
                    _logger?.LogError("Programming language is required for {MethodName}", methodName);
                    return null;
                }

                // Get the creation method with signature: (string name, bool autoNumbered, int number, [ProgrammingLanguage language])
                var blocksCollectionType = blocksCollection.GetType();

                // List available methods for diagnostics (only on first call)
                if (!_diagnosticsRun)
                {
                    PlcServiceDiagnostics.ListBlocksCollectionMethods(blocksCollection, _logger);
                    _diagnosticsRun = true;
                }

                // Try with programming language parameter
                if (programmingLanguageValue != null)
                {
                    var createMethod = blocksCollectionType.GetMethod(methodName,
                        new[] { typeof(string), typeof(bool), typeof(int), programmingLanguageValue.GetType() });

                    if (createMethod != null)
                    {
                        var createdBlock = createMethod.Invoke(blocksCollection, new object[] { blockName, true, 0, programmingLanguageValue });
                        _logger?.LogInformation("Block created successfully using {MethodName} with language", methodName);
                        return createdBlock;
                    }
                }

                // Try without programming language parameter
                var createMethodSimple = blocksCollectionType.GetMethod(methodName,
                    new[] { typeof(string), typeof(bool), typeof(int) });

                if (createMethodSimple != null)
                {
                    var createdBlock = createMethodSimple.Invoke(blocksCollection, new object[] { blockName, true, 0 });
                    _logger?.LogInformation("Block created successfully using {MethodName} without language", methodName);
                    return createdBlock;
                }

                _logger?.LogError("{MethodName} method not found on blocks collection", methodName);
                return null;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to create block using {MethodName}", methodName);
                return null;
            }
        }

        /// <summary>
        /// Creates an Organization Block (OB) in TIA Portal V21
        /// </summary>
        private object? CreateOBV21(object blocksCollection, string blockName)
        {
            try
            {
                _logger?.LogInformation("Creating OB using V21 API: CreateOB");

                // Try to extract OB number from block name (e.g., "OB1" -> 1, "Main" -> 1)
                int obNumber = 1; // Default to OB1 (Main)

                // Try to parse number from name
                var match = System.Text.RegularExpressions.Regex.Match(blockName, @"(\d+)");
                if (match.Success)
                {
                    int.TryParse(match.Groups[1].Value, out obNumber);
                }

                var blocksCollectionType = blocksCollection.GetType();
                var createMethod = blocksCollectionType.GetMethod("CreateOB",
                    new[] { typeof(int), typeof(string) });

                if (createMethod != null)
                {
                    var createdBlock = createMethod.Invoke(blocksCollection, new object[] { obNumber, blockName });
                    _logger?.LogInformation("OB created successfully: {BlockName} (number: {OBNumber})", blockName, obNumber);
                    return createdBlock;
                }

                // Try alternative signature: (string name, int number)
                var createMethodAlt = blocksCollectionType.GetMethod("CreateOB",
                    new[] { typeof(string), typeof(int) });

                if (createMethodAlt != null)
                {
                    var createdBlock = createMethodAlt.Invoke(blocksCollection, new object[] { blockName, obNumber });
                    _logger?.LogInformation("OB created successfully: {BlockName} (number: {OBNumber})", blockName, obNumber);
                    return createdBlock;
                }

                _logger?.LogError("CreateOB method not found on blocks collection");
                return null;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to create OB block");
                return null;
            }
        }

        /// <summary>
        /// Creates a User Defined Type (UDT) in TIA Portal V21
        /// </summary>
        private object? CreateUDTV21(object blocksCollection, string blockName)
        {
            try
            {
                _logger?.LogInformation("Creating UDT using V21 API");

                // Get BlockType.UDT enum value
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                Type? blockTypeEnum = null;
                object? udtEnumValue = null;

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
                                blockTypeEnum = foundType;
                                foreach (var val in Enum.GetValues(foundType))
                                {
                                    if (val.ToString() == "UDT")
                                    {
                                        udtEnumValue = val;
                                        break;
                                    }
                                }
                                break;
                            }
                        }
                    }
                    catch
                    {
                        // Skip assemblies that can't be loaded
                    }
                }

                if (blockTypeEnum == null || udtEnumValue == null)
                {
                    _logger?.LogError("Could not find BlockType.UDT enum value");
                    return null;
                }

                // Try the generic Create method: Create(string name, BlockType type)
                var blocksCollectionType = blocksCollection.GetType();
                var createMethod = blocksCollectionType.GetMethod("Create",
                    new[] { typeof(string), blockTypeEnum });

                if (createMethod != null)
                {
                    var createdBlock = createMethod.Invoke(blocksCollection, new object[] { blockName, udtEnumValue });
                    _logger?.LogInformation("UDT created successfully: {BlockName}", blockName);
                    return createdBlock;
                }

                _logger?.LogError("Create method not found for UDT");
                return null;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to create UDT block");
                return null;
            }
        }

        /// <summary>
        /// Gets the ProgrammingLanguage enum value from a string
        /// </summary>
        private object? GetProgrammingLanguageEnum(string? language)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(language))
                {
                    return null;
                }

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
                                    { "GRAPH", "GRAPH" },
                                    { "PRODIAG", "ProDiag" },
                                    { "S7_PDIAG", "ProDiag" }
                                };

                                var requestedLanguage = language!;
                                string targetName = languageMapping.TryGetValue(requestedLanguage, out var fullName) ? fullName : requestedLanguage;

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

        private string InferBlockTypeName(object block, string? explicitType)
        {
            if (!string.IsNullOrWhiteSpace(explicitType))
            {
                return explicitType!;
            }

            var runtimeTypeName = block.GetType().Name;

            return runtimeTypeName switch
            {
                "FB" => "FB",
                "FC" => "FC",
                "OB" => "OB",
                "GlobalDB" => "DB",
                "InstanceDB" => "InstanceDB",
                "CodeBlock" => "CodeBlock",
                _ => "Unknown"
            };
        }

        /// <inheritdoc/>
        public async Task<FindReplaceResult> FindAndReplaceTagsAsync(
            Dictionary<string, string> renameMap,
            bool caseInsensitive = false,
            bool dryRun = true,
            bool substringMatch = false)
        {
            var result = new FindReplaceResult();

            return await Task.Run(() =>
            {
                if (_plcSoftware == null || string.IsNullOrEmpty(CurrentPlcName))
                {
                    result.Summary.Errors = 1;
                    result.Errors.Add(new ErrorEntry
                    {
                        ItemType = "System",
                        Error = "No PLC software is currently open"
                    });
                    return result;
                }

                _logger?.LogInformation("FindAndReplaceTagsAsync called with {Count} mappings, caseInsensitive={CaseInsensitive}, dryRun={DryRun}, substringMatch={SubstringMatch}",
                    renameMap.Count, caseInsensitive, dryRun, substringMatch);

                try
                {
                    // Get TagTableGroup using reflection helper
                    var tagTableGroup = TiaReflectionHelper.GetTagTableGroup(_plcSoftware);
                    if (tagTableGroup == null)
                    {
                        result.Summary.Errors = 1;
                        result.Errors.Add(new ErrorEntry
                        {
                            ItemType = "System",
                            Error = "PLC software does not support tag tables (TagTableGroup property not found)"
                        });
                        return result;
                    }

                    // Get TagTables collection
                    var tagTables = TiaReflectionHelper.GetTagTables(tagTableGroup);
                    if (tagTables == null)
                    {
                        result.Summary.Errors = 1;
                        result.Errors.Add(new ErrorEntry
                        {
                            ItemType = "System",
                            Error = "TagTables collection is null or not enumerable"
                        });
                        return result;
                    }

                    // Collect existing tag names for collision detection
                    var existingTagNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    // Process each tag table
                    foreach (var tagTable in tagTables)
                    {
                        var tagTableName = TiaReflectionHelper.GetName(tagTable) ?? "Unknown";
                        _logger?.LogDebug("Processing tag table: {TagTableName}", tagTableName);

                        // Get tags collection
                        var tags = TiaReflectionHelper.GetTags(tagTable);
                        if (tags == null) continue;

                        foreach (var tag in tags)
                        {
                            result.Summary.TotalProcessed++;

                            // Get name using reflection helper for consistency
                            var oldName = TiaReflectionHelper.GetName(tag) ?? "Unknown";
                            var tagPath = BuildPath(tagTableName, oldName);
                            existingTagNames.Add(oldName);

                            try
                            {
                                // Look for matching rename entry
                                string? newName = null;

                                if (substringMatch)
                                {
                                    // Substring matching: find if any key in renameMap is a substring of oldName
                                    foreach (var kvp in renameMap)
                                    {
                                        bool matchFound;
                                        if (caseInsensitive)
                                        {
                                            matchFound = oldName.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase) >= 0;
                                        }
                                        else
                                        {
                                            matchFound = oldName.Contains(kvp.Key);
                                        }

                                        if (matchFound)
                                        {
                                            // Replace the substring with the new value
                                            if (caseInsensitive)
                                            {
                                                // Case-insensitive replacement
                                                var comparison = StringComparison.OrdinalIgnoreCase;
                                                var index = oldName.IndexOf(kvp.Key, comparison);
                                                newName = oldName.Substring(0, index) + kvp.Value + oldName.Substring(index + kvp.Key.Length);
                                            }
                                            else
                                            {
                                                // Case-sensitive replacement
                                                newName = oldName.Replace(kvp.Key, kvp.Value);
                                            }
                                            break;
                                        }
                                    }
                                }
                                else if (caseInsensitive)
                                {
                                    // Exact matching with case-insensitivity
                                    foreach (var kvp in renameMap)
                                    {
                                        if (string.Equals(oldName, kvp.Key, StringComparison.OrdinalIgnoreCase))
                                        {
                                            newName = kvp.Value;
                                            break;
                                        }
                                    }
                                }
                                else
                                {
                                    // Exact matching (default)
                                    if (renameMap.ContainsKey(oldName))
                                    {
                                        newName = renameMap[oldName];
                                    }
                                }

                                if (newName == null)
                                {
                                    result.Skipped.Add(new SkippedEntry
                                    {
                                        ItemType = "Tag",
                                        Path = tagPath,
                                        Name = oldName,
                                        Reason = substringMatch ? "No substring match in renameMap" : "No match in renameMap"
                                    });
                                    result.Summary.Skipped++;
                                    continue;
                                }

                                // Ensure the new name is actually different
                                if (string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase))
                                {
                                    result.Skipped.Add(new SkippedEntry
                                    {
                                        ItemType = "Tag",
                                        Path = tagPath,
                                        Name = oldName,
                                        Reason = "New name is identical to old name"
                                    });
                                    result.Summary.Skipped++;
                                    continue;
                                }

                                // Record the change
                                result.Summary.MatchesFound++;

                                if (!dryRun)
                                {
                                    // Check for collision before renaming
                                    if (existingTagNames.Contains(newName))
                                    {
                                        result.Warnings.Add(new WarningEntry
                                        {
                                            Code = "COLLISION",
                                            Message = $"Name '{newName}' already exists in tag tables",
                                            Path = tagPath
                                        });
                                        result.Summary.Warnings++;
                                        continue;
                                    }

                                    try
                                    {
                                        // Use reflection helper to set name property
                                        TiaReflectionHelper.SetProperty(tag, "Name", newName);
                                        existingTagNames.Add(newName);
                                        result.Summary.ChangesApplied++;
                                    }
                                    catch (Exception ex)
                                    {
                                        result.Errors.Add(new ErrorEntry
                                        {
                                            ItemType = "Tag",
                                            Path = tagPath,
                                            Name = oldName,
                                            Error = "Failed to rename tag",
                                            Details = ex.Message
                                        });
                                        result.Summary.Errors++;
                                        continue;
                                    }
                                }

                                result.Changes.Add(new ChangeEntry
                                {
                                    ItemType = "Tag",
                                    Path = tagPath,
                                    Before = oldName,
                                    After = newName
                                });
                            }
                            catch (Exception ex)
                            {
                                result.Errors.Add(new ErrorEntry
                                {
                                    ItemType = "Tag",
                                    Path = tagPath,
                                    Name = oldName,
                                    Error = "Failed to rename tag",
                                    Details = ex.Message
                                });
                                result.Summary.Errors++;
                            }
                        }
                    }

                    // Detect collisions in dry-run mode
                    if (dryRun)
                    {
                        var collisionWarnings = DetectCollisions(result.Changes, existingTagNames);
                        result.Warnings.AddRange(collisionWarnings);
                        result.Summary.Warnings += collisionWarnings.Count;
                    }

                    _logger?.LogInformation("FindAndReplaceTagsAsync completed: {TotalProcessed} processed, {MatchesFound} matches, {ChangesApplied} changes, {Skipped} skipped, {Warnings} warnings, {Errors} errors",
                        result.Summary.TotalProcessed, result.Summary.MatchesFound, result.Summary.ChangesApplied, result.Summary.Skipped, result.Summary.Warnings, result.Summary.Errors);

                    return result;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error during FindAndReplaceTagsAsync operation");
                    result.Summary.Errors = 1;
                    result.Errors.Add(new ErrorEntry
                    {
                        ItemType = "System",
                        Error = "Error during FindAndReplaceTagsAsync operation",
                        Details = ex.Message
                    });
                    return result;
                }
            });
        }

        /// <inheritdoc/>
        public async Task<FindReplaceResult> FindAndReplaceDbMembersAsync(
            Dictionary<string, string> renameMap,
            bool caseInsensitive = false,
            bool dryRun = true,
            string[]? targetBlocks = null)
        {
            var result = new FindReplaceResult();

            return await Task.Run(() =>
            {
                if (_plcSoftware == null || string.IsNullOrEmpty(CurrentPlcName))
                {
                    result.Summary.Errors = 1;
                    result.Errors.Add(new ErrorEntry
                    {
                        ItemType = "System",
                        Error = "No PLC software is currently open"
                    });
                    return result;
                }

                _logger?.LogInformation("FindAndReplaceDbMembersAsync called with {Count} mappings, caseInsensitive={CaseInsensitive}, dryRun={DryRun}, targetBlocks={TargetBlocks}",
                    renameMap.Count, caseInsensitive, dryRun, targetBlocks?.Length ?? 0);

                try
                {
                    // Get BlockGroup
                    var blockGroup = TiaReflectionHelper.GetBlockGroup(_plcSoftware);
                    if (blockGroup == null)
                    {
                        result.Summary.Errors = 1;
                        result.Errors.Add(new ErrorEntry
                        {
                            ItemType = "System",
                            Error = "BlockGroup property not found on PlcSoftware"
                        });
                        return result;
                    }

                    // Get target GlobalDBs
                    var targetBlockList = new List<object>();

                    if (targetBlocks == null || targetBlocks.Length == 0)
                    {
                        // Process all Global DBs
                        var blocks = TiaReflectionHelper.GetBlocks(blockGroup);
                        if (blocks != null)
                        {
                            foreach (var block in blocks)
                            {
                                var typeName = TiaReflectionHelper.GetTypeName(block);
                                if (typeName == "GlobalDB")
                                {
                                    targetBlockList.Add(block);
                                }
                            }
                        }

                        // Also search recursively in groups
                        var groups = TiaReflectionHelper.GetGroups(blockGroup);
                        if (groups != null)
                        {
                            FindGlobalDBsRecursive(groups, targetBlockList);
                        }
                    }
                    else
                    {
                        // Find specific blocks
                        var blocks = TiaReflectionHelper.GetBlocks(blockGroup);
                        if (blocks != null)
                        {
                            foreach (var block in blocks)
                            {
                                var blockName = TiaReflectionHelper.GetName(block);
                                if (targetBlocks.Contains(blockName, StringComparer.OrdinalIgnoreCase))
                                {
                                    targetBlockList.Add(block);
                                }
                            }
                        }

                        var groups = TiaReflectionHelper.GetGroups(blockGroup);
                        if (groups != null)
                        {
                            FindGlobalDBsInGroupsRecursive(groups, targetBlocks, targetBlockList);
                        }
                    }

                    // Process each GlobalDB
                    foreach (var block in targetBlockList)
                    {
                        var blockName = TiaReflectionHelper.GetName(block) ?? "Unknown";
                        _logger?.LogInformation("Processing GlobalDB: {BlockName}", blockName);

                        try
                        {
                            // Get Structure property
                            var structure = TiaReflectionHelper.GetBlockStructure(block);
                            if (structure == null) continue;

                            // Get Composition from Structure
                            var composition = TiaReflectionHelper.GetStructureComposition(structure);
                            if (composition == null) continue;

                            // Process members recursively
                            ProcessDbMembers(
                                composition,
                                renameMap,
                                caseInsensitive,
                                dryRun,
                                blockName,
                                result
                            );
                        }
                        catch (Exception ex)
                        {
                            result.Errors.Add(new ErrorEntry
                            {
                                ItemType = "GlobalDB",
                                Path = blockName,
                                Name = blockName,
                                Error = "Failed to process block",
                                Details = ex.Message
                            });
                            result.Summary.Errors++;
                        }
                    }

                    _logger?.LogInformation("FindAndReplaceDbMembersAsync completed: {TotalProcessed} processed, {MatchesFound} matches, {ChangesApplied} changes, {Skipped} skipped, {Warnings} warnings, {Errors} errors",
                        result.Summary.TotalProcessed, result.Summary.MatchesFound, result.Summary.ChangesApplied, result.Summary.Skipped, result.Summary.Warnings, result.Summary.Errors);

                    return result;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error during FindAndReplaceDbMembersAsync operation");
                    result.Summary.Errors = 1;
                    result.Errors.Add(new ErrorEntry
                    {
                        ItemType = "System",
                        Error = "Error during FindAndReplaceDbMembersAsync operation",
                        Details = ex.Message
                    });
                    return result;
                }
            });
        }

        /// <inheritdoc/>
        public async Task<FindReplaceResult> FindAndReplaceUdtMembersAsync(
            Dictionary<string, string> renameMap,
            bool caseInsensitive = false,
            bool dryRun = true,
            string[]? targetTypes = null)
        {
            var result = new FindReplaceResult();

            return await Task.Run(() =>
            {
                if (_plcSoftware == null || string.IsNullOrEmpty(CurrentPlcName))
                {
                    result.Summary.Errors = 1;
                    result.Errors.Add(new ErrorEntry
                    {
                        ItemType = "System",
                        Error = "No PLC software is currently open"
                    });
                    return result;
                }

                _logger?.LogInformation("FindAndReplaceUdtMembersAsync called with {Count} mappings, caseInsensitive={CaseInsensitive}, dryRun={DryRun}, targetTypes={TargetTypes}",
                    renameMap.Count, caseInsensitive, dryRun, targetTypes?.Length ?? 0);

                try
                {
                    // Get TypeGroup
                    var typeGroup = TiaReflectionHelper.GetTypeGroup(_plcSoftware);
                    if (typeGroup == null)
                    {
                        result.Summary.Errors = 1;
                        result.Errors.Add(new ErrorEntry
                        {
                            ItemType = "System",
                            Error = "TypeGroup property not found on PlcSoftware"
                        });
                        return result;
                    }

                    // Get target UDTs
                    var targetTypeList = new List<object>();

                    if (targetTypes == null || targetTypes.Length == 0)
                    {
                        // Process all UDTs
                        var types = TiaReflectionHelper.GetTypes(typeGroup);
                        if (types != null)
                        {
                            foreach (var type in types)
                            {
                                var typeName = TiaReflectionHelper.GetTypeName(type);
                                if (typeName == "PlcStruct")
                                {
                                    targetTypeList.Add(type);
                                }
                            }
                        }

                        // Also search recursively in groups
                        var groups = TiaReflectionHelper.GetGroups(typeGroup);
                        if (groups != null)
                        {
                            FindUdtsRecursive(groups, targetTypeList);
                        }
                    }
                    else
                    {
                        // Find specific types
                        var types = TiaReflectionHelper.GetTypes(typeGroup);
                        if (types != null)
                        {
                            foreach (var type in types)
                            {
                                var typeName = TiaReflectionHelper.GetName(type);
                                if (targetTypes.Contains(typeName, StringComparer.OrdinalIgnoreCase))
                                {
                                    targetTypeList.Add(type);
                                }
                            }
                        }

                        var groups = TiaReflectionHelper.GetGroups(typeGroup);
                        if (groups != null)
                        {
                            FindUdtsInGroupsRecursive(groups, targetTypes, targetTypeList);
                        }
                    }

                    // Process each UDT
                    foreach (var type in targetTypeList)
                    {
                        var typeName = TiaReflectionHelper.GetName(type) ?? "Unknown";
                        _logger?.LogInformation("Processing UDT: {TypeName}", typeName);

                        try
                        {
                            // Get Structure property
                            var structure = TiaReflectionHelper.GetBlockStructure(type);
                            if (structure == null) continue;

                            // Get Members from Structure
                            var members = TiaReflectionHelper.GetStructureMembers(structure);
                            if (members == null) continue;

                            // Process members recursively
                            ProcessUdtMembers(
                                members,
                                renameMap,
                                caseInsensitive,
                                dryRun,
                                typeName,
                                result
                            );
                        }
                        catch (Exception ex)
                        {
                            result.Errors.Add(new ErrorEntry
                            {
                                ItemType = "UDT",
                                Path = typeName,
                                Name = typeName,
                                Error = "Failed to process type",
                                Details = ex.Message
                            });
                            result.Summary.Errors++;
                        }
                    }

                    _logger?.LogInformation("FindAndReplaceUdtMembersAsync completed: {TotalProcessed} processed, {MatchesFound} matches, {ChangesApplied} changes, {Skipped} skipped, {Warnings} warnings, {Errors} errors",
                        result.Summary.TotalProcessed, result.Summary.MatchesFound, result.Summary.ChangesApplied, result.Summary.Skipped, result.Summary.Warnings, result.Summary.Errors);

                    return result;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error during FindAndReplaceUdtMembersAsync operation");
                    result.Summary.Errors = 1;
                    result.Errors.Add(new ErrorEntry
                    {
                        ItemType = "System",
                        Error = "Error during FindAndReplaceUdtMembersAsync operation",
                        Details = ex.Message
                    });
                    return result;
                }
            });
        }

        /// <inheritdoc/>
        public async Task<CreateTagResult> CreateTagAsync(
            string tagName,
            string dataType,
            string? logicalAddress = null,
            string? tagTableName = null)
        {
            var result = new CreateTagResult();

            return await Task.Run(() =>
            {
                if (_plcSoftware == null || string.IsNullOrEmpty(CurrentPlcName))
                {
                    result.Error = new ErrorEntry
                    {
                        ItemType = "System",
                        Error = "No PLC software is currently open"
                    };
                    return result;
                }

                _logger?.LogInformation("CreateTagAsync called: Tag={TagName}, Type={DataType}, Address={LogicalAddress}, Table={TableName}",
                    tagName, dataType, logicalAddress, tagTableName ?? "(default)");

                try
                {
                    // Get TagTableGroup using reflection helper
                    var tagTableGroup = TiaReflectionHelper.GetTagTableGroup(_plcSoftware);
                    if (tagTableGroup == null)
                    {
                        result.Error = new ErrorEntry
                        {
                            ItemType = "System",
                            Error = "PLC software does not support tag tables"
                        };
                        return result;
                    }

                    // Get TagTables collection
                    var tagTables = TiaReflectionHelper.GetTagTables(tagTableGroup);
                    if (tagTables == null)
                    {
                        result.Error = new ErrorEntry
                        {
                            ItemType = "System",
                            Error = "TagTables collection is null or not enumerable"
                        };
                        return result;
                    }

                    // Find the target tag table
                    object? targetTagTable = null;
                    string? actualTableName = null;

                    if (!string.IsNullOrEmpty(tagTableName))
                    {
                        // Find specific tag table by name
                        foreach (var table in tagTables)
                        {
                            var name = TiaReflectionHelper.GetName(table);
                            if (string.Equals(name, tagTableName, StringComparison.OrdinalIgnoreCase))
                            {
                                targetTagTable = table;
                                actualTableName = name;
                                break;
                            }
                        }

                        if (targetTagTable == null)
                        {
                            result.Error = new ErrorEntry
                            {
                                ItemType = "TagTable",
                                Error = $"Tag table '{tagTableName}' not found"
                            };
                            return result;
                        }
                    }
                    else
                    {
                        // Use the first/default tag table
                        targetTagTable = tagTables.FirstOrDefault();
                        actualTableName = TiaReflectionHelper.GetName(targetTagTable) ?? "Default";
                    }

                    if (targetTagTable == null)
                    {
                        result.Error = new ErrorEntry
                        {
                            ItemType = "System",
                            Error = "No tag table found in PLC software"
                        };
                        return result;
                    }

                    // Get tags collection
                    var tags = TiaReflectionHelper.GetTags(targetTagTable);
                    if (tags == null)
                    {
                        result.Error = new ErrorEntry
                        {
                            ItemType = "TagTable",
                            Error = "Tags collection is null or not accessible"
                        };
                        return result;
                    }

                    // Check if tag already exists
                    foreach (var tag in tags)
                    {
                        var name = TiaReflectionHelper.GetName(tag);
                        if (string.Equals(name, tagName, StringComparison.OrdinalIgnoreCase))
                        {
                            result.Error = new ErrorEntry
                            {
                                ItemType = "Tag",
                                Name = tagName,
                                Path = BuildPath(actualTableName, tagName),
                                Error = "Tag already exists",
                                Details = $"A tag with the name '{tagName}' already exists in tag table '{actualTableName}'"
                            };
                            return result;
                        }
                    }

                    // Create the tag using reflection to invoke Tags.Create() method
                    // Pattern from Create_Modify_Tag.md: defaultTagTable.Tags.Create(newTagName, dataType, logicalAddress)
                    try
                    {
                        // Get the Create method from the Tags collection
                        var tagsCollectionType = tags.GetType();
                        var createMethod = tagsCollectionType.GetMethod("Create", new[] { typeof(string), typeof(string), typeof(string) });

                        if (createMethod == null)
                        {
                            result.Error = new ErrorEntry
                            {
                                ItemType = "System",
                                Error = "Tags.Create() method not found",
                                Details = "The TIA Portal API version may not support direct tag creation"
                            };
                            return result;
                        }

                        // Invoke the Create method
                        var address = logicalAddress ?? string.Empty;
                        var createdTag = createMethod.Invoke(tags, new object[] { tagName, dataType, address });

                        if (createdTag == null)
                        {
                            result.Error = new ErrorEntry
                            {
                                ItemType = "Tag",
                                Name = tagName,
                                Error = "Tag creation failed",
                                Details = "The Tags.Create() method returned null"
                            };
                            return result;
                        }

                        // Verify the tag was created
                        var createdName = TiaReflectionHelper.GetName(createdTag);
                        var createdAddress = TiaReflectionHelper.GetProperty(createdTag, "LogicalAddress")?.ToString();
                        var createdType = TiaReflectionHelper.GetProperty(createdTag, "DataType")?.ToString();

                        result.Success = true;
                        result.TagName = createdName;
                        result.TagTableName = actualTableName;
                        result.DataType = createdType;
                        result.LogicalAddress = createdAddress;

                        _logger?.LogInformation("CreateTagAsync succeeded: {TagName} created in {TableName}", tagName, actualTableName);
                    }
                    catch (Exception ex)
                    {
                        result.Error = new ErrorEntry
                        {
                            ItemType = "Tag",
                            Name = tagName,
                            Error = "Exception during tag creation",
                            Details = $"{ex.GetType().Name}: {ex.Message}"
                        };
                        _logger?.LogError(ex, "CreateTagAsync failed for tag {TagName}", tagName);
                    }

                    return result;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error during CreateTagAsync operation");
                    result.Error = new ErrorEntry
                    {
                        ItemType = "System",
                        Error = "Error during CreateTagAsync operation",
                        Details = ex.Message
                    };
                    return result;
                }
            });
        }

        /// <inheritdoc/>
        public async Task<TagUpdateResult> UpdateTagsFromCsvAsync(
            string csvFilePath,
            bool dryRun = true)
        {
            var result = new TagUpdateResult();

            return await Task.Run(() =>
            {
                if (_plcSoftware == null || string.IsNullOrEmpty(CurrentPlcName))
                {
                    result.Errors.Add(new TagUpdateError
                    {
                        Item = "System",
                        Error = "No PLC software is currently open"
                    });
                    return result;
                }

                _logger?.LogInformation("UpdateTagsFromCsvAsync called: CsvPath={CsvPath}, DryRun={DryRun}",
                    csvFilePath, dryRun);

                try
                {
                    // Parse CSV file
                    List<TagUpdateRow> csvRows;
                    try
                    {
                        csvRows = TagUpdateCsvParser.Parse(csvFilePath);
                        result.Summary.TotalCsvRows = csvRows.Count;
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add(new TagUpdateError
                        {
                            Item = csvFilePath,
                            Error = "Failed to parse CSV file",
                            Details = ex.Message
                        });
                        return result;
                    }

                    // Regex pattern: ^(\d+)([a-zA-Z0-9_]+)$
                    var tagPattern = new Regex(@"^(\d+)([a-zA-Z0-9_]+)$");

                    // Build lookup dictionaries from existing tags
                    var tagsOverPage100 = new Dictionary<string, TagInfo>(StringComparer.OrdinalIgnoreCase); // Key: BaseName
                    var tagsUnderPage100 = new Dictionary<string, TagInfo>(StringComparer.OrdinalIgnoreCase); // Key: FullName
                    var tagsByTable = new Dictionary<string, List<TagInfo>>(StringComparer.OrdinalIgnoreCase); // Key: TableName

                    // Get TagTableGroup
                    var tagTableGroup = TiaReflectionHelper.GetTagTableGroup(_plcSoftware);
                    if (tagTableGroup == null)
                    {
                        result.Errors.Add(new TagUpdateError
                        {
                            Item = "System",
                            Error = "PLC software does not support tag tables"
                        });
                        return result;
                    }

                    // Get TagTables collection
                    var tagTables = TiaReflectionHelper.GetTagTables(tagTableGroup);
                    if (tagTables == null)
                    {
                        result.Errors.Add(new TagUpdateError
                        {
                            Item = "System",
                            Error = "TagTables collection is null or not enumerable"
                        });
                        return result;
                    }

                    // Scan all tag tables to build lookup dictionaries
                    foreach (var tagTable in tagTables)
                    {
                        var tagTableName = TiaReflectionHelper.GetName(tagTable) ?? "Unknown";
                        var tags = TiaReflectionHelper.GetTags(tagTable);
                        if (tags == null) continue;

                        // Initialize list for this table
                        if (!tagsByTable.ContainsKey(tagTableName))
                        {
                            tagsByTable[tagTableName] = new List<TagInfo>();
                        }

                        foreach (var tag in tags)
                        {
                            var tagName = TiaReflectionHelper.GetName(tag);
                            if (string.IsNullOrEmpty(tagName)) continue;

                            var nonNullTagName = tagName!;
                            var match = tagPattern.Match(nonNullTagName);
                            if (match.Success)
                            {
                                int pageNumber = int.Parse(match.Groups[1].Value);
                                string baseName = match.Groups[2].Value;

                                var tagInfo = new TagInfo
                                {
                                    TagObject = tag,
                                    TagTableName = tagTableName,
                                    FullName = nonNullTagName,
                                    BaseName = baseName,
                                    PageNumber = pageNumber
                                };

                                if (pageNumber >= 100)
                                {
                                    // For page >= 100, index by base name
                                    // If duplicate base name exists, keep the first one
                                    if (!tagsOverPage100.ContainsKey(baseName))
                                    {
                                        tagsOverPage100[baseName] = tagInfo;
                                    }
                                }
                                else
                                {
                                    // For page < 100, index by full name
                                    if (!tagsUnderPage100.ContainsKey(nonNullTagName))
                                    {
                                        tagsUnderPage100[nonNullTagName] = tagInfo;
                                    }
                                }

                                // Add to table-based dictionary
                                tagsByTable[tagTableName].Add(tagInfo);

                                result.Summary.ExistingTagsScanned++;
                            }
                        }
                    }

                    _logger?.LogInformation("Scanned {ExistingTags} existing tags ({OverPage} page>=100, {UnderPage} page<100)",
                        result.Summary.ExistingTagsScanned, tagsOverPage100.Count, tagsUnderPage100.Count);

                    // Process CSV rows
                    foreach (var csvRow in csvRows)
                    {
                        var match = tagPattern.Match(csvRow.TagName);
                        if (!match.Success)
                        {
                            result.Warnings.Add(new TagUpdateWarning
                            {
                                Item = csvRow.TagName,
                                Reason = "Tag name does not match pattern ^\\d+[a-zA-Z0-9_]+$ - skipping",
                                LineNumber = csvRow.LineNumber
                            });
                            result.Summary.TagsSkipped++;
                            continue;
                        }

                        int csvPageNumber = int.Parse(match.Groups[1].Value);
                        string csvBaseName = match.Groups[2].Value;

                        if (csvPageNumber >= 100)
                        {
                            // Search by base name
                            if (tagsOverPage100.TryGetValue(csvBaseName, out var tagInfo))
                            {
                                // Update existing tag
                                var oldAddress = TiaReflectionHelper.GetProperty(tagInfo.TagObject, "LogicalAddress")?.ToString();
                                var oldComment = GetCommentText(tagInfo.TagObject);

                                if (!dryRun)
                                {
                                    try
                                    {
                                        // Update name (if changed)
                                        if (!string.Equals(tagInfo.FullName, csvRow.TagName, StringComparison.Ordinal))
                                        {
                                            TiaReflectionHelper.SetProperty(tagInfo.TagObject, "Name", csvRow.TagName);
                                        }

                                        // Update address
                                        if (!string.IsNullOrEmpty(csvRow.Address))
                                        {
                                            TiaReflectionHelper.SetProperty(tagInfo.TagObject, "LogicalAddress", csvRow.Address);
                                        }

                                        // Update comment if provided
                                        if (csvRow.Comment != null)
                                        {
                                            SetTagComment(tagInfo.TagObject, csvRow.Comment);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        result.Errors.Add(new TagUpdateError
                                        {
                                            Item = csvRow.TagName,
                                            Error = "Failed to update tag",
                                            Details = ex.Message,
                                            LineNumber = csvRow.LineNumber
                                        });
                                        result.Summary.TagsSkipped++;
                                        continue;
                                    }
                                }

                                result.Updated.Add(new TagUpdateEntry
                                {
                                    TagTableName = tagInfo.TagTableName,
                                    MatchedBy = "BaseName",
                                    OldName = tagInfo.FullName,
                                    NewName = csvRow.TagName,
                                    OldAddress = oldAddress,
                                    NewAddress = csvRow.Address,
                                    OldComment = oldComment,
                                    NewComment = csvRow.Comment
                                });
                                result.Summary.TagsUpdated++;
                            }
                            else
                            {
                                // Create new tag
                                if (string.IsNullOrEmpty(csvRow.DataType))
                                {
                                    result.Errors.Add(new TagUpdateError
                                    {
                                        Item = csvRow.TagName,
                                        Error = "DataType is required for new tags",
                                        LineNumber = csvRow.LineNumber
                                    });
                                    result.Summary.TagsSkipped++;
                                    continue;
                                }

                                // Find the best table for this specific tag
                                var targetTagTable = FindBestTagTableForPage(tagTables, csvPageNumber, tagsByTable);
                                if (targetTagTable == null)
                                {
                                    result.Errors.Add(new TagUpdateError
                                    {
                                        Item = csvRow.TagName,
                                        Error = "No tag table found for creating new tags"
                                    });
                                    result.Summary.TagsSkipped++;
                                    continue;
                                }
                                var targetTableName = TiaReflectionHelper.GetName(targetTagTable) ?? "Default";

                                if (!dryRun)
                                {
                                    try
                                    {
                                        // Create tag using Tags.Create() method
                                        var tagsCollection = TiaReflectionHelper.GetTags(targetTagTable);
                                        if (tagsCollection == null)
                                        {
                                            result.Errors.Add(new TagUpdateError
                                            {
                                                Item = csvRow.TagName,
                                                Error = "Could not get tags collection",
                                                LineNumber = csvRow.LineNumber
                                            });
                                            result.Summary.TagsSkipped++;
                                            continue;
                                        }

                                        // Invoke Tags.Create(string name, string dataType, string logicalAddress) method
                                        // This matches the pattern used in CreateTagAsync
                                        var createMethod = tagsCollection.GetType().GetMethod("Create", new[] { typeof(string), typeof(string), typeof(string) });
                                        if (createMethod == null)
                                        {
                                            result.Errors.Add(new TagUpdateError
                                            {
                                                Item = csvRow.TagName,
                                                Error = "Tags.Create method not found",
                                                LineNumber = csvRow.LineNumber
                                            });
                                            result.Summary.TagsSkipped++;
                                            continue;
                                        }

                                        var address = string.IsNullOrEmpty(csvRow.Address) ? string.Empty : csvRow.Address;
                                        var createdTag = createMethod.Invoke(tagsCollection, new object[] { csvRow.TagName, csvRow.DataType, address });
                                        if (createdTag == null)
                                        {
                                            result.Errors.Add(new TagUpdateError
                                            {
                                                Item = csvRow.TagName,
                                                Error = "Tag creation failed - Create returned null",
                                                LineNumber = csvRow.LineNumber
                                            });
                                            result.Summary.TagsSkipped++;
                                            continue;
                                        }

                                        // Set comment if provided
                                        if (csvRow.Comment != null)
                                        {
                                            SetTagComment(createdTag, csvRow.Comment);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        result.Errors.Add(new TagUpdateError
                                        {
                                            Item = csvRow.TagName,
                                            Error = "Failed to create tag",
                                            Details = $"{ex.GetType().Name}: {ex.Message}",
                                            LineNumber = csvRow.LineNumber
                                        });
                                        result.Summary.TagsSkipped++;
                                        continue;
                                    }
                                }

                                result.Created.Add(new TagCreateEntry
                                {
                                    TagTableName = targetTableName,
                                    TagName = csvRow.TagName,
                                    DataType = csvRow.DataType,
                                    Address = csvRow.Address,
                                    Comment = csvRow.Comment
                                });
                                result.Summary.TagsCreated++;
                            }
                        }
                        else
                        {
                            // Search by full name (page < 100)
                            if (tagsUnderPage100.TryGetValue(csvRow.TagName, out var tagInfo))
                            {
                                // Update address and comment only (name stays the same)
                                var oldAddress = TiaReflectionHelper.GetProperty(tagInfo.TagObject, "LogicalAddress")?.ToString();
                                var oldComment = GetCommentText(tagInfo.TagObject);

                                if (!dryRun)
                                {
                                    try
                                    {
                                        // Update address
                                        if (!string.IsNullOrEmpty(csvRow.Address))
                                        {
                                            TiaReflectionHelper.SetProperty(tagInfo.TagObject, "LogicalAddress", csvRow.Address);
                                        }

                                        // Update comment if provided
                                        if (csvRow.Comment != null)
                                        {
                                            SetTagComment(tagInfo.TagObject, csvRow.Comment);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        result.Errors.Add(new TagUpdateError
                                        {
                                            Item = csvRow.TagName,
                                            Error = "Failed to update tag",
                                            Details = ex.Message,
                                            LineNumber = csvRow.LineNumber
                                        });
                                        result.Summary.TagsSkipped++;
                                        continue;
                                    }
                                }

                                result.Updated.Add(new TagUpdateEntry
                                {
                                    TagTableName = tagInfo.TagTableName,
                                    MatchedBy = "FullName",
                                    OldName = tagInfo.FullName,
                                    NewName = csvRow.TagName, // Same as old name
                                    OldAddress = oldAddress,
                                    NewAddress = csvRow.Address,
                                    OldComment = oldComment,
                                    NewComment = csvRow.Comment ?? oldComment
                                });
                                result.Summary.TagsUpdated++;
                            }
                            else
                            {
                                // Create new tag
                                if (string.IsNullOrEmpty(csvRow.DataType))
                                {
                                    result.Errors.Add(new TagUpdateError
                                    {
                                        Item = csvRow.TagName,
                                        Error = "DataType is required for new tags",
                                        LineNumber = csvRow.LineNumber
                                    });
                                    result.Summary.TagsSkipped++;
                                    continue;
                                }

                                // Find the best table for this specific tag
                                var targetTagTable = FindBestTagTableForPage(tagTables, csvPageNumber, tagsByTable);
                                if (targetTagTable == null)
                                {
                                    result.Errors.Add(new TagUpdateError
                                    {
                                        Item = csvRow.TagName,
                                        Error = "No tag table found for creating new tags"
                                    });
                                    result.Summary.TagsSkipped++;
                                    continue;
                                }
                                var targetTableName = TiaReflectionHelper.GetName(targetTagTable) ?? "Default";

                                if (!dryRun)
                                {
                                    try
                                    {
                                        // Create tag using Tags.Create() method
                                        var tagsCollection = TiaReflectionHelper.GetTags(targetTagTable);
                                        if (tagsCollection == null)
                                        {
                                            result.Errors.Add(new TagUpdateError
                                            {
                                                Item = csvRow.TagName,
                                                Error = "Could not get tags collection",
                                                LineNumber = csvRow.LineNumber
                                            });
                                            result.Summary.TagsSkipped++;
                                            continue;
                                        }

                                        // Invoke Tags.Create(string name, string dataType, string logicalAddress) method
                                        // This matches the pattern used in CreateTagAsync
                                        var createMethod = tagsCollection.GetType().GetMethod("Create", new[] { typeof(string), typeof(string), typeof(string) });
                                        if (createMethod == null)
                                        {
                                            result.Errors.Add(new TagUpdateError
                                            {
                                                Item = csvRow.TagName,
                                                Error = "Tags.Create method not found",
                                                LineNumber = csvRow.LineNumber
                                            });
                                            result.Summary.TagsSkipped++;
                                            continue;
                                        }

                                        var address = string.IsNullOrEmpty(csvRow.Address) ? string.Empty : csvRow.Address;
                                        var createdTag = createMethod.Invoke(tagsCollection, new object[] { csvRow.TagName, csvRow.DataType, address });
                                        if (createdTag == null)
                                        {
                                            result.Errors.Add(new TagUpdateError
                                            {
                                                Item = csvRow.TagName,
                                                Error = "Tag creation failed - Create returned null",
                                                LineNumber = csvRow.LineNumber
                                            });
                                            result.Summary.TagsSkipped++;
                                            continue;
                                        }

                                        // Set comment if provided
                                        if (csvRow.Comment != null)
                                        {
                                            bool commentSuccess = SetTagComment(createdTag, csvRow.Comment);
                                            if (!commentSuccess)
                                            {
                                                result.Warnings.Add(new TagUpdateWarning
                                                {
                                                    Item = csvRow.TagName,
                                                    Reason = $"Tag was created/updated, but failed to apply comment for {csvRow.TagName}.",
                                                    LineNumber = csvRow.LineNumber
                                                });
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        result.Errors.Add(new TagUpdateError
                                        {
                                            Item = csvRow.TagName,
                                            Error = "Failed to create tag",
                                            Details = $"{ex.GetType().Name}: {ex.Message}",
                                            LineNumber = csvRow.LineNumber
                                        });
                                        result.Summary.TagsSkipped++;
                                        continue;
                                    }
                                }

                                result.Created.Add(new TagCreateEntry
                                {
                                    TagTableName = targetTableName,
                                    TagName = csvRow.TagName,
                                    DataType = csvRow.DataType,
                                    Address = csvRow.Address,
                                    Comment = csvRow.Comment
                                });
                                result.Summary.TagsCreated++;
                            }
                        }
                    }

                    _logger?.LogInformation("UpdateTagsFromCsvAsync completed: {TotalCsvRows} CSV rows, {ExistingTags} scanned, {Updated} updated, {Created} created, {Skipped} skipped, {Warnings} warnings, {Errors} errors",
                        result.Summary.TotalCsvRows, result.Summary.ExistingTagsScanned, result.Summary.TagsUpdated, result.Summary.TagsCreated, result.Summary.TagsSkipped, result.Warnings.Count, result.Errors.Count);

                    return result;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error during UpdateTagsFromCsvAsync operation");
                    result.Errors.Add(new TagUpdateError
                    {
                        Item = "System",
                        Error = "Error during UpdateTagsFromCsvAsync operation",
                        Details = ex.Message
                    });
                    return result;
                }
            });
        }

        /// <summary>
        /// Finds the most appropriate tag table for a given page number by checking existing tags.
        /// </summary>
        /// <param name="tagTables">All available tag tables.</param>
        /// <param name="pageNumber">The page number for the new tag.</param>
        /// <param name="tagsByTable">Dictionary of tags organized by table name.</param>
        /// <returns>The best matching tag table, or the first table if no match found.</returns>
        private object? FindBestTagTableForPage(
            IEnumerable<object> tagTables,
            int pageNumber,
            Dictionary<string, List<TagInfo>> tagsByTable)
        {
            // Find tables that already have tags from this page
            var tablesWithSamePage = tagsByTable
                .Where(kvp => kvp.Value.Any(t => t.PageNumber == pageNumber))
                .Select(kvp => kvp.Key)
                .ToList();

            if (tablesWithSamePage.Count == 1)
            {
                // Found exactly one table with this page - use it
                var tableName = tablesWithSamePage.First();
                return tagTables.FirstOrDefault(t =>
                    TiaReflectionHelper.GetName(t)?.Equals(tableName, StringComparison.OrdinalIgnoreCase) == true);
            }
            else if (tablesWithSamePage.Count > 1)
            {
                // Multiple tables have this page - use the one with most tags from this page
                var bestTableName = tagsByTable
                    .Where(kvp => tablesWithSamePage.Contains(kvp.Key))
                    .OrderByDescending(kvp => kvp.Value.Count(t => t.PageNumber == pageNumber))
                    .First().Key;

                return tagTables.FirstOrDefault(t =>
                    TiaReflectionHelper.GetName(t)?.Equals(bestTableName, StringComparison.OrdinalIgnoreCase) == true);
            }

            // No table has this page - use default (first) table
            return tagTables.FirstOrDefault();
        }

        #region Private Helper Methods

        /// <summary>
        /// Gets the comment text for a PLC tag.
        /// Comments in TIA Portal are multilingual text collections, not simple strings.
        /// </summary>
        /// <param name="tag">The tag object.</param>
        /// <param name="culture">The culture name (e.g., "en-US"). Defaults to "en-US".</param>
        /// <returns>The comment text if found, null otherwise.</returns>
        private string? GetCommentText(object tag, string culture = "en-US")
        {
            try
            {
                var commentProperty = tag.GetType().GetProperty("Comment");
                if (commentProperty == null) return null;

                var comment = commentProperty.GetValue(tag);
                if (comment == null) return null;

                var itemsProperty = comment.GetType().GetProperty("Items");
                if (itemsProperty == null) return null;

                var items = itemsProperty.GetValue(comment);
                var itemsEnumerable = items as System.Collections.IEnumerable;
                if (itemsEnumerable == null) return null;

                foreach (var item in itemsEnumerable)
                {
                    var cultureProp = item?.GetType().GetProperty("Culture");
                    if (cultureProp == null) continue;

                    var cultureObj = cultureProp.GetValue(item);
                    if (cultureObj == null) continue;

                    var cultureNameProp = cultureObj.GetType().GetProperty("Name");
                    if (cultureNameProp == null) continue;

                    var cultureName = cultureNameProp.GetValue(cultureObj)?.ToString();
                    if (string.Equals(cultureName, culture, StringComparison.OrdinalIgnoreCase))
                    {
                        var textProp = item?.GetType().GetProperty("Text");
                        return textProp?.GetValue(item)?.ToString();
                    }
                }

                return null; // No comment found for this culture
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Sets the comment on a tag using multilingual text API.
        /// Comments in TIA Portal are multilingual text collections, not simple strings.
        /// </summary>
        /// <param name="tag">The tag object.</param>
        /// <param name="commentText">The comment text to set.</param>
        /// <param name="culture">The culture name (e.g., "en-US"). Defaults to "en-US".</param>
        private bool SetTagComment(object tag, string commentText, string culture = "en-US")
        {
            try
            {
            var commentProperty = tag.GetType().GetProperty("Comment");
            if (commentProperty == null) return false;

            var comment = commentProperty.GetValue(tag);
            if (comment == null) return false;

            var itemsProperty = comment.GetType().GetProperty("Items");
            if (itemsProperty == null) return false;

            var items = itemsProperty.GetValue(comment);
            var itemsEnumerable = items as System.Collections.IEnumerable;
            if (itemsEnumerable == null) return false;

            // Iterate through the pre-existing project language items
            foreach (var item in itemsEnumerable)
            {
                // 1. Get the 'Language' property from MultilingualTextItem
                var languageProp = item?.GetType().GetProperty("Language");
                if (languageProp == null) continue;

                var languageObj = languageProp.GetValue(item);
                if (languageObj == null) continue;

                // 2. Get the 'Culture' property from the Language object
                var cultureProp = languageObj.GetType().GetProperty("Culture");
                if (cultureProp == null) continue;

                var cultureObj = cultureProp.GetValue(languageObj);
                if (cultureObj == null) continue;

                // 3. Get the 'Name' property from CultureInfo (e.g., "en-US")
                var nameProp = cultureObj.GetType().GetProperty("Name");
                var currentCultureName = nameProp?.GetValue(cultureObj)?.ToString();

                // Check if this is the language we want to update
                if (string.Equals(currentCultureName, culture, StringComparison.OrdinalIgnoreCase))
                {
                    var textProp = item?.GetType().GetProperty("Text");
                    textProp?.SetValue(item, commentText);
                
                    _logger?.LogDebug("Successfully set comment for culture: {Culture}", culture);
                    return true; // Success!
                }
            }

            // If the loop finishes without returning, the target culture is NOT enabled in the TIA Portal project.
            _logger?.LogWarning("Culture '{Culture}' is not enabled in the TIA Portal Project Languages. Cannot set comment.", culture);
            return false;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Exception while setting comment for tag. Culture: {Culture}", culture);
                return false;
            }
        }

        /// <summary>
        /// Gets a Language object from the project for the specified culture.
        /// </summary>
        /// <param name="culture">The culture name (e.g., "en-US").</param>
        /// <returns>The Language object if found, null otherwise.</returns>
        private object? GetLanguageFromProject(string culture)
        {
            if (_portalService.Project == null) return null;

            try
            {
                var project = _portalService.Project;
                var languagesProperty = project?.GetType().GetProperty("Languages");
                if (languagesProperty == null) return null;

                var languages = languagesProperty.GetValue(project);
                if (languages == null) return null;

                // Try Find(string cultureName) method first
                var findMethod = languages.GetType().GetMethod("Find", new[] { typeof(string) });
                if (findMethod != null)
                {
                    return findMethod.Invoke(languages, new object[] { culture });
                }

                // Fallback: iterate through languages to find matching culture
                var langsEnum = languages as System.Collections.IEnumerable;
                if (langsEnum != null)
                {
                    foreach (var lang in langsEnum)
                    {
                        var nameProp = lang?.GetType().GetProperty("Name");
                        if (nameProp != null)
                        {
                            var langName = nameProp.GetValue(lang)?.ToString();
                            if (string.Equals(langName, culture, StringComparison.OrdinalIgnoreCase))
                            {
                                return lang;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Failed to get Language from project for culture: {Culture}", culture);
            }

            return null;
        }

        /// <summary>
        /// Internal class to store tag information during update operations.
        /// </summary>
        private class TagInfo
        {
            public object TagObject { get; set; } = null!;
            public string TagTableName { get; set; } = string.Empty;
            public string FullName { get; set; } = string.Empty;
            public string BaseName { get; set; } = string.Empty;
            public int PageNumber { get; set; }
        }

        /// <summary>
        /// Recursively finds GlobalDBs in groups.
        /// </summary>
        private void FindGlobalDBsRecursive(IEnumerable<object> groups, List<object> targetBlocks)
        {
            foreach (var group in groups)
            {
                var blocks = TiaReflectionHelper.GetBlocks(group);
                if (blocks != null)
                {
                    foreach (var block in blocks)
                    {
                        var typeName = TiaReflectionHelper.GetTypeName(block);
                        if (typeName == "GlobalDB")
                        {
                            targetBlocks.Add(block);
                        }
                    }
                }

                var nestedGroups = TiaReflectionHelper.GetGroups(group);
                if (nestedGroups != null)
                {
                    FindGlobalDBsRecursive(nestedGroups, targetBlocks);
                }
            }
        }

        /// <summary>
        /// Recursively finds GlobalDBs in groups by name.
        /// </summary>
        private void FindGlobalDBsInGroupsRecursive(IEnumerable<object> groups, string[] targetBlocks, List<object> targetBlockList)
        {
            foreach (var group in groups)
            {
                var blocks = TiaReflectionHelper.GetBlocks(group);
                if (blocks != null)
                {
                    foreach (var block in blocks)
                    {
                        var blockName = TiaReflectionHelper.GetName(block);
                        if (targetBlocks.Contains(blockName, StringComparer.OrdinalIgnoreCase))
                        {
                            targetBlockList.Add(block);
                        }
                    }
                }

                var nestedGroups = TiaReflectionHelper.GetGroups(group);
                if (nestedGroups != null)
                {
                    FindGlobalDBsInGroupsRecursive(nestedGroups, targetBlocks, targetBlockList);
                }
            }
        }

        /// <summary>
        /// Recursively finds UDTs in groups.
        /// </summary>
        private void FindUdtsRecursive(IEnumerable<object> groups, List<object> targetTypes)
        {
            foreach (var group in groups)
            {
                var types = TiaReflectionHelper.GetTypes(group);
                if (types != null)
                {
                    foreach (var type in types)
                    {
                        var typeName = TiaReflectionHelper.GetTypeName(type);
                        if (typeName == "PlcStruct")
                        {
                            targetTypes.Add(type);
                        }
                    }
                }

                var nestedGroups = TiaReflectionHelper.GetGroups(group);
                if (nestedGroups != null)
                {
                    FindUdtsRecursive(nestedGroups, targetTypes);
                }
            }
        }

        /// <summary>
        /// Recursively finds UDTs in groups by name.
        /// </summary>
        private void FindUdtsInGroupsRecursive(IEnumerable<object> groups, string[] targetTypes, List<object> targetTypeList)
        {
            foreach (var group in groups)
            {
                var types = TiaReflectionHelper.GetTypes(group);
                if (types != null)
                {
                    foreach (var type in types)
                    {
                        var typeName = TiaReflectionHelper.GetName(type);
                        if (targetTypes.Contains(typeName, StringComparer.OrdinalIgnoreCase))
                        {
                            targetTypeList.Add(type);
                        }
                    }
                }

                var nestedGroups = TiaReflectionHelper.GetGroups(group);
                if (nestedGroups != null)
                {
                    FindUdtsInGroupsRecursive(nestedGroups, targetTypes, targetTypeList);
                }
            }
        }

        /// <summary>
        /// Recursively processes DB members (handles nested structs).
        /// </summary>
        private void ProcessDbMembers(
            object composition,
            Dictionary<string, string> renameMap,
            bool caseInsensitive,
            bool dryRun,
            string parentPath,
            FindReplaceResult result)
        {
            var members = TiaReflectionHelper.GetCompositionMembers(composition);
            if (members == null) return;

            foreach (var member in members)
            {
                ProcessDbMember(member, renameMap, caseInsensitive, dryRun, parentPath, result);
            }
        }

        /// <summary>
        /// Processes a single DB member.
        /// </summary>
        private void ProcessDbMember(
            object member,
            Dictionary<string, string> renameMap,
            bool caseInsensitive,
            bool dryRun,
            string parentPath,
            FindReplaceResult result)
        {
            result.Summary.TotalProcessed++;

            var nameProperty = member?.GetType().GetProperty("Name");
            var memberName = nameProperty?.GetValue(member)?.ToString() ?? "Unknown";
            var memberPath = BuildPath(parentPath, memberName);

            try
            {
                // Look for matching rename entry
                string oldName = memberName;
                string? newName = null;

                if (caseInsensitive)
                {
                    foreach (var kvp in renameMap)
                    {
                        if (string.Equals(oldName, kvp.Key, StringComparison.OrdinalIgnoreCase))
                        {
                            newName = kvp.Value;
                            break;
                        }
                    }
                }
                else
                {
                    if (renameMap.ContainsKey(oldName))
                    {
                        newName = renameMap[oldName];
                    }
                }

                if (newName != null)
                {
                    result.Summary.MatchesFound++;

                    if (!dryRun)
                    {
                        // Set the new name using reflection
                        nameProperty?.SetValue(member, newName);
                        result.Summary.ChangesApplied++;
                    }

                    result.Changes.Add(new ChangeEntry
                    {
                        ItemType = "DB Member",
                        Path = memberPath,
                        Before = oldName,
                        After = newName
                    });
                }
                else
                {
                    result.Skipped.Add(new SkippedEntry
                    {
                        ItemType = "DB Member",
                        Path = memberPath,
                        Name = memberName,
                        Reason = "No match in renameMap"
                    });
                }

                // Recursively process nested members (UDT instances, structs, arrays)
                if (TiaReflectionHelper.HasNestedComposition(member))
                {
                    var nestedComposition = TiaReflectionHelper.GetStructureComposition(member);
                    if (nestedComposition != null)
                    {
                        ProcessDbMembers(
                            nestedComposition,
                            renameMap,
                            caseInsensitive,
                            dryRun,
                            memberPath,
                            result
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add(new ErrorEntry
                {
                    ItemType = "DB Member",
                    Path = memberPath,
                    Name = memberName,
                    Error = "Failed to process member",
                    Details = ex.Message
                });
                result.Summary.Errors++;
            }
        }

        /// <summary>
        /// Recursively processes UDT members (handles nested structs).
        /// </summary>
        private void ProcessUdtMembers(
            object members,
            Dictionary<string, string> renameMap,
            bool caseInsensitive,
            bool dryRun,
            string parentPath,
            FindReplaceResult result)
        {
            var membersCollection = members as System.Collections.IEnumerable;
            if (membersCollection == null) return;

            foreach (var member in membersCollection)
            {
                ProcessUdtMember(member, renameMap, caseInsensitive, dryRun, parentPath, result);
            }
        }

        /// <summary>
        /// Processes a single UDT member.
        /// </summary>
        private void ProcessUdtMember(
            object member,
            Dictionary<string, string> renameMap,
            bool caseInsensitive,
            bool dryRun,
            string parentPath,
            FindReplaceResult result)
        {
            result.Summary.TotalProcessed++;

            var nameProperty = member?.GetType().GetProperty("Name");
            var memberName = nameProperty?.GetValue(member)?.ToString() ?? "Unknown";
            var memberPath = BuildPath(parentPath, memberName);

            try
            {
                // Look for matching rename entry
                string oldName = memberName;
                string? newName = null;

                if (caseInsensitive)
                {
                    foreach (var kvp in renameMap)
                    {
                        if (string.Equals(oldName, kvp.Key, StringComparison.OrdinalIgnoreCase))
                        {
                            newName = kvp.Value;
                            break;
                        }
                    }
                }
                else
                {
                    if (renameMap.ContainsKey(oldName))
                    {
                        newName = renameMap[oldName];
                    }
                }

                if (newName != null)
                {
                    result.Summary.MatchesFound++;

                    if (!dryRun)
                    {
                        // Set the new name using reflection
                        nameProperty?.SetValue(member, newName);
                        result.Summary.ChangesApplied++;
                    }

                    result.Changes.Add(new ChangeEntry
                    {
                        ItemType = "UDT Member",
                        Path = memberPath,
                        Before = oldName,
                        After = newName
                    });
                }
                else
                {
                    result.Skipped.Add(new SkippedEntry
                    {
                        ItemType = "UDT Member",
                        Path = memberPath,
                        Name = memberName,
                        Reason = "No match in renameMap"
                    });
                }

                // Recursively process nested members (UDT instances, structs, arrays)
                if (TiaReflectionHelper.HasNestedComposition(member))
                {
                    var nestedComposition = TiaReflectionHelper.GetStructureComposition(member);
                    if (nestedComposition != null)
                    {
                        var members = TiaReflectionHelper.GetCompositionMembers(nestedComposition);
                        if (members != null)
                        {
                            ProcessUdtMembers(
                                members,
                                renameMap,
                                caseInsensitive,
                                dryRun,
                                memberPath,
                                result
                            );
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add(new ErrorEntry
                {
                    ItemType = "UDT Member",
                    Path = memberPath,
                    Name = memberName,
                    Error = "Failed to process member",
                    Details = ex.Message
                });
                result.Summary.Errors++;
            }
        }

        /// <summary>
        /// Builds a hierarchical path string for reporting.
        /// </summary>
        private string BuildPath(params string?[] segments)
        {
            var validSegments = segments.Where(s => !string.IsNullOrEmpty(s)).ToArray();
            return string.Join(".", validSegments);
        }

        /// <summary>
        /// Detects potential name collisions in the changes list.
        /// </summary>
        private List<WarningEntry> DetectCollisions(List<ChangeEntry> changes, HashSet<string> existingNames)
        {
            var warnings = new List<WarningEntry>();
            var newNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var change in changes)
            {
                if (change.After != null)
                {
                    if (existingNames.Contains(change.After) || newNames.Contains(change.After))
                    {
                        warnings.Add(new WarningEntry
                        {
                            Code = "COLLISION",
                            Message = $"Name '{change.After}' already exists or will be created multiple times",
                            Path = change.Path
                        });
                    }
                    newNames.Add(change.After);
                }
            }

            return warnings;
        }

        #endregion

        #region SIMATIC SD Export and Type Export (V20+)

        /// <inheritdoc/>
        public async Task<bool> ExportBlockAsDocumentsAsync(string blockName, string exportPath)
        {
            return await Task.Run(() =>
            {
                if (_plcSoftware == null || string.IsNullOrEmpty(CurrentPlcName))
                {
                    _logger?.LogWarning("No PLC software is currently open");
                    return false;
                }

                _logger?.LogInformation("Exporting block as SIMATIC SD documents: {BlockName} to {ExportPath}", blockName, exportPath);

                try
                {
                    // Version check - SIMATIC SD export requires V20+
                    var versionString = _portalService.Version;
                    var version = TiaReflectionHelper.ParseTiaVersion(versionString);
                    if (version == null || version.Major < 20)
                    {
                        _logger?.LogError("SIMATIC SD export requires TIA Portal V20 or newer. Current version: {Version}", versionString ?? "Unknown");
                        throw new InvalidOperationException($"SIMATIC SD export requires TIA Portal V20 or newer. Current version: {versionString ?? "Unknown"}");
                    }

                    // Get PlcSoftware type and BlockGroup
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

                    // Find the block by name
                    foreach (var block in blocksCollection)
                    {
                        try
                        {
                            var nameProperty = block.GetType().GetProperty("Name");
                            string name = nameProperty?.GetValue(block)?.ToString() ?? "";

                            if (name.Equals(blockName, StringComparison.OrdinalIgnoreCase))
                            {
                                // Ensure directory exists
                                if (!Directory.Exists(exportPath))
                                {
                                    Directory.CreateDirectory(exportPath);
                                }

                                // Call ExportAsDocuments method
                                var exportAsDocumentsMethod = block.GetType().GetMethod("ExportAsDocuments", new[] { typeof(DirectoryInfo), typeof(string) });
                                if (exportAsDocumentsMethod == null)
                                {
                                    _logger?.LogError("ExportAsDocuments method not found on block type");
                                    return false;
                                }

                                var result = exportAsDocumentsMethod.Invoke(block, new object[] { new DirectoryInfo(exportPath), blockName });

                                // Check result state
                                var resultType = result?.GetType();
                                if (resultType != null)
                                {
                                    var stateProperty = resultType.GetProperty("State");
                                    if (stateProperty != null)
                                    {
                                        var state = stateProperty.GetValue(result);
                                        if (state != null && state.ToString() == "Success")
                                        {
                                            _logger?.LogInformation("Successfully exported block as SIMATIC SD documents: {BlockName}", blockName);
                                            return true;
                                        }
                                    }
                                }

                                _logger?.LogError("ExportAsDocuments returned non-success state");
                                return false;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogWarning(ex, "Failed to process block: {BlockName}", blockName);
                        }
                    }

                    _logger?.LogWarning("Block not found: {BlockName}", blockName);
                    return false;
                }
                catch (InvalidOperationException ex)
                {
                    _logger?.LogError(ex, "Version check failed for ExportBlockAsDocumentsAsync");
                    throw;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to export block as documents: {BlockName}", blockName);
                    return false;
                }
            });
        }

        /// <inheritdoc/>
        public async Task<ExportResult> ExportBlocksAsDocumentsAsync(string? filterRegex, string exportPath)
        {
            var result = new ExportResult();

            return await Task.Run(async () =>
            {
                if (_plcSoftware == null || string.IsNullOrEmpty(CurrentPlcName))
                {
                    _logger?.LogWarning("No PLC software is currently open");
                    result.Errors.Add("No PLC software is currently open");
                    return result;
                }

                _logger?.LogInformation("Exporting blocks as SIMATIC SD documents: filter='{Filter}', path='{ExportPath}'", filterRegex ?? "all", exportPath);

                try
                {
                    // Version check
                    var versionString = _portalService.Version;
                    var version = TiaReflectionHelper.ParseTiaVersion(versionString);
                    if (version == null || version.Major < 20)
                    {
                        var error = $"SIMATIC SD export requires TIA Portal V20 or newer. Current version: {versionString ?? "Unknown"}";
                        _logger?.LogError(error);
                        result.Errors.Add(error);
                        return result;
                    }

                    // Get blocks
                    var blocks = await GetBlocksAsync(filterRegex);
                    result.TotalCount = blocks.Count;

                    // Ensure directory exists
                    if (!Directory.Exists(exportPath))
                    {
                        Directory.CreateDirectory(exportPath);
                    }

                    // Filter out OB and FOB blocks (they can't be exported as SIMATIC SD documents)
                    var blocksToSkip = blocks.Where(b => b.Type.Equals("OB", StringComparison.OrdinalIgnoreCase) ||
                                                           b.Type.Equals("FOB", StringComparison.OrdinalIgnoreCase)).ToList();

                    var blocksToExport = blocks.Where(b => !b.Type.Equals("OB", StringComparison.OrdinalIgnoreCase) &&
                                                          !b.Type.Equals("FOB", StringComparison.OrdinalIgnoreCase)).ToList();

                    // Log skipped blocks
                    foreach (var skippedBlock in blocksToSkip)
                    {
                        _logger?.LogInformation("Skipped {BlockType} block: {BlockName} (SIMATIC SD export not supported)", skippedBlock.Type, skippedBlock.Name);
                        result.FailedCount++;
                        result.FailedItems.Add(skippedBlock.Name);
                        result.Errors.Add($"Skipped {skippedBlock.Type} block '{skippedBlock.Name}': SIMATIC SD export not supported for Organization Blocks");
                    }

                    // Export each block
                    foreach (var block in blocksToExport)
                    {
                        try
                        {
                            var success = await ExportBlockAsDocumentsAsync(block.Name, exportPath);
                            if (success)
                            {
                                result.SuccessCount++;
                                result.ExportedItems.Add(block.Name);
                                _logger?.LogDebug("Successfully exported: {BlockName}", block.Name);
                            }
                            else
                            {
                                result.FailedCount++;
                                result.FailedItems.Add(block.Name);
                                result.Errors.Add($"Failed to export: {block.Name}");
                            }
                        }
                        catch (Exception ex)
                        {
                            result.FailedCount++;
                            result.FailedItems.Add(block.Name);
                            result.Errors.Add($"Error exporting {block.Name}: {ex.Message}");
                        }
                    }

                    _logger?.LogInformation("Export blocks as documents complete: {SuccessCount} succeeded, {FailedCount} failed", result.SuccessCount, result.FailedCount);
                    return result;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to export blocks as documents");
                    result.Errors.Add($"Error: {ex.Message}");
                    return result;
                }
            });
        }

        /// <inheritdoc/>
        public async Task<bool> ExportTypeAsync(string typeName, string exportPath)
        {
            return await Task.Run(() =>
            {
                if (_plcSoftware == null || string.IsNullOrEmpty(CurrentPlcName))
                {
                    _logger?.LogWarning("No PLC software is currently open");
                    return false;
                }

                _logger?.LogInformation("Exporting type: {TypeName} to {ExportPath}", typeName, exportPath);

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

                    // Get TypeList property
                    var typeListProperty = plcSoftwareType.GetProperty("TypeList");
                    if (typeListProperty == null)
                    {
                        _logger?.LogError("Could not find TypeList property");
                        return false;
                    }

                    var typeList = typeListProperty.GetValue(_plcSoftware);
                    if (typeList == null)
                    {
                        _logger?.LogError("TypeList is null");
                        return false;
                    }

                    // Get Types collection
                    var typesProperty = typeList.GetType().GetProperty("Types");
                    if (typesProperty == null)
                    {
                        _logger?.LogError("Could not find Types property");
                        return false;
                    }

                    var typesCollection = typesProperty.GetValue(typeList) as System.Collections.IEnumerable;
                    if (typesCollection == null)
                    {
                        _logger?.LogError("Types collection is null");
                        return false;
                    }

                    // Find ExportOptions type
                    Type? exportOptionsType = null;
                    object? none = null;

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
                        var noneField = exportOptionsType.GetField("None");
                        if (noneField != null)
                        {
                            none = noneField.GetValue(null);
                        }
                    }

                    // Find the type by name and export it
                    foreach (var type in typesCollection)
                    {
                        try
                        {
                            var nameProperty = type.GetType().GetProperty("Name");
                            string name = nameProperty?.GetValue(type)?.ToString() ?? "";

                            if (name.Equals(typeName, StringComparison.OrdinalIgnoreCase))
                            {
                                // Check consistency
                                var isConsistentProperty = type.GetType().GetProperty("IsConsistent");
                                if (isConsistentProperty != null)
                                {
                                    var isConsistent = isConsistentProperty.GetValue(type);
                                    if (isConsistent is bool consistent && !consistent)
                                    {
                                        _logger?.LogError("Type '{TypeName}' is not consistent and cannot be exported", typeName);
                                        return false;
                                    }
                                }

                                // Ensure directory exists
                                var directory = Path.GetDirectoryName(exportPath);
                                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                                {
                                    Directory.CreateDirectory(directory);
                                }

                                bool exported = false;

                                // Strategy 1: Try Export with ExportOptions.None
                                if (!exported && exportOptionsType != null && none != null)
                                {
                                    try
                                    {
                                        var exportMethod = type.GetType().GetMethod("Export", new[] { typeof(FileInfo), exportOptionsType });
                                        if (exportMethod != null)
                                        {
                                            exportMethod.Invoke(type, new object[] { new FileInfo(exportPath), none });
                                            exported = true;
                                            _logger?.LogInformation("Successfully exported type with None: {TypeName} to {ExportPath}", typeName, exportPath);
                                            return true;
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger?.LogDebug(ex, "Export with None failed for type: {TypeName}", typeName);
                                    }
                                }

                                // Strategy 2: Try Export with just FileInfo (no options)
                                if (!exported)
                                {
                                    try
                                    {
                                        var simpleExportMethod = type.GetType().GetMethod("Export", new[] { typeof(FileInfo) });
                                        if (simpleExportMethod != null)
                                        {
                                            simpleExportMethod.Invoke(type, new object[] { new FileInfo(exportPath) });
                                            exported = true;
                                            _logger?.LogInformation("Successfully exported type without options: {TypeName} to {ExportPath}", typeName, exportPath);
                                            return true;
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger?.LogDebug(ex, "Export without options failed for type: {TypeName}", typeName);
                                    }
                                }

                                _logger?.LogError("Failed to export type: {TypeName}", typeName);
                                return false;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogWarning(ex, "Failed to process type: {TypeName}", typeName);
                        }
                    }

                    _logger?.LogWarning("Type not found: {TypeName}", typeName);
                    return false;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to export type: {TypeName}", typeName);
                    return false;
                }
            });
        }

        /// <inheritdoc/>
        public async Task<ExportResult> ExportTypesAsync(string? filterRegex, string exportPath)
        {
            var result = new ExportResult();

            return await Task.Run(() =>
            {
                if (_plcSoftware == null || string.IsNullOrEmpty(CurrentPlcName))
                {
                    _logger?.LogWarning("No PLC software is currently open");
                    result.Errors.Add("No PLC software is currently open");
                    return result;
                }

                _logger?.LogInformation("Exporting types: filter='{Filter}', path='{ExportPath}'", filterRegex ?? "all", exportPath);

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
                        result.Errors.Add("Could not find PlcSoftware type");
                        return result;
                    }

                    // Get TypeList property
                    var typeListProperty = plcSoftwareType.GetProperty("TypeList");
                    if (typeListProperty == null)
                    {
                        _logger?.LogError("Could not find TypeList property");
                        result.Errors.Add("Could not find TypeList property");
                        return result;
                    }

                    var typeList = typeListProperty.GetValue(_plcSoftware);
                    if (typeList == null)
                    {
                        _logger?.LogError("TypeList is null");
                        result.Errors.Add("TypeList is null");
                        return result;
                    }

                    // Get Types collection
                    var typesProperty = typeList.GetType().GetProperty("Types");
                    if (typesProperty == null)
                    {
                        _logger?.LogError("Could not find Types property");
                        result.Errors.Add("Could not find Types property");
                        return result;
                    }

                    var typesCollection = typesProperty.GetValue(typeList) as System.Collections.IEnumerable;
                    if (typesCollection == null)
                    {
                        _logger?.LogError("Types collection is null");
                        result.Errors.Add("Types collection is null");
                        return result;
                    }

                    // Compile regex filter if provided
                    Regex? regex = null;
                    if (!string.IsNullOrEmpty(filterRegex))
                    {
                        try
                        {
                            regex = new Regex(filterRegex, RegexOptions.IgnoreCase);
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogError(ex, "Invalid regex filter: {FilterRegex}", filterRegex);
                            result.Errors.Add($"Invalid regex filter: {filterRegex}");
                            return result;
                        }
                    }

                    // Ensure directory exists
                    if (!Directory.Exists(exportPath))
                    {
                        Directory.CreateDirectory(exportPath);
                    }

                    // Count total and export each type
                    foreach (var type in typesCollection)
                    {
                        try
                        {
                            var nameProperty = type.GetType().GetProperty("Name");
                            string name = nameProperty?.GetValue(type)?.ToString() ?? "";

                            // Apply filter if provided
                            if (regex != null && !regex.IsMatch(name))
                            {
                                continue;
                            }

                            result.TotalCount++;

                            // Check consistency
                            var isConsistentProperty = type.GetType().GetProperty("IsConsistent");
                            if (isConsistentProperty != null)
                            {
                                var isConsistent = isConsistentProperty.GetValue(type);
                                if (isConsistent is bool consistent && !consistent)
                                {
                                    result.FailedCount++;
                                    result.FailedItems.Add(name);
                                    result.Errors.Add($"Type '{name}' is not consistent and cannot be exported");
                                    continue;
                                }
                            }

                            // Export the type
                            var typeExportPath = Path.Combine(exportPath, $"{name}.xml");
                            bool exported = false;

                            // Try Export methods
                            var simpleExportMethod = type.GetType().GetMethod("Export", new[] { typeof(FileInfo) });
                            if (simpleExportMethod != null)
                            {
                                try
                                {
                                    simpleExportMethod.Invoke(type, new object[] { new FileInfo(typeExportPath) });
                                    exported = true;
                                }
                                catch
                                {
                                    // Try with ExportOptions
                                }
                            }

                            if (exported)
                            {
                                result.SuccessCount++;
                                result.ExportedItems.Add(name);
                                _logger?.LogDebug("Successfully exported: {TypeName}", name);
                            }
                            else
                            {
                                result.FailedCount++;
                                result.FailedItems.Add(name);
                                result.Errors.Add($"Failed to export: {name}");
                            }
                        }
                        catch (Exception ex)
                        {
                            result.Errors.Add($"Error processing type: {ex.Message}");
                        }
                    }

                    _logger?.LogInformation("Export types complete: {SuccessCount} succeeded, {FailedCount} failed", result.SuccessCount, result.FailedCount);
                    return result;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to export types");
                    result.Errors.Add($"Error: {ex.Message}");
                    return result;
                }
            });
        }

        #endregion

        #region Import Operations

        /// <inheritdoc/>
        public async Task<bool> ImportBlockAsync(string importPath, string? groupPath = null)
        {
            return await Task.Run(() =>
            {
                if (_plcSoftware == null) return false;

                try
                {
                    var fileInfo = new FileInfo(importPath);
                    if (!fileInfo.Exists)
                    {
                        _logger?.LogError("Import file not found: {ImportPath}", importPath);
                        return false;
                    }

                    // Get block group (navigate to sub-group if path provided)
                    var blockGroup = TiaReflectionHelper.GetBlockGroup(_plcSoftware);
                    if (blockGroup == null) return false;

                    var targetGroup = string.IsNullOrEmpty(groupPath)
                        ? blockGroup
                        : TiaReflectionHelper.GetBlockGroupByPath(blockGroup, groupPath);

                    if (targetGroup == null)
                    {
                        _logger?.LogError("Block group not found: {GroupPath}", groupPath ?? "root");
                        return false;
                    }

                    // Get Blocks collection and call Import
                    var blocksCollection = TiaReflectionHelper.GetProperty(targetGroup, "Blocks");
                    if (blocksCollection == null) return false;

                    // Use reflection to call Import method with ImportOptions.Override = 2
                    var importMethod = blocksCollection.GetType().GetMethod("Import", new[] { typeof(FileInfo), typeof(int) });

                    if (importMethod != null)
                    {
                        var result = importMethod.Invoke(blocksCollection, new object[] { fileInfo, 2 });
                        var resultList = result as System.Collections.IEnumerable;
                        return resultList != null && resultList.Cast<object>().Any();
                    }

                    return false;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error importing block from {ImportPath}", importPath);
                    return false;
                }
            });
        }

        /// <inheritdoc/>
        public async Task<ImportResult> ImportBlocksAsync(string importDirectory, string? filterRegex = null, string? groupPath = null)
        {
            var result = new ImportResult();

            return await Task.Run(() =>
            {
                if (_plcSoftware == null || string.IsNullOrEmpty(CurrentPlcName))
                {
                    _logger?.LogWarning("No PLC software is currently open");
                    result.Errors.Add("No PLC software is currently open");
                    return result;
                }

                _logger?.LogInformation("Importing blocks: filter='{Filter}', directory='{Directory}'", filterRegex ?? "all", importDirectory);

                try
                {
                    var dirInfo = new DirectoryInfo(importDirectory);
                    if (!dirInfo.Exists)
                    {
                        _logger?.LogError("Import directory not found: {ImportDirectory}", importDirectory);
                        result.Errors.Add($"Import directory not found: {importDirectory}");
                        return result;
                    }

                    // Compile regex filter if provided
                    Regex? regex = null;
                    if (!string.IsNullOrEmpty(filterRegex))
                    {
                        try
                        {
                            regex = new Regex(filterRegex, RegexOptions.IgnoreCase);
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogError(ex, "Invalid regex filter: {FilterRegex}", filterRegex);
                            result.Errors.Add($"Invalid regex filter: {filterRegex}");
                            return result;
                        }
                    }

                    // Get block group
                    var blockGroup = TiaReflectionHelper.GetBlockGroup(_plcSoftware);
                    if (blockGroup == null)
                    {
                        result.Errors.Add("Could not get BlockGroup");
                        return result;
                    }

                    var targetGroup = string.IsNullOrEmpty(groupPath)
                        ? blockGroup
                        : TiaReflectionHelper.GetBlockGroupByPath(blockGroup, groupPath);

                    if (targetGroup == null)
                    {
                        _logger?.LogError("Block group not found: {GroupPath}", groupPath ?? "root");
                        result.Errors.Add($"Block group not found: {groupPath ?? "root"}");
                        return result;
                    }

                    var blocksCollection = TiaReflectionHelper.GetProperty(targetGroup, "Blocks");
                    if (blocksCollection == null)
                    {
                        result.Errors.Add("Could not get Blocks collection");
                        return result;
                    }

                    var importMethod = blocksCollection.GetType().GetMethod("Import", new[] { typeof(FileInfo), typeof(int) });
                    if (importMethod == null)
                    {
                        result.Errors.Add("Could not get Import method");
                        return result;
                    }

                    // Import each XML file
                    foreach (var file in dirInfo.GetFiles("*.xml"))
                    {
                        try
                        {
                            // Apply filter if provided
                            if (regex != null && !regex.IsMatch(Path.GetFileNameWithoutExtension(file.Name)))
                            {
                                continue;
                            }

                            result.TotalCount++;

                            var importResult = importMethod.Invoke(blocksCollection, new object[] { file, 2 });
                            var resultList = importResult as System.Collections.IEnumerable;

                            if (resultList != null && resultList.Cast<object>().Any())
                            {
                                result.SuccessCount++;
                                result.ImportedItems.Add(file.Name);
                                _logger?.LogDebug("Successfully imported: {FileName}", file.Name);
                            }
                            else
                            {
                                result.FailedCount++;
                                result.FailedItems.Add(file.Name);
                                result.Errors.Add($"Failed to import: {file.Name}");
                            }
                        }
                        catch (Exception ex)
                        {
                            result.FailedCount++;
                            result.FailedItems.Add(file.Name);
                            result.Errors.Add($"Error importing {file.Name}: {ex.Message}");
                        }
                    }

                    _logger?.LogInformation("Import blocks complete: {SuccessCount} succeeded, {FailedCount} failed", result.SuccessCount, result.FailedCount);
                    return result;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to import blocks");
                    result.Errors.Add($"Error: {ex.Message}");
                    return result;
                }
            });
        }

        /// <inheritdoc/>
        public async Task<bool> ImportTypeAsync(string importPath, string? groupPath = null)
        {
            return await Task.Run(() =>
            {
                if (_plcSoftware == null) return false;

                try
                {
                    var fileInfo = new FileInfo(importPath);
                    if (!fileInfo.Exists)
                    {
                        _logger?.LogError("Import file not found: {ImportPath}", importPath);
                        return false;
                    }

                    // Get type group (navigate to sub-group if path provided)
                    var typeGroup = TiaReflectionHelper.GetTypeGroup(_plcSoftware);
                    if (typeGroup == null) return false;

                    var targetGroup = string.IsNullOrEmpty(groupPath)
                        ? typeGroup
                        : TiaReflectionHelper.GetTypeGroupByPath(typeGroup, groupPath);

                    if (targetGroup == null)
                    {
                        _logger?.LogError("Type group not found: {GroupPath}", groupPath ?? "root");
                        return false;
                    }

                    // Get Types collection and call Import
                    var typesCollection = TiaReflectionHelper.GetProperty(targetGroup, "Types");
                    if (typesCollection == null) return false;

                    // Use reflection to call Import method with ImportOptions.Override = 2
                    var importMethod = typesCollection.GetType().GetMethod("Import", new[] { typeof(FileInfo), typeof(int) });

                    if (importMethod != null)
                    {
                        var result = importMethod.Invoke(typesCollection, new object[] { fileInfo, 2 });
                        var resultList = result as System.Collections.IEnumerable;
                        return resultList != null && resultList.Cast<object>().Any();
                    }

                    return false;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error importing type from {ImportPath}", importPath);
                    return false;
                }
            });
        }

        /// <inheritdoc/>
        public async Task<ImportResult> ImportTypesAsync(string importDirectory, string? filterRegex = null, string? groupPath = null)
        {
            var result = new ImportResult();

            return await Task.Run(() =>
            {
                if (_plcSoftware == null || string.IsNullOrEmpty(CurrentPlcName))
                {
                    _logger?.LogWarning("No PLC software is currently open");
                    result.Errors.Add("No PLC software is currently open");
                    return result;
                }

                _logger?.LogInformation("Importing types: filter='{Filter}', directory='{Directory}'", filterRegex ?? "all", importDirectory);

                try
                {
                    var dirInfo = new DirectoryInfo(importDirectory);
                    if (!dirInfo.Exists)
                    {
                        _logger?.LogError("Import directory not found: {ImportDirectory}", importDirectory);
                        result.Errors.Add($"Import directory not found: {importDirectory}");
                        return result;
                    }

                    // Compile regex filter if provided
                    Regex? regex = null;
                    if (!string.IsNullOrEmpty(filterRegex))
                    {
                        try
                        {
                            regex = new Regex(filterRegex, RegexOptions.IgnoreCase);
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogError(ex, "Invalid regex filter: {FilterRegex}", filterRegex);
                            result.Errors.Add($"Invalid regex filter: {filterRegex}");
                            return result;
                        }
                    }

                    // Get type group
                    var typeGroup = TiaReflectionHelper.GetTypeGroup(_plcSoftware);
                    if (typeGroup == null)
                    {
                        result.Errors.Add("Could not get TypeGroup");
                        return result;
                    }

                    var targetGroup = string.IsNullOrEmpty(groupPath)
                        ? typeGroup
                        : TiaReflectionHelper.GetTypeGroupByPath(typeGroup, groupPath);

                    if (targetGroup == null)
                    {
                        _logger?.LogError("Type group not found: {GroupPath}", groupPath ?? "root");
                        result.Errors.Add($"Type group not found: {groupPath ?? "root"}");
                        return result;
                    }

                    var typesCollection = TiaReflectionHelper.GetProperty(targetGroup, "Types");
                    if (typesCollection == null)
                    {
                        result.Errors.Add("Could not get Types collection");
                        return result;
                    }

                    var importMethod = typesCollection.GetType().GetMethod("Import", new[] { typeof(FileInfo), typeof(int) });
                    if (importMethod == null)
                    {
                        result.Errors.Add("Could not get Import method");
                        return result;
                    }

                    // Import each XML file
                    foreach (var file in dirInfo.GetFiles("*.xml"))
                    {
                        try
                        {
                            // Apply filter if provided
                            if (regex != null && !regex.IsMatch(Path.GetFileNameWithoutExtension(file.Name)))
                            {
                                continue;
                            }

                            result.TotalCount++;

                            var importResult = importMethod.Invoke(typesCollection, new object[] { file, 2 });
                            var resultList = importResult as System.Collections.IEnumerable;

                            if (resultList != null && resultList.Cast<object>().Any())
                            {
                                result.SuccessCount++;
                                result.ImportedItems.Add(file.Name);
                                _logger?.LogDebug("Successfully imported: {FileName}", file.Name);
                            }
                            else
                            {
                                result.FailedCount++;
                                result.FailedItems.Add(file.Name);
                                result.Errors.Add($"Failed to import: {file.Name}");
                            }
                        }
                        catch (Exception ex)
                        {
                            result.FailedCount++;
                            result.FailedItems.Add(file.Name);
                            result.Errors.Add($"Error importing {file.Name}: {ex.Message}");
                        }
                    }

                    _logger?.LogInformation("Import types complete: {SuccessCount} succeeded, {FailedCount} failed", result.SuccessCount, result.FailedCount);
                    return result;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to import types");
                    result.Errors.Add($"Error: {ex.Message}");
                    return result;
                }
            });
        }

        /// <inheritdoc/>
        public async Task<bool> ImportBlockFromDocumentsAsync(string importDirectory, string fileNameWithoutExtension, string? groupPath = null, string importOption = "Override")
        {
            return await Task.Run(() =>
            {
                if (_plcSoftware == null) return false;

                try
                {
                    // Check TIA Portal version - document import requires V20+
                    var tiaVersion = TiaReflectionHelper.ParseTiaVersion(_portalService.Version);
                    if (tiaVersion == null || tiaVersion.Major < 20)
                    {
                        _logger?.LogError("Import from documents requires TIA Portal V20 or newer (current: {Version})", _portalService.Version);
                        throw new InvalidOperationException("Import from documents requires TIA Portal V20 or newer");
                    }

                    var dirInfo = new DirectoryInfo(importDirectory);
                    if (!dirInfo.Exists)
                    {
                        _logger?.LogError("Import directory not found: {ImportDirectory}", importDirectory);
                        return false;
                    }

                    // Verify the .s7dcl file exists (for validation)
                    var s7dclFile = new FileInfo(Path.Combine(importDirectory, $"{fileNameWithoutExtension}.s7dcl"));
                    if (!s7dclFile.Exists)
                    {
                        _logger?.LogError("Document file not found: {FileName}.s7dcl", fileNameWithoutExtension);
                        return false;
                    }

                    // Get block group
                    var blockGroup = TiaReflectionHelper.GetBlockGroup(_plcSoftware);
                    if (blockGroup == null) return false;

                    var targetGroup = string.IsNullOrEmpty(groupPath)
                        ? blockGroup
                        : TiaReflectionHelper.GetBlockGroupByPath(blockGroup, groupPath);

                    if (targetGroup == null)
                    {
                        _logger?.LogError("Block group not found: {GroupPath}", groupPath ?? "root");
                        return false;
                    }

                    var blocksCollection = TiaReflectionHelper.GetProperty(targetGroup, "Blocks");
                    if (blocksCollection == null) return false;

                    // Parse import option string to enum value
                    int importOptionValue = importOption.ToLowerInvariant() switch
                    {
                        "none" => 0,
                        "skipinactivecultures" => 1,
                        "override" => 2,
                        "activateinactivecultures" => 3,
                        _ => 2 // Default to Override
                    };

                    // Look for ImportFromDocuments method (V20+)
                    var importMethod = blocksCollection.GetType().GetMethod("ImportFromDocuments",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                    if (importMethod != null)
                    {
                        // ImportFromDocuments(DirectoryInfo, string, ImportDocumentOptions)
                        var result = importMethod.Invoke(blocksCollection, new object[] { dirInfo, fileNameWithoutExtension, importOptionValue });
                        return result != null;
                    }

                    _logger?.LogError("ImportFromDocuments method not found (requires TIA Portal V20+)");
                    return false;
                }
                catch (InvalidOperationException)
                {
                    throw; // Re-throw InvalidOperationException for version check
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error importing block from documents in {ImportDirectory}", importDirectory);
                    return false;
                }
            });
        }

        /// <inheritdoc/>
        public async Task<ImportResult> ImportBlocksFromDocumentsAsync(string importDirectory, string? filterRegex = null, string? groupPath = null, string importOption = "Override")
        {
            var result = new ImportResult();

            return await Task.Run(() =>
            {
                if (_plcSoftware == null || string.IsNullOrEmpty(CurrentPlcName))
                {
                    _logger?.LogWarning("No PLC software is currently open");
                    result.Errors.Add("No PLC software is currently open");
                    return result;
                }

                _logger?.LogInformation("Importing blocks from documents: filter='{Filter}', directory='{Directory}'", filterRegex ?? "all", importDirectory);

                try
                {
                    // Check TIA Portal version - document import requires V20+
                    var tiaVersion = TiaReflectionHelper.ParseTiaVersion(_portalService.Version);
                    if (tiaVersion == null || tiaVersion.Major < 20)
                    {
                        _logger?.LogError("Import from documents requires TIA Portal V20 or newer (current: {Version})", _portalService.Version);
                        result.Errors.Add("Import from documents requires TIA Portal V20 or newer");
                        return result;
                    }

                    var dirInfo = new DirectoryInfo(importDirectory);
                    if (!dirInfo.Exists)
                    {
                        _logger?.LogError("Import directory not found: {ImportDirectory}", importDirectory);
                        result.Errors.Add($"Import directory not found: {importDirectory}");
                        return result;
                    }

                    // Compile regex filter if provided
                    Regex? regex = null;
                    if (!string.IsNullOrEmpty(filterRegex))
                    {
                        try
                        {
                            regex = new Regex(filterRegex, RegexOptions.IgnoreCase);
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogError(ex, "Invalid regex filter: {FilterRegex}", filterRegex);
                            result.Errors.Add($"Invalid regex filter: {filterRegex}");
                            return result;
                        }
                    }

                    // Get block group
                    var blockGroup = TiaReflectionHelper.GetBlockGroup(_plcSoftware);
                    if (blockGroup == null)
                    {
                        result.Errors.Add("Could not get BlockGroup");
                        return result;
                    }

                    var targetGroup = string.IsNullOrEmpty(groupPath)
                        ? blockGroup
                        : TiaReflectionHelper.GetBlockGroupByPath(blockGroup, groupPath);

                    if (targetGroup == null)
                    {
                        _logger?.LogError("Block group not found: {GroupPath}", groupPath ?? "root");
                        result.Errors.Add($"Block group not found: {groupPath ?? "root"}");
                        return result;
                    }

                    var blocksCollection = TiaReflectionHelper.GetProperty(targetGroup, "Blocks");
                    if (blocksCollection == null)
                    {
                        result.Errors.Add("Could not get Blocks collection");
                        return result;
                    }

                    // Parse import option string to enum value
                    int importOptionValue = importOption.ToLowerInvariant() switch
                    {
                        "none" => 0,
                        "skipinactivecultures" => 1,
                        "override" => 2,
                        "activateinactivecultures" => 3,
                        _ => 2 // Default to Override
                    };

                    // Look for ImportFromDocuments method (V20+)
                    var importMethod = blocksCollection.GetType().GetMethod("ImportFromDocuments",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                    if (importMethod == null)
                    {
                        result.Errors.Add("ImportFromDocuments method not found (requires TIA Portal V20+)");
                        return result;
                    }

                    // Find all .s7dcl files (document exports have paired .s7dcl and .s7res files)
                    var docFiles = dirInfo.GetFiles("*.s7dcl");
                    _logger?.LogDebug("Found {DocFileCount} .s7dcl files in directory", docFiles.Length);

                    // Apply filter to get list of files to import
                    var filesToImport = new List<FileInfo>();
                    foreach (var file in docFiles)
                    {
                        var blockName = Path.GetFileNameWithoutExtension(file.Name);
                        if (regex != null && !regex.IsMatch(blockName))
                        {
                            _logger?.LogDebug("Skipping {BlockName} (does not match filter)", blockName);
                            continue;
                        }
                        filesToImport.Add(file);
                    }

                    result.TotalCount = filesToImport.Count;

                    if (filesToImport.Count == 0)
                    {
                        _logger?.LogWarning("No .s7dcl files found to import (after filtering)");
                        return result;
                    }

                    // Import each file individually using ImportFromDocuments
                    _logger?.LogInformation("Importing {Count} blocks from documents...", filesToImport.Count);

                    foreach (var file in filesToImport)
                    {
                        var blockName = Path.GetFileNameWithoutExtension(file.Name);
                        _logger?.LogDebug("Importing {BlockName} from documents...", blockName);

                        try
                        {
                            var importResult = importMethod.Invoke(blocksCollection, new object[] { dirInfo, blockName, importOptionValue });

                            if (importResult != null)
                            {
                                result.SuccessCount++;
                                result.ImportedItems.Add(blockName);
                                _logger?.LogDebug("Successfully imported {BlockName}", blockName);
                            }
                            else
                            {
                                result.FailedCount++;
                                result.FailedItems.Add(blockName);
                                result.Errors.Add($"Failed to import {blockName}: ImportFromDocuments returned null");
                            }
                        }
                        catch (System.Reflection.TargetInvocationException tie)
                        {
                            result.FailedCount++;
                            result.FailedItems.Add(blockName);
                            var errorMsg = tie.InnerException?.Message ?? tie.Message;
                            result.Errors.Add($"Failed to import {blockName}: {errorMsg}");
                            _logger?.LogError(tie.InnerException, "Error importing {BlockName}", blockName);
                        }
                        catch (Exception ex)
                        {
                            result.FailedCount++;
                            result.FailedItems.Add(blockName);
                            result.Errors.Add($"Failed to import {blockName}: {ex.Message}");
                            _logger?.LogError(ex, "Error importing {BlockName}", blockName);
                        }
                    }

                    _logger?.LogInformation("Import blocks from documents complete: {SuccessCount} succeeded, {FailedCount} failed", result.SuccessCount, result.FailedCount);
                    return result;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to import blocks from documents");
                    result.Errors.Add($"Error: {ex.Message}");
                    return result;
                }
            });
        }

        private void AddSoftwareContainerNode(
            List<SoftwareTreeNode> target,
            string displayName,
            string containerType,
            string rootPath,
            object? group,
            int? maxDepth,
            int currentDepth,
            IReadOnlyList<string> itemCollectionPropertyNames)
        {
            if (group == null)
            {
                return;
            }

            var path = $"{rootPath}/{displayName}";
            var children = maxDepth.HasValue && currentDepth >= maxDepth.Value
                ? Array.Empty<SoftwareTreeNode>()
                : BuildSoftwareGroupChildren(group, path, maxDepth, currentDepth + 1, itemCollectionPropertyNames);

            target.Add(new SoftwareTreeNode(displayName, containerType, path, children));
        }

        private IReadOnlyList<SoftwareTreeNode> BuildSoftwareGroupChildren(
            object group,
            string parentPath,
            int? maxDepth,
            int currentDepth,
            IReadOnlyList<string> itemCollectionPropertyNames)
        {
            var children = new List<SoftwareTreeNode>();

            foreach (var propertyName in itemCollectionPropertyNames)
            {
                var items = TiaReflectionHelper.GetEnumerableProperty(group, propertyName);
                if (items == null)
                {
                    continue;
                }

                foreach (var item in items)
                {
                    children.Add(BuildSoftwareLeafNode(item, parentPath));
                }
            }

            var groups = TiaReflectionHelper.GetGroups(group);
            if (groups != null)
            {
                foreach (var childGroup in groups)
                {
                    children.Add(BuildSoftwareGroupNode(childGroup, parentPath, maxDepth, currentDepth, itemCollectionPropertyNames));
                }
            }

            return children;
        }

        private SoftwareTreeNode BuildSoftwareGroupNode(
            object group,
            string parentPath,
            int? maxDepth,
            int currentDepth,
            IReadOnlyList<string> itemCollectionPropertyNames)
        {
            var name = TiaReflectionHelper.GetName(group) ?? TiaReflectionHelper.GetTypeName(group) ?? "Group";
            var path = $"{parentPath}/{name}";
            var children = maxDepth.HasValue && currentDepth >= maxDepth.Value
                ? Array.Empty<SoftwareTreeNode>()
                : BuildSoftwareGroupChildren(group, path, maxDepth, currentDepth + 1, itemCollectionPropertyNames);

            return new SoftwareTreeNode(
                name,
                TiaReflectionHelper.GetTypeName(group) ?? "Group",
                path,
                children);
        }

        private static SoftwareTreeNode BuildSoftwareLeafNode(object item, string parentPath)
        {
            var name = TiaReflectionHelper.GetName(item) ?? TiaReflectionHelper.GetTypeName(item) ?? "Item";
            var path = $"{parentPath}/{name}";
            var nodeType = GetSoftwareLeafType(item);

            return new SoftwareTreeNode(name, nodeType, path, Array.Empty<SoftwareTreeNode>());
        }

        private static string GetSoftwareLeafType(object item)
        {
            var explicitType = TiaReflectionHelper.GetProperty(item, "Type")?.ToString();
            if (!string.IsNullOrWhiteSpace(explicitType))
            {
                return explicitType!;
            }

            var programmingLanguage = TiaReflectionHelper.GetProperty(item, "ProgrammingLanguage")?.ToString();
            if (!string.IsNullOrWhiteSpace(programmingLanguage))
            {
                return $"{TiaReflectionHelper.GetTypeName(item) ?? "Item"} ({programmingLanguage})";
            }

            return TiaReflectionHelper.GetTypeName(item) ?? "Item";
        }

        private object? GetEngineeringService(object engineeringObject, Type serviceType)
        {
            var getServiceMethod = engineeringObject.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "GetService" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);

            if (getServiceMethod == null)
            {
                return null;
            }

            try
            {
                return getServiceMethod.MakeGenericMethod(serviceType).Invoke(engineeringObject, null);
            }
            catch (TargetInvocationException ex)
            {
                _logger?.LogDebug(ex.InnerException ?? ex, "Failed to resolve engineering service {ServiceType}", serviceType.FullName);
                return null;
            }
        }

        private static Type? FindSiemensType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (assembly.FullName == null || !assembly.FullName.StartsWith("Siemens.Engineering", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var type = assembly.GetType(fullName, throwOnError: false, ignoreCase: false);
                    if (type != null)
                    {
                        return type;
                    }
                }
                catch
                {
                    // Skip assemblies that cannot be inspected.
                }
            }

            return null;
        }

        private static int GetIntProperty(object source, string propertyName)
        {
            var value = TiaReflectionHelper.GetProperty(source, propertyName);
            if (value == null)
            {
                return 0;
            }

            if (value is int intValue)
            {
                return intValue;
            }

            return int.TryParse(value.ToString(), out var parsed) ? parsed : 0;
        }

        private static string BuildCompilationMessage(object compileResult, string state, int errorCount, int warningCount)
        {
            var summary = $"Compilation state: {state}. Errors: {errorCount}. Warnings: {warningCount}.";
            var rawMessages = TiaReflectionHelper.GetProperty(compileResult, "Messages") as System.Collections.IEnumerable;
            if (rawMessages == null)
            {
                return summary;
            }

            var messages = new List<string>();
            foreach (var rawMessage in rawMessages)
            {
                if (rawMessage == null)
                {
                    continue;
                }

                var description = TiaReflectionHelper.GetProperty(rawMessage, "Description")?.ToString();
                if (string.IsNullOrWhiteSpace(description))
                {
                    continue;
                }

                var messageState = TiaReflectionHelper.GetProperty(rawMessage, "State")?.ToString();
                var path = TiaReflectionHelper.GetProperty(rawMessage, "Path")?.ToString();
                var formatted = string.IsNullOrWhiteSpace(path)
                    ? $"{messageState}: {description}"
                    : $"{messageState}: {path} - {description}";

                messages.Add(formatted);
                if (messages.Count == 3)
                {
                    break;
                }
            }

            return messages.Count == 0
                ? summary
                : $"{summary} {string.Join(" | ", messages)}";
        }

        #endregion
    }
}
