using OpenTiaToolkit.Core.Models;
using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
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
        private readonly EngineeringSessionContext _sessionContext;
        private readonly V21OpennessAdapter _v21Adapter;
        private readonly PlcBrowseService _browseService;
        private readonly PlcCompileService _compileService;
        private readonly PlcVerificationService _verificationService;
        private readonly PlcBlockMutationService _blockMutationService;
        private readonly PlcMemberRenameService _memberRenameService;
        private readonly PlcImportExportService _importExportService;
        private readonly PlcTagService _tagService;
        private readonly PlcSurfaceService _surfaceService;
        private readonly ILogger<PlcService>? _logger;

        private object? _plcSoftware
        {
            get => _sessionContext.PlcSoftware;
            set => _sessionContext.PlcSoftware = value;
        }

        private Device? _currentDevice
        {
            get => _sessionContext.CurrentPlcDevice;
            set => _sessionContext.CurrentPlcDevice = value;
        }

        private DeviceItem? _currentDeviceItem
        {
            get => _sessionContext.CurrentPlcDeviceItem;
            set => _sessionContext.CurrentPlcDeviceItem = value;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PlcService"/> class.
        /// </summary>
        /// <param name="portalService">The portal service instance.</param>
        /// <param name="logger">Optional logger for diagnostic output.</param>
        public PlcService(IPortalService portalService, ILogger<PlcService>? logger = null)
            : this(portalService, ResolveSessionContext(portalService), logger)
        {
        }

        internal PlcService(IPortalService portalService, EngineeringSessionContext sessionContext, ILogger<PlcService>? logger = null)
        {
            _portalService = portalService;
            _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
            _logger = logger;
            _v21Adapter = new V21OpennessAdapter();
            _browseService = new PlcBrowseService(_sessionContext, _logger);
            _compileService = new PlcCompileService(_sessionContext, _logger, _v21Adapter);
            _verificationService = new PlcVerificationService(_sessionContext, _logger, _v21Adapter);
            _blockMutationService = new PlcBlockMutationService(_sessionContext, _browseService, _logger, _v21Adapter);
            _memberRenameService = new PlcMemberRenameService(_sessionContext, _logger, _v21Adapter);
            _importExportService = new PlcImportExportService(_sessionContext, _portalService, _browseService, _logger, _v21Adapter);
            _tagService = new PlcTagService(_sessionContext, _logger, _v21Adapter);
            _surfaceService = new PlcSurfaceService(_sessionContext, _browseService, _logger, _v21Adapter);
        }

        private static EngineeringSessionContext ResolveSessionContext(IPortalService portalService)
        {
            if (portalService is IEngineeringSessionContextAccessor accessor)
            {
                return accessor.SessionContext;
            }

            throw new ArgumentException(
                "The portal service must expose the shared engineering session context. Use PortalService or the DI constructor that accepts EngineeringSessionContext.",
                nameof(portalService));
        }

        /// <inheritdoc/>
        public bool IsSoftwareOpen => _plcSoftware != null;

        /// <inheritdoc/>
        public string? CurrentPlcName
        {
            get => _sessionContext.CurrentPlcName;
            private set => _sessionContext.CurrentPlcName = value;
        }

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

                    object? plcSoftware = null;

                    var deviceItemsToSearch = foundDeviceItem != null
                        ? new[] { foundDeviceItem }
                        : (System.Collections.IEnumerable)device.DeviceItems;

                    foreach (DeviceItem deviceItem in deviceItemsToSearch)
                    {
                        try
                        {
                            plcSoftware = _v21Adapter.GetPlcSoftware(deviceItem);
                            if (plcSoftware != null)
                            {
                                _currentDevice = device;
                                _currentDeviceItem = deviceItem;
                                break;
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
        public async Task<SafetyLoginResult> LoginToSafetyOfflineProgramAsync(string password)
        {
            return await Task.Run(() =>
            {
                if (string.IsNullOrEmpty(password))
                {
                    return new SafetyLoginResult(
                        success: false,
                        serviceAvailable: false,
                        passwordRequired: true,
                        alreadyLoggedIn: false,
                        message: "Safety password is empty.");
                }

                if (_currentDeviceItem == null)
                {
                    return new SafetyLoginResult(
                        success: false,
                        serviceAvailable: false,
                        passwordRequired: false,
                        alreadyLoggedIn: false,
                        message: "No PLC device item is currently open. Open the PLC before logging into the safety program.");
                }

                _logger?.LogInformation("Attempting safety offline program login for PLC {PlcName}", CurrentPlcName);
                return _v21Adapter.LoginToSafetyOfflineProgram(_currentDeviceItem, password);
            });
        }

        /// <inheritdoc/>
        public async Task<bool> ClosePlcAsync()
        {
            return await Task.Run(() =>
            {
                _logger?.LogInformation("Closing PLC software");

                _sessionContext.ClearPlcSelection();

                return true;
            });
        }

        /// <inheritdoc/>
        public async Task<SoftwareTreeNode?> GetSoftwareTreeAsync(int? maxDepth = null)
        {
            return await _browseService.GetSoftwareTreeAsync(maxDepth);
        }

        /// <inheritdoc/>
        public async Task<CompilationResult> CompileSoftwareAsync(string? password = null)
        {
            return await _compileService.CompileSoftwareAsync(password);
        }

        /// <inheritdoc/>
        public async Task<PlcSoftwareCompareResult> CompareSoftwareToOnlineAsync()
        {
            return await _verificationService.CompareSoftwareToOnlineAsync();
        }

        /// <inheritdoc/>
        public async Task<PlcSoftwareDownloadResult> DownloadSoftwareAsync(bool toSimulator, bool dryRun = true, bool confirm = false, bool onlyChanges = false)
        {
            return await _verificationService.DownloadSoftwareAsync(toSimulator, dryRun, confirm, onlyChanges);
        }

        /// <inheritdoc/>
        public async Task<PlcOnlineStatusResult> GetOnlineStatusAsync(bool goOnline = true)
        {
            return await _verificationService.GetOnlineStatusAsync(goOnline);
        }

        /// <inheritdoc/>
        public async Task<PlcCpuControlResult> StartCpuAsync(bool confirm = false)
        {
            return await _verificationService.ControlCpuAsync("start", confirm);
        }

        /// <inheritdoc/>
        public async Task<PlcCpuControlResult> StopCpuAsync(bool confirm = false)
        {
            return await _verificationService.ControlCpuAsync("stop", confirm);
        }

        /// <inheritdoc/>
        public async Task<PlcTagExportResult> ExportTagsAsync(string culture = "en-US")
        {
            return await _surfaceService.ExportTagsAsync(culture);
        }

        /// <inheritdoc/>
        public async Task<PlcCrossReferenceResult> GetCrossReferencesAsync(string name)
        {
            return await _surfaceService.GetCrossReferencesAsync(name);
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<PlcSurfaceItem>> ListExternalSourcesAsync()
        {
            return await _surfaceService.ListExternalSourcesAsync();
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<PlcSurfaceItem>> ListWatchTablesAsync()
        {
            return await _surfaceService.ListWatchTablesAsync();
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<PlcSurfaceItem>> ListTechnologyObjectsAsync()
        {
            return await _surfaceService.ListTechnologyObjectsAsync();
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<PlcSurfaceItem>> ListPlcAlarmTextListsAsync()
        {
            return await _surfaceService.ListPlcAlarmTextListsAsync();
        }

        /// <inheritdoc/>
        public async Task<PlcSourceOperationResult> ImportSourceAsync(string sourcePath, bool dryRun = true, bool confirm = false)
        {
            return await _surfaceService.ImportSourceAsync(sourcePath, dryRun, confirm);
        }

        /// <inheritdoc/>
        public async Task<PlcSourceOperationResult> GenerateBlocksFromSourceAsync(string sourceName, bool dryRun = true, bool confirm = false)
        {
            return await _surfaceService.GenerateBlocksFromSourceAsync(sourceName, dryRun, confirm);
        }

        /// <inheritdoc/>
        public async Task<PlcSourceOperationResult> ExportBlockAsSourceAsync(string blockName, string outputPath)
        {
            return await _surfaceService.ExportBlockAsSourceAsync(blockName, outputPath);
        }

        /// <inheritdoc/>
        public async Task<bool> ExportPlcAsync(string exportPath)
        {
            return await _importExportService.ExportPlcAsync(exportPath);
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<BlockInfo>> GetBlocksAsync(string? filterRegex = null)
        {
            return await _browseService.GetBlocksAsync(filterRegex);
        }

        /// <inheritdoc/>
        public async Task<bool> DeleteBlocksAsync(string filterRegex, bool confirm = false)
        {
            return await _blockMutationService.DeleteBlocksAsync(filterRegex, confirm);
        }

        /// <inheritdoc/>
        public async Task<BlockInfoDetail?> GetBlockInfoAsync(string blockName)
        {
            return await _browseService.GetBlockInfoAsync(blockName);
        }

        /// <inheritdoc/>
        public async Task<bool> ExportBlockAsync(string blockName, string exportPath)
        {
            return await _importExportService.ExportBlockAsync(blockName, exportPath);
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<BlockInfo>> SearchBlocksAsync(string pattern, string searchIn = "name", string? blockType = null)
        {
            return await _browseService.SearchBlocksAsync(pattern, searchIn, blockType);
        }

        /// <inheritdoc/>
        public async Task<bool> CreateBlockAsync(string blockName, string blockType, string? programmingLanguage = null)
        {
            return await _blockMutationService.CreateBlockAsync(blockName, blockType, programmingLanguage);
        }

        /// <inheritdoc/>
        public async Task<FindReplaceResult> FindAndReplaceTagsAsync(
            Dictionary<string, string> renameMap,
            bool caseInsensitive = false,
            bool dryRun = true,
            bool substringMatch = false)
        {
            return await _tagService.FindAndReplaceTagsAsync(renameMap, caseInsensitive, dryRun, substringMatch);
        }

        /// <inheritdoc/>
        public async Task<FindReplaceResult> FindAndReplaceDbMembersAsync(
            Dictionary<string, string> renameMap,
            bool caseInsensitive = false,
            bool dryRun = true,
            string[]? targetBlocks = null)
        {
            return await _memberRenameService.FindAndReplaceDbMembersAsync(renameMap, caseInsensitive, dryRun, targetBlocks);
        }

        /// <inheritdoc/>
        public async Task<FindReplaceResult> FindAndReplaceUdtMembersAsync(
            Dictionary<string, string> renameMap,
            bool caseInsensitive = false,
            bool dryRun = true,
            string[]? targetTypes = null)
        {
            return await _memberRenameService.FindAndReplaceUdtMembersAsync(renameMap, caseInsensitive, dryRun, targetTypes);
        }

        /// <inheritdoc/>
        public async Task<TagModificationResult> ModifyTagsAsync(
            IReadOnlyList<TagModificationOperation> operations,
            bool dryRun = true)
        {
            return await _tagService.ModifyTagsAsync(operations, dryRun);
        }

        #region SIMATIC SD Export and Type Export

        /// <inheritdoc/>
        public async Task<bool> ExportBlockAsDocumentsAsync(string blockName, string exportPath)
        {
            return await _importExportService.ExportBlockAsDocumentsAsync(blockName, exportPath);
        }

        /// <inheritdoc/>
        public async Task<ExportResult> ExportBlocksAsDocumentsAsync(string? filterRegex, string exportPath)
        {
            return await _importExportService.ExportBlocksAsDocumentsAsync(filterRegex, exportPath);
        }

        /// <inheritdoc/>
        public async Task<bool> ExportTypeAsync(string typeName, string exportPath)
        {
            return await _importExportService.ExportTypeAsync(typeName, exportPath);
        }

        /// <inheritdoc/>
        public async Task<ExportResult> ExportTypesAsync(string? filterRegex, string exportPath)
        {
            return await _importExportService.ExportTypesAsync(filterRegex, exportPath);
        }

        #endregion

        #region Import Operations

        /// <inheritdoc/>
        public async Task<bool> ImportBlockAsync(string importPath, string? groupPath = null)
        {
            return await _importExportService.ImportBlockAsync(importPath, groupPath);
        }

        /// <inheritdoc/>
        public async Task<ImportResult> ImportBlocksAsync(string importDirectory, string? filterRegex = null, string? groupPath = null)
        {
            return await _importExportService.ImportBlocksAsync(importDirectory, filterRegex, groupPath);
        }

        /// <inheritdoc/>
        public async Task<bool> ImportTypeAsync(string importPath, string? groupPath = null)
        {
            return await _importExportService.ImportTypeAsync(importPath, groupPath);
        }

        /// <inheritdoc/>
        public async Task<ImportResult> ImportTypesAsync(string importDirectory, string? filterRegex = null, string? groupPath = null)
        {
            return await _importExportService.ImportTypesAsync(importDirectory, filterRegex, groupPath);
        }

        /// <inheritdoc/>
        public async Task<bool> ImportBlockFromDocumentsAsync(string importDirectory, string fileNameWithoutExtension, string? groupPath = null, string importOption = "Override")
        {
            return await _importExportService.ImportBlockFromDocumentsAsync(importDirectory, fileNameWithoutExtension, groupPath, importOption);
        }

        /// <inheritdoc/>
        public async Task<ImportResult> ImportBlocksFromDocumentsAsync(string importDirectory, string? filterRegex = null, string? groupPath = null, string importOption = "Override")
        {
            return await _importExportService.ImportBlocksFromDocumentsAsync(importDirectory, filterRegex, groupPath, importOption);
        }

        #endregion
    }
}
