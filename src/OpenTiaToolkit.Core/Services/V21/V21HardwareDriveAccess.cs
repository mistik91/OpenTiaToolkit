using OpenTiaToolkit.Core.Models;
using Siemens.Engineering.HW;
using Siemens.Engineering.MC.Drives;
using Siemens.Engineering.MC.Drives.Enums;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OpenTiaToolkit.Core.Services.V21
{
    /// <summary>
    /// Owns Startdrive discovery, telegram mutation, address projection, and drive parameters.
    /// </summary>
    internal sealed class V21HardwareDriveAccess
    {
        private readonly V21HardwareAttributeAccess _attributes;

        public V21HardwareDriveAccess(V21HardwareAttributeAccess attributes)
        {
            _attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
        }

        public IReadOnlyList<HardwareDriveTelegramInfo> GetDriveTelegrams(
            DeviceItem target,
            HardwareObjectRef reference,
            IEnumerable<string>? addressAttributeNames)
        {
            var container = _attributes.GetFeatureService(target, "DriveObjectContainer") as DriveObjectContainer;
            if (container == null) return Array.Empty<HardwareDriveTelegramInfo>();
            var result = new List<HardwareDriveTelegramInfo>();
            var driveIndex = 0;
            foreach (var driveObject in container.DriveObjects)
            {
                var telegramIndex = 0;
                foreach (var telegram in driveObject.Telegrams)
                {
                    var addresses = new List<HardwareIoAddressInfo>();
                    var addressIndex = 0;
                    foreach (var address in telegram.Addresses)
                        addresses.Add(_attributes.ToIoAddressInfo(address, reference, addressIndex++, addressAttributeNames));
                    result.Add(new HardwareDriveTelegramInfo
                    {
                        Target = reference,
                        DriveObjectIndex = driveIndex,
                        DriveObjectNumber = TryGetPropertyString(driveObject, "DriveObjectNumber"),
                        TelegramIndex = telegramIndex++,
                        TelegramNumber = TryGetPropertyString(telegram, "TelegramNumber"),
                        TelegramType = TryGetPropertyString(telegram, "Type"),
                        Addresses = addresses
                    });
                }
                driveIndex++;
            }
            return result;
        }

        public HardwareDriveTelegramChangeInfo ChangeDriveTelegramNumber(
            DeviceItem target,
            int driveObjectIndex,
            string telegramType,
            int telegramNumber,
            int? expectedTelegramNumber)
        {
            var telegram = RequireTelegram(target, driveObjectIndex, telegramType);
            var before = telegram.TelegramNumber;
            if (expectedTelegramNumber.HasValue && before != expectedTelegramNumber.Value)
                throw new InvalidOperationException($"{telegramType} number differs from expectedTelegramNumber.");
            if (!telegram.CanChangeTelegram(telegramNumber))
                throw new InvalidOperationException($"{telegramType} cannot be changed to telegram number {telegramNumber}.");
            telegram.TelegramNumber = telegramNumber;
            return new HardwareDriveTelegramChangeInfo
            {
                DriveObjectIndex = driveObjectIndex,
                TelegramType = telegramType,
                BeforeTelegramNumber = before,
                WrittenTelegramNumber = telegramNumber
            };
        }

        public HardwareDriveTelegramChangeInfo SetDriveTelegramAddress(
            DeviceItem target,
            int driveObjectIndex,
            string telegramType,
            string ioType,
            int startAddress,
            int? expectedStartAddress) =>
            SetDriveTelegramAddress(
                RequireTelegram(target, driveObjectIndex, telegramType),
                driveObjectIndex,
                telegramType,
                ParseAddressIoType(ioType),
                startAddress,
                expectedStartAddress);

        public IReadOnlyList<HardwareDriveTelegramChangeInfo> ConfigureDriveTelegram(
            DeviceItem target,
            int driveObjectIndex,
            string telegramType,
            int telegramNumber,
            int? expectedTelegramNumber,
            int? inputStartAddress,
            int? expectedInputStartAddress,
            int? outputStartAddress,
            int? expectedOutputStartAddress)
        {
            var driveObject = RequireDriveObject(target, driveObjectIndex);
            var type = ParseTelegramType(telegramType);
            var requiredDirections = RequiredDirections(inputStartAddress, outputStartAddress).ToArray();
            var changes = new List<HardwareDriveTelegramChangeInfo>();
            var telegram = driveObject.Telegrams.Find(type);
            var beforeTelegramNumber = telegram?.TelegramNumber;
            if (telegram == null)
            {
                telegram = InsertTelegram(driveObject, type, telegramType, telegramNumber);
                changes.Add(new HardwareDriveTelegramChangeInfo
                {
                    DriveObjectIndex = driveObjectIndex,
                    TelegramType = telegramType,
                    WrittenTelegramNumber = telegramNumber
                });
            }
            else
            {
                if (expectedTelegramNumber.HasValue && beforeTelegramNumber != expectedTelegramNumber.Value)
                    throw new InvalidOperationException($"{telegramType} number differs from expectedTelegramNumber.");
                if (beforeTelegramNumber != telegramNumber)
                {
                    if (telegram.CanChangeTelegram(telegramNumber))
                        telegram.TelegramNumber = telegramNumber;
                    else
                        telegram = RecreateTelegram(driveObject, type, telegramType, telegramNumber);
                    changes.Add(new HardwareDriveTelegramChangeInfo
                    {
                        DriveObjectIndex = driveObjectIndex,
                        TelegramType = telegramType,
                        BeforeTelegramNumber = beforeTelegramNumber,
                        WrittenTelegramNumber = telegramNumber
                    });
                }
            }

            telegram = driveObject.Telegrams.Find(type)
                ?? throw new InvalidOperationException($"{telegramType} telegram was not found after configuration.");
            if (!HasRequiredAddresses(telegram, requiredDirections))
            {
                telegram = RecoverTelegramAddresses(driveObject, type, telegramType, telegramNumber, requiredDirections);
                if (!HasRequiredAddresses(telegram, requiredDirections))
                    throw MissingAddressException(telegram, telegramType, requiredDirections);
            }
            if (inputStartAddress.HasValue)
                changes.Add(SetDriveTelegramAddress(telegram, driveObjectIndex, telegramType, AddressIoType.Input, inputStartAddress.Value, expectedInputStartAddress));
            if (outputStartAddress.HasValue)
                changes.Add(SetDriveTelegramAddress(telegram, driveObjectIndex, telegramType, AddressIoType.Output, outputStartAddress.Value, expectedOutputStartAddress));
            return changes;
        }

        public HardwareDriveTelegramChangeInfo ChangeDriveTelegramSize(
            DeviceItem target,
            int driveObjectIndex,
            string telegramType,
            string ioType,
            int sizeInWords,
            int? expectedSizeInWords,
            bool keepOriginalAddress)
        {
            var telegram = RequireTelegram(target, driveObjectIndex, telegramType);
            var direction = ParseAddressIoType(ioType);
            var before = telegram.GetSize(direction);
            if (expectedSizeInWords.HasValue && before != expectedSizeInWords.Value)
                throw new InvalidOperationException($"{telegramType} {ioType} size differs from expectedSizeInWords.");
            if (!telegram.CanChangeSize(direction, sizeInWords, keepOriginalAddress))
                throw new InvalidOperationException($"{telegramType} {ioType} size cannot be changed to {sizeInWords} word(s).");
            var beforeBytes = telegram.GetSizeInBytes(direction);
            telegram.ChangeSize(direction, sizeInWords, keepOriginalAddress);
            return new HardwareDriveTelegramChangeInfo
            {
                DriveObjectIndex = driveObjectIndex,
                TelegramType = telegramType,
                BeforeTelegramNumber = telegram.TelegramNumber,
                WrittenTelegramNumber = telegram.TelegramNumber,
                IoType = ioType,
                BeforeSizeInWords = before,
                WrittenSizeInWords = sizeInWords,
                BeforeSizeInBytes = beforeBytes,
                WrittenSizeInBytes = telegram.GetSizeInBytes(direction)
            };
        }

        public HardwareDriveTelegramChangeInfo InsertDriveTelegram(
            DeviceItem target,
            int driveObjectIndex,
            string telegramType,
            int telegramNumber)
        {
            var driveObject = RequireDriveObject(target, driveObjectIndex);
            var type = ParseTelegramType(telegramType);
            if (!driveObject.Telegrams.CanInsertTelegram(telegramNumber, type))
                throw new InvalidOperationException($"{telegramType} telegram number {telegramNumber} cannot be inserted.");
            driveObject.Telegrams.InsertTelegram(telegramNumber, type);
            return new HardwareDriveTelegramChangeInfo
            {
                DriveObjectIndex = driveObjectIndex,
                TelegramType = telegramType,
                WrittenTelegramNumber = telegramNumber
            };
        }

        public HardwareDriveTelegramChangeInfo EraseDriveTelegram(
            DeviceItem target,
            int driveObjectIndex,
            string telegramType,
            int? expectedTelegramNumber)
        {
            var driveObject = RequireDriveObject(target, driveObjectIndex);
            var type = ParseTelegramType(telegramType);
            var telegram = driveObject.Telegrams.Find(type)
                ?? throw new InvalidOperationException($"{telegramType} telegram was not found.");
            var before = telegram.TelegramNumber;
            if (expectedTelegramNumber.HasValue && before != expectedTelegramNumber.Value)
                throw new InvalidOperationException($"{telegramType} number differs from expectedTelegramNumber.");
            driveObject.Telegrams.EraseTelegram(type);
            return new HardwareDriveTelegramChangeInfo
            {
                DriveObjectIndex = driveObjectIndex,
                TelegramType = telegramType,
                BeforeTelegramNumber = before
            };
        }

        public IReadOnlyList<HardwareDriveParameterInfo> GetDriveParameters(
            DeviceItem target,
            HardwareObjectRef reference,
            int? requestedDriveObjectIndex,
            string? filter,
            int maxResults)
        {
            var container = _attributes.GetFeatureService(target, "DriveObjectContainer") as DriveObjectContainer;
            if (container == null) return Array.Empty<HardwareDriveParameterInfo>();
            var result = new List<HardwareDriveParameterInfo>();
            var driveIndex = 0;
            foreach (DriveObject driveObject in container.DriveObjects)
            {
                if (!requestedDriveObjectIndex.HasValue || requestedDriveObjectIndex.Value == driveIndex)
                {
                    foreach (DriveParameter parameter in driveObject.Parameters)
                    {
                        if (!MatchesParameter(Convert.ToString(SafeRead(parameter, "Name")), Convert.ToInt32(SafeRead(parameter, "Number") ?? 0), Convert.ToString(SafeRead(parameter, "ParameterText")), filter)) continue;
                        result.Add(ToParameterInfo(reference, driveIndex, driveObject, parameter, true));
                        if (result.Count >= maxResults) return result;
                    }
                    foreach (ReadDriveParameter parameter in driveObject.ReadParameters)
                    {
                        if (!MatchesParameter(Convert.ToString(SafeRead(parameter, "Name")), Convert.ToInt32(SafeRead(parameter, "Number") ?? 0), Convert.ToString(SafeRead(parameter, "ParameterText")), filter)) continue;
                        result.Add(ToParameterInfo(reference, driveIndex, driveObject, parameter, false));
                        if (result.Count >= maxResults) return result;
                    }
                }
                driveIndex++;
            }
            return result;
        }

        private DriveObject RequireDriveObject(DeviceItem target, int index)
        {
            var container = _attributes.GetFeatureService(target, "DriveObjectContainer") as DriveObjectContainer
                ?? throw new InvalidOperationException("Hardware item does not provide the DriveObjectContainer feature service.");
            var driveObjects = container.DriveObjects.ToList();
            if (index < 0 || index >= driveObjects.Count)
                throw new InvalidOperationException($"Drive object was not found at index {index}.");
            return driveObjects[index];
        }

        private Telegram RequireTelegram(DeviceItem target, int driveObjectIndex, string telegramType) =>
            RequireDriveObject(target, driveObjectIndex).Telegrams.Find(ParseTelegramType(telegramType))
            ?? throw new InvalidOperationException($"{telegramType} telegram was not found.");

        private static Telegram InsertTelegram(DriveObject driveObject, TelegramType type, string telegramType, int telegramNumber)
        {
            if (!driveObject.Telegrams.CanInsertTelegram(telegramNumber, type))
                throw new InvalidOperationException($"{telegramType} telegram number {telegramNumber} cannot be inserted.");
            driveObject.Telegrams.InsertTelegram(telegramNumber, type);
            return driveObject.Telegrams.Find(type)
                ?? throw new InvalidOperationException($"{telegramType} telegram was not found after insertion.");
        }

        private static Telegram RecreateTelegram(DriveObject driveObject, TelegramType type, string telegramType, int telegramNumber)
        {
            try
            {
                if (driveObject.Telegrams.Find(type) != null) driveObject.Telegrams.EraseTelegram(type);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"{telegramType} telegram number {telegramNumber} did not expose the required IO address object(s), and the telegram could not be recreated: {ex.Message}",
                    ex);
            }
            return InsertTelegram(driveObject, type, telegramType, telegramNumber);
        }

        private static Telegram RecoverTelegramAddresses(
            DriveObject driveObject,
            TelegramType type,
            string telegramType,
            int telegramNumber,
            IEnumerable<AddressIoType> directions) =>
            type == TelegramType.MainTelegram
                ? RefreshMainTelegramSizes(driveObject, type, telegramType, telegramNumber, directions)
                : RecreateTelegram(driveObject, type, telegramType, telegramNumber);

        private static Telegram RefreshMainTelegramSizes(
            DriveObject driveObject,
            TelegramType type,
            string telegramType,
            int telegramNumber,
            IEnumerable<AddressIoType> directions)
        {
            var telegram = driveObject.Telegrams.Find(type)
                ?? throw new InvalidOperationException($"{telegramType} telegram was not found before size refresh.");
            try
            {
                foreach (var direction in directions)
                {
                    var size = telegram.GetSize(direction);
                    if (telegram.CanChangeSize(direction, size, false))
                        telegram.ChangeSize(direction, size, false);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"{telegramType} telegram number {telegramNumber} did not expose the required IO address object(s), and the main telegram size-refresh recovery failed: {ex.Message}",
                    ex);
            }
            return driveObject.Telegrams.Find(type)
                ?? throw new InvalidOperationException($"{telegramType} telegram was not found after size refresh.");
        }

        private static HardwareDriveTelegramChangeInfo SetDriveTelegramAddress(
            Telegram telegram,
            int driveObjectIndex,
            string telegramType,
            AddressIoType direction,
            int startAddress,
            int? expectedStartAddress)
        {
            var address = telegram.Addresses.FirstOrDefault(candidate => candidate.IoType == direction)
                ?? throw MissingAddressException(telegram, telegramType, new[] { direction });
            var before = address.StartAddress;
            if (expectedStartAddress.HasValue && before != expectedStartAddress.Value)
                throw new InvalidOperationException($"{telegramType} {direction} address differs from expectedStartAddress.");
            address.StartAddress = startAddress;
            return new HardwareDriveTelegramChangeInfo
            {
                DriveObjectIndex = driveObjectIndex,
                TelegramType = telegramType,
                BeforeTelegramNumber = telegram.TelegramNumber,
                WrittenTelegramNumber = telegram.TelegramNumber,
                IoType = Convert.ToString(direction, CultureInfo.InvariantCulture),
                BeforeStartAddress = before,
                WrittenStartAddress = startAddress
            };
        }

        private static IEnumerable<AddressIoType> RequiredDirections(int? input, int? output)
        {
            if (input.HasValue) yield return AddressIoType.Input;
            if (output.HasValue) yield return AddressIoType.Output;
        }

        private static bool HasRequiredAddresses(Telegram telegram, IEnumerable<AddressIoType> directions)
        {
            var addresses = telegram.Addresses.ToList();
            return directions.All(direction => addresses.Any(address => address.IoType == direction));
        }

        private static InvalidOperationException MissingAddressException(
            Telegram telegram,
            string telegramType,
            IEnumerable<AddressIoType> directions)
        {
            var requested = string.Join(", ", directions.Select(direction => Convert.ToString(direction, CultureInfo.InvariantCulture)));
            var available = string.Join(", ", telegram.Addresses.Select(address => Convert.ToString(address.IoType, CultureInfo.InvariantCulture)));
            if (string.IsNullOrWhiteSpace(available)) available = "none";
            return new InvalidOperationException(
                $"{telegramType} telegram number {telegram.TelegramNumber} has no required address object(s): {requested}. Available address IoType(s): {available}. configure-drive-telegram could not materialize the missing Startdrive address object(s) for this target.");
        }

        private static TelegramType ParseTelegramType(string value)
        {
            if (!Enum.TryParse(value, true, out TelegramType type))
                throw new InvalidOperationException($"Unsupported telegramType: {value}");
            return type;
        }

        private static AddressIoType ParseAddressIoType(string value)
        {
            if (!Enum.TryParse(value, true, out AddressIoType type))
                throw new InvalidOperationException($"Unsupported ioType: {value}");
            return type;
        }

        private static HardwareDriveParameterInfo ToParameterInfo(
            HardwareObjectRef reference,
            int driveIndex,
            DriveObject driveObject,
            object parameter,
            bool writable)
        {
            object? Read(string name) => SafeRead(parameter, name);
            var enumValues = Read("EnumValueList") as IDictionary<int, string>;
            return new HardwareDriveParameterInfo
            {
                Target = reference,
                DriveObjectIndex = driveIndex,
                DriveObjectNumber = Convert.ToString(SafeRead(driveObject, "DriveObjectNumber"), CultureInfo.InvariantCulture),
                Name = Convert.ToString(Read("Name"), CultureInfo.InvariantCulture) ?? string.Empty,
                Number = Convert.ToInt32(Read("Number"), CultureInfo.InvariantCulture),
                ArrayIndex = Convert.ToInt32(Read("ArrayIndex"), CultureInfo.InvariantCulture),
                ArrayLength = Convert.ToInt32(Read("ArrayLength"), CultureInfo.InvariantCulture),
                ParameterText = Convert.ToString(Read("ParameterText"), CultureInfo.InvariantCulture),
                Value = Convert.ToString(Read("Value"), CultureInfo.InvariantCulture),
                Unit = Convert.ToString(Read("Unit"), CultureInfo.InvariantCulture),
                Minimum = Convert.ToString(Read("MinValue"), CultureInfo.InvariantCulture),
                Maximum = Convert.ToString(Read("MaxValue"), CultureInfo.InvariantCulture),
                IsWritable = writable,
                EnumValues = enumValues == null ? new Dictionary<int, string>() : new Dictionary<int, string>(enumValues)
            };
        }

        private static object? SafeRead(object target, string propertyName)
        {
            try { return target.GetType().GetProperty(propertyName)?.GetValue(target); }
            catch (Exception) { return null; }
        }

        private static bool MatchesParameter(string? name, int number, string? text, string? filter) =>
            string.IsNullOrWhiteSpace(filter)
            || (name?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
            || (text?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
            || number.ToString(CultureInfo.InvariantCulture).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;

        private static string? TryGetPropertyString(object target, string propertyName)
        {
            try { return Convert.ToString(TiaReflectionHelper.GetProperty(target, propertyName), CultureInfo.InvariantCulture); }
            catch { return null; }
        }
    }
}
