using System;
using System.Collections.Generic;

namespace OpenTiaToolkit.Core.Models
{
    /// <summary>
    /// Identifies a device or nested hardware item in the current project.
    /// </summary>
    public sealed class HardwareObjectRef
    {
        public string DeviceName { get; set; } = string.Empty;

        public string[] ItemPath { get; set; } = Array.Empty<string>();

        public string? ExpectedTypeIdentifier { get; set; }
    }

    public sealed class HardwareCatalogItemInfo
    {
        public string? TypeIdentifier { get; set; }
        public string? TypeIdentifierNormalized { get; set; }
        public string? TypeName { get; set; }
        public string? ArticleNumber { get; set; }
        public string? Description { get; set; }
        public string? Version { get; set; }
        public string? CatalogPath { get; set; }
    }

    public sealed class HardwareItemInfo
    {
        public HardwareObjectRef Target { get; set; } = new HardwareObjectRef();
        public string Name { get; set; } = string.Empty;
        public string? TypeIdentifier { get; set; }
        public int? PositionNumber { get; set; }
        public List<HardwareItemInfo> Items { get; set; } = new List<HardwareItemInfo>();
    }

    public sealed class HardwareDeviceTreeInfo
    {
        public string DeviceName { get; set; } = string.Empty;
        public string? TypeIdentifier { get; set; }
        public HardwareObjectRef Target { get; set; } = new HardwareObjectRef();
        public List<HardwareItemInfo> Items { get; set; } = new List<HardwareItemInfo>();
    }

    public sealed class HardwareIoAddressInfo
    {
        public HardwareObjectRef Target { get; set; } = new HardwareObjectRef();
        public int Index { get; set; }
        public List<HardwareAttributeInfo> Attributes { get; set; } = new List<HardwareAttributeInfo>();
    }

    public sealed class HardwareDriveTelegramInfo
    {
        public HardwareObjectRef Target { get; set; } = new HardwareObjectRef();
        public int DriveObjectIndex { get; set; }
        public string? DriveObjectNumber { get; set; }
        public int TelegramIndex { get; set; }
        public string? TelegramNumber { get; set; }
        public string? TelegramType { get; set; }
        public List<HardwareIoAddressInfo> Addresses { get; set; } = new List<HardwareIoAddressInfo>();
    }

    public sealed class HardwareCapabilityInfo
    {
        public HardwareObjectRef Target { get; set; } = new HardwareObjectRef();
        public string? TypeIdentifier { get; set; }
        public bool IsDevice { get; set; }
        public bool SupportsPlugging { get; set; }
        public bool SupportsGsdParameters { get; set; }
        public bool SupportsNetworkInterface { get; set; }
        public List<string> AvailableServices { get; set; } = new List<string>();
        public List<HardwareAttributeInfo> Attributes { get; set; } = new List<HardwareAttributeInfo>();
    }

    public sealed class HardwareAttributeInfo
    {
        public string Name { get; set; } = string.Empty;
        public string? Value { get; set; }
        public bool? IsReadOnly { get; set; }
    }

    public sealed class HardwareFeatureAttributesInfo
    {
        public HardwareObjectRef Target { get; set; } = new HardwareObjectRef();
        public string FeatureService { get; set; } = string.Empty;
        public List<HardwareAttributeInfo> Attributes { get; set; } = new List<HardwareAttributeInfo>();
    }

    public sealed class HardwareNetworkItemAttributesInfo
    {
        public HardwareObjectRef Target { get; set; } = new HardwareObjectRef();
        public string CollectionName { get; set; } = string.Empty;
        public int Index { get; set; }
        public List<HardwareAttributeInfo> Attributes { get; set; } = new List<HardwareAttributeInfo>();
    }

    public sealed class HardwarePlugCheckInfo
    {
        public HardwareObjectRef Target { get; set; } = new HardwareObjectRef();
        public string TypeIdentifier { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public int PositionNumber { get; set; }
        public bool CanPlug { get; set; }
    }

    public sealed class HardwareNetworkInfo
    {
        public HardwareObjectRef Target { get; set; } = new HardwareObjectRef();
        public List<HardwareNodeInfo> Nodes { get; set; } = new List<HardwareNodeInfo>();
        public List<HardwareIoControllerInfo> IoControllers { get; set; } = new List<HardwareIoControllerInfo>();
        public List<HardwareIoConnectorInfo> IoConnectors { get; set; } = new List<HardwareIoConnectorInfo>();
        public List<HardwareConnectedIoDeviceInfo> ConnectedIoDevices { get; set; } = new List<HardwareConnectedIoDeviceInfo>();
    }

