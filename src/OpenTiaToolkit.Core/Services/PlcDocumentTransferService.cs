using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Services.V21;
using System;
using System.IO;
using System.Linq;
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
        private readonly V21DocumentAccess _documents;
        private readonly ILogger? _logger;

        public PlcDocumentTransferService(
            EngineeringSessionContext sessionContext,
            IPortalService portalService,
            ILogger? logger,
            V21DocumentAccess documents)
        {
            _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
            _portalService = portalService ?? throw new ArgumentNullException(nameof(portalService));
            _documents = documents ?? throw new ArgumentNullException(nameof(documents));
            _logger = logger;
        }

        public Task<PlcDocumentExportResult> ExportBlockAsync(string blockName, string exportPath) =>
            Task.Run(() =>
            {
                var software = _sessionContext.PlcSoftware;
                if (software == null || string.IsNullOrEmpty(_sessionContext.CurrentPlcName))
                    return new PlcDocumentExportResult { Message = "No PLC software is currently open." };

                EnsureSupported("SIMATIC SD export");
                try
                {
                    var root = TiaReflectionHelper.GetBlockGroup(software);
                    if (root == null) return new PlcDocumentExportResult { Message = "Could not get BlockGroup." };
                    var block = PlcTransferRules.EnumerateBlocks(root).FirstOrDefault(candidate =>
                        string.Equals(TiaReflectionHelper.GetName(candidate), blockName, StringComparison.OrdinalIgnoreCase));
                    if (block == null) return new PlcDocumentExportResult { Message = $"PLC block was not found: {blockName}" };
                    var directory = new DirectoryInfo(Path.GetFullPath(exportPath));
                    directory.Create();
                    return ExportBlock(block, blockName, directory);
                }
                catch (Exception ex)
                {
                    return ExportFailure(blockName, ex);
                }
            });

        private PlcDocumentExportResult ExportBlock(object block, string name, DirectoryInfo directory)
        {
            try { return _documents.ExportWithResult(block, directory, name); }
            catch (Exception ex) { return ExportFailure(name, ex); }
        }

        private PlcDocumentExportResult ExportFailure(string name, Exception exception)
        {
            _logger?.LogError(exception, "Failed to export block as documents: {BlockName}", name);
            var detail = EngineeringFailure.Describe(exception);
            return new PlcDocumentExportResult
            {
                Diagnostics = new[] { detail },
                Message = $"Failed to export '{name}' as SIMATIC SD documents. {detail}"
            };
        }

        public Task<ExportResult> ExportBlocksAsync(string? filterRegex, string exportPath) => Task.Run(() =>
        {
            var result = new ExportResult();
            var software = _sessionContext.PlcSoftware;
            if (software == null || string.IsNullOrEmpty(_sessionContext.CurrentPlcName))
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
            var root = TiaReflectionHelper.GetBlockGroup(software);
            if (root == null) { result.Errors.Add("Could not get BlockGroup"); return result; }
            var targets = PlcTransferRules.EnumerateBlocks(root)
                .Select(block => (Block: block, Name: TiaReflectionHelper.GetName(block) ?? "Unknown"))
                .Where(target => regex == null || regex.IsMatch(target.Name)).ToArray();
            var collisions = targets.GroupBy(target => target.Name, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Directory.CreateDirectory(exportPath);
            var directory = new DirectoryInfo(exportPath);
            result.TotalCount = targets.Length;
            foreach (var target in targets)
            {
                var name = target.Name;
                try
                {
                    if (collisions.Contains(name)) throw new InvalidOperationException($"Multiple selected blocks share the export filename '{name}'; no documents were written for that name.");
                    // Export the captured object, without metadata projection or another name search.
                    var item = ExportBlock(target.Block, name, directory);
                    if (item.Success)
                    {
                        result.SuccessCount++;
                        result.ExportedItems.Add(name);
                    }
                    else
                    {
                        result.FailedCount++;
                        result.FailedItems.Add(name);
                        result.Errors.Add(item.Message);
                    }
                }
                catch (Exception ex)
                {
                    result.FailedCount++;
                    result.FailedItems.Add(name);
                    result.Errors.Add($"Error exporting {name}: {ex.Message}");
                }
            }
            _logger?.LogInformation("Export blocks as documents complete: {SuccessCount} succeeded, {FailedCount} failed", result.SuccessCount, result.FailedCount);
            return result;
        });

        public Task<PlcDocumentImportResult> ImportBlockAsync(string importDirectory, string name, string? groupPath, string importOption, bool verify) =>
            Task.Run(() =>
            {
                var plcSoftware = _sessionContext.PlcSoftware;
                if (plcSoftware == null) return new PlcDocumentImportResult { Message = "No PLC software is currently open." };
                EnsureSupported("Import from documents");
                var outcome = new PlcDocumentImportResult();
                try
                {
                    var directory = new DirectoryInfo(Path.GetFullPath(importDirectory));
                    var input = Path.Combine(directory.FullName, name + ".s7dcl");
                    if (!File.Exists(input))
                    {
                        outcome.Message = $"Document file was not found: {input}";
                        return outcome;
                    }

                    var blockGroup = TiaReflectionHelper.GetBlockGroup(plcSoftware);
                    var targetGroup = blockGroup == null || string.IsNullOrEmpty(groupPath)
                        ? blockGroup
                        : TiaReflectionHelper.GetBlockGroupByPath(blockGroup, groupPath);
                    var blocks = TiaReflectionHelper.GetProperty(targetGroup, "Blocks");
                    if (blocks == null)
                    {
                        outcome.Message = $"Could not open PLC block group '{groupPath ?? string.Empty}'.";
                        return outcome;
                    }

                    string? expected = verify ? PlcDocumentReadBack.Fingerprint(directory.FullName, name) : null;
                    string? before = verify ? PlcDocumentReadBack.Capture(_documents, targetGroup!, name, _logger) : null;
                    if (!_documents.TryImportFromDocuments(blocks, directory, name, ParseImportOption(importOption), out var result, out var failure))
                    {
                        if (!string.IsNullOrWhiteSpace(failure)) outcome.Diagnostics = new[] { failure! };
                        outcome.Message = $"TIA Portal rejected the document import for '{name}'. {failure}".TrimEnd();
                        return outcome;
                    }

                    outcome.SiemensState = TiaReflectionHelper.GetProperty(result, "State")?.ToString();
                    outcome.Diagnostics = V21DocumentMessages.Read(result);
                    outcome.Success = string.Equals(outcome.SiemensState, "Success", StringComparison.OrdinalIgnoreCase);
                    if (!outcome.Success)
                    {
                        string state = outcome.SiemensState ?? "no result state";
                        outcome.Message = $"TIA Portal did not complete the document import for '{name}' ({state}).";
                        if (outcome.Diagnostics.Count > 0) outcome.Message += " " + string.Join(" ", outcome.Diagnostics);
                        return outcome;
                    }

                    if (!verify) return outcome;
                    string? after = PlcDocumentReadBack.Capture(_documents, targetGroup!, name, _logger);
                    PlcDocumentReadBack.Compare(outcome, name, expected, before, after);
                    return outcome;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error importing block from documents in {ImportDirectory}", importDirectory);
                    outcome.Message = $"Could not complete the document import for '{name}'. {EngineeringFailure.Describe(ex)}";
                    return outcome;
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
                    var item = await ImportBlockAsync(importDirectory, blockName, groupPath, importOption, verify: false).ConfigureAwait(false);
                    if (item.Success)
                    {
                        result.SuccessCount++;
                        result.ImportedItems.Add(blockName);
                    }
                    else
                    {
                        result.FailedCount++;
                        result.FailedItems.Add(blockName);
                        result.Errors.Add(item.Message);
                    }
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
