using OpenTiaToolkit.Core.Models;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using System;
using System.Linq;

namespace OpenTiaToolkit.Core.Services.V21
{
    /// <summary>
    /// Owns V21 hardware network projection and connection primitives.
    /// </summary>
    internal sealed class V21HardwareNetworkAccess
    {
        private static readonly string[] DefaultNodeAttributeNames =
        {
            "Address", "SubnetMask", "PnDeviceName", "PnDeviceNameAutoGeneration"
        };
        private static readonly string[] DefaultIoConnectorAttributeNames = { "PnDeviceNumber" };
        private readonly V21HardwareAttributeAccess _attributes;

        public V21HardwareNetworkAccess(V21HardwareAttributeAccess attributes)
        {
            _attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
        }

        public HardwareNetworkInfo? GetNetworkInfo(HardwareObject target, HardwareObjectRef reference)
        {
            if (_attributes.GetFeatureService(target, "NetworkInterface") is not NetworkInterface network) return null;
            var info = new HardwareNetworkInfo { Target = reference };
            var index = 0;
            foreach (var node in network.Nodes)
            {
                info.Nodes.Add(new HardwareNodeInfo
                {
                    Index = index++,
                    Name = node.Name,
                    NodeType = node.NodeType.ToString(),
                    ConnectedSubnet = node.ConnectedSubnet?.Name,
                    Attributes = _attributes.GetSelectedEngineeringAttributeInfos(node, DefaultNodeAttributeNames)
                });
            }
            index = 0;
            foreach (var controller in network.IoControllers)
                info.IoControllers.Add(new HardwareIoControllerInfo { Index = index++, IoSystemName = controller.IoSystem?.Name });
            index = 0;
            foreach (var connector in network.IoConnectors)
            {
                var connectorNetwork = TiaReflectionHelper.GetProperty(connector, "Parent");
                var owner = TiaReflectionHelper.GetProperty(connectorNetwork, "OwnedBy")
                    ?? TiaReflectionHelper.GetProperty(connectorNetwork, "Parent");
                info.IoConnectors.Add(new HardwareIoConnectorInfo
                {
                    Index = index++,
                    ConnectedIoSystemName = connector.ConnectedToIoSystem?.Name,
                    OwnerName = TiaReflectionHelper.GetName(owner),
                    OwnerTypeIdentifier = TiaReflectionHelper.GetProperty(owner, "TypeIdentifier")?.ToString(),
                    InterfaceOperatingMode = TiaReflectionHelper.GetProperty(connectorNetwork, "InterfaceOperatingMode")?.ToString(),
                    Attributes = _attributes.GetSelectedEngineeringAttributeInfos(connector, DefaultIoConnectorAttributeNames)
                });
            }
            return info;
        }

        public void ConnectNodeToSubnet(object node, Subnet subnet) =>
            InvokeNoResult(node, "ConnectToSubnet", subnet);

        public void DisconnectNode(object node) =>
            InvokeNoResult(node, "DisconnectFromSubnet");

        public object CreateIoSystem(object controller, string ioSystemName) =>
            Invoke(controller, "CreateIoSystem", ioSystemName)
            ?? throw new InvalidOperationException("Creating the IO system returned no object.");

        public void ConnectIoConnector(object connector, object ioSystem) =>
            InvokeNoResult(connector, "ConnectToIoSystem", ioSystem);

        public void DisconnectIoConnector(object connector) =>
            InvokeNoResult(connector, "DisconnectFromIoSystem");

        private static void InvokeNoResult(object target, string methodName, params object[] args) =>
            _ = Invoke(target, methodName, args);

        private static object? Invoke(object target, string methodName, params object[] args)
        {
            var method = target.GetType().GetMethods()
                .FirstOrDefault(candidate => candidate.Name == methodName && candidate.GetParameters().Length == args.Length);
            if (method == null)
                throw new InvalidOperationException($"Method '{methodName}' is not available on {target.GetType().Name}.");
            try { return method.Invoke(target, args); }
            catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
        }
    }
}
