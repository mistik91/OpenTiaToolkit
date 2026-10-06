using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Services.Hardware;
using OpenTiaToolkit.Core.Services.Hardware.Patching;
using OpenTiaToolkit.Core.Services.PlcTags;
using OpenTiaToolkit.Core.Services.V21;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Services
{
    /// <summary>
    /// Compatibility facade for vendor-neutral hardware operations against the selected TIA project.
    /// </summary>
    public sealed class HardwareService : IHardwareService
    {
        private readonly EngineeringSessionContext _sessionContext;
        private readonly HardwareCatalogCapabilityReader _catalog;
        private readonly HardwareTopologyReader _topology;
        private readonly HardwareDriveReader _drives;
        private readonly HardwareTagMappingAuditWorkflow _audit;
        private readonly HardwarePatchWorkflow _patch;

        internal HardwareService(
            IPortalService portalService,
            EngineeringSessionContext sessionContext,
            V21HardwareCatalogAccess catalog,
            V21HardwareAttributeAccess attributes,
            V21HardwareDriveAccess drives,
            V21HardwareNetworkAccess networks,
            V21SoftwareDiscovery software,
            IPlcTagRepository tagRepository,
            ILogger<HardwareService>? logger = null)
        {
            _ = portalService ?? throw new ArgumentNullException(nameof(portalService));
            _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
            _ = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _ = attributes ?? throw new ArgumentNullException(nameof(attributes));
            _ = drives ?? throw new ArgumentNullException(nameof(drives));
            _ = networks ?? throw new ArgumentNullException(nameof(networks));
            _ = software ?? throw new ArgumentNullException(nameof(software));
            _ = tagRepository ?? throw new ArgumentNullException(nameof(tagRepository));
            var engineering = new HardwareEngineeringContext(_sessionContext);
            var resolver = new HardwareTargetResolver(engineering);
            _topology = new HardwareTopologyReader(engineering, attributes, networks);
            _drives = new HardwareDriveReader(engineering, drives);
            _catalog = new HardwareCatalogCapabilityReader(engineering, resolver, catalog, attributes);
            _audit = new HardwareTagMappingAuditWorkflow(
                engineering, _topology, _drives, catalog, software, tagRepository, logger);
            _patch = new HardwarePatchWorkflow(engineering, attributes, drives, networks, resolver, _topology, logger);
        }

        public Task<IReadOnlyList<HardwareCatalogItemInfo>> FindCatalogItemsAsync(string searchText, string? culture = null) =>
            Task.Run(() => _catalog.FindCatalogItems(searchText, culture));

        public Task<IReadOnlyList<HardwareDeviceTreeInfo>> GetHardwareTreeAsync(string? deviceName = null) =>
            Task.Run(() => _topology.GetHardwareTree(deviceName));

        public Task<IReadOnlyList<HardwareIoAddressInfo>> GetIoAddressesAsync(string? deviceName = null, string[]? attributes = null) =>
            Task.Run(() => _topology.GetIoAddresses(deviceName, attributes));

        public Task<IReadOnlyList<HardwareDriveTelegramInfo>> GetDriveTelegramsAsync(string? deviceName = null, string[]? addressAttributes = null) =>
            Task.Run(() => _drives.GetDriveTelegrams(deviceName, addressAttributes));

        public Task<IReadOnlyList<HardwareDriveParameterInfo>> GetDriveParametersAsync(
            string? deviceName = null, int? driveObjectIndex = null, string? filter = null, int maxResults = 500) =>
            Task.Run(() => _drives.GetDriveParameters(deviceName, driveObjectIndex, filter, maxResults));

        public Task<HardwareTagMappingAuditResult> AuditHardwareTagMappingsAsync(
            string plcName, string? tagTableFilter = null, string? catalogSearchText = null, int maxUnmatchedTags = 100) =>
            Task.Run(() => _audit.Audit(plcName, tagTableFilter, catalogSearchText, maxUnmatchedTags));

        public Task<HardwareCapabilityInfo?> GetCapabilitiesAsync(HardwareObjectRef target, string[]? attributes = null) =>
            Task.Run(() => _catalog.GetCapabilities(target, attributes));

        public Task<HardwareFeatureAttributesInfo> GetFeatureAttributesAsync(HardwareObjectRef target, string featureService, string[]? attributes = null) =>
            Task.Run(() => _catalog.GetFeatureAttributes(target, featureService, attributes));

        public Task<HardwareNetworkItemAttributesInfo> GetNetworkItemAttributesAsync(
            HardwareObjectRef target, string collectionName, int index, string[]? attributes = null) =>
            Task.Run(() => _catalog.GetNetworkItemAttributes(target, collectionName, index, attributes));

        public Task<HardwarePlugCheckInfo> CanPlugAsync(HardwareObjectRef target, string typeIdentifier, string itemName, int positionNumber) =>
            Task.Run(() => _catalog.CanPlug(target, typeIdentifier, itemName, positionNumber));

        public Task<IReadOnlyList<string>> FindModuleTypesAsync(string partialTypeIdentifier) =>
            Task.Run(() => _catalog.FindModuleTypes(partialTypeIdentifier));

        public Task<IReadOnlyList<HardwareNetworkInfo>> GetNetworksAsync(string? deviceName = null) =>
            Task.Run(() => _topology.GetNetworks(deviceName));

        public Task<HardwareGsdParameterData?> GetGsdParameterDataAsync(HardwareObjectRef target, int datasetNumber, int byteOffset, int length) =>
            Task.Run(() => _catalog.GetGsdParameterData(target, datasetNumber, byteOffset, length));

        public Task<HardwarePatchResult> ApplyPatchAsync(
            IReadOnlyList<HardwarePatchOperation> operations,
            bool dryRun = true,
            bool confirmDestructive = false,
            string? transactionDescription = null) =>
            _patch.ApplyAsync(operations, dryRun, confirmDestructive, transactionDescription);
    }
}
