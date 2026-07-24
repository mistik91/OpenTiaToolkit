using OpenTiaToolkit.Core.Models;
using Siemens.Engineering.HW;
using System;
using System.Collections.Generic;

namespace OpenTiaToolkit.Core.Services.Hardware.Patching
{
    internal sealed class HardwareStructurePatchHandler : IHardwarePatchOperationHandler
    {
        public IReadOnlyCollection<string> Operations { get; } = new[]
        {
            "create-device", "create-device-with-item", "plug-item", "delete-device", "delete-item"
        };

        public HardwareOperationResult Execute(string operation, int index, HardwarePatchOperation request, HardwarePatchExecutionContext context)
        {
            switch (operation)
            {
                case "create-device": return CreateDevice(index, request, context);
                case "create-device-with-item": return CreateDeviceWithItem(index, request, context);
                case "plug-item": return PlugItem(index, request, context);
                case "delete-device": return DeleteDevice(index, request, context);
                case "delete-item": return DeleteItem(index, request, context);
                default: throw new InvalidOperationException($"Unsupported structure operation: {operation}");
            }
        }

        private static HardwareOperationResult CreateDevice(int index, HardwarePatchOperation op, HardwarePatchExecutionContext context)
        {
            HardwarePatchRules.RequireText(op.TypeIdentifier, "typeIdentifier");
            HardwarePatchRules.RequireText(op.DeviceName, "deviceName");
            var created = context.Engineering.RequireProject().Devices.Create(op.TypeIdentifier!, op.DeviceName!);
            var target = new HardwareObjectRef { DeviceName = created.Name, ExpectedTypeIdentifier = created.TypeIdentifier };
            context.Register(op.ResultId, new HardwareResolvedTarget(created, target));
            return context.Success(index, op, target, $"Created device: {created.Name}", context.Topology.ToDeviceTree(created));
        }

        private static HardwareOperationResult CreateDeviceWithItem(int index, HardwarePatchOperation op, HardwarePatchExecutionContext context)
        {
            HardwarePatchRules.RequireText(op.TypeIdentifier, "typeIdentifier");
            HardwarePatchRules.RequireText(op.DeviceName, "deviceName");
            HardwarePatchRules.RequireText(op.ItemName, "itemName");
            var created = context.Engineering.RequireProject().Devices.CreateWithItem(op.TypeIdentifier!, op.ItemName!, op.DeviceName!);
            var target = new HardwareObjectRef { DeviceName = created.Name, ExpectedTypeIdentifier = created.TypeIdentifier };
            context.Register(op.ResultId, new HardwareResolvedTarget(created, target));
            return context.Success(index, op, target, $"Created device with item: {created.Name}", context.Topology.ToDeviceTree(created));
        }

        private static HardwareOperationResult PlugItem(int index, HardwarePatchOperation op, HardwarePatchExecutionContext context)
        {
            HardwarePatchRules.RequireText(op.TypeIdentifier, "typeIdentifier");
            HardwarePatchRules.RequireText(op.ItemName, "itemName");
            if (!op.PositionNumber.HasValue) throw new InvalidOperationException("positionNumber is required for plug-item.");
            var parent = context.Resolve(op);
            if (!parent.Object.CanPlugNew(op.TypeIdentifier!, op.ItemName!, op.PositionNumber.Value))
                throw new InvalidOperationException($"Cannot plug '{op.ItemName}' at position {op.PositionNumber.Value}.");
            var created = parent.Object.PlugNew(op.TypeIdentifier!, op.ItemName!, op.PositionNumber.Value);
            var requested = HardwareReferenceRules.Child(parent.Reference, created.Name, created.TypeIdentifier);
            var actual = context.Resolver.FindActualReference(created, parent.Reference.DeviceName)
                ?? throw new InvalidOperationException($"Plugged hardware item '{created.Name}' could not be located after creation.");
            var displaced = !HardwareReferenceRules.PathsEqual(requested, actual);
            var warning = HardwareReferenceRules.ValidatePlugPlacement(created.Name, requested, actual, op.AllowDisplacedPlacement);
            context.Register(op.ResultId, new HardwareResolvedTarget(created, actual));
            return context.Success(index, op, actual, $"Plugged hardware item: {created.Name}", new HardwarePlugItemInfo
            {
                Item = context.Topology.ToItemInfoWithReference(created, actual),
                RequestedTarget = requested,
                ActualTarget = actual,
                ActualParentMismatch = displaced,
                Warning = warning
            });
        }

        private static HardwareOperationResult DeleteDevice(int index, HardwarePatchOperation op, HardwarePatchExecutionContext context)
        {
            var target = context.Resolve(op);
            if (!(target.Object is Device device)) throw new InvalidOperationException("delete-device target must be a device root.");
            device.Delete();
            return context.Success(index, op, target.Reference, $"Deleted device: {target.Reference.DeviceName}");
        }

        private static HardwareOperationResult DeleteItem(int index, HardwarePatchOperation op, HardwarePatchExecutionContext context)
        {
            var target = context.Resolve(op);
            if (!(target.Object is DeviceItem item)) throw new InvalidOperationException("delete-item target must be a nested device item.");
            item.Delete();
            return context.Success(index, op, target.Reference, "Deleted hardware item.");
        }
    }
}
