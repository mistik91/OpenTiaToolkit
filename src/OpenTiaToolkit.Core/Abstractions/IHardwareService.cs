using OpenTiaToolkit.Core.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Abstractions
{
    /// <summary>
    /// Provides generic TIA Portal hardware catalog, device structure, network and GSD parameter operations.
    /// </summary>
    public interface IHardwareService
    {
        Task<IReadOnlyList<HardwareCatalogItemInfo>> FindCatalogItemsAsync(string searchText, string? culture = null);
        Task<IReadOnlyList<HardwareDeviceTreeInfo>> GetHardwareTreeAsync(string? deviceName = null);
        Task<HardwareCapabilityInfo?> GetCapabilitiesAsync(HardwareObjectRef target, string[]? attributes = null);
        Task<IReadOnlyList<string>> FindModuleTypesAsync(string partialTypeIdentifier);
        Task<HardwarePlugCheckInfo> CanPlugAsync(HardwareObjectRef target, string typeIdentifier, string itemName, int positionNumber);
        Task<IReadOnlyList<HardwareNetworkInfo>> GetNetworksAsync(string? deviceName = null);
        Task<HardwareGsdParameterData?> GetGsdParameterDataAsync(HardwareObjectRef target, int datasetNumber, int byteOffset, int length);
        Task<HardwarePatchResult> ApplyPatchAsync(
            IReadOnlyList<HardwarePatchOperation> operations,
            bool dryRun = true,
            bool confirmDestructive = false,
            string? transactionDescription = null);
    }
}
