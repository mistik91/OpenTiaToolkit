using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Models;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Services
{
    /// <summary>
    /// PLC import/export operations extracted from PlcService.
    /// </summary>
    internal sealed class PlcImportExportService
    {
        private readonly EngineeringSessionContext _sessionContext;
        private readonly IPortalService _portalService;
        private readonly PlcBrowseService _browseService;
        private readonly V21OpennessAdapter _v21Adapter;
        private readonly ILogger? _logger;

        public PlcImportExportService(
            EngineeringSessionContext sessionContext,
            IPortalService portalService,
            PlcBrowseService browseService,
            ILogger? logger,
            V21OpennessAdapter? v21Adapter = null)
        {
            _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
            _portalService = portalService ?? throw new ArgumentNullException(nameof(portalService));
            _browseService = browseService ?? throw new ArgumentNullException(nameof(browseService));
            _v21Adapter = v21Adapter ?? new V21OpennessAdapter();
            _logger = logger;
        }

        public Task<bool> ExportPlcAsync(string exportPath)
        {
            return Task.Run(() =>
            {
                var plcSoftware = _sessionContext.PlcSoftware;
                if (plcSoftware == null || string.IsNullOrEmpty(_sessionContext.CurrentPlcName))
                {
                    _logger?.LogWarning("No PLC software is currently open");
                    return false;
                }

                _logger?.LogInformation("Exporting PLC blocks to: {ExportPath}", exportPath);

                try
                {
                    Directory.CreateDirectory(exportPath);

                    var blockGroup = TiaReflectionHelper.GetBlockGroup(plcSoftware);
                    if (blockGroup == null)
                    {
                        _logger?.LogError("Could not get BlockGroup");
                        return false;
                    }

                    var successCount = 0;
                    var failureCount = 0;

                    foreach (var block in EnumerateBlocks(blockGroup))
                    {
                        var name = TiaReflectionHelper.GetName(block) ?? "Unknown";
                        var type = TiaReflectionHelper.GetProperty(block, "Type")?.ToString();
                        var fileName = !string.IsNullOrWhiteSpace(type) ? $"{name}.{type}.xml" : $"{name}.xml";
                        var filePath = Path.Combine(exportPath, fileName);

                        if (_v21Adapter.TryExportEngineeringObject(block, filePath, "WithDefaults"))
                        {
                            successCount++;
                        }
                        else
                        {
                            failureCount++;
                            _logger?.LogWarning("Could not export block: {BlockName}", name);
                        }
                    }

                    _logger?.LogInformation(
                        "Export complete: {SuccessCount} succeeded, {FailureCount} failed",
                        successCount,
                        failureCount);

                    return successCount > 0;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to export PLC blocks");
                    return false;
                }
            });
        }

        public async Task<bool> ExportBlockAsync(string blockName, string exportPath)
        {
            return await Task.Run(() =>
            {
                var plcSoftware = _sessionContext.PlcSoftware;
                if (plcSoftware == null || string.IsNullOrEmpty(_sessionContext.CurrentPlcName))
                {
                    _logger?.LogWarning("No PLC software is currently open");
                    return false;
                }

                _logger?.LogInformation("Exporting block: {BlockName} to {ExportPath}", blockName, exportPath);

                try
                {
                    var blockGroup = TiaReflectionHelper.GetBlockGroup(plcSoftware);
                    if (blockGroup == null)
                    {
                        _logger?.LogError("Could not get BlockGroup");
                        return false;
                    }

                    var block = EnumerateBlocks(blockGroup).FirstOrDefault(candidate =>
                        string.Equals(TiaReflectionHelper.GetName(candidate), blockName, StringComparison.OrdinalIgnoreCase));

                    if (block == null)
                    {
                        _logger?.LogWarning("Block not found: {BlockName}", blockName);
                        return false;
                    }

                    var directory = Path.GetDirectoryName(exportPath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    return _v21Adapter.TryExportEngineeringObject(block, exportPath, "WithDefaults");
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to export block: {BlockName}", blockName);
                    return false;
                }
            });
        }

        public async Task<bool> ExportBlockAsDocumentsAsync(string blockName, string exportPath)
        {
            return await Task.Run(() =>
            {
                var plcSoftware = _sessionContext.PlcSoftware;
                if (plcSoftware == null || string.IsNullOrEmpty(_sessionContext.CurrentPlcName))
                {
                    _logger?.LogWarning("No PLC software is currently open");
                    return false;
                }

                _logger?.LogInformation("Exporting block as SIMATIC SD documents: {BlockName} to {ExportPath}", blockName, exportPath);

                EnsureDocumentsSupported("SIMATIC SD export");

                try
                {
                    var blockGroup = TiaReflectionHelper.GetBlockGroup(plcSoftware);
                    if (blockGroup == null)
                    {
                        _logger?.LogError("Could not get BlockGroup");
                        return false;
                    }

                    var block = EnumerateBlocks(blockGroup).FirstOrDefault(candidate =>
                        string.Equals(TiaReflectionHelper.GetName(candidate), blockName, StringComparison.OrdinalIgnoreCase));

                    if (block == null)
                    {
                        _logger?.LogWarning("Block not found: {BlockName}", blockName);
                        return false;
                    }

                    Directory.CreateDirectory(exportPath);

                    if (!_v21Adapter.TryExportAsDocuments(block, new DirectoryInfo(exportPath), blockName, out var state))
                    {
                        _logger?.LogError("ExportAsDocuments returned non-success state or is not available on block type");
                        return false;
                    }

                    var success = string.Equals(state, "Success", StringComparison.OrdinalIgnoreCase);

                    if (success)
                    {
                        _logger?.LogInformation("Successfully exported block as SIMATIC SD documents: {BlockName}", blockName);
                    }
                    else
                    {
                        _logger?.LogError("ExportAsDocuments returned non-success state for block: {BlockName}", blockName);
                    }

                    return success;
                }
                catch (InvalidOperationException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to export block as documents: {BlockName}", blockName);
                    return false;
                }
            });
        }

        public async Task<ExportResult> ExportBlocksAsDocumentsAsync(string? filterRegex, string exportPath)
        {
            var result = new ExportResult();
            var plcSoftware = _sessionContext.PlcSoftware;
            if (plcSoftware == null || string.IsNullOrEmpty(_sessionContext.CurrentPlcName))
            {
                _logger?.LogWarning("No PLC software is currently open");
                result.Errors.Add("No PLC software is currently open");
                return result;
            }

            _logger?.LogInformation("Exporting blocks as SIMATIC SD documents: filter='{Filter}', path='{ExportPath}'", filterRegex ?? "all", exportPath);

            try
            {
                EnsureDocumentsSupported("SIMATIC SD export");
            }
            catch (InvalidOperationException ex)
            {
                _logger?.LogError(ex, "Version check failed for ExportBlocksAsDocumentsAsync");
                result.Errors.Add(ex.Message);
                return result;
            }

            var regex = TryCreateRegex(filterRegex, result.Errors);
            if (!string.IsNullOrEmpty(filterRegex) && regex == null)
            {
                return result;
            }

            Directory.CreateDirectory(exportPath);

            var blocks = await _browseService.GetBlocksAsync(filterRegex);
            result.TotalCount = blocks.Count;

            foreach (var block in blocks)
            {
                if (block.Type.Equals("OB", StringComparison.OrdinalIgnoreCase) ||
                    block.Type.Equals("FOB", StringComparison.OrdinalIgnoreCase))
                {
                    result.FailedCount++;
                    result.FailedItems.Add(block.Name);
                    result.Errors.Add($"Skipped {block.Type} block '{block.Name}': SIMATIC SD export not supported for Organization Blocks");
                    continue;
                }

                if (regex != null && !regex.IsMatch(block.Name))
                {
                    continue;
                }

                try
                {
                    var success = await ExportBlockAsDocumentsAsync(block.Name, exportPath);
                    if (success)
                    {
                        result.SuccessCount++;
                        result.ExportedItems.Add(block.Name);
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

            _logger?.LogInformation(
                "Export blocks as documents complete: {SuccessCount} succeeded, {FailedCount} failed",
                result.SuccessCount,
                result.FailedCount);

            return result;
        }

        public async Task<bool> ExportTypeAsync(string typeName, string exportPath)
        {
            return await Task.Run(() =>
            {
                var plcSoftware = _sessionContext.PlcSoftware;
                if (plcSoftware == null || string.IsNullOrEmpty(_sessionContext.CurrentPlcName))
                {
                    _logger?.LogWarning("No PLC software is currently open");
                    return false;
                }

                _logger?.LogInformation("Exporting type: {TypeName} to {ExportPath}", typeName, exportPath);

                try
                {
                    var typeGroup = TiaReflectionHelper.GetTypeGroup(plcSoftware);
                    if (typeGroup == null)
                    {
                        _logger?.LogError("Could not get TypeGroup");
                        return false;
                    }

                    var type = EnumerateTypes(typeGroup).FirstOrDefault(candidate =>
                        string.Equals(TiaReflectionHelper.GetName(candidate), typeName, StringComparison.OrdinalIgnoreCase));

                    if (type == null)
                    {
                        _logger?.LogWarning("Type not found: {TypeName}", typeName);
                        return false;
                    }

                    if (!_v21Adapter.IsConsistent(type))
                    {
                        _logger?.LogError("Type '{TypeName}' is not consistent and cannot be exported", typeName);
                        return false;
                    }

                    var directory = Path.GetDirectoryName(exportPath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    return _v21Adapter.TryExportEngineeringObject(type, exportPath, "None");
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to export type: {TypeName}", typeName);
                    return false;
                }
            });
        }

        public Task<ExportResult> ExportTypesAsync(string? filterRegex, string exportPath)
        {
            return Task.Run(() =>
            {
                var result = new ExportResult();
                var plcSoftware = _sessionContext.PlcSoftware;
                if (plcSoftware == null || string.IsNullOrEmpty(_sessionContext.CurrentPlcName))
                {
                    _logger?.LogWarning("No PLC software is currently open");
                    result.Errors.Add("No PLC software is currently open");
                    return result;
                }

                _logger?.LogInformation("Exporting types: filter='{Filter}', path='{ExportPath}'", filterRegex ?? "all", exportPath);

                try
                {
                    var typeGroup = TiaReflectionHelper.GetTypeGroup(plcSoftware);
                    if (typeGroup == null)
                    {
                        result.Errors.Add("Could not get TypeGroup");
                        return result;
                    }

                    var regex = TryCreateRegex(filterRegex, result.Errors);
                    if (!string.IsNullOrEmpty(filterRegex) && regex == null)
                    {
                        return result;
                    }

                    Directory.CreateDirectory(exportPath);

                    foreach (var type in EnumerateTypes(typeGroup))
                    {
                        var name = TiaReflectionHelper.GetName(type) ?? string.Empty;
                        if (string.IsNullOrEmpty(name))
                        {
                            continue;
                        }

                        if (regex != null && !regex.IsMatch(name))
                        {
                            continue;
                        }

                        result.TotalCount++;

                        if (!_v21Adapter.IsConsistent(type))
                        {
                            result.FailedCount++;
                            result.FailedItems.Add(name);
                            result.Errors.Add($"Type '{name}' is not consistent and cannot be exported");
                            continue;
                        }

                        var typeExportPath = Path.Combine(exportPath, $"{name}.xml");
                        if (_v21Adapter.TryExportEngineeringObject(type, typeExportPath, "None"))
                        {
                            result.SuccessCount++;
                            result.ExportedItems.Add(name);
                        }
                        else
                        {
                            result.FailedCount++;
                            result.FailedItems.Add(name);
                            result.Errors.Add($"Failed to export: {name}");
                        }
                    }

                    _logger?.LogInformation(
                        "Export types complete: {SuccessCount} succeeded, {FailedCount} failed",
                        result.SuccessCount,
                        result.FailedCount);

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

        public async Task<bool> ImportBlockAsync(string importPath, string? groupPath = null)
        {
            return await Task.Run(() =>
            {
                var plcSoftware = _sessionContext.PlcSoftware;
                if (plcSoftware == null)
                {
                    return false;
                }

                try
                {
                    var fileInfo = new FileInfo(importPath);
                    if (!fileInfo.Exists)
                    {
                        _logger?.LogError("Import file not found: {ImportPath}", importPath);
                        return false;
                    }

                    var blockGroup = TiaReflectionHelper.GetBlockGroup(plcSoftware);
                    if (blockGroup == null)
                    {
                        return false;
                    }

                    var targetGroup = string.IsNullOrEmpty(groupPath)
                        ? blockGroup
                        : TiaReflectionHelper.GetBlockGroupByPath(blockGroup, groupPath);

                    if (targetGroup == null)
                    {
                        _logger?.LogError("Block group not found: {GroupPath}", groupPath ?? "root");
                        return false;
                    }

                    var blocksCollection = TiaReflectionHelper.GetProperty(targetGroup, "Blocks");
                    if (blocksCollection == null)
                    {
                        return false;
                    }

                    if (!_v21Adapter.TryImport(blocksCollection, fileInfo, 2, out var result))
                    {
                        return false;
                    }

                    return result is IEnumerable resultList && resultList.Cast<object>().Any();
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error importing block from {ImportPath}", importPath);
                    return false;
                }
            });
        }

        public async Task<ImportResult> ImportBlocksAsync(string importDirectory, string? filterRegex = null, string? groupPath = null)
        {
            var result = new ImportResult();
            var plcSoftware = _sessionContext.PlcSoftware;
            if (plcSoftware == null || string.IsNullOrEmpty(_sessionContext.CurrentPlcName))
            {
                _logger?.LogWarning("No PLC software is currently open");
                result.Errors.Add("No PLC software is currently open");
                return result;
            }

            _logger?.LogInformation("Importing blocks: filter='{Filter}', directory='{Directory}'", filterRegex ?? "all", importDirectory);

            var dirInfo = new DirectoryInfo(importDirectory);
            if (!dirInfo.Exists)
            {
                _logger?.LogError("Import directory not found: {ImportDirectory}", importDirectory);
                result.Errors.Add($"Import directory not found: {importDirectory}");
                return result;
            }

            var regex = TryCreateRegex(filterRegex, result.Errors);
            if (!string.IsNullOrEmpty(filterRegex) && regex == null)
            {
                return result;
            }

            foreach (var file in dirInfo.GetFiles("*.xml"))
            {
                if (regex != null && !regex.IsMatch(Path.GetFileNameWithoutExtension(file.Name)))
                {
                    continue;
                }

                result.TotalCount++;

                var success = await ImportBlockAsync(file.FullName, groupPath);
                if (success)
                {
                    result.SuccessCount++;
                    result.ImportedItems.Add(file.Name);
                }
                else
                {
                    result.FailedCount++;
                    result.FailedItems.Add(file.Name);
                    result.Errors.Add($"Failed to import: {file.Name}");
                }
            }

            _logger?.LogInformation(
                "Import blocks complete: {SuccessCount} succeeded, {FailedCount} failed",
                result.SuccessCount,
                result.FailedCount);

            return result;
        }

        public async Task<bool> ImportTypeAsync(string importPath, string? groupPath = null)
        {
            return await Task.Run(() =>
            {
                var plcSoftware = _sessionContext.PlcSoftware;
                if (plcSoftware == null)
                {
                    return false;
                }

                try
                {
                    var fileInfo = new FileInfo(importPath);
                    if (!fileInfo.Exists)
                    {
                        _logger?.LogError("Import file not found: {ImportPath}", importPath);
                        return false;
                    }

                    var typeGroup = TiaReflectionHelper.GetTypeGroup(plcSoftware);
                    if (typeGroup == null)
                    {
                        return false;
                    }

                    var targetGroup = string.IsNullOrEmpty(groupPath)
                        ? typeGroup
                        : TiaReflectionHelper.GetTypeGroupByPath(typeGroup, groupPath);

                    if (targetGroup == null)
                    {
                        _logger?.LogError("Type group not found: {GroupPath}", groupPath ?? "root");
                        return false;
                    }

                    var typesCollection = TiaReflectionHelper.GetProperty(targetGroup, "Types");
                    if (typesCollection == null)
                    {
                        return false;
                    }

                    if (!_v21Adapter.TryImport(typesCollection, fileInfo, 2, out var result))
                    {
                        return false;
                    }

                    return result is IEnumerable resultList && resultList.Cast<object>().Any();
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error importing type from {ImportPath}", importPath);
                    return false;
                }
            });
        }

        public async Task<ImportResult> ImportTypesAsync(string importDirectory, string? filterRegex = null, string? groupPath = null)
        {
            var result = new ImportResult();
            var plcSoftware = _sessionContext.PlcSoftware;
            if (plcSoftware == null || string.IsNullOrEmpty(_sessionContext.CurrentPlcName))
            {
                _logger?.LogWarning("No PLC software is currently open");
                result.Errors.Add("No PLC software is currently open");
                return result;
            }

            _logger?.LogInformation("Importing types: filter='{Filter}', directory='{Directory}'", filterRegex ?? "all", importDirectory);

            var dirInfo = new DirectoryInfo(importDirectory);
            if (!dirInfo.Exists)
            {
                _logger?.LogError("Import directory not found: {ImportDirectory}", importDirectory);
                result.Errors.Add($"Import directory not found: {importDirectory}");
                return result;
            }

            var regex = TryCreateRegex(filterRegex, result.Errors);
            if (!string.IsNullOrEmpty(filterRegex) && regex == null)
            {
                return result;
            }

            foreach (var file in dirInfo.GetFiles("*.xml"))
            {
                if (regex != null && !regex.IsMatch(Path.GetFileNameWithoutExtension(file.Name)))
                {
                    continue;
                }

                result.TotalCount++;

                var success = await ImportTypeAsync(file.FullName, groupPath);
                if (success)
                {
                    result.SuccessCount++;
                    result.ImportedItems.Add(file.Name);
                }
                else
                {
                    result.FailedCount++;
                    result.FailedItems.Add(file.Name);
                    result.Errors.Add($"Failed to import: {file.Name}");
                }
            }

            _logger?.LogInformation(
                "Import types complete: {SuccessCount} succeeded, {FailedCount} failed",
                result.SuccessCount,
                result.FailedCount);

            return result;
        }

        public async Task<bool> ImportBlockFromDocumentsAsync(string importDirectory, string fileNameWithoutExtension, string? groupPath = null, string importOption = "Override")
        {
            return await Task.Run(() =>
            {
                var plcSoftware = _sessionContext.PlcSoftware;
                if (plcSoftware == null)
                {
                    return false;
                }

                EnsureDocumentsSupported("Import from documents");

                try
                {
                    var dirInfo = new DirectoryInfo(importDirectory);
                    if (!dirInfo.Exists)
                    {
                        _logger?.LogError("Import directory not found: {ImportDirectory}", importDirectory);
                        return false;
                    }

                    var s7dclFile = new FileInfo(Path.Combine(importDirectory, $"{fileNameWithoutExtension}.s7dcl"));
                    if (!s7dclFile.Exists)
                    {
                        _logger?.LogError("Document file not found: {FileName}.s7dcl", fileNameWithoutExtension);
                        return false;
                    }

                    var blockGroup = TiaReflectionHelper.GetBlockGroup(plcSoftware);
                    if (blockGroup == null)
                    {
                        return false;
                    }

                    var targetGroup = string.IsNullOrEmpty(groupPath)
                        ? blockGroup
                        : TiaReflectionHelper.GetBlockGroupByPath(blockGroup, groupPath);

                    if (targetGroup == null)
                    {
                        _logger?.LogError("Block group not found: {GroupPath}", groupPath ?? "root");
                        return false;
                    }

                    var blocksCollection = TiaReflectionHelper.GetProperty(targetGroup, "Blocks");
                    if (blocksCollection == null)
                    {
                        return false;
                    }

                    if (!_v21Adapter.TryImportFromDocuments(blocksCollection, dirInfo, fileNameWithoutExtension, ParseImportDocumentOption(importOption), out var result))
                    {
                        _logger?.LogError("ImportFromDocuments method not found (requires TIA Portal V20+)");
                        return false;
                    }

                    return result != null;
                }
                catch (InvalidOperationException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error importing block from documents in {ImportDirectory}", importDirectory);
                    return false;
                }
            });
        }

        public async Task<ImportResult> ImportBlocksFromDocumentsAsync(string importDirectory, string? filterRegex = null, string? groupPath = null, string importOption = "Override")
        {
            var result = new ImportResult();
            var plcSoftware = _sessionContext.PlcSoftware;
            if (plcSoftware == null || string.IsNullOrEmpty(_sessionContext.CurrentPlcName))
            {
                _logger?.LogWarning("No PLC software is currently open");
                result.Errors.Add("No PLC software is currently open");
                return result;
            }

            _logger?.LogInformation("Importing blocks from documents: filter='{Filter}', directory='{Directory}'", filterRegex ?? "all", importDirectory);

            try
            {
                EnsureDocumentsSupported("Import from documents");
            }
            catch (InvalidOperationException ex)
            {
                _logger?.LogError(ex, "Version check failed for ImportBlocksFromDocumentsAsync");
                result.Errors.Add(ex.Message);
                return result;
            }

            var dirInfo = new DirectoryInfo(importDirectory);
            if (!dirInfo.Exists)
            {
                _logger?.LogError("Import directory not found: {ImportDirectory}", importDirectory);
                result.Errors.Add($"Import directory not found: {importDirectory}");
                return result;
            }

            var regex = TryCreateRegex(filterRegex, result.Errors);
            if (!string.IsNullOrEmpty(filterRegex) && regex == null)
            {
                return result;
            }

            foreach (var file in dirInfo.GetFiles("*.s7dcl"))
            {
                var blockName = Path.GetFileNameWithoutExtension(file.Name);
                if (regex != null && !regex.IsMatch(blockName))
                {
                    continue;
                }

                result.TotalCount++;

                try
                {
                    var success = await ImportBlockFromDocumentsAsync(importDirectory, blockName, groupPath, importOption);
                    if (success)
                    {
                        result.SuccessCount++;
                        result.ImportedItems.Add(blockName);
                    }
                    else
                    {
                        result.FailedCount++;
                        result.FailedItems.Add(blockName);
                        result.Errors.Add($"Failed to import {blockName}: ImportFromDocuments returned null");
                    }
                }
                catch (TargetInvocationException ex)
                {
                    result.FailedCount++;
                    result.FailedItems.Add(blockName);
                    result.Errors.Add($"Failed to import {blockName}: {ex.InnerException?.Message ?? ex.Message}");
                    _logger?.LogError(ex.InnerException ?? ex, "Error importing {BlockName}", blockName);
                }
                catch (Exception ex)
                {
                    result.FailedCount++;
                    result.FailedItems.Add(blockName);
                    result.Errors.Add($"Failed to import {blockName}: {ex.Message}");
                    _logger?.LogError(ex, "Error importing {BlockName}", blockName);
                }
            }

            _logger?.LogInformation(
                "Import blocks from documents complete: {SuccessCount} succeeded, {FailedCount} failed",
                result.SuccessCount,
                result.FailedCount);

            return result;
        }

        private void EnsureDocumentsSupported(string operation)
        {
            var tiaVersion = TiaReflectionHelper.ParseTiaVersion(_portalService.Version);
            if (tiaVersion == null || tiaVersion.Major < 20)
            {
                throw new InvalidOperationException($"{operation} requires TIA Portal V20 or newer. Current version: {_portalService.Version ?? "Unknown"}");
            }
        }

        private static Regex? TryCreateRegex(string? filterRegex, List<string> errors)
        {
            if (string.IsNullOrEmpty(filterRegex))
            {
                return null;
            }

            try
            {
                return new Regex(filterRegex, RegexOptions.IgnoreCase);
            }
            catch (Exception)
            {
                errors.Add($"Invalid regex filter: {filterRegex}");
                return null;
            }
        }

        private static IEnumerable<object> EnumerateBlocks(object group)
        {
            var blocks = TiaReflectionHelper.GetBlocks(group);
            if (blocks != null)
            {
                foreach (var block in blocks)
                {
                    yield return block;
                }
            }

            var groups = TiaReflectionHelper.GetGroups(group);
            if (groups == null)
            {
                yield break;
            }

            foreach (var childGroup in groups)
            {
                foreach (var block in EnumerateBlocks(childGroup))
                {
                    yield return block;
                }
            }
        }

        private static IEnumerable<object> EnumerateTypes(object group)
        {
            var types = TiaReflectionHelper.GetTypes(group);
            if (types != null)
            {
                foreach (var type in types)
                {
                    yield return type;
                }
            }

            var groups = TiaReflectionHelper.GetGroups(group);
            if (groups == null)
            {
                yield break;
            }

            foreach (var childGroup in groups)
            {
                foreach (var type in EnumerateTypes(childGroup))
                {
                    yield return type;
                }
            }
        }

        private static int ParseImportDocumentOption(string importOption)
        {
            return importOption.ToLowerInvariant() switch
            {
                "none" => 0,
                "skipinactivecultures" => 1,
                "override" => 2,
                "activateinactivecultures" => 3,
                _ => 2
            };
        }
    }
}
