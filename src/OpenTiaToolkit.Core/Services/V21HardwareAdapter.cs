using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Services.V21;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using System;
using System.Collections.Generic;

namespace OpenTiaToolkit.Core.Services
{
    /// <summary>
    /// Compatibility facade over focused V21 hardware capabilities.
    /// </summary>
    internal sealed class V21HardwareAdapter
    {
        private readonly V21HardwareCatalogAccess _catalog;
        private readonly V21HardwareAttributeAccess _attributes;
        private readonly V21HardwareDriveAccess _drives;
        private readonly V21HardwareNetworkAccess _networks;

        public V21HardwareAdapter(V21TypeServiceResolver services)
            : this(
                new V21HardwareCatalogAccess(),
                new V21HardwareAttributeAccess(services),
                services)
        {
        }

        private V21HardwareAdapter(
            V21HardwareCatalogAccess catalog,
            V21HardwareAttributeAccess attributes,
            V21TypeServiceResolver services)
            : this(catalog, attributes, new V21HardwareDriveAccess(attributes), new V21HardwareNetworkAccess(attributes))
        {
            _ = services;
        }

        public V21HardwareAdapter(
            V21HardwareCatalogAccess catalog,
            V21HardwareAttributeAccess attributes,
            V21HardwareDriveAccess drives,
            V21HardwareNetworkAccess networks)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
            _drives = drives ?? throw new ArgumentNullException(nameof(drives));
            _networks = networks ?? throw new ArgumentNullException(nameof(networks));
        }

