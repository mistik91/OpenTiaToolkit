using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Services.V21;
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Services
{
    /// <summary>
    /// Owns XML block and PLC data-type import/export.
    /// </summary>
    internal sealed class PlcXmlTransferService
    {
        private readonly EngineeringSessionContext _sessionContext;
        private readonly V21DocumentAccess _documents;
        private readonly V21EngineeringObjectAccess _objects;
        private readonly ILogger? _logger;

        public PlcXmlTransferService(
            EngineeringSessionContext sessionContext,
            ILogger? logger,
            V21DocumentAccess documents,
            V21EngineeringObjectAccess objects)
        {
            _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
            _documents = documents ?? throw new ArgumentNullException(nameof(documents));
            _objects = objects ?? throw new ArgumentNullException(nameof(objects));
            _logger = logger;
        }

        public Task<bool> ExportPlcAsync(string exportPath) =>
            Task.Run(() =>
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
                    foreach (var block in PlcTransferRules.EnumerateBlocks(blockGroup))
                    {
                        var name = TiaReflectionHelper.GetName(block) ?? "Unknown";
                        var type = TiaReflectionHelper.GetProperty(block, "Type")?.ToString();
                        var fileName = !string.IsNullOrWhiteSpace(type) ? $"{name}.{type}.xml" : $"{name}.xml";
                        if (_documents.TryExportEngineeringObject(block, Path.Combine(exportPath, fileName), "WithDefaults", out var failureReason))
                            successCount++;
                        else
                        {
                            failureCount++;
                            _logger?.LogWarning("Could not export block {BlockName}: {FailureReason}", name, failureReason ?? "No supported export overload succeeded");
                        }
                    }
                    _logger?.LogInformation("Export complete: {SuccessCount} succeeded, {FailureCount} failed", successCount, failureCount);
                    return successCount > 0;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to export PLC blocks");
                    return false;
                }
            });

        public Task<bool> ExportBlockAsync(string blockName, string exportPath) =>
            Task.Run(() =>
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
                    var block = PlcTransferRules.EnumerateBlocks(blockGroup).FirstOrDefault(candidate =>
                        string.Equals(TiaReflectionHelper.GetName(candidate), blockName, StringComparison.OrdinalIgnoreCase));
                    if (block == null)
                    {
                        _logger?.LogWarning("Block not found: {BlockName}", blockName);
                        return false;
                    }
                    var directory = Path.GetDirectoryName(exportPath);
                    if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                    if (_documents.TryExportEngineeringObject(block, exportPath, "WithDefaults", out var failureReason)) return true;
                    _logger?.LogError("Could not export block {BlockName}: {FailureReason}", blockName, failureReason ?? "No supported export overload succeeded");
                    return false;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to export block: {BlockName}", blockName);
                    return false;
                }
            });

        public Task<bool> ExportTypeAsync(string typeName, string exportPath) =>
            Task.Run(() =>
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
                    var type = PlcTransferRules.EnumerateTypes(typeGroup).FirstOrDefault(candidate =>
                        string.Equals(TiaReflectionHelper.GetName(candidate), typeName, StringComparison.OrdinalIgnoreCase));
                    if (type == null)
                    {
                        _logger?.LogWarning("Type not found: {TypeName}", typeName);
                        return false;
                    }
                    if (!_objects.IsConsistent(type))
                    {
                        _logger?.LogError("Type '{TypeName}' is not consistent and cannot be exported", typeName);
                        return false;
                    }
                    var directory = Path.GetDirectoryName(exportPath);
                    if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                    if (_documents.TryExportEngineeringObject(type, exportPath, "None", out var failureReason)) return true;
                    _logger?.LogError("Could not export type {TypeName}: {FailureReason}", typeName, failureReason ?? "No supported export overload succeeded");
                    return false;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to export type: {TypeName}", typeName);
                    return false;
                }
            });

        public Task<ExportResult> ExportTypesAsync(string? filterRegex, string exportPath) =>
            Task.Run(() =>
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
                    var regex = PlcTransferRules.TryCreateRegex(filterRegex, result.Errors);
                    if (!string.IsNullOrEmpty(filterRegex) && regex == null) return result;
                    Directory.CreateDirectory(exportPath);
                    foreach (var type in PlcTransferRules.EnumerateTypes(typeGroup))
                    {
                        var name = TiaReflectionHelper.GetName(type) ?? string.Empty;
                        if (string.IsNullOrEmpty(name) || (regex != null && !regex.IsMatch(name))) continue;
                        result.TotalCount++;
                        if (!_objects.IsConsistent(type))
                        {
                            result.FailedCount++;
                            result.FailedItems.Add(name);
                            result.Errors.Add($"Type '{name}' is not consistent and cannot be exported");
                            continue;
                        }
                        if (_documents.TryExportEngineeringObject(type, Path.Combine(exportPath, $"{name}.xml"), "None", out var failureReason))
                        {
                            result.SuccessCount++;
                            result.ExportedItems.Add(name);
                        }
                        else
                        {
                            result.FailedCount++;
                            result.FailedItems.Add(name);
                            result.Errors.Add($"Failed to export {name}: {failureReason ?? "No supported export overload succeeded"}");
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

        public Task<bool> ImportBlockAsync(string importPath, string? groupPath = null) =>
            ImportSingleAsync(importPath, groupPath, isType: false);

        public Task<bool> ImportTypeAsync(string importPath, string? groupPath = null) =>
            ImportSingleAsync(importPath, groupPath, isType: true);

        public Task<ImportResult> ImportBlocksAsync(string importDirectory, string? filterRegex = null, string? groupPath = null) =>
            ImportManyAsync(importDirectory, filterRegex, groupPath, isType: false);

        public Task<ImportResult> ImportTypesAsync(string importDirectory, string? filterRegex = null, string? groupPath = null) =>
            ImportManyAsync(importDirectory, filterRegex, groupPath, isType: true);

        private Task<bool> ImportSingleAsync(string importPath, string? groupPath, bool isType) =>
            Task.Run(() =>
            {
                var plcSoftware = _sessionContext.PlcSoftware;
                if (plcSoftware == null) return false;
                try
                {
                    var fileInfo = new FileInfo(importPath);
                    if (!fileInfo.Exists)
                    {
                        _logger?.LogError("Import file not found: {ImportPath}", importPath);
                        return false;
                    }
                    var root = isType ? TiaReflectionHelper.GetTypeGroup(plcSoftware) : TiaReflectionHelper.GetBlockGroup(plcSoftware);
                    if (root == null) return false;
                    var targetGroup = string.IsNullOrEmpty(groupPath)
                        ? root
                        : isType
                            ? TiaReflectionHelper.GetTypeGroupByPath(root, groupPath)
                            : TiaReflectionHelper.GetBlockGroupByPath(root, groupPath);
                    if (targetGroup == null)
                    {
                        _logger?.LogError(isType ? "Type group not found: {GroupPath}" : "Block group not found: {GroupPath}", groupPath ?? "root");
                        return false;
                    }
                    var collection = TiaReflectionHelper.GetProperty(targetGroup, isType ? "Types" : "Blocks");
                    if (collection == null) return false;
                    if (!_documents.TryImport(collection, fileInfo, 1, out var importResult, out var failureReason))
                    {
                        _logger?.LogError(isType
                            ? "Could not import type from {ImportPath}: {FailureReason}"
                            : "Could not import block from {ImportPath}: {FailureReason}",
                            importPath,
                            failureReason ?? "No supported import overload succeeded");
                        return false;
                    }
                    return importResult is IEnumerable values && values.Cast<object>().Any();
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, isType ? "Error importing type from {ImportPath}" : "Error importing block from {ImportPath}", importPath);
                    return false;
                }
            });

        private async Task<ImportResult> ImportManyAsync(string importDirectory, string? filterRegex, string? groupPath, bool isType)
        {
            var result = new ImportResult();
            if (_sessionContext.PlcSoftware == null || string.IsNullOrEmpty(_sessionContext.CurrentPlcName))
            {
                _logger?.LogWarning("No PLC software is currently open");
                result.Errors.Add("No PLC software is currently open");
                return result;
            }
            _logger?.LogInformation(isType
                ? "Importing types: filter='{Filter}', directory='{Directory}'"
                : "Importing blocks: filter='{Filter}', directory='{Directory}'", filterRegex ?? "all", importDirectory);
            var directory = new DirectoryInfo(importDirectory);
            if (!directory.Exists)
            {
                _logger?.LogError("Import directory not found: {ImportDirectory}", importDirectory);
                result.Errors.Add($"Import directory not found: {importDirectory}");
                return result;
            }
            var regex = PlcTransferRules.TryCreateRegex(filterRegex, result.Errors);
            if (!string.IsNullOrEmpty(filterRegex) && regex == null) return result;
            foreach (var file in directory.GetFiles("*.xml"))
            {
                if (regex != null && !regex.IsMatch(Path.GetFileNameWithoutExtension(file.Name))) continue;
                result.TotalCount++;
                var success = await ImportSingleAsync(file.FullName, groupPath, isType).ConfigureAwait(false);
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
            _logger?.LogInformation(isType
                ? "Import types complete: {SuccessCount} succeeded, {FailedCount} failed"
                : "Import blocks complete: {SuccessCount} succeeded, {FailedCount} failed", result.SuccessCount, result.FailedCount);
            return result;
        }
    }
}
