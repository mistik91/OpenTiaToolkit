using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Utilities;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTiaToolkit.Core.Services.Hardware
{
    internal sealed class HardwareTopologyReader
    {
        private static readonly string[] DefaultNodeAttributeNames = { "Address", "SubnetMask", "PnDeviceName", "PnDeviceNameAutoGeneration" };
        private static readonly string[] DefaultIoConnectorAttributeNames = { "PnDeviceNumber" };
        private readonly HardwareEngineeringContext _context;
        private readonly V21HardwareAdapter _adapter;

        public HardwareTopologyReader(HardwareEngineeringContext context, V21HardwareAdapter adapter)
        {
            _context = context;
            _adapter = adapter;
        }

        public IReadOnlyList<HardwareDeviceTreeInfo> GetHardwareTree(string? deviceName) =>
            FilterDevices(deviceName).Select(ToDeviceTree).ToList();

        public IReadOnlyList<HardwareIoAddressInfo> GetIoAddresses(string? deviceName, string[]? attributes)
        {
            var result = new List<HardwareIoAddressInfo>();
            foreach (var device in FilterDevices(deviceName))
            {
                var root = Root(device);
                foreach (var item in device.DeviceItems)
                    AddIoAddresses(item, HardwareReferenceRules.Child(root, item.Name, item.TypeIdentifier), attributes, result);
            }
            return result;
        }

        public IReadOnlyList<HardwareNetworkInfo> GetNetworks(string? deviceName = null)
        {
            var result = new List<HardwareNetworkInfo>();
            foreach (var device in FilterDevices(deviceName)) AddNetworkInfo(device, new HardwareObjectRef { DeviceName = device.Name }, result);
            if (string.IsNullOrWhiteSpace(deviceName)) AddProjectIoSystems(_context.RequireProject(), result);
            return result;
        }

        public HardwareDeviceTreeInfo ToDeviceTree(Device device)
        {
            var reference = Root(device);
            return new HardwareDeviceTreeInfo
            {
                DeviceName = device.Name, TypeIdentifier = device.TypeIdentifier, Target = reference,
                Items = device.DeviceItems.Select(item => ToItemInfo(item, reference)).ToList()
            };
        }

        public HardwareItemInfo ToItemInfoWithReference(DeviceItem item, HardwareObjectRef reference) => new HardwareItemInfo
        {
            Target = HardwareReferenceRules.Clone(reference), Name = item.Name, TypeIdentifier = item.TypeIdentifier,
            PositionNumber = item.PositionNumber, Items = item.DeviceItems.Select(child => ToItemInfo(child, reference)).ToList()
        };

        private IEnumerable<Device> FilterDevices(string? name) => ProjectDeviceEnumerator.EnumerateDevices(_context.RequireProject())
            .Where(device => string.IsNullOrWhiteSpace(name) || string.Equals(device.Name, name, StringComparison.OrdinalIgnoreCase));
        private static HardwareObjectRef Root(Device device) => new HardwareObjectRef { DeviceName = device.Name, ExpectedTypeIdentifier = device.TypeIdentifier };
        private HardwareItemInfo ToItemInfo(DeviceItem item, HardwareObjectRef parent)
        {
            var reference = HardwareReferenceRules.Child(parent, item.Name, item.TypeIdentifier);
            return ToItemInfoWithReference(item, reference);
        }

        private void AddIoAddresses(DeviceItem item, HardwareObjectRef reference, string[]? attributes, ICollection<HardwareIoAddressInfo> result)
        {
            foreach (var value in _adapter.GetIoAddresses(item, reference, attributes)) result.Add(value);
            foreach (var child in item.DeviceItems)
                AddIoAddresses(child, HardwareReferenceRules.Child(reference, child.Name, child.TypeIdentifier), attributes, result);
        }

        private void AddNetworkInfo(HardwareObject target, HardwareObjectRef reference, ICollection<HardwareNetworkInfo> result)
        {
            var info = _adapter.GetNetworkInfo(target, reference);
            if (info != null) result.Add(info);
            foreach (var child in target.DeviceItems)
                AddNetworkInfo(child, HardwareReferenceRules.Child(reference, child.Name, child.TypeIdentifier), result);
        }

        private static void AddProjectIoSystems(ProjectBase project, ICollection<HardwareNetworkInfo> result)
        {
            foreach (var subnet in TiaReflectionHelper.GetEnumerableProperty(project, "Subnets") ?? Enumerable.Empty<object>())
            foreach (var ioSystem in TiaReflectionHelper.GetEnumerableProperty(subnet, "IoSystems") ?? Enumerable.Empty<object>())
            {
                var info = new HardwareNetworkInfo
                {
                    Target = new HardwareObjectRef
                    {
                        DeviceName = $"Subnet:{TiaReflectionHelper.GetName(subnet) ?? "Subnet"}",
                        ItemPath = new[] { TiaReflectionHelper.GetName(ioSystem) ?? "IoSystem" }
                    }
                };
                var index = 0;
                foreach (var connector in TiaReflectionHelper.GetEnumerableProperty(ioSystem, "ConnectedIoDevices") ?? Enumerable.Empty<object>())
                    info.ConnectedIoDevices.Add(ToConnectedIoDeviceInfo(info.Target, index++, TiaReflectionHelper.GetName(ioSystem), connector));
                if (info.ConnectedIoDevices.Count > 0) result.Add(info);
            }
        }

        private static HardwareConnectedIoDeviceInfo ToConnectedIoDeviceInfo(HardwareObjectRef target, int connectorIndex, string? ioSystemName, object connector)
        {
            var network = TiaReflectionHelper.GetProperty(connector, "Parent");
            var owner = TiaReflectionHelper.GetProperty(network, "OwnedBy") ?? TiaReflectionHelper.GetProperty(network, "Parent");
            var nodes = EnumerateNetworkItems(network, "Nodes").Select((node, index) =>
            {
                var subnet = TiaReflectionHelper.GetProperty(node, "ConnectedSubnet");
                return new HardwareNodeInfo
                {
                    Index = index, Name = TiaReflectionHelper.GetProperty(node, "Name")?.ToString(),
                    NodeType = TiaReflectionHelper.GetProperty(node, "NodeType")?.ToString(),
                    ConnectedSubnet = TiaReflectionHelper.GetName(subnet),
                    Attributes = V21HardwareAdapter.GetSelectedEngineeringAttributeInfos(node, DefaultNodeAttributeNames)
                };
            }).ToList();
            var networkConnectorIndex = EnumerateNetworkItems(network, "IoConnectors")
                .Select((candidate, index) => new { Connector = candidate, Index = index })
                .FirstOrDefault(candidate => ReferenceEquals(candidate.Connector, connector))?.Index ?? connectorIndex;
            return new HardwareConnectedIoDeviceInfo
            {
                NetworkTarget = HardwareReferenceRules.Clone(target), ConnectorIndex = connectorIndex,
                ConnectedIoSystemName = ioSystemName, OwnerName = TiaReflectionHelper.GetName(owner),
                OwnerTypeIdentifier = TiaReflectionHelper.GetProperty(owner, "TypeIdentifier")?.ToString(),
                InterfaceOperatingMode = TiaReflectionHelper.GetProperty(network, "InterfaceOperatingMode")?.ToString(),
                Nodes = nodes,
                Connector = new HardwareIoConnectorInfo
                {
                    Index = networkConnectorIndex, ConnectedIoSystemName = ioSystemName,
                    OwnerName = TiaReflectionHelper.GetName(owner),
                    OwnerTypeIdentifier = TiaReflectionHelper.GetProperty(owner, "TypeIdentifier")?.ToString(),
                    InterfaceOperatingMode = TiaReflectionHelper.GetProperty(network, "InterfaceOperatingMode")?.ToString(),
                    Attributes = V21HardwareAdapter.GetSelectedEngineeringAttributeInfos(connector, DefaultIoConnectorAttributeNames)
                }
            };
        }

        private static IEnumerable<object> EnumerateNetworkItems(object? network, string collectionName) =>
            TiaReflectionHelper.GetEnumerableProperty(network, collectionName) ?? Enumerable.Empty<object>();
    }
}
