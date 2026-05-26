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
    }

    public sealed class HardwareNodeInfo
    {
        public string? Name { get; set; }
        public string? NodeType { get; set; }
        public string? ConnectedSubnet { get; set; }
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
    }

    public sealed class HardwareGsdParameterData
    {
        public HardwareObjectRef Target { get; set; } = new HardwareObjectRef();
        public int DatasetNumber { get; set; }
        public int ByteOffset { get; set; }
        public int Length { get; set; }
        public string DataHex { get; set; } = string.Empty;
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
        public Dictionary<string, object?>? Attributes { get; set; }
        public Dictionary<string, object?>? ExpectedAttributes { get; set; }
        public int? DatasetNumber { get; set; }
        public int ByteOffset { get; set; }
        public int? Length { get; set; }
        public string? DataHex { get; set; }
        public string? ExpectedDataHex { get; set; }
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
