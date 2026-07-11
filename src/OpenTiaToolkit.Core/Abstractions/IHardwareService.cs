using OpenTiaToolkit.Core.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Abstractions
{
    /// <summary>
    /// Provides generic TIA Portal hardware catalog, device structure, network and GSD parameter operations.
    /// </summary>
    public interface IHardwareQueryCapability
    {
        Task<IReadOnlyList<HardwareCatalogItemInfo>> FindCatalogItemsAsync(string searchText, string? culture = null);
        Task<IReadOnlyList<HardwareDeviceTreeInfo>> GetHardwareTreeAsync(string? deviceName = null);
        Task<IReadOnlyList<HardwareIoAddressInfo>> GetIoAddressesAsync(string? deviceName = null, string[]? attributes = null);
        Task<IReadOnlyList<HardwareDriveTelegramInfo>> GetDriveTelegramsAsync(string? deviceName = null, string[]? addressAttributes = null);
        Task<HardwareTagMappingAuditResult> AuditHardwareTagMappingsAsync(
            string plcName,
            string? tagTableFilter = null,
            string? catalogSearchText = null,
            int maxUnmatchedTags = 100);
        Task<HardwareCapabilityInfo?> GetCapabilitiesAsync(HardwareObjectRef target, string[]? attributes = null);
        Task<HardwareFeatureAttributesInfo> GetFeatureAttributesAsync(HardwareObjectRef target, string featureService, string[]? attributes = null);
        Task<HardwareNetworkItemAttributesInfo> GetNetworkItemAttributesAsync(HardwareObjectRef target, string collectionName, int index, string[]? attributes = null);
        Task<IReadOnlyList<string>> FindModuleTypesAsync(string partialTypeIdentifier);
        Task<HardwarePlugCheckInfo> CanPlugAsync(HardwareObjectRef target, string typeIdentifier, string itemName, int positionNumber);
        Task<IReadOnlyList<HardwareNetworkInfo>> GetNetworksAsync(string? deviceName = null);
        Task<HardwareGsdParameterData?> GetGsdParameterDataAsync(HardwareObjectRef target, int datasetNumber, int byteOffset, int length);
    }

    public interface IHardwarePatchCapability
    {
        Task<HardwarePatchResult> ApplyPatchAsync(
            IReadOnlyList<HardwarePatchOperation> operations,
            bool dryRun = true,
            bool confirmDestructive = false,
            string? transactionDescription = null);
    }

    public interface IHardwareService : IHardwareQueryCapability, IHardwarePatchCapability
    {
    }
}
