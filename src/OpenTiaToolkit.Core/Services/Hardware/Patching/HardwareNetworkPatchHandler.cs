using OpenTiaToolkit.Core.Models;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTiaToolkit.Core.Services.Hardware.Patching
{
    internal sealed class HardwareNetworkPatchHandler : IHardwarePatchOperationHandler
    {
        public IReadOnlyCollection<string> Operations { get; } = new[]
        {
            "create-subnet", "connect-node-to-subnet", "disconnect-node", "create-io-system",
            "connect-io-connector", "disconnect-io-connector"
        };

        public HardwareOperationResult Execute(string operation, int index, HardwarePatchOperation request, HardwarePatchExecutionContext context)
        {
            switch (operation)
            {
                case "create-subnet": return CreateSubnet(index, request, context);
                case "connect-node-to-subnet": return ConnectNode(index, request, context);
                case "disconnect-node": return DisconnectNode(index, request, context);
                case "create-io-system": return CreateIoSystem(index, request, context);
                case "connect-io-connector": return ConnectIoConnector(index, request, context);
                case "disconnect-io-connector": return DisconnectIoConnector(index, request, context);
                default: throw new InvalidOperationException($"Unsupported network operation: {operation}");
            }
        }

        private static HardwareOperationResult CreateSubnet(int index, HardwarePatchOperation op, HardwarePatchExecutionContext context)
        {
            HardwarePatchRules.RequireText(op.TypeIdentifier, "typeIdentifier");
            HardwarePatchRules.RequireText(op.SubnetName, "subnetName");
            var subnet = context.Engineering.RequireProject().Subnets.Create(op.TypeIdentifier!, op.SubnetName!);
            return context.Success(index, op, null, $"Created subnet: {subnet.Name}");
        }

        private static HardwareOperationResult ConnectNode(int index, HardwarePatchOperation op, HardwarePatchExecutionContext context)
        {
            HardwarePatchRules.RequireText(op.SubnetName, "subnetName");
            var target = context.Resolve(op);
            var node = context.Attributes.RequireNetworkItem(target.Object, "Nodes", op.NodeIndex ?? 0);
            var subnet = context.Engineering.RequireProject().Subnets.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, op.SubnetName, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Subnet not found: {op.SubnetName}");
            context.Networks.ConnectNodeToSubnet(node, subnet);
            return context.Success(index, op, target.Reference, $"Connected node to subnet: {subnet.Name}");
        }

        private static HardwareOperationResult DisconnectNode(int index, HardwarePatchOperation op, HardwarePatchExecutionContext context)
        {
            var target = context.Resolve(op);
            context.Networks.DisconnectNode(context.Attributes.RequireNetworkItem(target.Object, "Nodes", op.NodeIndex ?? 0));
            return context.Success(index, op, target.Reference, "Disconnected node from subnet.");
        }

        private static HardwareOperationResult CreateIoSystem(int index, HardwarePatchOperation op, HardwarePatchExecutionContext context)
        {
            HardwarePatchRules.RequireText(op.IoSystemName, "ioSystemName");
            var target = context.Resolve(op);
            context.Networks.CreateIoSystem(context.Attributes.RequireNetworkItem(target.Object, "IoControllers", op.ControllerIndex ?? 0), op.IoSystemName!);
            return context.Success(index, op, target.Reference, $"Created IO system: {op.IoSystemName}");
        }

        private static HardwareOperationResult ConnectIoConnector(int index, HardwarePatchOperation op, HardwarePatchExecutionContext context)
        {
            HardwarePatchRules.RequireText(op.IoSystemName, "ioSystemName");
            var target = context.Resolve(op);
            var connector = context.Attributes.RequireNetworkItem(target.Object, "IoConnectors", op.ConnectorIndex ?? 0);
            context.Networks.ConnectIoConnector(connector, FindIoSystem(op.IoSystemName!, context));
            return context.Success(index, op, target.Reference, $"Connected IO connector to IO system: {op.IoSystemName}");
        }

        private static HardwareOperationResult DisconnectIoConnector(int index, HardwarePatchOperation op, HardwarePatchExecutionContext context)
        {
            var target = context.Resolve(op);
            context.Networks.DisconnectIoConnector(context.Attributes.RequireNetworkItem(target.Object, "IoConnectors", op.ConnectorIndex ?? 0));
            return context.Success(index, op, target.Reference, "Disconnected IO connector.");
        }

        private static IoSystem FindIoSystem(string name, HardwarePatchExecutionContext context)
        {
            foreach (var network in context.Topology.GetNetworks())
            {
                HardwareResolvedTarget target;
                try { target = context.Resolver.Resolve(network.Target); }
                catch (InvalidOperationException) { continue; }
                if (!(context.Attributes.GetFeatureService(target.Object, "NetworkInterface") is NetworkInterface feature)) continue;
                foreach (var controller in feature.IoControllers)
                    if (controller.IoSystem != null && string.Equals(controller.IoSystem.Name, name, StringComparison.OrdinalIgnoreCase))
                        return controller.IoSystem;
            }
            throw new InvalidOperationException($"IO system not found: {name}");
        }
    }
}
