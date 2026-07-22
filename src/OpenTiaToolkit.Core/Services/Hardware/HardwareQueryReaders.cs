using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Services.Hardware.Patching;
using Siemens.Engineering.HW;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTiaToolkit.Core.Services.Hardware
{
    internal sealed class HardwareCatalogCapabilityReader
    {
        private readonly HardwareEngineeringContext _context;
        private readonly HardwareTargetResolver _resolver;
        private readonly V21HardwareAdapter _adapter;

        public HardwareCatalogCapabilityReader(HardwareEngineeringContext context, HardwareTargetResolver resolver, V21HardwareAdapter adapter)
        {
            _context = context;
            _resolver = resolver;
            _adapter = adapter;
        }

        public IReadOnlyList<HardwareCatalogItemInfo> FindCatalogItems(string searchText, string? culture) =>
            _adapter.FindCatalogItems(_context.RequirePortal(), searchText ?? string.Empty, culture);

        public HardwareCapabilityInfo? GetCapabilities(HardwareObjectRef target, string[]? attributes)
        {
            var resolved = _resolver.Resolve(target).Object;
            var services = new[] { "GsdDevice", "GsdDeviceItem", "NetworkInterface" }
                .Where(name => _adapter.GetFeatureService(resolved, name) != null).ToList();
            return new HardwareCapabilityInfo
            {
                Target = HardwareReferenceRules.Clone(target),
                TypeIdentifier = resolved.TypeIdentifier,
                IsDevice = resolved is Device,
                SupportsPlugging = true,
                SupportsGsdParameters = services.Contains("GsdDeviceItem"),
                SupportsNetworkInterface = services.Contains("NetworkInterface"),
                AvailableServices = services,
                Attributes = _adapter.GetAttributeInfos(resolved, attributes).ToList()
            };
        }

        public HardwareFeatureAttributesInfo GetFeatureAttributes(HardwareObjectRef target, string featureService, string[]? attributes)
        {
            HardwarePatchRules.RequireText(featureService, "featureService");
            var resolved = _resolver.Resolve(target).Object;
            return new HardwareFeatureAttributesInfo
            {
                Target = HardwareReferenceRules.Clone(target),
                FeatureService = featureService,
                Attributes = _adapter.GetFeatureAttributeInfos(resolved, featureService, attributes).ToList()
            };
        }

        public HardwareNetworkItemAttributesInfo GetNetworkItemAttributes(HardwareObjectRef target, string collectionName, int index, string[]? attributes)
        {
            HardwarePatchRules.RequireText(collectionName, "networkCollection");
            var resolved = _resolver.Resolve(target).Object;
            return new HardwareNetworkItemAttributesInfo
            {
                Target = HardwareReferenceRules.Clone(target),
                CollectionName = collectionName,
                Index = index,
                Attributes = _adapter.GetNetworkItemAttributeInfos(resolved, collectionName, index, attributes).ToList()
            };
        }

        public HardwarePlugCheckInfo CanPlug(HardwareObjectRef target, string typeIdentifier, string itemName, int positionNumber)
        {
            var parent = _resolver.Resolve(target).Object;
            return new HardwarePlugCheckInfo
            {
                Target = HardwareReferenceRules.Clone(target),
                TypeIdentifier = typeIdentifier,
                ItemName = itemName,
                PositionNumber = positionNumber,
                CanPlug = parent.CanPlugNew(typeIdentifier, itemName, positionNumber)
            };
        }

        public IReadOnlyList<string> FindModuleTypes(string partialTypeIdentifier) =>
            _adapter.FindModuleTypes(_context.RequireProject(), partialTypeIdentifier ?? string.Empty);

        public HardwareGsdParameterData? GetGsdParameterData(HardwareObjectRef target, int datasetNumber, int byteOffset, int length)
        {
            var bytes = _adapter.GetGsdParameterData(_resolver.Resolve(target).Object, datasetNumber, byteOffset, length);
            return bytes == null ? null : new HardwareGsdParameterData
            {
                Target = HardwareReferenceRules.Clone(target),
                DatasetNumber = datasetNumber,
                ByteOffset = byteOffset,
                Length = bytes.Length,
                DataHex = HardwareHexCodec.Format(bytes)
            };
        }
    }

    internal sealed class HardwareDriveReader
    {
        private readonly HardwareEngineeringContext _context;
        private readonly V21HardwareAdapter _adapter;

        public HardwareDriveReader(HardwareEngineeringContext context, V21HardwareAdapter adapter)
        {
            _context = context;
            _adapter = adapter;
        }

        public IReadOnlyList<HardwareDriveTelegramInfo> GetDriveTelegrams(string? deviceName, string[]? attributes)
        {
            var result = new List<HardwareDriveTelegramInfo>();
            foreach (var device in FilterDevices(deviceName))
            {
                var root = Root(device);
                foreach (var item in device.DeviceItems)
                    AddTelegrams(item, HardwareReferenceRules.Child(root, item.Name, item.TypeIdentifier), attributes, result);
            }
            return result;
        }

        public IReadOnlyList<HardwareDriveParameterInfo> GetDriveParameters(string? deviceName, int? driveObjectIndex, string? filter, int maxResults)
        {
            if (maxResults < 1 || maxResults > 5000)
                throw new ArgumentOutOfRangeException(nameof(maxResults), "maxResults must be between 1 and 5000.");
            var result = new List<HardwareDriveParameterInfo>();
            foreach (var device in FilterDevices(deviceName))
            {
                var root = Root(device);
                foreach (var item in device.DeviceItems)
                {
                    AddParameters(item, HardwareReferenceRules.Child(root, item.Name, item.TypeIdentifier), driveObjectIndex, filter, maxResults, result);
                    if (result.Count >= maxResults) return result;
                }
            }
            return result;
        }

        private IEnumerable<Device> FilterDevices(string? name) => ProjectDeviceEnumerator.EnumerateDevices(_context.RequireProject())
            .Where(device => string.IsNullOrWhiteSpace(name) || string.Equals(device.Name, name, StringComparison.OrdinalIgnoreCase));
        private static HardwareObjectRef Root(Device device) => new HardwareObjectRef { DeviceName = device.Name, ExpectedTypeIdentifier = device.TypeIdentifier };

        private void AddTelegrams(DeviceItem item, HardwareObjectRef reference, string[]? attributes, ICollection<HardwareDriveTelegramInfo> result)
        {
            foreach (var value in _adapter.GetDriveTelegrams(item, reference, attributes)) result.Add(value);
            foreach (var child in item.DeviceItems)
                AddTelegrams(child, HardwareReferenceRules.Child(reference, child.Name, child.TypeIdentifier), attributes, result);
        }

        private void AddParameters(DeviceItem item, HardwareObjectRef reference, int? index, string? filter, int max, ICollection<HardwareDriveParameterInfo> result)
        {
            foreach (var value in _adapter.GetDriveParameters(item, reference, index, filter, max - result.Count)) result.Add(value);
            if (result.Count >= max) return;
            foreach (var child in item.DeviceItems)
            {
                AddParameters(child, HardwareReferenceRules.Child(reference, child.Name, child.TypeIdentifier), index, filter, max, result);
                if (result.Count >= max) return;
            }
        }
    }
}
