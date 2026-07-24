using OpenTiaToolkit.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTiaToolkit.Core.Services.Hardware.Patching
{
    internal sealed class HardwareAttributePatchHandler : IHardwarePatchOperationHandler
    {
        public IReadOnlyCollection<string> Operations { get; } = new[]
        {
            "set-attributes", "get-feature-attributes", "set-feature-attributes",
            "get-network-item-attributes", "set-network-item-attributes", "configure-profinet-device"
        };

        public HardwareOperationResult Execute(string operation, int index, HardwarePatchOperation request, HardwarePatchExecutionContext context)
        {
            switch (operation)
            {
                case "set-attributes": return SetAttributes(index, request, context);
                case "get-feature-attributes": return GetFeatureAttributes(index, request, context);
                case "set-feature-attributes": return SetFeatureAttributes(index, request, context);
                case "get-network-item-attributes": return GetNetworkItemAttributes(index, request, context);
                case "set-network-item-attributes": return SetNetworkItemAttributes(index, request, context);
                case "configure-profinet-device": return ConfigureProfinetDevice(index, request, context);
                default: throw new InvalidOperationException($"Unsupported attribute operation: {operation}");
            }
        }

        private static HardwareOperationResult SetAttributes(int index, HardwarePatchOperation op, HardwarePatchExecutionContext context)
        {
            if (op.Attributes == null || op.Attributes.Count == 0) throw new InvalidOperationException("attributes is required for set-attributes.");
            var target = context.Resolve(op);
            context.EnsureExpectedAttributes(target.Object, op.ExpectedAttributes);
            var before = op.Attributes.Keys.ToDictionary(name => name, name => context.TryGetAttribute(target.Object, name), StringComparer.OrdinalIgnoreCase);
            var values = ConvertValues(op.Attributes);
            target.Object.SetAttributes(values);
            return context.Success(index, op, target.Reference, $"Set {values.Count} hardware attribute(s).", Change(before, values));
        }

        private static HardwareOperationResult GetFeatureAttributes(int index, HardwarePatchOperation op, HardwarePatchExecutionContext context)
        {
            HardwarePatchRules.RequireText(op.FeatureService, "featureService");
            if (op.AttributeNames == null || op.AttributeNames.Length == 0) throw new InvalidOperationException("attributeNames is required for get-feature-attributes.");
            var target = context.Resolve(op);
            var values = context.Attributes.GetFeatureAttributeInfos(target.Object, op.FeatureService!, op.AttributeNames).ToList();
            return context.Success(index, op, target.Reference, $"Read {values.Count} {op.FeatureService} feature attribute(s).",
                new HardwareFeatureAttributesInfo { Target = target.Reference, FeatureService = op.FeatureService!, Attributes = values });
        }

        private static HardwareOperationResult SetFeatureAttributes(int index, HardwarePatchOperation op, HardwarePatchExecutionContext context)
        {
            HardwarePatchRules.RequireText(op.FeatureService, "featureService");
            if (op.Attributes == null || op.Attributes.Count == 0) throw new InvalidOperationException("attributes is required for set-feature-attributes.");
            var target = context.Resolve(op);
            context.EnsureExpectedFeatureAttributes(target.Object, op.FeatureService!, op.ExpectedAttributes);
            var before = op.Attributes.Keys.ToDictionary(name => name,
                name => context.TryGetFeatureAttribute(target.Object, op.FeatureService!, name), StringComparer.OrdinalIgnoreCase);
            var values = ConvertValues(op.Attributes);
            context.Attributes.SetFeatureAttributes(target.Object, op.FeatureService!, values);
            return context.Success(index, op, target.Reference, $"Set {values.Count} {op.FeatureService} feature attribute(s).", Change(before, values));
        }

        private static HardwareOperationResult GetNetworkItemAttributes(int index, HardwarePatchOperation op, HardwarePatchExecutionContext context)
        {
            HardwarePatchRules.RequireText(op.NetworkCollection, "networkCollection");
            if (op.AttributeNames == null || op.AttributeNames.Length == 0) throw new InvalidOperationException("attributeNames is required for get-network-item-attributes.");
            var target = context.Resolve(op);
            var itemIndex = op.NetworkItemIndex ?? 0;
            var values = context.Attributes.GetNetworkItemAttributeInfos(target.Object, op.NetworkCollection!, itemIndex, op.AttributeNames).ToList();
            return context.Success(index, op, target.Reference, $"Read {values.Count} {op.NetworkCollection} item attribute(s).",
                new HardwareNetworkItemAttributesInfo { Target = target.Reference, CollectionName = op.NetworkCollection!, Index = itemIndex, Attributes = values });
        }

        private static HardwareOperationResult SetNetworkItemAttributes(int index, HardwarePatchOperation op, HardwarePatchExecutionContext context)
        {
            HardwarePatchRules.RequireText(op.NetworkCollection, "networkCollection");
            if (op.Attributes == null || op.Attributes.Count == 0) throw new InvalidOperationException("attributes is required for set-network-item-attributes.");
            var target = context.Resolve(op);
            var itemIndex = op.NetworkItemIndex ?? 0;
            context.EnsureExpectedNetworkItemAttributes(target.Object, op.NetworkCollection!, itemIndex, op.ExpectedAttributes);
            var before = op.Attributes.Keys.ToDictionary(name => name,
                name => context.TryGetNetworkItemAttribute(target.Object, op.NetworkCollection!, itemIndex, name), StringComparer.OrdinalIgnoreCase);
            var values = ConvertValues(op.Attributes);
            context.Attributes.SetNetworkItemAttributes(target.Object, op.NetworkCollection!, itemIndex, values);
            return context.Success(index, op, target.Reference, $"Set {values.Count} {op.NetworkCollection} item attribute(s).", Change(before, values));
        }

        private static HardwareOperationResult ConfigureProfinetDevice(int index, HardwarePatchOperation op, HardwarePatchExecutionContext context)
        {
            var nodeValues = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            var nodeExpected = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            var connectorValues = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            var connectorExpected = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(op.IpAddress)) nodeValues["Address"] = op.IpAddress!;
            if (!string.IsNullOrWhiteSpace(op.ExpectedIpAddress)) nodeExpected["Address"] = op.ExpectedIpAddress;
            if (!string.IsNullOrWhiteSpace(op.SubnetMask)) nodeValues["SubnetMask"] = op.SubnetMask!;
            if (!string.IsNullOrWhiteSpace(op.ExpectedSubnetMask)) nodeExpected["SubnetMask"] = op.ExpectedSubnetMask;
            if (!string.IsNullOrWhiteSpace(op.PnDeviceName)) { nodeValues["PnDeviceNameAutoGeneration"] = false; nodeValues["PnDeviceName"] = op.PnDeviceName!; }
            if (!string.IsNullOrWhiteSpace(op.ExpectedPnDeviceName)) nodeExpected["PnDeviceName"] = op.ExpectedPnDeviceName;
            if (op.PnDeviceNumber.HasValue) connectorValues["PnDeviceNumber"] = op.PnDeviceNumber.Value;
            if (op.ExpectedPnDeviceNumber.HasValue) connectorExpected["PnDeviceNumber"] = op.ExpectedPnDeviceNumber.Value;
            if (nodeValues.Count == 0 && connectorValues.Count == 0)
                throw new InvalidOperationException("configure-profinet-device requires ipAddress, subnetMask, pnDeviceName, or pnDeviceNumber.");
            var target = context.Resolve(op);
            var nodeIndex = op.NodeIndex ?? 0;
            var connectorIndex = op.ConnectorIndex ?? 0;
            context.EnsureExpectedNetworkItemAttributes(target.Object, "Nodes", nodeIndex, nodeExpected);
            context.EnsureExpectedNetworkItemAttributes(target.Object, "IoConnectors", connectorIndex, connectorExpected);
            var change = new HardwareProfinetConfigurationChangeInfo();
            foreach (var value in nodeValues) { change.Node.Before[value.Key] = context.TryGetNetworkItemAttribute(target.Object, "Nodes", nodeIndex, value.Key); change.Node.Written[value.Key] = value.Value; }
            foreach (var value in connectorValues) { change.Connector.Before[value.Key] = context.TryGetNetworkItemAttribute(target.Object, "IoConnectors", connectorIndex, value.Key); change.Connector.Written[value.Key] = value.Value; }
            if (nodeValues.Count > 0) context.Attributes.SetNetworkItemAttributes(target.Object, "Nodes", nodeIndex, nodeValues);
            if (connectorValues.Count > 0) context.Attributes.SetNetworkItemAttributes(target.Object, "IoConnectors", connectorIndex, connectorValues);
            return context.Success(index, op, target.Reference, "Configured PROFINET device identity and addressing.", change);
        }

        private static List<KeyValuePair<string, object>> ConvertValues(IDictionary<string, object?> values) => values
            .Select(pair => new KeyValuePair<string, object>(pair.Key, HardwareAttributeValueConverter.ConvertJsonValue(pair.Value))).ToList();
        private static HardwareAttributeChangeInfo Change(Dictionary<string, object?> before, IEnumerable<KeyValuePair<string, object>> values) =>
            new HardwareAttributeChangeInfo { Before = before, Written = values.ToDictionary(pair => pair.Key, pair => (object?)pair.Value, StringComparer.OrdinalIgnoreCase) };
    }
}
