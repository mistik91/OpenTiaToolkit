using OpenTiaToolkit.Core.Models;
using System;
using System.Collections.Generic;

namespace OpenTiaToolkit.Core.Services.Hardware
{
    internal static class HardwarePatchOperationContractValidator
    {
        private static readonly HashSet<string> SupportedOperations = new HashSet<string>(new[]
        {
            "create-device", "create-device-with-item", "plug-item", "delete-device", "delete-item",
            "create-subnet", "connect-node-to-subnet", "disconnect-node", "create-io-system",
            "connect-io-connector", "disconnect-io-connector",
            "set-attributes", "get-feature-attributes", "set-feature-attributes",
            "get-network-item-attributes", "set-network-item-attributes", "configure-profinet-device",
            "set-io-address-start", "change-drive-telegram", "configure-drive-telegram",
            "set-drive-telegram-address", "change-drive-telegram-size", "insert-drive-telegram",
            "erase-drive-telegram", "set-gsd-parameter-data", "get-gsd-parameter-data"
        }, StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> TargetOperations = new HashSet<string>(new[]
        {
            "plug-item", "delete-device", "delete-item", "connect-node-to-subnet", "disconnect-node",
            "create-io-system", "connect-io-connector", "disconnect-io-connector",
            "set-attributes", "get-feature-attributes", "set-feature-attributes",
            "get-network-item-attributes", "set-network-item-attributes", "configure-profinet-device",
            "set-io-address-start", "change-drive-telegram", "configure-drive-telegram",
            "set-drive-telegram-address", "change-drive-telegram-size", "insert-drive-telegram",
            "erase-drive-telegram", "set-gsd-parameter-data", "get-gsd-parameter-data"
        }, StringComparer.OrdinalIgnoreCase);

        public static IReadOnlyCollection<string> OperationNames => SupportedOperations;

        public static IReadOnlyList<HardwarePatchValidationIssue> Validate(
            HardwarePatchOperation operation,
            string op,
            int index)
        {
            var issues = new List<HardwarePatchValidationIssue>();
            if (!SupportedOperations.Contains(op))
            {
                issues.Add(Issue(index, "unsupported-operation",
                    $"Operation {index} uses unsupported op '{operation.Op}'."));
                return issues;
            }

            if (TargetOperations.Contains(op) &&
                string.IsNullOrWhiteSpace(operation.TargetRef) &&
                string.IsNullOrWhiteSpace(operation.Target?.DeviceName))
            {
                issues.Add(Issue(index, "missing-operation-target",
                    $"Operation {index} ({op}) requires targetRef or target.deviceName."));
            }

            switch (op)
            {
                case "create-device":
                    RequireText(operation.TypeIdentifier, "typeIdentifier", op, index, issues);
                    RequireText(operation.DeviceName, "deviceName", op, index, issues);
                    break;
                case "create-device-with-item":
                    RequireText(operation.TypeIdentifier, "typeIdentifier", op, index, issues);
                    RequireText(operation.DeviceName, "deviceName", op, index, issues);
                    RequireText(operation.ItemName, "itemName", op, index, issues);
                    break;
                case "plug-item":
                    RequireText(operation.TypeIdentifier, "typeIdentifier", op, index, issues);
                    RequireText(operation.ItemName, "itemName", op, index, issues);
                    RequireValue(operation.PositionNumber.HasValue, "positionNumber", op, index, issues);
                    break;
                case "create-subnet":
                    RequireText(operation.TypeIdentifier, "typeIdentifier", op, index, issues);
                    RequireText(operation.SubnetName, "subnetName", op, index, issues);
                    break;
                case "connect-node-to-subnet":
                    RequireText(operation.SubnetName, "subnetName", op, index, issues);
                    break;
                case "create-io-system":
                case "connect-io-connector":
                    RequireText(operation.IoSystemName, "ioSystemName", op, index, issues);
                    break;
                case "set-attributes":
                    RequireValues(operation.Attributes, "attributes", op, index, issues);
                    break;
                case "get-feature-attributes":
                    RequireText(operation.FeatureService, "featureService", op, index, issues);
                    RequireNames(operation.AttributeNames, "attributeNames", op, index, issues);
                    break;
                case "set-feature-attributes":
                    RequireText(operation.FeatureService, "featureService", op, index, issues);
                    RequireValues(operation.Attributes, "attributes", op, index, issues);
                    break;
                case "get-network-item-attributes":
                    RequireText(operation.NetworkCollection, "networkCollection", op, index, issues);
                    RequireNames(operation.AttributeNames, "attributeNames", op, index, issues);
                    break;
                case "set-network-item-attributes":
                    RequireText(operation.NetworkCollection, "networkCollection", op, index, issues);
                    RequireValues(operation.Attributes, "attributes", op, index, issues);
                    break;
                case "configure-profinet-device":
                    if (string.IsNullOrWhiteSpace(operation.IpAddress) &&
                        string.IsNullOrWhiteSpace(operation.SubnetMask) &&
                        string.IsNullOrWhiteSpace(operation.PnDeviceName) &&
                        !operation.PnDeviceNumber.HasValue)
                    {
                        issues.Add(Issue(index, "missing-required-choice",
                            $"Operation {index} ({op}) requires ipAddress, subnetMask, pnDeviceName, or pnDeviceNumber."));
                    }
                    break;
                case "set-io-address-start":
                    RequireValue(operation.AddressIndex.HasValue, "addressIndex", op, index, issues);
                    RequireValue(operation.StartAddress.HasValue, "startAddress", op, index, issues);
                    break;
                case "change-drive-telegram":
                case "insert-drive-telegram":
                    RequireText(operation.TelegramType, "telegramType", op, index, issues);
                    RequireValue(operation.TelegramNumber.HasValue, "telegramNumber", op, index, issues);
                    break;
                case "configure-drive-telegram":
                    RequireText(operation.TelegramType, "telegramType", op, index, issues);
                    RequireValue(operation.TelegramNumber.HasValue, "telegramNumber", op, index, issues);
                    if (!operation.InputStartAddress.HasValue && !operation.OutputStartAddress.HasValue)
                    {
                        issues.Add(Issue(index, "missing-required-choice",
                            $"Operation {index} ({op}) requires inputStartAddress or outputStartAddress."));
                    }
                    break;
                case "set-drive-telegram-address":
                    RequireText(operation.TelegramType, "telegramType", op, index, issues);
                    RequireText(operation.IoType, "ioType", op, index, issues);
                    RequireValue(operation.StartAddress.HasValue, "startAddress", op, index, issues);
                    break;
                case "change-drive-telegram-size":
                    RequireText(operation.TelegramType, "telegramType", op, index, issues);
                    RequireText(operation.IoType, "ioType", op, index, issues);
                    RequireValue(operation.SizeInWords.HasValue, "sizeInWords", op, index, issues);
                    break;
                case "erase-drive-telegram":
                    RequireText(operation.TelegramType, "telegramType", op, index, issues);
                    break;
                case "set-gsd-parameter-data":
                    RequireValue(operation.DatasetNumber.HasValue, "datasetNumber", op, index, issues);
                    RequireText(operation.DataHex, "dataHex", op, index, issues);
                    break;
                case "get-gsd-parameter-data":
                    RequireValue(operation.DatasetNumber.HasValue, "datasetNumber", op, index, issues);
                    if (!operation.Length.HasValue)
                    {
                        RequireValue(false, "length", op, index, issues);
                    }
                    else if (operation.Length.Value <= 0)
                    {
                        issues.Add(Issue(index, "invalid-required-field",
                            $"Operation {index} ({op}) requires length greater than zero."));
                    }
                    break;
            }

            return issues;
        }

        private static void RequireText(
            string? value,
            string field,
            string op,
            int index,
            ICollection<HardwarePatchValidationIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(value)) RequireValue(false, field, op, index, issues);
        }

        private static void RequireNames(
            string[]? values,
            string field,
            string op,
            int index,
            ICollection<HardwarePatchValidationIssue> issues)
        {
            if (values == null || values.Length == 0) RequireValue(false, field, op, index, issues);
        }

        private static void RequireValues(
            IDictionary<string, object?>? values,
            string field,
            string op,
            int index,
            ICollection<HardwarePatchValidationIssue> issues)
        {
            if (values == null || values.Count == 0) RequireValue(false, field, op, index, issues);
        }

        private static void RequireValue(
            bool present,
            string field,
            string op,
            int index,
            ICollection<HardwarePatchValidationIssue> issues)
        {
            if (!present)
            {
                issues.Add(Issue(index, "missing-required-field",
                    $"Operation {index} ({op}) requires {field}."));
            }
        }

        private static HardwarePatchValidationIssue Issue(int index, string code, string message) =>
            new HardwarePatchValidationIssue
            {
                OperationIndex = index,
                Code = code,
                Message = message
            };
    }
}