    public sealed class HardwareNodeInfo
    {
        public int Index { get; set; }
        public string? Name { get; set; }
        public string? NodeType { get; set; }
        public string? ConnectedSubnet { get; set; }
        public List<HardwareAttributeInfo> Attributes { get; set; } = new List<HardwareAttributeInfo>();
    }

    public sealed class HardwareIoControllerInfo
    {
        public int Index { get; set; }
        public string? IoSystemName { get; set; }
    }

    public sealed class HardwareIoConnectorInfo
    {
        public int Index { get; set; }
        public string? ConnectedIoSystemName { get; set; }
        public string? OwnerName { get; set; }
        public string? OwnerTypeIdentifier { get; set; }
        public string? InterfaceOperatingMode { get; set; }
        public List<HardwareAttributeInfo> Attributes { get; set; } = new List<HardwareAttributeInfo>();
    }

    public sealed class HardwareGsdParameterData
    {
        public HardwareObjectRef Target { get; set; } = new HardwareObjectRef();
        public int DatasetNumber { get; set; }
        public int ByteOffset { get; set; }
        public int Length { get; set; }
        public string DataHex { get; set; } = string.Empty;
    }

    public sealed class HardwareTagMappingAuditResult
    {
        public string PlcName { get; set; } = string.Empty;
        public string? TagTableFilter { get; set; }
        public string? CatalogSearchText { get; set; }
        public int HardwareDeviceCount { get; set; }
        public int HardwareIoRangeCount { get; set; }
        public int ScannedTagCount { get; set; }
        public int AddressedTagCount { get; set; }
        public int MatchedTagCount { get; set; }
        public int UnmatchedTagCount { get; set; }
        public List<string> HardwareDevices { get; set; } = new List<string>();
        public List<HardwareAddressRangeInfo> HardwareRanges { get; set; } = new List<HardwareAddressRangeInfo>();
        public List<HardwareTagAddressInfo> UnmatchedTags { get; set; } = new List<HardwareTagAddressInfo>();
        public List<HardwareTagAddressRangeInfo> UnmatchedRanges { get; set; } = new List<HardwareTagAddressRangeInfo>();
        public List<HardwareCatalogItemInfo> CatalogMatches { get; set; } = new List<HardwareCatalogItemInfo>();
        public List<HardwareObjectRef> MatchingHardwareTargets { get; set; } = new List<HardwareObjectRef>();
        public List<HardwareConnectedIoDeviceInfo> MatchingConnectedIoDevices { get; set; } = new List<HardwareConnectedIoDeviceInfo>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class HardwareConnectedIoDeviceInfo
    {
        public HardwareObjectRef NetworkTarget { get; set; } = new HardwareObjectRef();
        public int ConnectorIndex { get; set; }
        public string? ConnectedIoSystemName { get; set; }
        public string? OwnerName { get; set; }
        public string? OwnerTypeIdentifier { get; set; }
        public string? InterfaceOperatingMode { get; set; }
        public List<HardwareNodeInfo> Nodes { get; set; } = new List<HardwareNodeInfo>();
        public HardwareIoConnectorInfo? Connector { get; set; }
    }

    public sealed class HardwareTagAddressInfo
    {
        public string TagTableName { get; set; } = string.Empty;
        public string TagName { get; set; } = string.Empty;
        public string LogicalAddress { get; set; } = string.Empty;
        public string Area { get; set; } = string.Empty;
        public int StartAddress { get; set; }
        public int Length { get; set; }
        public int EndAddress { get; set; }
        public string? MatchedHardwareRange { get; set; }
    }

    public sealed class HardwareAddressRangeInfo
    {
        public HardwareObjectRef Target { get; set; } = new HardwareObjectRef();
        public string Source { get; set; } = string.Empty;
        public string Area { get; set; } = string.Empty;
        public int StartAddress { get; set; }
        public int Length { get; set; }
        public int EndAddress { get; set; }
        public string? IoType { get; set; }
    }

    public sealed class HardwareTagAddressRangeInfo
    {
        public string TagTableName { get; set; } = string.Empty;
        public string Area { get; set; } = string.Empty;
        public int StartAddress { get; set; }
        public int EndAddress { get; set; }
        public int TagCount { get; set; }
        public List<string> ExampleTags { get; set; } = new List<string>();
    }

