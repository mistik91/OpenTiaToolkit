using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Services.V21;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Services
{
    /// <summary>
    /// Owns SIMATIC SD document export/import and its V21 compatibility rules.
    /// </summary>
    internal sealed class PlcDocumentTransferService
    {
        private readonly EngineeringSessionContext _sessionContext;
        private readonly IPortalService _portalService;
        private readonly PlcBrowseService _browseService;
        private readonly V21DocumentAccess _documents;
        private readonly ILogger? _logger;

        public PlcDocumentTransferService(
            EngineeringSessionContext sessionContext,
            IPortalService portalService,
            PlcBrowseService browseService,
            ILogger? logger,
            V21DocumentAccess documents)
        {
            _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
            _portalService = portalService ?? throw new ArgumentNullException(nameof(portalService));
            _browseService = browseService ?? throw new ArgumentNullException(nameof(browseService));
            _documents = documents ?? throw new ArgumentNullException(nameof(documents));
            _logger = logger;
        }

        public Task<bool> ExportBlockAsync(string blockName, string exportPath) =>
            Task.Run(() =>
            {
                var plcSoftware = _sessionContext.PlcSoftware;
                if (plcSoftware == null || string.IsNullOrEmpty(_sessionContext.CurrentPlcName))
                {
                    _logger?.LogWarning("No PLC software is currently open");
                    return false;
                }
                _logger?.LogInformation("Exporting block as SIMATIC SD documents: {BlockName} to {ExportPath}", blockName, exportPath);
                EnsureSupported("SIMATIC SD export");
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
                    Directory.CreateDirectory(exportPath);
                    if (!_documents.TryExportAsDocuments(block, new DirectoryInfo(exportPath), blockName, out var state))
                    {
                        _logger?.LogError("ExportAsDocuments returned non-success state or is not available on block type");
                        return false;
                    }
                    var success = string.Equals(state, "Success", StringComparison.OrdinalIgnoreCase);
                    if (success)
                        _logger?.LogInformation("Successfully exported block as SIMATIC SD documents: {BlockName}", blockName);
                    else
                        _logger?.LogError("ExportAsDocuments returned non-success state for block: {BlockName}", blockName);
                    return success;
                }
                catch (InvalidOperationException) { throw; }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to export block as documents: {BlockName}", blockName);
                    return false;
                }
            });

        public async Task<ExportResult> ExportBlocksAsync(string? filterRegex, string exportPath)
        {
            var result = new ExportResult();
            if (_sessionContext.PlcSoftware == null || string.IsNullOrEmpty(_sessionContext.CurrentPlcName))
            {
                _logger?.LogWarning("No PLC software is currently open");
                result.Errors.Add("No PLC software is currently open");
                return result;
            }
            _logger?.LogInformation("Exporting blocks as SIMATIC SD documents: filter='{Filter}', path='{ExportPath}'", filterRegex ?? "all", exportPath);
            try { EnsureSupported("SIMATIC SD export"); }
            catch (InvalidOperationException ex)
            {
                _logger?.LogError(ex, "Version check failed for ExportBlocksAsDocumentsAsync");
                result.Errors.Add(ex.Message);
                return result;
            }
            var regex = PlcTransferRules.TryCreateRegex(filterRegex, result.Errors);
            if (!string.IsNullOrEmpty(filterRegex) && regex == null) return result;
            Directory.CreateDirectory(exportPath);
            var blocks = await _browseService.GetBlocksAsync(filterRegex).ConfigureAwait(false);
            result.TotalCount = blocks.Count;
            foreach (var block in blocks)
            {
                if (regex != null && !regex.IsMatch(block.Name)) continue;
                try
                {
                    // Use the same per-target API as single export; block type alone
                    // does not determine SIMATIC SD export support.
                    if (await ExportBlockAsync(block.Name, exportPath).ConfigureAwait(false))
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
            _logger?.LogInformation("Export blocks as documents complete: {SuccessCount} succeeded, {FailedCount} failed", result.SuccessCount, result.FailedCount);
            return result;
        }

        public Task<bool> ImportBlockAsync(
            string importDirectory,
            string fileNameWithoutExtension,
            string? groupPath = null,
            string importOption = "Override") =>
            Task.Run(() =>
            {
                var plcSoftware = _sessionContext.PlcSoftware;
                if (plcSoftware == null) return false;
                EnsureSupported("Import from documents");
                try
                {
                    var directory = new DirectoryInfo(importDirectory);
                    if (!directory.Exists)
                    {
                        _logger?.LogError("Import directory not found: {ImportDirectory}", importDirectory);
                        return false;
                    }
                    var document = new FileInfo(Path.Combine(importDirectory, $"{fileNameWithoutExtension}.s7dcl"));
                    if (!document.Exists)
                    {
                        _logger?.LogError("Document file not found: {FileName}.s7dcl", fileNameWithoutExtension);
                        return false;
                    }
                    var blockGroup = TiaReflectionHelper.GetBlockGroup(plcSoftware);
                    if (blockGroup == null) return false;
                    var targetGroup = string.IsNullOrEmpty(groupPath)
                        ? blockGroup
                        : TiaReflectionHelper.GetBlockGroupByPath(blockGroup, groupPath);
                    if (targetGroup == null)
                    {
                        _logger?.LogError("Block group not found: {GroupPath}", groupPath ?? "root");
                        return false;
                    }
                    var blocks = TiaReflectionHelper.GetProperty(targetGroup, "Blocks");
                    if (blocks == null) return false;
                    if (!_documents.TryImportFromDocuments(
                        blocks,
                        directory,
                        fileNameWithoutExtension,
                        ParseImportOption(importOption),
                        out var result,
                        out var failureReason))
                    {
                        _logger?.LogError(
                            "Could not import block documents from {ImportDirectory}: {FailureReason}",
                            importDirectory,
                            failureReason ?? "ImportFromDocuments method not found in the current TIA Portal Openness API");
                        return false;
                    }
                    return result != null;
                }
                catch (InvalidOperationException) { throw; }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error importing block from documents in {ImportDirectory}", importDirectory);
                    return false;
                }
            });

        public async Task<ImportResult> ImportBlocksAsync(
            string importDirectory,
            string? filterRegex = null,
            string? groupPath = null,
            string importOption = "Override")
        {
            var result = new ImportResult();
            if (_sessionContext.PlcSoftware == null || string.IsNullOrEmpty(_sessionContext.CurrentPlcName))
            {
                _logger?.LogWarning("No PLC software is currently open");
                result.Errors.Add("No PLC software is currently open");
                return result;
            }
            _logger?.LogInformation("Importing blocks from documents: filter='{Filter}', directory='{Directory}'", filterRegex ?? "all", importDirectory);
            try { EnsureSupported("Import from documents"); }
            catch (InvalidOperationException ex)
            {
                _logger?.LogError(ex, "Version check failed for ImportBlocksFromDocumentsAsync");
                result.Errors.Add(ex.Message);
                return result;
            }
            var directory = new DirectoryInfo(importDirectory);
            if (!directory.Exists)
            {
                _logger?.LogError("Import directory not found: {ImportDirectory}", importDirectory);
                result.Errors.Add($"Import directory not found: {importDirectory}");
                return result;
            }
            var regex = PlcTransferRules.TryCreateRegex(filterRegex, result.Errors);
            if (!string.IsNullOrEmpty(filterRegex) && regex == null) return result;
            foreach (var file in directory.GetFiles("*.s7dcl"))
            {
                var blockName = Path.GetFileNameWithoutExtension(file.Name);
                if (regex != null && !regex.IsMatch(blockName)) continue;
                result.TotalCount++;
                try
                {
                    if (await ImportBlockAsync(importDirectory, blockName, groupPath, importOption).ConfigureAwait(false))
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
            _logger?.LogInformation("Import blocks from documents complete: {SuccessCount} succeeded, {FailedCount} failed", result.SuccessCount, result.FailedCount);
            return result;
        }

        private void EnsureSupported(string operation)
        {
            var tiaVersion = TiaReflectionHelper.ParseTiaVersion(_portalService.Version);
            if (tiaVersion == null || tiaVersion.Major < 21)
                throw new InvalidOperationException($"{operation} requires a supported TIA Portal version. Current version: {_portalService.Version ?? "Unknown"}");
        }

        private static int ParseImportOption(string importOption) =>
            importOption.ToLowerInvariant() switch
            {
                "none" => 0,
                "override" => 1,
                "skipinactivecultures" => 2,
                "activateinactivecultures" => 4,
                _ => 1
            };
    }
}
