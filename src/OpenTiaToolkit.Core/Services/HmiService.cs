using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands.Hmi;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Services
{
    /// <summary>
    /// Provides Unified HMI operations over the TIA Openness API.
    /// </summary>
    public sealed class HmiService : IHmiService
    {
        static HmiService()
        {
            TiaPortalAssemblyResolver.Register();
        }

        private readonly IPortalService _portalService;
        private readonly EngineeringSessionContext _sessionContext;
        private readonly HmiBrowseService _browseService;
        private readonly HmiMutationService _mutationService;
        private readonly HmiCopyService _copyService;
        private readonly HmiTrendService _trendService;
        private readonly HmiJsonExportService _jsonExportService;
        private readonly HmiJsonDiffService _jsonDiffService;
        private readonly HmiJsonImportService _jsonImportService;
        private readonly HmiScreenJsonEditService _screenJsonEditService;
        private readonly V21UnifiedHmiAdapter _adapter;
        private readonly ILogger<HmiService>? _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="HmiService"/> class.
        /// </summary>
        /// <param name="portalService">The portal service instance.</param>
        /// <param name="logger">Optional logger for diagnostic output.</param>
        public HmiService(IPortalService portalService, ILogger<HmiService>? logger = null)
            : this(portalService, ResolveSessionContext(portalService), logger)
        {
        }

        internal HmiService(IPortalService portalService, EngineeringSessionContext sessionContext, ILogger<HmiService>? logger = null)
        {
            _portalService = portalService ?? throw new ArgumentNullException(nameof(portalService));
            _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
            _logger = logger;
            _adapter = new V21UnifiedHmiAdapter(logger);
            _browseService = new HmiBrowseService(_sessionContext, _adapter, logger);
            _mutationService = new HmiMutationService(_sessionContext, _adapter, logger);
            _copyService = new HmiCopyService(_sessionContext, _adapter, logger);
            _trendService = new HmiTrendService(_sessionContext, _adapter, logger);
            _jsonExportService = new HmiJsonExportService(_sessionContext, _adapter, logger);
            _jsonDiffService = new HmiJsonDiffService();
            _jsonImportService = new HmiJsonImportService(_sessionContext, _adapter, _jsonExportService, logger);
            _screenJsonEditService = new HmiScreenJsonEditService(_sessionContext, _jsonExportService, _jsonDiffService, _jsonImportService, logger);
        }

        /// <inheritdoc/>
        public bool IsHmiOpen => _sessionContext.HmiSoftware != null;

        /// <inheritdoc/>
        public string? CurrentHmiName => _sessionContext.CurrentHmiName;

        /// <inheritdoc/>
        public async Task<bool> OpenUnifiedHmiAsync(string hmiName)
        {
            return await Task.Run(() =>
            {
                if (string.IsNullOrWhiteSpace(hmiName))
                {
                    _logger?.LogWarning("Cannot open Unified HMI with an empty name");
                    return false;
                }

                if (_portalService.Project == null)
                {
                    _logger?.LogWarning("No project is currently open");
                    return false;
                }

                if (IsHmiOpen && string.Equals(CurrentHmiName, hmiName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (!_adapter.TryResolveUnifiedHmi(_portalService.Project, hmiName, out var device, out var _, out var software) ||
                    device == null ||
                    software == null)
                {
                    _logger?.LogWarning("Unified HMI not found: {HmiName}", hmiName);
                    return false;
                }

                _sessionContext.ClearHmiSelection();
                _sessionContext.CurrentHmiDevice = device;
                _sessionContext.CurrentHmiName = hmiName;
                _sessionContext.HmiSoftware = software;

                _logger?.LogInformation("Unified HMI opened successfully: {HmiName}", hmiName);
                return true;
            });
        }

        /// <inheritdoc/>
        public async Task<bool> CloseUnifiedHmiAsync()
        {
            return await Task.Run(() =>
            {
                _sessionContext.ClearHmiSelection();
                return true;
            });
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<HmiScreenInfo>> GetScreensAsync(string? groupPath = null, bool recursive = true)
        {
            return await _browseService.GetScreensAsync(groupPath, recursive);
        }

        /// <inheritdoc/>
        public async Task<HmiScreenInfo?> GetScreenInfoAsync(string screenPath)
        {
            return await _browseService.GetScreenInfoAsync(screenPath);
        }

        /// <inheritdoc/>
        public async Task<HmiScreenInfo?> CreateScreenAsync(string screenPath, int? width = null, int? height = null)
        {
            return await _mutationService.CreateScreenAsync(screenPath, width, height);
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<HmiItemTypeInfo>> GetSupportedItemTypesAsync(string? filter = null)
        {
            return await _browseService.GetSupportedItemTypesAsync(filter);
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<HmiTagInfo>> GetTagsAsync(string? filter = null, string? rootPath = null)
        {
            return await _browseService.GetTagsAsync(filter, rootPath);
        }

        /// <inheritdoc/>
        public async Task<HmiScreenItemInfo?> CreateScreenItemAsync(
            string screenPath,
            string itemType,
            string itemName,
            int? left = null,
            int? top = null,
            int? width = null,
            int? height = null)
        {
            return await _mutationService.CreateScreenItemAsync(screenPath, itemType, itemName, left, top, width, height);
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<HmiItemPropertyInfo>> GetItemPropertiesAsync(
            string screenPath,
            string itemName,
            string[]? propertyNames = null)
        {
            return await _browseService.GetItemPropertiesAsync(screenPath, itemName, propertyNames);
        }

        /// <inheritdoc/>
        public async Task<bool> SetItemPropertyAsync(string screenPath, string itemName, string propertyName, string value)
        {
            return await _mutationService.SetItemPropertyAsync(screenPath, itemName, propertyName, value);
        }

        /// <inheritdoc/>
        public async Task<HmiScreenCopyResult?> CopyScreenFromTemplateAsync(
            string templateScreenPath,
            string targetScreenPath,
            string? findText = null,
            string? replaceText = null)
        {
            return await _copyService.CopyScreenFromTemplateAsync(templateScreenPath, targetScreenPath, findText, replaceText);
        }

        /// <inheritdoc/>
        public async Task<HmiJsonExportResult?> ExportJsonAsync(string outputPath, HmiJsonFilterOptions? filters = null)
        {
            return await _jsonExportService.ExportAsync(outputPath, filters);
        }

        /// <inheritdoc/>
        public async Task<HmiJsonDiffResult?> DiffJsonAsync(
            string basePath,
            string modifiedPath,
            string patchPath,
            HmiJsonFilterOptions? filters = null)
        {
            return await _jsonDiffService.CreatePatchAsync(basePath, modifiedPath, patchPath, filters);
        }

        /// <inheritdoc/>
        public async Task<HmiJsonImportResult?> ImportJsonAsync(
            string inputPath,
            HmiJsonFilterOptions? filters = null,
            bool dryRun = false,
            HmiJsonImportExecutionOptions? executionOptions = null)
        {
            return await _jsonImportService.ImportAsync(inputPath, filters, dryRun, executionOptions);
        }

        /// <inheritdoc/>
        public async Task<HmiScreenJsonSnapshotResult?> GetScreenJsonAsync(
            string screenPath,
            HmiJsonFilterOptions? filters = null,
            string? workingPath = null)
        {
            return await _screenJsonEditService.GetScreenJsonAsync(screenPath, filters, workingPath);
        }

        /// <inheritdoc/>
        public async Task<HmiFaceplateInterfaceProbeResult?> ProbeFaceplateInterfaceAsync(
            string screenPath,
            string itemName,
            string? propertyName = null,
            bool write = false,
            string? value = null,
            bool restore = true)
        {
            return await Task.Run(() =>
            {
                var software = _sessionContext.HmiSoftware;
                if (software == null)
                {
                    _logger?.LogWarning("No Unified HMI is currently open");
                    return null;
                }

                if (!_adapter.TryGetScreen(software, screenPath, out var screen, out var normalizedPath) || screen == null)
                {
                    _logger?.LogWarning("Unified HMI screen not found: {ScreenPath}", screenPath);
                    return null;
                }

                var item = screen.ScreenItems.Find(itemName);
                if (item == null)
                {
                    _logger?.LogWarning("Unified HMI item not found: {ScreenPath}/{ItemName}", normalizedPath, itemName);
                    return null;
                }

                // Reacquire immediately before write probes so the operation does not rely on an older item reference.
                if (write)
                {
                    if (!_adapter.TryGetScreen(software, normalizedPath, out screen, out _) || screen == null)
                    {
                        _logger?.LogWarning("Unified HMI screen could not be reacquired before probe: {ScreenPath}", normalizedPath);
                        return null;
                    }

                    item = screen.ScreenItems.Find(itemName);
                    if (item == null)
                    {
                        _logger?.LogWarning("Unified HMI item could not be reacquired before probe: {ScreenPath}/{ItemName}", normalizedPath, itemName);
                        return null;
                    }
                }

                return _adapter.ProbeFaceplateInterface(normalizedPath, item, propertyName, write, value, restore);
            });
        }

        /// <inheritdoc/>
        public async Task<HmiScreenJsonEditResult?> ApplyScreenJsonEditAsync(
            string screenPath,
            HmiJsonScreen editedScreen,
            string? baseHash = null,
            HmiJsonFilterOptions? filters = null,
            bool dryRun = true,
            HmiJsonImportExecutionOptions? executionOptions = null,
            string? workingPath = null,
            bool allowStaleBase = false)
        {
            return await _screenJsonEditService.ApplyScreenJsonEditAsync(
                screenPath,
                editedScreen,
                baseHash,
                filters,
                dryRun,
                executionOptions,
                workingPath,
                allowStaleBase);
        }

        /// <inheritdoc/>
        public async Task<HmiScreenJsonEditResult?> ApplyScreenJsonPatchAsync(
            string screenPath,
            IReadOnlyList<HmiScreenJsonPatchOperation> operations,
            string? baseHash = null,
            HmiJsonFilterOptions? filters = null,
            bool dryRun = true,
            HmiJsonImportExecutionOptions? executionOptions = null,
            string? workingPath = null,
            bool allowStaleBase = false)
        {
            return await _screenJsonEditService.ApplyScreenJsonPatchAsync(
                screenPath,
                operations,
                baseHash,
                filters,
                dryRun,
                executionOptions,
                workingPath,
                allowStaleBase);
        }

        /// <inheritdoc/>
        public async Task<HmiTrendInfo?> CreateTrendAsync(
            string tagPath,
            string dataLogName,
            bool cyclic = false,
            string? cycle = null,
            string storageFolder = "/Trends",
            int logMaxSize = 2000,
            int durationDays = 365,
            int segmentMaxSize = 500)
        {
            return await _trendService.CreateTrendAsync(tagPath, dataLogName, cyclic, cycle, storageFolder, logMaxSize, durationDays, segmentMaxSize);
        }

        private static EngineeringSessionContext ResolveSessionContext(IPortalService portalService)
        {
            if (portalService is IEngineeringSessionContextAccessor accessor)
            {
                return accessor.SessionContext;
            }

            return new EngineeringSessionContext();
        }
    }
}