    public sealed class HardwarePatchOperation
    {
        public string Op { get; set; } = string.Empty;
        public string? ResultId { get; set; }
        public string? TargetRef { get; set; }
        public HardwareObjectRef? Target { get; set; }
        public HardwareObjectRef? ControllerTarget { get; set; }
        public HardwareObjectRef? ConnectorTarget { get; set; }
        public string? TypeIdentifier { get; set; }
        public string? DeviceName { get; set; }
        public string? ItemName { get; set; }
        public int? PositionNumber { get; set; }
        public string? SubnetName { get; set; }
        public string? IoSystemName { get; set; }
        public int? NodeIndex { get; set; }
        public int? ControllerIndex { get; set; }
        public int? ConnectorIndex { get; set; }
        public string? FeatureService { get; set; }
        public string? NetworkCollection { get; set; }
        public int? NetworkItemIndex { get; set; }
        public string[]? AttributeNames { get; set; }
        public Dictionary<string, object?>? Attributes { get; set; }
        public Dictionary<string, object?>? ExpectedAttributes { get; set; }
        public int? DatasetNumber { get; set; }
        public int ByteOffset { get; set; }
        public int? Length { get; set; }
        public string? DataHex { get; set; }
        public string? ExpectedDataHex { get; set; }
        public string? IpAddress { get; set; }
        public string? ExpectedIpAddress { get; set; }
        public string? SubnetMask { get; set; }
        public string? ExpectedSubnetMask { get; set; }
        public string? PnDeviceName { get; set; }
        public string? ExpectedPnDeviceName { get; set; }
        public int? PnDeviceNumber { get; set; }
        public int? ExpectedPnDeviceNumber { get; set; }
        public int? AddressIndex { get; set; }
        public int? StartAddress { get; set; }
        public int? ExpectedStartAddress { get; set; }
        public int? DriveObjectIndex { get; set; }
        public string? TelegramType { get; set; }
        public int? TelegramNumber { get; set; }
        public int? ExpectedTelegramNumber { get; set; }
        public string? IoType { get; set; }
        public int? InputStartAddress { get; set; }
        public int? ExpectedInputStartAddress { get; set; }
        public int? OutputStartAddress { get; set; }
        public int? ExpectedOutputStartAddress { get; set; }
        public int? SizeInWords { get; set; }
        public int? ExpectedSizeInWords { get; set; }
        public bool KeepOriginalAddress { get; set; } = true;
    }

    public sealed class HardwareAttributeChangeInfo
    {
        public Dictionary<string, object?> Before { get; set; } = new Dictionary<string, object?>();
        public Dictionary<string, object?> Written { get; set; } = new Dictionary<string, object?>();
    }

    public sealed class HardwareGsdParameterChangeInfo
    {
        public int DatasetNumber { get; set; }
        public int ByteOffset { get; set; }
        public string? BeforeHex { get; set; }
        public string WrittenHex { get; set; } = string.Empty;
    }

    public sealed class HardwareProfinetConfigurationChangeInfo
    {
        public HardwareAttributeChangeInfo Node { get; set; } = new HardwareAttributeChangeInfo();
        public HardwareAttributeChangeInfo Connector { get; set; } = new HardwareAttributeChangeInfo();
    }

    public sealed class HardwareIoAddressChangeInfo
    {
        public int AddressIndex { get; set; }
        public string? IoType { get; set; }
        public int BeforeStartAddress { get; set; }
        public int WrittenStartAddress { get; set; }
    }

    public sealed class HardwareDriveTelegramChangeInfo
    {
        public int DriveObjectIndex { get; set; }
        public string? TelegramType { get; set; }
        public int? BeforeTelegramNumber { get; set; }
        public int? WrittenTelegramNumber { get; set; }
        public string? IoType { get; set; }
        public int? BeforeStartAddress { get; set; }
        public int? WrittenStartAddress { get; set; }
        public int? BeforeSizeInWords { get; set; }
        public int? WrittenSizeInWords { get; set; }
        public int? BeforeSizeInBytes { get; set; }
        public int? WrittenSizeInBytes { get; set; }
    }

    public sealed class HardwareDriveTelegramConfigurationInfo
    {
        public List<HardwareDriveTelegramChangeInfo> Changes { get; set; } = new List<HardwareDriveTelegramChangeInfo>();
    }

    public sealed class HardwarePatchResult
    {
        public bool DryRun { get; set; }
        public bool Applied { get; set; }
        public bool RolledBack { get; set; }
        public bool SafetyBlocked { get; set; }
        public string? SafetyMessage { get; set; }
        public string? TransactionDescription { get; set; }
        public List<HardwareOperationResult> Operations { get; set; } = new List<HardwareOperationResult>();
    }

    public sealed class HardwareOperationResult
    {
        public int Index { get; set; }
        public string Op { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public HardwareObjectRef? Target { get; set; }
        public string? ResultId { get; set; }
        public object? Data { get; set; }
    }
}
