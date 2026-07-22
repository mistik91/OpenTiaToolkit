using OpenTiaToolkit.Core.Models;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.MC.Drives;
using Siemens.Engineering.MC.Drives.Enums;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace OpenTiaToolkit.Core.Services
{
    /// <summary>
    /// Isolates V21 feature-service and invocation details used by generic hardware workflows.
    /// </summary>
    internal sealed class V21HardwareAdapter
    {
        private static readonly string[] DefaultNodeAttributeNames =
        {
            "Address",
            "SubnetMask",
            "PnDeviceName",
            "PnDeviceNameAutoGeneration"
        };

        private static readonly string[] DefaultIoConnectorAttributeNames =
        {
            "PnDeviceNumber"
        };

        private readonly V21.V21TypeServiceResolver _services;

        public V21HardwareAdapter(V21OpennessAdapter? adapter = null)
        {
            _services = (adapter ?? new V21OpennessAdapter()).Types;
        }

        public IReadOnlyList<HardwareCatalogItemInfo> FindCatalogItems(TiaPortal portal, string searchText, string? culture)
        {
            var entries = string.IsNullOrWhiteSpace(culture)
                ? portal.HardwareCatalog.Find(searchText ?? string.Empty)
                : portal.HardwareCatalog.Find(searchText ?? string.Empty, culture);

            return entries.Select(entry => new HardwareCatalogItemInfo
            {
                TypeIdentifier = entry.TypeIdentifier,
                TypeIdentifierNormalized = entry.TypeIdentifierNormalized,
                TypeName = entry.TypeName,
                ArticleNumber = entry.ArticleNumber,
                Description = entry.Description,
                Version = entry.Version,
                CatalogPath = entry.CatalogPath
            }).ToList();
        }

        public object? GetFeatureService(HardwareObject target, string serviceType)
        {
            return _services.GetService(target, serviceType);
        }

        public IReadOnlyList<HardwareAttributeInfo> GetAttributeInfos(HardwareObject target, IEnumerable<string>? attributeNames)
        {
            var requested = attributeNames?.Where(name => !string.IsNullOrWhiteSpace(name)).ToArray() ?? Array.Empty<string>();
            if (requested.Length > 0)
            {
                return requested.Select(name => new HardwareAttributeInfo
                {
                    Name = name,
                    Value = TryGetAttribute(target, name)
                }).ToList();
            }

            var infos = target.GetAttributeInfos();
            var result = new List<HardwareAttributeInfo>();
            foreach (var info in infos)
            {
                var name = TiaReflectionHelper.GetProperty(info, "Name")?.ToString();
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                result.Add(new HardwareAttributeInfo
                {
                    Name = name!,
                    IsReadOnly = TiaReflectionHelper.GetProperty(info, "IsReadOnly") as bool?
                });
            }

            return result;
        }

        public string[] FindModuleTypes(ProjectBase project, string partialTypeIdentifier)
        {
            var utilities = TiaReflectionHelper.GetProperty(project, "HwUtilities")
                ?? throw new InvalidOperationException("The current project does not expose hardware utilities.");
            var provider = Invoke(utilities, "Find", "ModuleInformationProvider");
            if (provider == null)
            {
                return Array.Empty<string>();
            }

            var method = provider.GetType().GetMethod("FindModuleTypes", new[] { typeof(string) });
            return method?.Invoke(provider, new object[] { partialTypeIdentifier ?? string.Empty }) as string[] ?? Array.Empty<string>();
        }

        public object? GetAttribute(HardwareObject target, string name)
        {
            return target.GetAttribute(name);
        }

        public IReadOnlyList<HardwareIoAddressInfo> GetIoAddresses(DeviceItem target, HardwareObjectRef reference, IEnumerable<string>? attributeNames)
        {
            var result = new List<HardwareIoAddressInfo>();
            var index = 0;
            foreach (Address address in target.Addresses)
            {
                result.Add(ToIoAddressInfo(address, reference, index++, attributeNames));
            }
            return result;
        }

        public HardwareIoAddressChangeInfo SetIoAddressStart(DeviceItem target, int addressIndex, int startAddress, int? expectedStartAddress)
        {
            var addresses = target.Addresses.Cast<Address>().ToList();
            if (addressIndex < 0 || addressIndex >= addresses.Count)
            {
                throw new InvalidOperationException($"Hardware item has no IO address at index {addressIndex}.");
            }

            var address = addresses[addressIndex];
            var before = address.StartAddress;
            if (expectedStartAddress.HasValue && before != expectedStartAddress.Value)
            {
                throw new InvalidOperationException($"IO address start differs from expectedStartAddress at index {addressIndex}.");
            }

            address.StartAddress = startAddress;
            return new HardwareIoAddressChangeInfo
            {
                AddressIndex = addressIndex,
                IoType = Convert.ToString(address.IoType, CultureInfo.InvariantCulture),
                BeforeStartAddress = before,
                WrittenStartAddress = startAddress
            };
        }

        public IReadOnlyList<HardwareDriveTelegramInfo> GetDriveTelegrams(DeviceItem target, HardwareObjectRef reference, IEnumerable<string>? addressAttributeNames)
        {
            var container = GetFeatureService(target, "DriveObjectContainer") as DriveObjectContainer;
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
                    {
                        addresses.Add(ToIoAddressInfo(address, reference, addressIndex++, addressAttributeNames));
                    }
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

        public HardwareDriveTelegramChangeInfo ChangeDriveTelegramNumber(DeviceItem target, int driveObjectIndex, string telegramType, int telegramNumber, int? expectedTelegramNumber)
        {
            var telegram = RequireTelegram(target, driveObjectIndex, telegramType);
            var before = telegram.TelegramNumber;
            if (expectedTelegramNumber.HasValue && before != expectedTelegramNumber.Value)
            {
                throw new InvalidOperationException($"{telegramType} number differs from expectedTelegramNumber.");
            }
            if (!telegram.CanChangeTelegram(telegramNumber))
            {
                throw new InvalidOperationException($"{telegramType} cannot be changed to telegram number {telegramNumber}.");
            }

            telegram.TelegramNumber = telegramNumber;
            return new HardwareDriveTelegramChangeInfo
            {
                DriveObjectIndex = driveObjectIndex,
                TelegramType = telegramType,
                BeforeTelegramNumber = before,
                WrittenTelegramNumber = telegramNumber
            };
        }

        public HardwareDriveTelegramChangeInfo SetDriveTelegramAddress(DeviceItem target, int driveObjectIndex, string telegramType, string ioType, int startAddress, int? expectedStartAddress)
        {
            var telegram = RequireTelegram(target, driveObjectIndex, telegramType);
            var direction = ParseAddressIoType(ioType);
            return SetDriveTelegramAddress(telegram, driveObjectIndex, telegramType, direction, startAddress, expectedStartAddress);
        }

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
                {
                    throw new InvalidOperationException($"{telegramType} number differs from expectedTelegramNumber.");
                }

                if (beforeTelegramNumber != telegramNumber)
                {
                    if (telegram.CanChangeTelegram(telegramNumber))
                    {
                        telegram.TelegramNumber = telegramNumber;
                    }
                    else
                    {
                        telegram = RecreateTelegram(driveObject, type, telegramType, telegramNumber);
                    }

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
                {
                    throw MissingAddressException(telegram, telegramType, requiredDirections);
                }
            }

            if (inputStartAddress.HasValue)
            {
                changes.Add(SetDriveTelegramAddress(
                    telegram,
                    driveObjectIndex,
                    telegramType,
                    AddressIoType.Input,
                    inputStartAddress.Value,
                    expectedInputStartAddress));
            }

            if (outputStartAddress.HasValue)
            {
                changes.Add(SetDriveTelegramAddress(
                    telegram,
                    driveObjectIndex,
                    telegramType,
                    AddressIoType.Output,
                    outputStartAddress.Value,
                    expectedOutputStartAddress));
            }

            return changes;
        }

        public HardwareDriveTelegramChangeInfo ChangeDriveTelegramSize(DeviceItem target, int driveObjectIndex, string telegramType, string ioType, int sizeInWords, int? expectedSizeInWords, bool keepOriginalAddress)
        {
            var telegram = RequireTelegram(target, driveObjectIndex, telegramType);
            var direction = ParseAddressIoType(ioType);
            var before = telegram.GetSize(direction);
            if (expectedSizeInWords.HasValue && before != expectedSizeInWords.Value)
            {
                throw new InvalidOperationException($"{telegramType} {ioType} size differs from expectedSizeInWords.");
            }
            if (!telegram.CanChangeSize(direction, sizeInWords, keepOriginalAddress))
            {
                throw new InvalidOperationException($"{telegramType} {ioType} size cannot be changed to {sizeInWords} word(s).");
            }

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

        public HardwareDriveTelegramChangeInfo InsertDriveTelegram(DeviceItem target, int driveObjectIndex, string telegramType, int telegramNumber)
        {
            var driveObject = RequireDriveObject(target, driveObjectIndex);
            var type = ParseTelegramType(telegramType);
            if (!driveObject.Telegrams.CanInsertTelegram(telegramNumber, type))
            {
                throw new InvalidOperationException($"{telegramType} telegram number {telegramNumber} cannot be inserted.");
            }

            driveObject.Telegrams.InsertTelegram(telegramNumber, type);
            return new HardwareDriveTelegramChangeInfo
            {
                DriveObjectIndex = driveObjectIndex,
                TelegramType = telegramType,
                WrittenTelegramNumber = telegramNumber
            };
        }

        public HardwareDriveTelegramChangeInfo EraseDriveTelegram(DeviceItem target, int driveObjectIndex, string telegramType, int? expectedTelegramNumber)
        {
            var driveObject = RequireDriveObject(target, driveObjectIndex);
            var type = ParseTelegramType(telegramType);
            var telegram = driveObject.Telegrams.Find(type)
                ?? throw new InvalidOperationException($"{telegramType} telegram was not found.");
            var before = telegram.TelegramNumber;
            if (expectedTelegramNumber.HasValue && before != expectedTelegramNumber.Value)
            {
                throw new InvalidOperationException($"{telegramType} number differs from expectedTelegramNumber.");
            }

            driveObject.Telegrams.EraseTelegram(type);
            return new HardwareDriveTelegramChangeInfo
            {
                DriveObjectIndex = driveObjectIndex,
                TelegramType = telegramType,
                BeforeTelegramNumber = before
            };
        }

        public IReadOnlyList<HardwareAttributeInfo> GetFeatureAttributeInfos(HardwareObject target, string featureService, IEnumerable<string>? attributeNames)
        {
            var feature = RequireFeatureService(target, featureService);
            var requested = attributeNames?.Where(name => !string.IsNullOrWhiteSpace(name)).ToArray() ?? Array.Empty<string>();
            if (requested.Length > 0)
            {
                return requested.Select(name => new HardwareAttributeInfo
                {
                    Name = name,
                    Value = Convert.ToString(GetFeatureAttribute(feature, name), CultureInfo.InvariantCulture)
                }).ToList();
            }

            var infos = Invoke(feature, "GetAttributeInfos") as System.Collections.IEnumerable;
            if (infos == null)
            {
                return Array.Empty<HardwareAttributeInfo>();
            }

            var result = new List<HardwareAttributeInfo>();
            foreach (var info in infos)
            {
                var name = TiaReflectionHelper.GetProperty(info, "Name")?.ToString();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    result.Add(new HardwareAttributeInfo
                    {
                        Name = name!,
                        IsReadOnly = TiaReflectionHelper.GetProperty(info, "IsReadOnly") as bool?
                    });
                }
            }
            return result;
        }

        public object? GetFeatureAttribute(HardwareObject target, string featureService, string name)
        {
            return GetFeatureAttribute(RequireFeatureService(target, featureService), name);
        }

        public void SetFeatureAttributes(HardwareObject target, string featureService, IEnumerable<KeyValuePair<string, object>> values)
        {
            Invoke(RequireFeatureService(target, featureService), "SetAttributes", values);
        }

        public IReadOnlyList<HardwareAttributeInfo> GetNetworkItemAttributeInfos(HardwareObject target, string collectionName, int index, IEnumerable<string>? attributeNames)
        {
            return GetEngineeringAttributeInfos(RequireNetworkItem(target, collectionName, index), attributeNames);
        }

        public object? GetNetworkItemAttribute(HardwareObject target, string collectionName, int index, string name)
        {
            return Invoke(RequireNetworkItem(target, collectionName, index), "GetAttribute", name);
        }

        public void SetNetworkItemAttributes(HardwareObject target, string collectionName, int index, IEnumerable<KeyValuePair<string, object>> values)
        {
            Invoke(RequireNetworkItem(target, collectionName, index), "SetAttributes", values);
        }

        public byte[]? GetGsdParameterData(HardwareObject target, int datasetNumber, int byteOffset, int length)
        {
            var service = RequireFeatureService(target, "GsdDeviceItem");
            var value = Invoke(service, "GetPrmData", datasetNumber, byteOffset, length);
            return value is IEnumerable<byte> bytes ? bytes.ToArray() : null;
        }

        public void SetGsdParameterData(HardwareObject target, int datasetNumber, int byteOffset, IEnumerable<byte> data)
        {
            var service = RequireFeatureService(target, "GsdDeviceItem");
            Invoke(service, "SetPrmData", datasetNumber, byteOffset, data);
        }

        public HardwareNetworkInfo? GetNetworkInfo(HardwareObject target, HardwareObjectRef reference)
        {
            if (GetFeatureService(target, "NetworkInterface") is not NetworkInterface network)
            {
                return null;
            }

            var info = new HardwareNetworkInfo { Target = reference };
            int index = 0;
            foreach (var node in network.Nodes)
            {
                var subnet = node.ConnectedSubnet;
                info.Nodes.Add(new HardwareNodeInfo
                {
                    Index = index,
                    Name = node.Name,
                    NodeType = node.NodeType.ToString(),
                    ConnectedSubnet = subnet?.Name,
                    Attributes = GetSelectedEngineeringAttributeInfos(node, DefaultNodeAttributeNames)
                });
                index++;
            }

            index = 0;
            foreach (var controller in network.IoControllers)
            {
                var ioSystem = controller.IoSystem;
                info.IoControllers.Add(new HardwareIoControllerInfo
                {
                    Index = index++,
                    IoSystemName = ioSystem?.Name
                });
            }

            index = 0;
            foreach (var connector in network.IoConnectors)
            {
                var ioSystem = connector.ConnectedToIoSystem;
                var connectorNetwork = TiaReflectionHelper.GetProperty(connector, "Parent");
                var owner = TiaReflectionHelper.GetProperty(connectorNetwork, "OwnedBy")
                    ?? TiaReflectionHelper.GetProperty(connectorNetwork, "Parent");
                info.IoConnectors.Add(new HardwareIoConnectorInfo
                {
                    Index = index++,
                    ConnectedIoSystemName = ioSystem?.Name,
                    OwnerName = TiaReflectionHelper.GetName(owner),
                    OwnerTypeIdentifier = TiaReflectionHelper.GetProperty(owner, "TypeIdentifier")?.ToString(),
                    InterfaceOperatingMode = TiaReflectionHelper.GetProperty(connectorNetwork, "InterfaceOperatingMode")?.ToString(),
                    Attributes = GetSelectedEngineeringAttributeInfos(connector, DefaultIoConnectorAttributeNames)
                });
            }

            return info;
        }

        public object RequireNetworkItem(HardwareObject target, string collectionName, int index)
        {
            var network = RequireFeatureService(target, "NetworkInterface");
            var values = Enumerate(network, collectionName).ToList();
            if (index < 0 || index >= values.Count)
            {
                throw new InvalidOperationException($"Network interface collection '{collectionName}' has no item at index {index}.");
            }

            return values[index];
        }

        public void ConnectNodeToSubnet(object node, Subnet subnet)
        {
            Invoke(node, "ConnectToSubnet", subnet);
        }

        public void DisconnectNode(object node)
        {
            Invoke(node, "DisconnectFromSubnet");
        }

        public object CreateIoSystem(object controller, string ioSystemName)
        {
            return Invoke(controller, "CreateIoSystem", ioSystemName)
                ?? throw new InvalidOperationException("Creating the IO system returned no object.");
        }

        public void ConnectIoConnector(object connector, object ioSystem)
        {
            Invoke(connector, "ConnectToIoSystem", ioSystem);
        }

        public void DisconnectIoConnector(object connector)
        {
            Invoke(connector, "DisconnectFromIoSystem");
        }

        private object RequireFeatureService(HardwareObject target, string serviceType)
        {
            return GetFeatureService(target, serviceType)
                ?? throw new InvalidOperationException($"Hardware item does not provide the {serviceType} feature service.");
        }

        private DriveObject RequireDriveObject(DeviceItem target, int index)
        {
            var container = GetFeatureService(target, "DriveObjectContainer") as DriveObjectContainer
                ?? throw new InvalidOperationException("Hardware item does not provide the DriveObjectContainer feature service.");
            var driveObjects = container.DriveObjects.ToList();
            if (index < 0 || index >= driveObjects.Count)
            {
                throw new InvalidOperationException($"Drive object was not found at index {index}.");
            }
            return driveObjects[index];
        }

        private Telegram RequireTelegram(DeviceItem target, int driveObjectIndex, string telegramType)
        {
            var type = ParseTelegramType(telegramType);
            return RequireDriveObject(target, driveObjectIndex).Telegrams.Find(type)
                ?? throw new InvalidOperationException($"{telegramType} telegram was not found.");
        }

        private static Telegram InsertTelegram(DriveObject driveObject, TelegramType type, string telegramType, int telegramNumber)
        {
            if (!driveObject.Telegrams.CanInsertTelegram(telegramNumber, type))
            {
                throw new InvalidOperationException($"{telegramType} telegram number {telegramNumber} cannot be inserted.");
            }

            driveObject.Telegrams.InsertTelegram(telegramNumber, type);
            return driveObject.Telegrams.Find(type)
                ?? throw new InvalidOperationException($"{telegramType} telegram was not found after insertion.");
        }

        private static Telegram RecreateTelegram(DriveObject driveObject, TelegramType type, string telegramType, int telegramNumber)
        {
            try
            {
                if (driveObject.Telegrams.Find(type) != null)
                {
                    driveObject.Telegrams.EraseTelegram(type);
                }
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
            IEnumerable<AddressIoType> directions)
        {
            if (type == TelegramType.MainTelegram)
            {
                return RefreshMainTelegramSizes(driveObject, type, telegramType, telegramNumber, directions);
            }

            return RecreateTelegram(driveObject, type, telegramType, telegramNumber);
        }

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
                    if (telegram.CanChangeSize(direction, size, keepOriginalAddress: false))
                    {
                        telegram.ChangeSize(direction, size, keepOriginalAddress: false);
                    }
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
            {
                throw new InvalidOperationException($"{telegramType} {direction} address differs from expectedStartAddress.");
            }

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

        private static IEnumerable<AddressIoType> RequiredDirections(int? inputStartAddress, int? outputStartAddress)
        {
            if (inputStartAddress.HasValue) yield return AddressIoType.Input;
            if (outputStartAddress.HasValue) yield return AddressIoType.Output;
        }

        private static bool HasRequiredAddresses(Telegram telegram, IEnumerable<AddressIoType> directions)
        {
            var addresses = telegram.Addresses.ToList();
            return directions.All(direction => addresses.Any(address => address.IoType == direction));
        }

        private static InvalidOperationException MissingAddressException(Telegram telegram, string telegramType, IEnumerable<AddressIoType> directions)
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
            {
                throw new InvalidOperationException($"Unsupported telegramType: {value}");
            }
            return type;
        }

        private static AddressIoType ParseAddressIoType(string value)
        {
            if (!Enum.TryParse(value, true, out AddressIoType type))
            {
                throw new InvalidOperationException($"Unsupported ioType: {value}");
            }
            return type;
        }

        private static object? GetFeatureAttribute(object feature, string name)
        {
            return Invoke(feature, "GetAttribute", name);
        }

        private static IReadOnlyList<HardwareAttributeInfo> GetEngineeringAttributeInfos(object target, IEnumerable<string>? attributeNames)
        {
            var requested = attributeNames?.Where(name => !string.IsNullOrWhiteSpace(name)).ToArray() ?? Array.Empty<string>();
            if (requested.Length > 0)
            {
                return requested.Select(name => new HardwareAttributeInfo
                {
                    Name = name,
                    Value = Convert.ToString(Invoke(target, "GetAttribute", name), CultureInfo.InvariantCulture)
                }).ToList();
            }

            var infos = Invoke(target, "GetAttributeInfos") as System.Collections.IEnumerable;
            if (infos == null) return Array.Empty<HardwareAttributeInfo>();
            var result = new List<HardwareAttributeInfo>();
            foreach (var info in infos)
            {
                var name = TiaReflectionHelper.GetProperty(info, "Name")?.ToString();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    result.Add(new HardwareAttributeInfo
                    {
                        Name = name!,
                        IsReadOnly = TiaReflectionHelper.GetProperty(info, "IsReadOnly") as bool?
                    });
                }
            }
            return result;
        }

        public IReadOnlyList<HardwareDriveParameterInfo> GetDriveParameters(DeviceItem target, HardwareObjectRef reference, int? requestedDriveObjectIndex, string? filter, int maxResults)
        {
            var container = GetFeatureService(target, "DriveObjectContainer") as DriveObjectContainer;
            if (container == null) return Array.Empty<HardwareDriveParameterInfo>();
            var result = new List<HardwareDriveParameterInfo>();
            int driveIndex = 0;
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

        private static HardwareDriveParameterInfo ToParameterInfo(HardwareObjectRef reference, int driveIndex, DriveObject driveObject, object parameter, bool writable)
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

        private static bool MatchesParameter(string? name, int number, string? text, string? filter)
        {
            if (string.IsNullOrWhiteSpace(filter)) return true;
            return (name?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                (text?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                number.ToString(CultureInfo.InvariantCulture).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static List<HardwareAttributeInfo> GetSelectedEngineeringAttributeInfos(object? target, IEnumerable<string> attributeNames)
        {
            if (target == null) return new List<HardwareAttributeInfo>();
            var result = new List<HardwareAttributeInfo>();
            foreach (var name in attributeNames.Where(name => !string.IsNullOrWhiteSpace(name)))
            {
                var value = TryGetEngineeringAttribute(target, name);
                if (value != null)
                {
                    result.Add(new HardwareAttributeInfo
                    {
                        Name = name,
                        Value = value
                    });
                }
            }
            return result;
        }

        private static IEnumerable<string> GetEngineeringAttributeNames(object target)
        {
            var infos = Invoke(target, "GetAttributeInfos") as System.Collections.IEnumerable;
            if (infos == null) yield break;
            foreach (var info in infos)
            {
                var name = TiaReflectionHelper.GetProperty(info, "Name")?.ToString();
                if (!string.IsNullOrWhiteSpace(name)) yield return name!;
            }
        }

        private static HardwareIoAddressInfo ToIoAddressInfo(object address, HardwareObjectRef reference, int index, IEnumerable<string>? attributeNames)
        {
            var requested = attributeNames?.Where(name => !string.IsNullOrWhiteSpace(name)).ToArray() ?? Array.Empty<string>();
            var names = requested.Length > 0 ? requested : GetEngineeringAttributeNames(address).ToArray();
            return new HardwareIoAddressInfo
            {
                Target = reference,
                Index = index,
                Attributes = names.Select(name => new HardwareAttributeInfo
                {
                    Name = name,
                    Value = TryGetEngineeringAttribute(address, name)
                }).ToList()
            };
        }

        private static string? TryGetEngineeringAttribute(object target, string name)
        {
            try
            {
                return Convert.ToString(Invoke(target, "GetAttribute", name), CultureInfo.InvariantCulture);
            }
            catch
            {
                return null;
            }
        }

        private static string? TryGetPropertyString(object target, string propertyName)
        {
            try
            {
                return Convert.ToString(TiaReflectionHelper.GetProperty(target, propertyName), CultureInfo.InvariantCulture);
            }
            catch
            {
                return null;
            }
        }

        private static IEnumerable<object> Enumerate(object target, string propertyName)
        {
            return TiaReflectionHelper.GetEnumerableProperty(target, propertyName) ?? Enumerable.Empty<object>();
        }

        private static string? TryGetAttribute(HardwareObject target, string name)
        {
            try
            {
                return Convert.ToString(target.GetAttribute(name), CultureInfo.InvariantCulture);
            }
            catch
            {
                return null;
            }
        }

        private static object? Invoke(object target, string methodName, params object[] args)
        {
            var method = target.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(candidate =>
                    candidate.Name == methodName &&
                    candidate.GetParameters().Length == args.Length);
            if (method == null)
            {
                throw new InvalidOperationException($"Method '{methodName}' is not available on {target.GetType().Name}.");
            }

            try
            {
                return method.Invoke(target, args);
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                throw ex.InnerException;
            }
        }
    }
}
