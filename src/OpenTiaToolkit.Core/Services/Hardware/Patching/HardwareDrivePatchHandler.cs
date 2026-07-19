using OpenTiaToolkit.Core.Models;
using Siemens.Engineering.HW;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTiaToolkit.Core.Services.Hardware.Patching
{
    internal sealed class HardwareDrivePatchHandler : IHardwarePatchOperationHandler
    {
        public IReadOnlyCollection<string> Operations { get; } = new[]
        {
            "set-io-address-start", "change-drive-telegram", "configure-drive-telegram",
            "set-drive-telegram-address", "change-drive-telegram-size", "insert-drive-telegram", "erase-drive-telegram"
        };

        public HardwareOperationResult Execute(string operation, int index, HardwarePatchOperation request, HardwarePatchExecutionContext context)
        {
            switch (operation)
            {
                case "set-io-address-start": return SetIoAddressStart(index, request, context);
                case "change-drive-telegram": return ChangeTelegram(index, request, context);
                case "configure-drive-telegram": return ConfigureTelegram(index, request, context);
                case "set-drive-telegram-address": return SetTelegramAddress(index, request, context);
                case "change-drive-telegram-size": return ChangeTelegramSize(index, request, context);
                case "insert-drive-telegram": return InsertTelegram(index, request, context);
                case "erase-drive-telegram": return EraseTelegram(index, request, context);
                default: throw new InvalidOperationException($"Unsupported drive operation: {operation}");
            }
        }

        private static HardwareOperationResult SetIoAddressStart(int index, HardwarePatchOperation op, HardwarePatchExecutionContext context)
        {
            if (!op.AddressIndex.HasValue) throw new InvalidOperationException("addressIndex is required for set-io-address-start.");
            if (!op.StartAddress.HasValue) throw new InvalidOperationException("startAddress is required for set-io-address-start.");
            var target = context.Resolve(op);
            if (!(target.Object is DeviceItem item)) throw new InvalidOperationException("set-io-address-start target must be a nested device item.");
            var change = context.Adapter.SetIoAddressStart(item, op.AddressIndex.Value, op.StartAddress.Value, op.ExpectedStartAddress);
            return context.Success(index, op, target.Reference, "Set hardware IO start address.", change);
        }

        private static HardwareOperationResult ChangeTelegram(int index, HardwarePatchOperation op, HardwarePatchExecutionContext context)
        {
            HardwarePatchRules.RequireText(op.TelegramType, "telegramType");
            if (!op.TelegramNumber.HasValue) throw new InvalidOperationException("telegramNumber is required for change-drive-telegram.");
            var item = ResolveDriveItem(op, context, "change-drive-telegram", out var target);
            var change = context.Adapter.ChangeDriveTelegramNumber(item, op.DriveObjectIndex ?? 0, op.TelegramType!, op.TelegramNumber.Value, op.ExpectedTelegramNumber);
            return context.Success(index, op, target.Reference, "Changed Startdrive telegram number.", change);
        }

        private static HardwareOperationResult SetTelegramAddress(int index, HardwarePatchOperation op, HardwarePatchExecutionContext context)
        {
            HardwarePatchRules.RequireText(op.TelegramType, "telegramType");
            HardwarePatchRules.RequireText(op.IoType, "ioType");
            if (!op.StartAddress.HasValue) throw new InvalidOperationException("startAddress is required for set-drive-telegram-address.");
            var item = ResolveDriveItem(op, context, "set-drive-telegram-address", out var target);
            var change = context.Adapter.SetDriveTelegramAddress(item, op.DriveObjectIndex ?? 0, op.TelegramType!, op.IoType!, op.StartAddress.Value, op.ExpectedStartAddress);
            return context.Success(index, op, target.Reference, "Set Startdrive telegram IO start address.", change);
        }

        private static HardwareOperationResult ConfigureTelegram(int index, HardwarePatchOperation op, HardwarePatchExecutionContext context)
        {
            HardwarePatchRules.RequireText(op.TelegramType, "telegramType");
            if (!op.TelegramNumber.HasValue) throw new InvalidOperationException("telegramNumber is required for configure-drive-telegram.");
            if (!op.InputStartAddress.HasValue && !op.OutputStartAddress.HasValue)
                throw new InvalidOperationException("configure-drive-telegram requires inputStartAddress or outputStartAddress.");
            var item = ResolveDriveItem(op, context, "configure-drive-telegram", out var target);
            var changes = context.Adapter.ConfigureDriveTelegram(item, op.DriveObjectIndex ?? 0, op.TelegramType!, op.TelegramNumber.Value,
                op.ExpectedTelegramNumber, op.InputStartAddress, op.ExpectedInputStartAddress, op.OutputStartAddress, op.ExpectedOutputStartAddress);
            return context.Success(index, op, target.Reference, "Configured Startdrive telegram number and IO start address(es).",
                new HardwareDriveTelegramConfigurationInfo { Changes = changes.ToList() });
        }

        private static HardwareOperationResult ChangeTelegramSize(int index, HardwarePatchOperation op, HardwarePatchExecutionContext context)
        {
            HardwarePatchRules.RequireText(op.TelegramType, "telegramType");
            HardwarePatchRules.RequireText(op.IoType, "ioType");
            if (!op.SizeInWords.HasValue) throw new InvalidOperationException("sizeInWords is required for change-drive-telegram-size.");
            var item = ResolveDriveItem(op, context, "change-drive-telegram-size", out var target);
            var change = context.Adapter.ChangeDriveTelegramSize(item, op.DriveObjectIndex ?? 0, op.TelegramType!, op.IoType!,
                op.SizeInWords.Value, op.ExpectedSizeInWords, op.KeepOriginalAddress);
            return context.Success(index, op, target.Reference, "Changed Startdrive telegram IO size.", change);
        }

        private static HardwareOperationResult InsertTelegram(int index, HardwarePatchOperation op, HardwarePatchExecutionContext context)
        {
            HardwarePatchRules.RequireText(op.TelegramType, "telegramType");
            if (!op.TelegramNumber.HasValue) throw new InvalidOperationException("telegramNumber is required for insert-drive-telegram.");
            var item = ResolveDriveItem(op, context, "insert-drive-telegram", out var target);
            var change = context.Adapter.InsertDriveTelegram(item, op.DriveObjectIndex ?? 0, op.TelegramType!, op.TelegramNumber.Value);
            return context.Success(index, op, target.Reference, "Inserted Startdrive telegram.", change);
        }

        private static HardwareOperationResult EraseTelegram(int index, HardwarePatchOperation op, HardwarePatchExecutionContext context)
        {
            HardwarePatchRules.RequireText(op.TelegramType, "telegramType");
            var item = ResolveDriveItem(op, context, "erase-drive-telegram", out var target);
            var change = context.Adapter.EraseDriveTelegram(item, op.DriveObjectIndex ?? 0, op.TelegramType!, op.ExpectedTelegramNumber);
            return context.Success(index, op, target.Reference, "Erased Startdrive telegram.", change);
        }

        private static DeviceItem ResolveDriveItem(HardwarePatchOperation op, HardwarePatchExecutionContext context, string name, out HardwareResolvedTarget target)
        {
            target = context.Resolve(op);
            if (!(target.Object is DeviceItem item)) throw new InvalidOperationException($"{name} target must be a nested drive device item.");
            return item;
        }
    }
}