        public IReadOnlyList<HardwareCatalogItemInfo> FindCatalogItems(TiaPortal portal, string searchText, string? typeIdentifier) =>
            _catalog.FindCatalogItems(portal, searchText, typeIdentifier);
        public string[] FindModuleTypes(ProjectBase project, string partialTypeIdentifier) =>
            _catalog.FindModuleTypes(project, partialTypeIdentifier);
        public object? GetFeatureService(HardwareObject target, string serviceType) =>
            _attributes.GetFeatureService(target, serviceType);
        public IReadOnlyList<HardwareAttributeInfo> GetAttributeInfos(HardwareObject target, IEnumerable<string>? attributeNames) =>
            _attributes.GetAttributeInfos(target, attributeNames);
        public object? GetAttribute(HardwareObject target, string name) =>
            _attributes.GetAttribute(target, name);
        public IReadOnlyList<HardwareIoAddressInfo> GetIoAddresses(DeviceItem target, HardwareObjectRef reference, IEnumerable<string>? attributeNames) =>
            _attributes.GetIoAddresses(target, reference, attributeNames);
        public HardwareIoAddressChangeInfo SetIoAddressStart(DeviceItem target, int addressIndex, int startAddress, int? expectedStartAddress) =>
            _attributes.SetIoAddressStart(target, addressIndex, startAddress, expectedStartAddress);
        public IReadOnlyList<HardwareAttributeInfo> GetFeatureAttributeInfos(HardwareObject target, string featureService, IEnumerable<string>? attributeNames) =>
            _attributes.GetFeatureAttributeInfos(target, featureService, attributeNames);
        public object? GetFeatureAttribute(HardwareObject target, string featureService, string name) =>
            _attributes.GetFeatureAttribute(target, featureService, name);
        public void SetFeatureAttributes(HardwareObject target, string featureService, IEnumerable<KeyValuePair<string, object>> values) =>
            _attributes.SetFeatureAttributes(target, featureService, values);
        public IReadOnlyList<HardwareAttributeInfo> GetNetworkItemAttributeInfos(HardwareObject target, string collectionName, int index, IEnumerable<string>? attributeNames) =>
            _attributes.GetNetworkItemAttributeInfos(target, collectionName, index, attributeNames);
        public object? GetNetworkItemAttribute(HardwareObject target, string collectionName, int index, string name) =>
            _attributes.GetNetworkItemAttribute(target, collectionName, index, name);
        public void SetNetworkItemAttributes(HardwareObject target, string collectionName, int index, IEnumerable<KeyValuePair<string, object>> values) =>
            _attributes.SetNetworkItemAttributes(target, collectionName, index, values);
        public byte[]? GetGsdParameterData(HardwareObject target, int datasetNumber, int byteOffset, int length) =>
            _attributes.GetGsdParameterData(target, datasetNumber, byteOffset, length);
        public void SetGsdParameterData(HardwareObject target, int datasetNumber, int byteOffset, IEnumerable<byte> data) =>
            _attributes.SetGsdParameterData(target, datasetNumber, byteOffset, data);
        public object RequireNetworkItem(HardwareObject target, string collectionName, int index) =>
            _attributes.RequireNetworkItem(target, collectionName, index);
        public IReadOnlyList<HardwareDriveTelegramInfo> GetDriveTelegrams(DeviceItem target, HardwareObjectRef reference, IEnumerable<string>? addressAttributeNames) =>
            _drives.GetDriveTelegrams(target, reference, addressAttributeNames);
        public HardwareDriveTelegramChangeInfo ChangeDriveTelegramNumber(DeviceItem target, int driveObjectIndex, string telegramType, int telegramNumber, int? expectedTelegramNumber) =>
            _drives.ChangeDriveTelegramNumber(target, driveObjectIndex, telegramType, telegramNumber, expectedTelegramNumber);
        public HardwareDriveTelegramChangeInfo SetDriveTelegramAddress(DeviceItem target, int driveObjectIndex, string telegramType, string ioType, int startAddress, int? expectedStartAddress) =>
            _drives.SetDriveTelegramAddress(target, driveObjectIndex, telegramType, ioType, startAddress, expectedStartAddress);
        public IReadOnlyList<HardwareDriveTelegramChangeInfo> ConfigureDriveTelegram(
            DeviceItem target, int driveObjectIndex, string telegramType, int telegramNumber, int? expectedTelegramNumber,
            int? inputStartAddress, int? expectedInputStartAddress, int? outputStartAddress, int? expectedOutputStartAddress) =>
            _drives.ConfigureDriveTelegram(target, driveObjectIndex, telegramType, telegramNumber, expectedTelegramNumber,
                inputStartAddress, expectedInputStartAddress, outputStartAddress, expectedOutputStartAddress);
        public HardwareDriveTelegramChangeInfo ChangeDriveTelegramSize(
            DeviceItem target, int driveObjectIndex, string telegramType, string ioType, int sizeInWords,
            int? expectedSizeInWords, bool keepOriginalAddress) =>
            _drives.ChangeDriveTelegramSize(target, driveObjectIndex, telegramType, ioType, sizeInWords, expectedSizeInWords, keepOriginalAddress);
        public HardwareDriveTelegramChangeInfo InsertDriveTelegram(DeviceItem target, int driveObjectIndex, string telegramType, int telegramNumber) =>
            _drives.InsertDriveTelegram(target, driveObjectIndex, telegramType, telegramNumber);
        public HardwareDriveTelegramChangeInfo EraseDriveTelegram(DeviceItem target, int driveObjectIndex, string telegramType, int? expectedTelegramNumber) =>
            _drives.EraseDriveTelegram(target, driveObjectIndex, telegramType, expectedTelegramNumber);
        public IReadOnlyList<HardwareDriveParameterInfo> GetDriveParameters(
            DeviceItem target, HardwareObjectRef reference, int? requestedDriveObjectIndex, string? filter, int maxResults) =>
            _drives.GetDriveParameters(target, reference, requestedDriveObjectIndex, filter, maxResults);
        public HardwareNetworkInfo? GetNetworkInfo(HardwareObject target, HardwareObjectRef reference) =>
            _networks.GetNetworkInfo(target, reference);
        public void ConnectNodeToSubnet(object node, Subnet subnet) =>
            _networks.ConnectNodeToSubnet(node, subnet);
        public void DisconnectNode(object node) =>
            _networks.DisconnectNode(node);
        public object CreateIoSystem(object controller, string ioSystemName) =>
            _networks.CreateIoSystem(controller, ioSystemName);
        public void ConnectIoConnector(object connector, object ioSystem) =>
            _networks.ConnectIoConnector(connector, ioSystem);
        public void DisconnectIoConnector(object connector) =>
            _networks.DisconnectIoConnector(connector);

        internal static List<HardwareAttributeInfo> GetSelectedEngineeringAttributeInfos(object? target, IEnumerable<string> attributeNames) =>
            V21HardwareAttributeAccess.ProjectSelectedEngineeringAttributeInfos(target, attributeNames);
    }
}
