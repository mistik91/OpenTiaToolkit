using OpenTiaToolkit.Core.Models;
using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Services
{
    /// <summary>
    /// Implements vendor-neutral hardware operations against the currently selected TIA project.
    /// </summary>
    public sealed class HardwareService : IHardwareService
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

        private readonly IPortalService _portalService;
        private readonly EngineeringSessionContext _sessionContext;
        private readonly V21HardwareAdapter _adapter;
        private readonly V21OpennessAdapter _opennessAdapter;
        private readonly ILogger<HardwareService>? _logger;

        internal HardwareService(
            IPortalService portalService,
            EngineeringSessionContext sessionContext,
            ILogger<HardwareService>? logger = null,
            V21HardwareAdapter? adapter = null,
            V21OpennessAdapter? opennessAdapter = null)
        {
            _portalService = portalService ?? throw new ArgumentNullException(nameof(portalService));
            _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
            _logger = logger;
            _adapter = adapter ?? new V21HardwareAdapter();
            _opennessAdapter = opennessAdapter ?? new V21OpennessAdapter();
        }

        public Task<IReadOnlyList<HardwareCatalogItemInfo>> FindCatalogItemsAsync(string searchText, string? culture = null)
        {
            return Task.Run<IReadOnlyList<HardwareCatalogItemInfo>>(() =>
                _adapter.FindCatalogItems(RequirePortal(), searchText ?? string.Empty, culture));
        }

        public Task<IReadOnlyList<HardwareDeviceTreeInfo>> GetHardwareTreeAsync(string? deviceName = null)
        {
            return Task.Run<IReadOnlyList<HardwareDeviceTreeInfo>>(() =>
            {
                var project = RequireProject();
                var devices = ProjectDeviceEnumerator.EnumerateDevices(project)
                    .Where(device => string.IsNullOrWhiteSpace(deviceName) ||
                        string.Equals(device.Name, deviceName, StringComparison.OrdinalIgnoreCase))
                    .Select(ToDeviceTree)
                    .ToList();
                return devices;
            });
        }

        public Task<IReadOnlyList<HardwareIoAddressInfo>> GetIoAddressesAsync(string? deviceName = null, string[]? attributes = null)
        {
            return Task.Run<IReadOnlyList<HardwareIoAddressInfo>>(() =>
            {
                var result = new List<HardwareIoAddressInfo>();
                foreach (var device in ProjectDeviceEnumerator.EnumerateDevices(RequireProject()))
                {
                    if (!string.IsNullOrWhiteSpace(deviceName) &&
                        !string.Equals(device.Name, deviceName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var root = new HardwareObjectRef { DeviceName = device.Name, ExpectedTypeIdentifier = device.TypeIdentifier };
                    foreach (var item in device.DeviceItems)
                    {
                        AddIoAddressesRecursive(item, ChildRef(root, item.Name, item.TypeIdentifier), attributes, result);
                    }
                }
                return result;
            });
        }

        public Task<IReadOnlyList<HardwareDriveTelegramInfo>> GetDriveTelegramsAsync(string? deviceName = null, string[]? addressAttributes = null)
        {
            return Task.Run<IReadOnlyList<HardwareDriveTelegramInfo>>(() =>
            {
                var result = new List<HardwareDriveTelegramInfo>();
                foreach (var device in ProjectDeviceEnumerator.EnumerateDevices(RequireProject()))
                {
                    if (!string.IsNullOrWhiteSpace(deviceName) &&
                        !string.Equals(device.Name, deviceName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var root = new HardwareObjectRef { DeviceName = device.Name, ExpectedTypeIdentifier = device.TypeIdentifier };
                    foreach (var item in device.DeviceItems)
                    {
                        AddDriveTelegramsRecursive(item, ChildRef(root, item.Name, item.TypeIdentifier), addressAttributes, result);
                    }
                }
                return result;
            });
        }

        public Task<HardwareTagMappingAuditResult> AuditHardwareTagMappingsAsync(
            string plcName,
            string? tagTableFilter = null,
            string? catalogSearchText = null,
            int maxUnmatchedTags = 100)
        {
            return Task.Run(() =>
            {
                RequireText(plcName, "plcName");
                var project = RequireProject();
                var result = new HardwareTagMappingAuditResult
                {
                    PlcName = plcName,
                    TagTableFilter = string.IsNullOrWhiteSpace(tagTableFilter) ? null : tagTableFilter,
                    CatalogSearchText = string.IsNullOrWhiteSpace(catalogSearchText) ? null : catalogSearchText
                };

                var devices = ProjectDeviceEnumerator.EnumerateDevices(project).ToList();
                result.HardwareDeviceCount = devices.Count;
                result.HardwareDevices = devices
                    .Select(device => $"{device.Name} ({device.TypeIdentifier})")
                    .ToList();

                var plcSoftware = ResolvePlcSoftware(project, plcName);
                var tags = EnumeratePlcTags(plcSoftware, tagTableFilter).ToList();
                result.ScannedTagCount = tags.Count;

                var tagAddresses = tags
                    .Select(tag => TryParseTagAddress(tag.TagTableName, tag.TagName, tag.LogicalAddress))
                    .Where(tag => tag != null)
                    .Cast<HardwareTagAddressInfo>()
                    .ToList();
                result.AddressedTagCount = tagAddresses.Count;

                var hardwareRanges = CollectHardwareAddressRanges().ToList();
                result.HardwareIoRangeCount = hardwareRanges.Count;
                result.HardwareRanges = hardwareRanges;

                foreach (var tag in tagAddresses)
                {
                    var match = hardwareRanges.FirstOrDefault(range =>
                        string.Equals(range.Area, tag.Area, StringComparison.OrdinalIgnoreCase) &&
                        RangesOverlap(tag.StartAddress, tag.EndAddress, range.StartAddress, range.EndAddress));
                    if (match == null)
                    {
                        result.UnmatchedTags.Add(tag);
                        continue;
                    }

                    tag.MatchedHardwareRange = FormatRange(match);
                    result.MatchedTagCount++;
                }

                result.UnmatchedTagCount = result.UnmatchedTags.Count;
                result.UnmatchedRanges = BuildUnmatchedRanges(result.UnmatchedTags).ToList();
                if (maxUnmatchedTags >= 0 && result.UnmatchedTags.Count > maxUnmatchedTags)
                {
                    result.UnmatchedTags = result.UnmatchedTags.Take(maxUnmatchedTags).ToList();
                }

                if (!string.IsNullOrWhiteSpace(catalogSearchText))
                {
                    result.CatalogMatches = _adapter.FindCatalogItems(RequirePortal(), catalogSearchText!, null)
                        .Take(50)
                        .ToList();
                    result.MatchingHardwareTargets = FindHardwareTargets(devices, catalogSearchText!, result.CatalogMatches).ToList();
                    result.MatchingConnectedIoDevices = FindMatchingConnectedIoDevices(
                        catalogSearchText!,
                        result.CatalogMatches).ToList();
                }

                AddAuditWarnings(result);
                return result;
            });
        }

        public Task<HardwareCapabilityInfo?> GetCapabilitiesAsync(HardwareObjectRef target, string[]? attributes = null)
        {
            return Task.Run<HardwareCapabilityInfo?>(() =>
            {
                var resolved = Resolve(target);
                var services = new[] { "GsdDevice", "GsdDeviceItem", "NetworkInterface" }
                    .Where(name => _adapter.GetFeatureService(resolved, name) != null)
                    .ToList();
                return new HardwareCapabilityInfo
                {
                    Target = CloneRef(target),
                    TypeIdentifier = resolved.TypeIdentifier,
                    IsDevice = resolved is Device,
                    SupportsPlugging = true,
                    SupportsGsdParameters = services.Contains("GsdDeviceItem"),
                    SupportsNetworkInterface = services.Contains("NetworkInterface"),
                    AvailableServices = services,
                    Attributes = _adapter.GetAttributeInfos(resolved, attributes).ToList()
                };
            });
        }

        public Task<HardwareFeatureAttributesInfo> GetFeatureAttributesAsync(HardwareObjectRef target, string featureService, string[]? attributes = null)
        {
            return Task.Run(() =>
            {
                RequireText(featureService, "featureService");
                var resolved = Resolve(target);
                return new HardwareFeatureAttributesInfo
                {
                    Target = CloneRef(target),
                    FeatureService = featureService,
                    Attributes = _adapter.GetFeatureAttributeInfos(resolved, featureService, attributes).ToList()
                };
            });
        }

        public Task<HardwareNetworkItemAttributesInfo> GetNetworkItemAttributesAsync(HardwareObjectRef target, string collectionName, int index, string[]? attributes = null)
        {
            return Task.Run(() =>
            {
                RequireText(collectionName, "networkCollection");
                var resolved = Resolve(target);
                return new HardwareNetworkItemAttributesInfo
                {
                    Target = CloneRef(target),
                    CollectionName = collectionName,
                    Index = index,
                    Attributes = _adapter.GetNetworkItemAttributeInfos(resolved, collectionName, index, attributes).ToList()
                };
            });
        }

        public Task<HardwarePlugCheckInfo> CanPlugAsync(HardwareObjectRef target, string typeIdentifier, string itemName, int positionNumber)
        {
            return Task.Run(() =>
            {
                var parent = Resolve(target);
                return new HardwarePlugCheckInfo
                {
                    Target = CloneRef(target),
                    TypeIdentifier = typeIdentifier,
                    ItemName = itemName,
                    PositionNumber = positionNumber,
                    CanPlug = parent.CanPlugNew(typeIdentifier, itemName, positionNumber)
                };
            });
        }

        public Task<IReadOnlyList<string>> FindModuleTypesAsync(string partialTypeIdentifier)
        {
            return Task.Run<IReadOnlyList<string>>(() =>
                _adapter.FindModuleTypes(RequireProject(), partialTypeIdentifier ?? string.Empty));
        }

        public Task<IReadOnlyList<HardwareNetworkInfo>> GetNetworksAsync(string? deviceName = null)
        {
            return Task.Run<IReadOnlyList<HardwareNetworkInfo>>(() =>
            {
                var result = new List<HardwareNetworkInfo>();
                foreach (var device in ProjectDeviceEnumerator.EnumerateDevices(RequireProject()))
                {
                    if (!string.IsNullOrWhiteSpace(deviceName) &&
                        !string.Equals(device.Name, deviceName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    AddNetworkInfoRecursive(device, new HardwareObjectRef { DeviceName = device.Name }, result);
                }

                if (string.IsNullOrWhiteSpace(deviceName))
                {
                    AddProjectIoSystemNetworkInfos(RequireProject(), result);
                }

                return result;
            });
        }

        public Task<HardwareGsdParameterData?> GetGsdParameterDataAsync(HardwareObjectRef target, int datasetNumber, int byteOffset, int length)
        {
            return Task.Run<HardwareGsdParameterData?>(() =>
            {
                var resolved = Resolve(target);
                var bytes = _adapter.GetGsdParameterData(resolved, datasetNumber, byteOffset, length);
                return bytes == null ? null : new HardwareGsdParameterData
                {
                    Target = CloneRef(target),
                    DatasetNumber = datasetNumber,
                    ByteOffset = byteOffset,
                    Length = bytes.Length,
                    DataHex = ToHex(bytes)
                };
            });
        }

        public Task<HardwarePatchResult> ApplyPatchAsync(
            IReadOnlyList<HardwarePatchOperation> operations,
            bool dryRun = true,
            bool confirmDestructive = false,
            string? transactionDescription = null)
        {
            return Task.Run(() =>
            {
                if (operations == null || operations.Count == 0)
                {
                    throw new ArgumentException("At least one hardware operation is required.", nameof(operations));
                }

                var result = new HardwarePatchResult
                {
                    DryRun = dryRun,
                    TransactionDescription = string.IsNullOrWhiteSpace(transactionDescription)
                        ? "OpenTiaToolkit hardware patch"
                        : transactionDescription
                };
                var references = new Dictionary<string, ResolvedTarget>(StringComparer.OrdinalIgnoreCase);

                try
                {
                    var project = RequireProject();
                    using (var access = RequirePortal().ExclusiveAccess(result.TransactionDescription))
                    using (var transaction = access.Transaction(project, result.TransactionDescription))
                    {
                        for (int index = 0; index < operations.Count; index++)
                        {
                            ExecuteOperation(index, operations[index], confirmDestructive, references, result);
                        }

                        if (!dryRun)
                        {
                            transaction.CommitOnDispose();
                            result.Applied = true;
                        }
                        else
                        {
                            result.RolledBack = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Hardware patch failed");
                    result.Applied = false;
                    result.RolledBack = true;
                    result.SafetyBlocked = true;
                    result.SafetyMessage = ex.Message;
                }

                return result;
            });
        }

        private void ExecuteOperation(
            int index,
            HardwarePatchOperation operation,
            bool confirmDestructive,
            IDictionary<string, ResolvedTarget> references,
            HardwarePatchResult result)
        {
            if (operation == null || string.IsNullOrWhiteSpace(operation.Op))
            {
                throw new InvalidOperationException($"Operation {index} has no op value.");
            }

            var op = NormalizeOperation(operation.Op);
            HardwareOperationResult item;
            switch (op)
            {
                case "create-device":
                    item = CreateDevice(index, operation, references);
                    break;
                case "create-device-with-item":
                    item = CreateDeviceWithItem(index, operation, references);
                    break;
                case "plug-item":
                    item = PlugItem(index, operation, references);
                    break;
                case "set-attributes":
                    item = SetAttributes(index, operation, references);
                    break;
                case "get-feature-attributes":
                    item = ReadFeatureAttributes(index, operation, references);
                    break;
                case "set-feature-attributes":
                    item = SetFeatureAttributes(index, operation, references);
                    break;
                case "get-network-item-attributes":
                    item = ReadNetworkItemAttributes(index, operation, references);
                    break;
                case "set-network-item-attributes":
                    item = SetNetworkItemAttributes(index, operation, references);
                    break;
                case "configure-profinet-device":
                    item = ConfigureProfinetDevice(index, operation, references);
                    break;
                case "set-io-address-start":
                    item = SetIoAddressStart(index, operation, references);
                    break;
                case "change-drive-telegram":
                    item = ChangeDriveTelegram(index, operation, references);
                    break;
                case "configure-drive-telegram":
                    item = ConfigureDriveTelegram(index, operation, references);
                    break;
                case "set-drive-telegram-address":
                    item = SetDriveTelegramAddress(index, operation, references);
                    break;
                case "change-drive-telegram-size":
                    item = ChangeDriveTelegramSize(index, operation, references);
                    break;
                case "insert-drive-telegram":
                    item = InsertDriveTelegram(index, operation, references);
                    break;
                case "erase-drive-telegram":
                    RequireDestructiveConfirmation(confirmDestructive, op);
                    item = EraseDriveTelegram(index, operation, references);
                    break;
                case "set-gsd-parameter-data":
                    item = SetGsdParameterData(index, operation, references);
                    break;
                case "get-gsd-parameter-data":
                    item = ReadGsdParameterData(index, operation, references);
                    break;
                case "create-subnet":
                    item = CreateSubnet(index, operation);
                    break;
                case "connect-node-to-subnet":
                    item = ConnectNodeToSubnet(index, operation, references);
                    break;
                case "disconnect-node":
                    RequireDestructiveConfirmation(confirmDestructive, op);
                    item = DisconnectNode(index, operation, references);
                    break;
                case "create-io-system":
                    item = CreateIoSystem(index, operation, references);
                    break;
                case "connect-io-connector":
                    item = ConnectIoConnector(index, operation, references);
                    break;
                case "disconnect-io-connector":
                    RequireDestructiveConfirmation(confirmDestructive, op);
                    item = DisconnectIoConnector(index, operation, references);
                    break;
                case "delete-device":
                    RequireDestructiveConfirmation(confirmDestructive, op);
                    item = DeleteDevice(index, operation, references);
                    break;
                case "delete-item":
                    RequireDestructiveConfirmation(confirmDestructive, op);
                    item = DeleteItem(index, operation, references);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported hardware patch operation: {operation.Op}");
            }

            result.Operations.Add(item);
        }

        private HardwareOperationResult CreateDevice(int index, HardwarePatchOperation op, IDictionary<string, ResolvedTarget> refs)
        {
            RequireText(op.TypeIdentifier, "typeIdentifier");
            RequireText(op.DeviceName, "deviceName");
            var created = RequireProject().Devices.Create(op.TypeIdentifier!, op.DeviceName!);
            var target = new HardwareObjectRef { DeviceName = created.Name, ExpectedTypeIdentifier = created.TypeIdentifier };
            AddResultReference(op.ResultId, new ResolvedTarget(created, target), refs);
            return Success(index, op, target, $"Created device: {created.Name}", ToDeviceTree(created));
        }

        private HardwareOperationResult CreateDeviceWithItem(int index, HardwarePatchOperation op, IDictionary<string, ResolvedTarget> refs)
        {
            RequireText(op.TypeIdentifier, "typeIdentifier");
            RequireText(op.DeviceName, "deviceName");
            RequireText(op.ItemName, "itemName");
            var created = RequireProject().Devices.CreateWithItem(op.TypeIdentifier!, op.ItemName!, op.DeviceName!);
            var target = new HardwareObjectRef { DeviceName = created.Name, ExpectedTypeIdentifier = created.TypeIdentifier };
            AddResultReference(op.ResultId, new ResolvedTarget(created, target), refs);
            return Success(index, op, target, $"Created device with item: {created.Name}", ToDeviceTree(created));
        }

        private HardwareOperationResult PlugItem(int index, HardwarePatchOperation op, IDictionary<string, ResolvedTarget> refs)
        {
            RequireText(op.TypeIdentifier, "typeIdentifier");
            RequireText(op.ItemName, "itemName");
            if (!op.PositionNumber.HasValue) throw new InvalidOperationException("positionNumber is required for plug-item.");
            var parent = ResolveOperationTarget(op, refs);
            if (!parent.Object.CanPlugNew(op.TypeIdentifier!, op.ItemName!, op.PositionNumber.Value))
            {
                throw new InvalidOperationException($"Cannot plug '{op.ItemName}' at position {op.PositionNumber.Value}.");
            }

            var created = parent.Object.PlugNew(op.TypeIdentifier!, op.ItemName!, op.PositionNumber.Value);
            var requestedReference = ChildRef(parent.Reference, created.Name, created.TypeIdentifier);
            var actualReference = FindActualReference(created, parent.Reference.DeviceName)
                ?? throw new InvalidOperationException($"Plugged hardware item '{created.Name}' could not be located after creation.");
            var placementDisplaced = !HardwarePathsEqual(requestedReference, actualReference);
            var warning = ValidatePlugPlacement(created.Name, requestedReference, actualReference, op.AllowDisplacedPlacement);
            AddResultReference(op.ResultId, new ResolvedTarget(created, actualReference), refs);
            return Success(index, op, actualReference, $"Plugged hardware item: {created.Name}", new HardwarePlugItemInfo
            {
                Item = ToItemInfoWithReference(created, actualReference),
                RequestedTarget = requestedReference,
                ActualTarget = actualReference,
                ActualParentMismatch = placementDisplaced,
                Warning = warning
            });
        }

        private HardwareOperationResult SetAttributes(int index, HardwarePatchOperation op, IDictionary<string, ResolvedTarget> refs)
        {
            if (op.Attributes == null || op.Attributes.Count == 0)
            {
                throw new InvalidOperationException("attributes is required for set-attributes.");
            }

            var target = ResolveOperationTarget(op, refs);
            EnsureExpectedAttributes(target.Object, op.ExpectedAttributes);
            var before = op.Attributes.Keys.ToDictionary(
                name => name,
                name => TryGetAttribute(target.Object, name),
                StringComparer.OrdinalIgnoreCase);
            var values = op.Attributes.Select(pair =>
                new KeyValuePair<string, object>(pair.Key, ConvertJsonValue(pair.Value))).ToList();
            target.Object.SetAttributes(values);
            return Success(index, op, target.Reference, $"Set {values.Count} hardware attribute(s).", new HardwareAttributeChangeInfo
            {
                Before = before,
                Written = values.ToDictionary(pair => pair.Key, pair => (object?)pair.Value, StringComparer.OrdinalIgnoreCase)
            });
        }

        private HardwareOperationResult ReadFeatureAttributes(int index, HardwarePatchOperation op, IDictionary<string, ResolvedTarget> refs)
        {
            RequireText(op.FeatureService, "featureService");
            if (op.AttributeNames == null || op.AttributeNames.Length == 0)
            {
                throw new InvalidOperationException("attributeNames is required for get-feature-attributes.");
            }

            var target = ResolveOperationTarget(op, refs);
            var values = _adapter.GetFeatureAttributeInfos(target.Object, op.FeatureService!, op.AttributeNames).ToList();
            return Success(index, op, target.Reference, $"Read {values.Count} {op.FeatureService} feature attribute(s).", new HardwareFeatureAttributesInfo
            {
                Target = target.Reference,
                FeatureService = op.FeatureService!,
                Attributes = values
            });
        }

        private HardwareOperationResult SetFeatureAttributes(int index, HardwarePatchOperation op, IDictionary<string, ResolvedTarget> refs)
        {
            RequireText(op.FeatureService, "featureService");
            if (op.Attributes == null || op.Attributes.Count == 0)
            {
                throw new InvalidOperationException("attributes is required for set-feature-attributes.");
            }

            var target = ResolveOperationTarget(op, refs);
            EnsureExpectedFeatureAttributes(target.Object, op.FeatureService!, op.ExpectedAttributes);
            var before = op.Attributes.Keys.ToDictionary(
                name => name,
                name => TryGetFeatureAttribute(target.Object, op.FeatureService!, name),
                StringComparer.OrdinalIgnoreCase);
            var values = op.Attributes.Select(pair =>
                new KeyValuePair<string, object>(pair.Key, ConvertJsonValue(pair.Value))).ToList();
            _adapter.SetFeatureAttributes(target.Object, op.FeatureService!, values);
            return Success(index, op, target.Reference, $"Set {values.Count} {op.FeatureService} feature attribute(s).", new HardwareAttributeChangeInfo
            {
                Before = before,
                Written = values.ToDictionary(pair => pair.Key, pair => (object?)pair.Value, StringComparer.OrdinalIgnoreCase)
            });
        }

        private HardwareOperationResult ReadNetworkItemAttributes(int index, HardwarePatchOperation op, IDictionary<string, ResolvedTarget> refs)
        {
            RequireText(op.NetworkCollection, "networkCollection");
            if (op.AttributeNames == null || op.AttributeNames.Length == 0)
            {
                throw new InvalidOperationException("attributeNames is required for get-network-item-attributes.");
            }

            var target = ResolveOperationTarget(op, refs);
            var itemIndex = op.NetworkItemIndex ?? 0;
            var values = _adapter.GetNetworkItemAttributeInfos(target.Object, op.NetworkCollection!, itemIndex, op.AttributeNames).ToList();
            return Success(index, op, target.Reference, $"Read {values.Count} {op.NetworkCollection} item attribute(s).", new HardwareNetworkItemAttributesInfo
            {
                Target = target.Reference,
                CollectionName = op.NetworkCollection!,
                Index = itemIndex,
                Attributes = values
            });
        }

        private HardwareOperationResult SetNetworkItemAttributes(int index, HardwarePatchOperation op, IDictionary<string, ResolvedTarget> refs)
        {
            RequireText(op.NetworkCollection, "networkCollection");
            if (op.Attributes == null || op.Attributes.Count == 0)
            {
                throw new InvalidOperationException("attributes is required for set-network-item-attributes.");
            }

            var target = ResolveOperationTarget(op, refs);
            var itemIndex = op.NetworkItemIndex ?? 0;
            EnsureExpectedNetworkItemAttributes(target.Object, op.NetworkCollection!, itemIndex, op.ExpectedAttributes);
            var before = op.Attributes.Keys.ToDictionary(
                name => name,
                name => TryGetNetworkItemAttribute(target.Object, op.NetworkCollection!, itemIndex, name),
                StringComparer.OrdinalIgnoreCase);
            var values = op.Attributes.Select(pair =>
                new KeyValuePair<string, object>(pair.Key, ConvertJsonValue(pair.Value))).ToList();
            _adapter.SetNetworkItemAttributes(target.Object, op.NetworkCollection!, itemIndex, values);
            return Success(index, op, target.Reference, $"Set {values.Count} {op.NetworkCollection} item attribute(s).", new HardwareAttributeChangeInfo
            {
                Before = before,
                Written = values.ToDictionary(pair => pair.Key, pair => (object?)pair.Value, StringComparer.OrdinalIgnoreCase)
            });
        }

        private HardwareOperationResult ConfigureProfinetDevice(int index, HardwarePatchOperation op, IDictionary<string, ResolvedTarget> refs)
        {
            var nodeValues = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            var nodeExpected = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            var connectorValues = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            var connectorExpected = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(op.IpAddress)) nodeValues["Address"] = op.IpAddress!;
            if (!string.IsNullOrWhiteSpace(op.ExpectedIpAddress)) nodeExpected["Address"] = op.ExpectedIpAddress;
            if (!string.IsNullOrWhiteSpace(op.SubnetMask)) nodeValues["SubnetMask"] = op.SubnetMask!;
            if (!string.IsNullOrWhiteSpace(op.ExpectedSubnetMask)) nodeExpected["SubnetMask"] = op.ExpectedSubnetMask;
            if (!string.IsNullOrWhiteSpace(op.PnDeviceName))
            {
                nodeValues["PnDeviceNameAutoGeneration"] = false;
                nodeValues["PnDeviceName"] = op.PnDeviceName!;
            }
            if (!string.IsNullOrWhiteSpace(op.ExpectedPnDeviceName)) nodeExpected["PnDeviceName"] = op.ExpectedPnDeviceName;
            if (op.PnDeviceNumber.HasValue) connectorValues["PnDeviceNumber"] = op.PnDeviceNumber.Value;
            if (op.ExpectedPnDeviceNumber.HasValue) connectorExpected["PnDeviceNumber"] = op.ExpectedPnDeviceNumber.Value;
            if (nodeValues.Count == 0 && connectorValues.Count == 0)
            {
                throw new InvalidOperationException("configure-profinet-device requires ipAddress, subnetMask, pnDeviceName, or pnDeviceNumber.");
            }

            var target = ResolveOperationTarget(op, refs);
            var nodeIndex = op.NodeIndex ?? 0;
            var connectorIndex = op.ConnectorIndex ?? 0;
            EnsureExpectedNetworkItemAttributes(target.Object, "Nodes", nodeIndex, nodeExpected);
            EnsureExpectedNetworkItemAttributes(target.Object, "IoConnectors", connectorIndex, connectorExpected);
            var change = new HardwareProfinetConfigurationChangeInfo();
            foreach (var value in nodeValues)
            {
                change.Node.Before[value.Key] = TryGetNetworkItemAttribute(target.Object, "Nodes", nodeIndex, value.Key);
                change.Node.Written[value.Key] = value.Value;
            }
            foreach (var value in connectorValues)
            {
                change.Connector.Before[value.Key] = TryGetNetworkItemAttribute(target.Object, "IoConnectors", connectorIndex, value.Key);
                change.Connector.Written[value.Key] = value.Value;
            }
            if (nodeValues.Count > 0) _adapter.SetNetworkItemAttributes(target.Object, "Nodes", nodeIndex, nodeValues);
            if (connectorValues.Count > 0) _adapter.SetNetworkItemAttributes(target.Object, "IoConnectors", connectorIndex, connectorValues);
            return Success(index, op, target.Reference, "Configured PROFINET device identity and addressing.", change);
        }

        private HardwareOperationResult SetIoAddressStart(int index, HardwarePatchOperation op, IDictionary<string, ResolvedTarget> refs)
        {
            if (!op.AddressIndex.HasValue) throw new InvalidOperationException("addressIndex is required for set-io-address-start.");
            if (!op.StartAddress.HasValue) throw new InvalidOperationException("startAddress is required for set-io-address-start.");
            var target = ResolveOperationTarget(op, refs);
            if (!(target.Object is DeviceItem item))
            {
                throw new InvalidOperationException("set-io-address-start target must be a nested device item.");
            }
            var change = _adapter.SetIoAddressStart(item, op.AddressIndex.Value, op.StartAddress.Value, op.ExpectedStartAddress);
            return Success(index, op, target.Reference, "Set hardware IO start address.", change);
        }

        private HardwareOperationResult ChangeDriveTelegram(int index, HardwarePatchOperation op, IDictionary<string, ResolvedTarget> refs)
        {
            RequireText(op.TelegramType, "telegramType");
            if (!op.TelegramNumber.HasValue) throw new InvalidOperationException("telegramNumber is required for change-drive-telegram.");
            var item = ResolveDriveItem(op, refs, "change-drive-telegram", out var target);
            var change = _adapter.ChangeDriveTelegramNumber(item, op.DriveObjectIndex ?? 0, op.TelegramType!, op.TelegramNumber.Value, op.ExpectedTelegramNumber);
            return Success(index, op, target.Reference, "Changed Startdrive telegram number.", change);
        }

        private HardwareOperationResult SetDriveTelegramAddress(int index, HardwarePatchOperation op, IDictionary<string, ResolvedTarget> refs)
        {
            RequireText(op.TelegramType, "telegramType");
            RequireText(op.IoType, "ioType");
            if (!op.StartAddress.HasValue) throw new InvalidOperationException("startAddress is required for set-drive-telegram-address.");
            var item = ResolveDriveItem(op, refs, "set-drive-telegram-address", out var target);
            var change = _adapter.SetDriveTelegramAddress(item, op.DriveObjectIndex ?? 0, op.TelegramType!, op.IoType!, op.StartAddress.Value, op.ExpectedStartAddress);
            return Success(index, op, target.Reference, "Set Startdrive telegram IO start address.", change);
        }

        private HardwareOperationResult ConfigureDriveTelegram(int index, HardwarePatchOperation op, IDictionary<string, ResolvedTarget> refs)
        {
            RequireText(op.TelegramType, "telegramType");
            if (!op.TelegramNumber.HasValue) throw new InvalidOperationException("telegramNumber is required for configure-drive-telegram.");
            if (!op.InputStartAddress.HasValue && !op.OutputStartAddress.HasValue)
            {
                throw new InvalidOperationException("configure-drive-telegram requires inputStartAddress or outputStartAddress.");
            }

            var item = ResolveDriveItem(op, refs, "configure-drive-telegram", out var target);
            var changes = _adapter.ConfigureDriveTelegram(
                item,
                op.DriveObjectIndex ?? 0,
                op.TelegramType!,
                op.TelegramNumber.Value,
                op.ExpectedTelegramNumber,
                op.InputStartAddress,
                op.ExpectedInputStartAddress,
                op.OutputStartAddress,
                op.ExpectedOutputStartAddress);
            return Success(index, op, target.Reference, "Configured Startdrive telegram number and IO start address(es).", new HardwareDriveTelegramConfigurationInfo
            {
                Changes = changes.ToList()
            });
        }

        private HardwareOperationResult ChangeDriveTelegramSize(int index, HardwarePatchOperation op, IDictionary<string, ResolvedTarget> refs)
        {
            RequireText(op.TelegramType, "telegramType");
            RequireText(op.IoType, "ioType");
            if (!op.SizeInWords.HasValue) throw new InvalidOperationException("sizeInWords is required for change-drive-telegram-size.");
            var item = ResolveDriveItem(op, refs, "change-drive-telegram-size", out var target);
            var change = _adapter.ChangeDriveTelegramSize(item, op.DriveObjectIndex ?? 0, op.TelegramType!, op.IoType!, op.SizeInWords.Value, op.ExpectedSizeInWords, op.KeepOriginalAddress);
            return Success(index, op, target.Reference, "Changed Startdrive telegram IO size.", change);
        }

        private HardwareOperationResult InsertDriveTelegram(int index, HardwarePatchOperation op, IDictionary<string, ResolvedTarget> refs)
        {
            RequireText(op.TelegramType, "telegramType");
            if (!op.TelegramNumber.HasValue) throw new InvalidOperationException("telegramNumber is required for insert-drive-telegram.");
            var item = ResolveDriveItem(op, refs, "insert-drive-telegram", out var target);
            var change = _adapter.InsertDriveTelegram(item, op.DriveObjectIndex ?? 0, op.TelegramType!, op.TelegramNumber.Value);
            return Success(index, op, target.Reference, "Inserted Startdrive telegram.", change);
        }

        private HardwareOperationResult EraseDriveTelegram(int index, HardwarePatchOperation op, IDictionary<string, ResolvedTarget> refs)
        {
            RequireText(op.TelegramType, "telegramType");
            var item = ResolveDriveItem(op, refs, "erase-drive-telegram", out var target);
            var change = _adapter.EraseDriveTelegram(item, op.DriveObjectIndex ?? 0, op.TelegramType!, op.ExpectedTelegramNumber);
            return Success(index, op, target.Reference, "Erased Startdrive telegram.", change);
        }

        private HardwareOperationResult SetGsdParameterData(int index, HardwarePatchOperation op, IDictionary<string, ResolvedTarget> refs)
        {
            if (!op.DatasetNumber.HasValue) throw new InvalidOperationException("datasetNumber is required for set-gsd-parameter-data.");
            RequireText(op.DataHex, "dataHex");
            var target = ResolveOperationTarget(op, refs);
            var bytes = ParseHex(op.DataHex!);
            var before = _adapter.GetGsdParameterData(target.Object, op.DatasetNumber.Value, op.ByteOffset, bytes.Length);
            if (!string.IsNullOrWhiteSpace(op.ExpectedDataHex))
            {
                var expected = ParseHex(op.ExpectedDataHex!);
                if (before == null || !before.SequenceEqual(expected))
                {
                    throw new InvalidOperationException(
                        $"GSD parameter data differs from expectedDataHex at dataset {op.DatasetNumber.Value}, offset {op.ByteOffset}.");
                }
            }
            _adapter.SetGsdParameterData(target.Object, op.DatasetNumber.Value, op.ByteOffset, bytes);
            return Success(index, op, target.Reference, $"Wrote {bytes.Length} GSD parameter byte(s).", new HardwareGsdParameterChangeInfo
            {
                DatasetNumber = op.DatasetNumber.Value,
                ByteOffset = op.ByteOffset,
                BeforeHex = before == null ? null : ToHex(before),
                WrittenHex = ToHex(bytes)
            });
        }

        private HardwareOperationResult ReadGsdParameterData(int index, HardwarePatchOperation op, IDictionary<string, ResolvedTarget> refs)
        {
            if (!op.DatasetNumber.HasValue) throw new InvalidOperationException("datasetNumber is required for get-gsd-parameter-data.");
            if (!op.Length.HasValue || op.Length.Value <= 0) throw new InvalidOperationException("length is required for get-gsd-parameter-data.");
            var target = ResolveOperationTarget(op, refs);
            var bytes = _adapter.GetGsdParameterData(target.Object, op.DatasetNumber.Value, op.ByteOffset, op.Length.Value)
                ?? throw new InvalidOperationException("GSD parameter data could not be read.");
            return Success(index, op, target.Reference, $"Read {bytes.Length} GSD parameter byte(s).", new HardwareGsdParameterData
            {
                Target = target.Reference,
                DatasetNumber = op.DatasetNumber.Value,
                ByteOffset = op.ByteOffset,
                Length = bytes.Length,
                DataHex = ToHex(bytes)
            });
        }

        private HardwareOperationResult CreateSubnet(int index, HardwarePatchOperation op)
        {
            RequireText(op.TypeIdentifier, "typeIdentifier");
            RequireText(op.SubnetName, "subnetName");
            var subnet = RequireProject().Subnets.Create(op.TypeIdentifier!, op.SubnetName!);
            return Success(index, op, null, $"Created subnet: {subnet.Name}");
        }

        private HardwareOperationResult ConnectNodeToSubnet(int index, HardwarePatchOperation op, IDictionary<string, ResolvedTarget> refs)
        {
            RequireText(op.SubnetName, "subnetName");
            var target = ResolveOperationTarget(op, refs);
            var node = _adapter.RequireNetworkItem(target.Object, "Nodes", op.NodeIndex ?? 0);
            var subnet = RequireProject().Subnets.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, op.SubnetName, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Subnet not found: {op.SubnetName}");
            _adapter.ConnectNodeToSubnet(node, subnet);
            return Success(index, op, target.Reference, $"Connected node to subnet: {subnet.Name}");
        }

        private HardwareOperationResult DisconnectNode(int index, HardwarePatchOperation op, IDictionary<string, ResolvedTarget> refs)
        {
            var target = ResolveOperationTarget(op, refs);
            var node = _adapter.RequireNetworkItem(target.Object, "Nodes", op.NodeIndex ?? 0);
            _adapter.DisconnectNode(node);
            return Success(index, op, target.Reference, "Disconnected node from subnet.");
        }

        private HardwareOperationResult CreateIoSystem(int index, HardwarePatchOperation op, IDictionary<string, ResolvedTarget> refs)
        {
            RequireText(op.IoSystemName, "ioSystemName");
            var target = ResolveOperationTarget(op, refs);
            var controller = _adapter.RequireNetworkItem(target.Object, "IoControllers", op.ControllerIndex ?? 0);
            _adapter.CreateIoSystem(controller, op.IoSystemName!);
            return Success(index, op, target.Reference, $"Created IO system: {op.IoSystemName}");
        }

        private HardwareOperationResult ConnectIoConnector(int index, HardwarePatchOperation op, IDictionary<string, ResolvedTarget> refs)
        {
            RequireText(op.IoSystemName, "ioSystemName");
            var target = ResolveOperationTarget(op, refs);
            var connector = _adapter.RequireNetworkItem(target.Object, "IoConnectors", op.ConnectorIndex ?? 0);
            var ioSystem = FindIoSystem(op.IoSystemName!);
            _adapter.ConnectIoConnector(connector, ioSystem);
            return Success(index, op, target.Reference, $"Connected IO connector to IO system: {op.IoSystemName}");
        }

        private HardwareOperationResult DisconnectIoConnector(int index, HardwarePatchOperation op, IDictionary<string, ResolvedTarget> refs)
        {
            var target = ResolveOperationTarget(op, refs);
            var connector = _adapter.RequireNetworkItem(target.Object, "IoConnectors", op.ConnectorIndex ?? 0);
            _adapter.DisconnectIoConnector(connector);
            return Success(index, op, target.Reference, "Disconnected IO connector.");
        }

        private HardwareOperationResult DeleteDevice(int index, HardwarePatchOperation op, IDictionary<string, ResolvedTarget> refs)
        {
            var target = ResolveOperationTarget(op, refs);
            if (!(target.Object is Device device))
            {
                throw new InvalidOperationException("delete-device target must be a device root.");
            }
            device.Delete();
            return Success(index, op, target.Reference, $"Deleted device: {target.Reference.DeviceName}");
        }

        private HardwareOperationResult DeleteItem(int index, HardwarePatchOperation op, IDictionary<string, ResolvedTarget> refs)
        {
            var target = ResolveOperationTarget(op, refs);
            if (!(target.Object is DeviceItem item))
            {
                throw new InvalidOperationException("delete-item target must be a nested device item.");
            }
            item.Delete();
            return Success(index, op, target.Reference, "Deleted hardware item.");
        }

        private IoSystem FindIoSystem(string name)
        {
            foreach (var network in GetNetworksAsync().GetAwaiter().GetResult())
            {
                var target = Resolve(network.Target);
                if (_adapter.GetFeatureService(target, "NetworkInterface") is not NetworkInterface feature)
                {
                    continue;
                }

                foreach (var controller in feature.IoControllers)
                {
                    var ioSystem = controller.IoSystem;
                    if (ioSystem != null && string.Equals(ioSystem.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return ioSystem;
                    }
                }
            }

            throw new InvalidOperationException($"IO system not found: {name}");
        }

        private HardwareDeviceTreeInfo ToDeviceTree(Device device)
        {
            var reference = new HardwareObjectRef { DeviceName = device.Name, ExpectedTypeIdentifier = device.TypeIdentifier };
            return new HardwareDeviceTreeInfo
            {
                DeviceName = device.Name,
                TypeIdentifier = device.TypeIdentifier,
                Target = reference,
                Items = device.DeviceItems.Select(item => ToItemInfo(item, reference)).ToList()
            };
        }

        private HardwareItemInfo ToItemInfo(DeviceItem item, HardwareObjectRef parent)
        {
            var reference = ChildRef(parent, item.Name, item.TypeIdentifier);
            return ToItemInfoWithReference(item, reference);
        }

        private HardwareItemInfo ToItemInfoWithReference(DeviceItem item, HardwareObjectRef reference)
        {
            return new HardwareItemInfo
            {
                Target = CloneRef(reference),
                Name = item.Name,
                TypeIdentifier = item.TypeIdentifier,
                PositionNumber = item.PositionNumber,
                Items = item.DeviceItems.Select(child => ToItemInfo(child, reference)).ToList()
            };
        }

        private void AddNetworkInfoRecursive(HardwareObject target, HardwareObjectRef reference, ICollection<HardwareNetworkInfo> result)
        {
            var info = _adapter.GetNetworkInfo(target, reference);
            if (info != null)
            {
                result.Add(info);
            }

            foreach (var child in target.DeviceItems)
            {
                AddNetworkInfoRecursive(child, ChildRef(reference, child.Name, child.TypeIdentifier), result);
            }
        }

        private static void AddProjectIoSystemNetworkInfos(ProjectBase project, ICollection<HardwareNetworkInfo> result)
        {
            foreach (var subnet in TiaReflectionHelper.GetEnumerableProperty(project, "Subnets") ?? Enumerable.Empty<object>())
            {
                var subnetName = TiaReflectionHelper.GetName(subnet) ?? "Subnet";
                foreach (var ioSystem in TiaReflectionHelper.GetEnumerableProperty(subnet, "IoSystems") ?? Enumerable.Empty<object>())
                {
                    var ioSystemName = TiaReflectionHelper.GetName(ioSystem) ?? "IoSystem";
                    var info = new HardwareNetworkInfo
                    {
                        Target = new HardwareObjectRef
                        {
                            DeviceName = $"Subnet:{subnetName}",
                            ItemPath = new[] { ioSystemName }
                        }
                    };

                    var index = 0;
                    foreach (var connector in TiaReflectionHelper.GetEnumerableProperty(ioSystem, "ConnectedIoDevices") ?? Enumerable.Empty<object>())
                    {
                        info.ConnectedIoDevices.Add(ToConnectedIoDeviceInfo(
                            info.Target,
                            index++,
                            ioSystemName,
                            connector));
                    }

                    if (info.ConnectedIoDevices.Count > 0)
                    {
                        result.Add(info);
                    }
                }
            }
        }

        private static HardwareConnectedIoDeviceInfo ToConnectedIoDeviceInfo(
            HardwareObjectRef networkTarget,
            int connectorIndex,
            string? connectedIoSystemName,
            object connector)
        {
            var network = TiaReflectionHelper.GetProperty(connector, "Parent");
            var owner = TiaReflectionHelper.GetProperty(network, "OwnedBy")
                ?? TiaReflectionHelper.GetProperty(network, "Parent");
            var nodes = EnumerateNetworkItems(network, "Nodes")
                .Select((node, index) =>
                {
                    var subnet = TiaReflectionHelper.GetProperty(node, "ConnectedSubnet");
                    return new HardwareNodeInfo
                    {
                        Index = index,
                        Name = TiaReflectionHelper.GetProperty(node, "Name")?.ToString(),
                        NodeType = TiaReflectionHelper.GetProperty(node, "NodeType")?.ToString(),
                        ConnectedSubnet = TiaReflectionHelper.GetName(subnet),
                        Attributes = V21HardwareAdapter.GetSelectedEngineeringAttributeInfos(node, DefaultNodeAttributeNames)
                    };
                })
                .ToList();
            var networkConnectorIndex = EnumerateNetworkItems(network, "IoConnectors")
                .Select((candidate, index) => new { Connector = candidate, Index = index })
                .FirstOrDefault(candidate => ReferenceEquals(candidate.Connector, connector))?.Index ?? connectorIndex;
            return new HardwareConnectedIoDeviceInfo
            {
                NetworkTarget = CloneRef(networkTarget),
                ConnectorIndex = connectorIndex,
                ConnectedIoSystemName = connectedIoSystemName,
                OwnerName = TiaReflectionHelper.GetName(owner),
                OwnerTypeIdentifier = TiaReflectionHelper.GetProperty(owner, "TypeIdentifier")?.ToString(),
                InterfaceOperatingMode = TiaReflectionHelper.GetProperty(network, "InterfaceOperatingMode")?.ToString(),
                Nodes = nodes,
                Connector = new HardwareIoConnectorInfo
                {
                    Index = networkConnectorIndex,
                    ConnectedIoSystemName = connectedIoSystemName,
                    OwnerName = TiaReflectionHelper.GetName(owner),
                    OwnerTypeIdentifier = TiaReflectionHelper.GetProperty(owner, "TypeIdentifier")?.ToString(),
                    InterfaceOperatingMode = TiaReflectionHelper.GetProperty(network, "InterfaceOperatingMode")?.ToString(),
                    Attributes = V21HardwareAdapter.GetSelectedEngineeringAttributeInfos(connector, DefaultIoConnectorAttributeNames)
                }
            };
        }

        private static IEnumerable<object> EnumerateNetworkItems(object? network, string collectionName)
        {
            return TiaReflectionHelper.GetEnumerableProperty(network, collectionName) ?? Enumerable.Empty<object>();
        }

        private void AddIoAddressesRecursive(DeviceItem item, HardwareObjectRef reference, string[]? attributes, ICollection<HardwareIoAddressInfo> result)
        {
            foreach (var value in _adapter.GetIoAddresses(item, reference, attributes))
            {
                result.Add(value);
            }

            foreach (var child in item.DeviceItems)
            {
                AddIoAddressesRecursive(child, ChildRef(reference, child.Name, child.TypeIdentifier), attributes, result);
            }
        }

        private void AddDriveTelegramsRecursive(DeviceItem item, HardwareObjectRef reference, string[]? attributes, ICollection<HardwareDriveTelegramInfo> result)
        {
            foreach (var value in _adapter.GetDriveTelegrams(item, reference, attributes))
            {
                result.Add(value);
            }

            foreach (var child in item.DeviceItems)
            {
                AddDriveTelegramsRecursive(child, ChildRef(reference, child.Name, child.TypeIdentifier), attributes, result);
            }
        }

        private ResolvedTarget ResolveOperationTarget(HardwarePatchOperation operation, IDictionary<string, ResolvedTarget> refs)
        {
            if (!string.IsNullOrWhiteSpace(operation.TargetRef))
            {
                if (!refs.TryGetValue(operation.TargetRef!, out var existing))
                {
                    throw new InvalidOperationException($"Unknown targetRef: {operation.TargetRef}");
                }
                if (operation.Target?.ItemPath != null && operation.Target.ItemPath.Length > 0)
                {
                    return ResolveRelative(existing, operation.Target);
                }
                return existing;
            }

            if (operation.Target == null)
            {
                throw new InvalidOperationException($"Operation '{operation.Op}' requires target or targetRef.");
            }

            return new ResolvedTarget(Resolve(operation.Target), CloneRef(operation.Target));
        }

        private ResolvedTarget ResolveRelative(ResolvedTarget parent, HardwareObjectRef relative)
        {
            var current = parent.Object;
            var reference = CloneRef(parent.Reference);
            foreach (var name in relative.ItemPath ?? Array.Empty<string>())
            {
                var matches = current.DeviceItems.Where(candidate =>
                    string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
                if (matches.Count == 0)
                {
                    throw new InvalidOperationException($"Relative hardware item path was not found: {string.Join("/", relative.ItemPath)}");
                }
                if (matches.Count > 1)
                {
                    throw new InvalidOperationException($"Relative hardware item path is ambiguous at: {name}");
                }
                current = matches[0];
                reference = ChildRef(reference, current.Name, current.TypeIdentifier);
            }

            if (!string.IsNullOrWhiteSpace(relative.ExpectedTypeIdentifier) &&
                !string.Equals(current.TypeIdentifier, relative.ExpectedTypeIdentifier, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Hardware target type does not match expected type identifier: {relative.ExpectedTypeIdentifier}");
            }

            return new ResolvedTarget(current, reference);
        }

        private DeviceItem ResolveDriveItem(HardwarePatchOperation operation, IDictionary<string, ResolvedTarget> refs, string operationName, out ResolvedTarget target)
        {
            target = ResolveOperationTarget(operation, refs);
            if (!(target.Object is DeviceItem item))
            {
                throw new InvalidOperationException($"{operationName} target must be a nested drive device item.");
            }
            return item;
        }

        private HardwareObject Resolve(HardwareObjectRef reference)
        {
            if (reference == null || string.IsNullOrWhiteSpace(reference.DeviceName))
            {
                throw new ArgumentException("Hardware target deviceName is required.");
            }

            var device = ProjectDeviceEnumerator.FindDevice(RequireProject(), reference.DeviceName)
                ?? throw new InvalidOperationException($"Device not found: {reference.DeviceName}");

            HardwareObject current = device;
            foreach (var name in reference.ItemPath ?? Array.Empty<string>())
            {
                var matches = current.DeviceItems.Where(candidate =>
                    string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
                if (matches.Count == 0)
                {
                    throw new InvalidOperationException($"Hardware item path was not found: {reference.DeviceName}/{string.Join("/", reference.ItemPath)}");
                }
                if (matches.Count > 1)
                {
                    throw new InvalidOperationException($"Hardware item path is ambiguous at: {name}");
                }
                current = matches[0];
            }

            if (!string.IsNullOrWhiteSpace(reference.ExpectedTypeIdentifier) &&
                !string.Equals(current.TypeIdentifier, reference.ExpectedTypeIdentifier, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Hardware target type does not match expected type identifier: {reference.ExpectedTypeIdentifier}");
            }

            return current;
        }

        private ProjectBase RequireProject()
        {
            return _sessionContext.Project ?? throw new InvalidOperationException("No TIA Portal project is open.");
        }

        private TiaPortal RequirePortal()
        {
            return _sessionContext.Portal ?? throw new InvalidOperationException("Not connected to TIA Portal.");
        }

        private static string NormalizeOperation(string op)
        {
            return op.Trim().Replace("_", "-").ToLowerInvariant();
        }

        private static void RequireText(string? value, string property)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"{property} is required.");
        }

        private static void RequireDestructiveConfirmation(bool confirmed, string operation)
        {
            if (!confirmed) throw new InvalidOperationException($"{operation} requires confirmDestructive=true.");
        }

        private void EnsureExpectedAttributes(HardwareObject target, IDictionary<string, object?>? expectedAttributes)
        {
            if (expectedAttributes == null) return;
            foreach (var expected in expectedAttributes)
            {
                var actual = _adapter.GetAttribute(target, expected.Key);
                if (!AttributeValuesEqual(actual, ConvertJsonValue(expected.Value)))
                {
                    throw new InvalidOperationException($"Hardware attribute '{expected.Key}' differs from expectedAttributes.");
                }
            }
        }

        private void EnsureExpectedFeatureAttributes(HardwareObject target, string featureService, IDictionary<string, object?>? expectedAttributes)
        {
            if (expectedAttributes == null) return;
            foreach (var expected in expectedAttributes)
            {
                var actual = _adapter.GetFeatureAttribute(target, featureService, expected.Key);
                if (!AttributeValuesEqual(actual, ConvertJsonValue(expected.Value)))
                {
                    throw new InvalidOperationException($"{featureService} feature attribute '{expected.Key}' differs from expectedAttributes.");
                }
            }
        }

        private void EnsureExpectedNetworkItemAttributes(HardwareObject target, string collectionName, int index, IDictionary<string, object?>? expectedAttributes)
        {
            if (expectedAttributes == null) return;
            foreach (var expected in expectedAttributes)
            {
                var actual = _adapter.GetNetworkItemAttribute(target, collectionName, index, expected.Key);
                if (!AttributeValuesEqual(actual, ConvertJsonValue(expected.Value)))
                {
                    throw new InvalidOperationException($"{collectionName} item attribute '{expected.Key}' differs from expectedAttributes.");
                }
            }
        }

        private object? TryGetAttribute(HardwareObject target, string name)
        {
            try
            {
                return _adapter.GetAttribute(target, name);
            }
            catch
            {
                return null;
            }
        }

        private object? TryGetFeatureAttribute(HardwareObject target, string featureService, string name)
        {
            try
            {
                return _adapter.GetFeatureAttribute(target, featureService, name);
            }
            catch
            {
                return null;
            }
        }

        private object? TryGetNetworkItemAttribute(HardwareObject target, string collectionName, int index, string name)
        {
            try
            {
                return _adapter.GetNetworkItemAttribute(target, collectionName, index, name);
            }
            catch
            {
                return null;
            }
        }

        private static bool AttributeValuesEqual(object? actual, object? expected)
        {
            return string.Equals(
                Convert.ToString(actual, CultureInfo.InvariantCulture),
                Convert.ToString(expected, CultureInfo.InvariantCulture),
                StringComparison.Ordinal);
        }

        private static void AddResultReference(string? id, ResolvedTarget value, IDictionary<string, ResolvedTarget> references)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            if (references.ContainsKey(id!)) throw new InvalidOperationException($"Duplicate resultId: {id}");
            references[id!] = value;
        }

        private static HardwareOperationResult Success(int index, HardwarePatchOperation operation, HardwareObjectRef? target, string message, object? data = null)
        {
            return new HardwareOperationResult
            {
                Index = index,
                Op = operation.Op,
                Success = true,
                Message = message,
                Target = target,
                ResultId = operation.ResultId,
                Data = data
            };
        }

        private static HardwareObjectRef ChildRef(HardwareObjectRef parent, string name, string? typeIdentifier)
        {
            return new HardwareObjectRef
            {
                DeviceName = parent.DeviceName,
                ItemPath = (parent.ItemPath ?? Array.Empty<string>()).Concat(new[] { name }).ToArray(),
                ExpectedTypeIdentifier = typeIdentifier
            };
        }

        private HardwareObjectRef? FindActualReference(HardwareObject target, string deviceName)
        {
            var device = ProjectDeviceEnumerator.FindDevice(RequireProject(), deviceName)
                ?? throw new InvalidOperationException($"Device not found: {deviceName}");
            var root = new HardwareObjectRef
            {
                DeviceName = device.Name,
                ExpectedTypeIdentifier = device.TypeIdentifier
            };
            if (ReferenceEquals(device, target))
            {
                return root;
            }

            foreach (var item in device.DeviceItems)
            {
                var found = FindActualReferenceRecursive(item, target, ChildRef(root, item.Name, item.TypeIdentifier));
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static HardwareObjectRef? FindActualReferenceRecursive(DeviceItem current, HardwareObject target, HardwareObjectRef reference)
        {
            if (HardwareObjectsEqual(current, target))
            {
                return CloneRef(reference);
            }

            foreach (var child in current.DeviceItems)
            {
                var found = FindActualReferenceRecursive(child, target, ChildRef(reference, child.Name, child.TypeIdentifier));
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static bool HardwareObjectsEqual(HardwareObject left, HardwareObject right)
        {
            return ReferenceEquals(left, right) || left.Equals(right);
        }

        internal static string? ValidatePlugPlacement(string itemName, HardwareObjectRef requestedReference, HardwareObjectRef actualReference, bool allowDisplacedPlacement)
        {
            if (HardwarePathsEqual(requestedReference, actualReference))
            {
                return null;
            }

            if (!allowDisplacedPlacement)
            {
                throw new InvalidOperationException(
                    $"Plugged hardware item '{itemName}' was placed at '{FormatHardwarePath(actualReference)}' instead of requested '{FormatHardwarePath(requestedReference)}'. " +
                    "Set allowDisplacedPlacement=true to accept the actual placement.");
            }

            return $"TIA placed the plugged item at '{FormatHardwarePath(actualReference)}' instead of requested '{FormatHardwarePath(requestedReference)}'.";
        }

        internal static bool HardwarePathsEqual(HardwareObjectRef? left, HardwareObjectRef? right)
        {
            if (left == null || right == null)
            {
                return left == right;
            }

            return string.Equals(left.DeviceName, right.DeviceName, StringComparison.OrdinalIgnoreCase)
                && (left.ItemPath ?? Array.Empty<string>()).SequenceEqual(
                    right.ItemPath ?? Array.Empty<string>(),
                    StringComparer.OrdinalIgnoreCase);
        }

        internal static string FormatHardwarePath(HardwareObjectRef reference)
        {
            var parts = new[] { reference.DeviceName }
                .Concat(reference.ItemPath ?? Array.Empty<string>())
                .Where(part => !string.IsNullOrWhiteSpace(part));
            return string.Join("/", parts);
        }

        private static HardwareObjectRef CloneRef(HardwareObjectRef source)
        {
            return new HardwareObjectRef
            {
                DeviceName = source.DeviceName,
                ItemPath = source.ItemPath?.ToArray() ?? Array.Empty<string>(),
                ExpectedTypeIdentifier = source.ExpectedTypeIdentifier
            };
        }

        internal static byte[] ParseHex(string text)
        {
            var compact = new string((text ?? string.Empty).Where(character => !char.IsWhiteSpace(character) && character != '-' && character != ',').ToArray());
            if (compact.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) compact = compact.Substring(2);
            if (compact.Length == 0 || compact.Length % 2 != 0)
            {
                throw new FormatException("dataHex must contain an even number of hexadecimal digits.");
            }

            var bytes = new byte[compact.Length / 2];
            for (var index = 0; index < bytes.Length; index++)
            {
                bytes[index] = byte.Parse(compact.Substring(index * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }
            return bytes;
        }

        internal static string ToHex(IEnumerable<byte> data)
        {
            return string.Join(" ", data.Select(value => value.ToString("X2", CultureInfo.InvariantCulture)));
        }

        private static object ConvertJsonValue(object? value)
        {
            if (!(value is JsonElement element)) return value ?? string.Empty;
            switch (element.ValueKind)
            {
                case JsonValueKind.True: return true;
                case JsonValueKind.False: return false;
                case JsonValueKind.Number:
                    if (element.TryGetInt32(out var intValue)) return intValue;
                    if (element.TryGetInt64(out var longValue)) return longValue;
                    return element.GetDouble();
                case JsonValueKind.String: return element.GetString() ?? string.Empty;
                default: return element.ToString();
            }
        }

        private object ResolvePlcSoftware(ProjectBase project, string plcName)
        {
            if (_sessionContext.PlcSoftware != null &&
                string.Equals(_sessionContext.CurrentPlcName, plcName, StringComparison.OrdinalIgnoreCase))
            {
                return _sessionContext.PlcSoftware;
            }

            foreach (var device in ProjectDeviceEnumerator.EnumerateDevices(project))
            {
                var searchItems = device.Name.Equals(plcName, StringComparison.OrdinalIgnoreCase)
                    ? device.DeviceItems.Cast<DeviceItem>()
                    : device.DeviceItems.Cast<DeviceItem>().Where(item => item.Name.Equals(plcName, StringComparison.OrdinalIgnoreCase));

                foreach (var item in searchItems)
                {
                    try
                    {
                        var software = _opennessAdapter.GetPlcSoftware(item);
                        if (software == null)
                        {
                            continue;
                        }

                        _sessionContext.PlcSoftware = software;
                        _sessionContext.CurrentPlcDevice = device;
                        _sessionContext.CurrentPlcDeviceItem = item;
                        _sessionContext.CurrentPlcName = plcName;
                        return software;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogDebug(ex, "Failed to inspect PLC software for device item {DeviceItemName}", item.Name);
                    }
                }
            }

            throw new InvalidOperationException($"Could not find PLC software for PLC device '{plcName}'.");
        }

        private IEnumerable<PlcTagSnapshot> EnumeratePlcTags(object plcSoftware, string? tagTableFilter)
        {
            var tagTables = _opennessAdapter.GetTagTables(plcSoftware);
            if (tagTables == null)
            {
                yield break;
            }

            foreach (var tagTable in tagTables)
            {
                var tableName = _opennessAdapter.GetName(tagTable) ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(tagTableFilter) &&
                    tableName.IndexOf(tagTableFilter!, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                var tags = _opennessAdapter.GetTags(tagTable);
                if (tags == null)
                {
                    continue;
                }

                foreach (var tag in tags)
                {
                    yield return new PlcTagSnapshot
                    {
                        TagTableName = tableName,
                        TagName = _opennessAdapter.GetName(tag) ?? string.Empty,
                        LogicalAddress = _opennessAdapter.GetLogicalAddress(tag) ?? string.Empty
                    };
                }
            }
        }

        private IEnumerable<HardwareAddressRangeInfo> CollectHardwareAddressRanges()
        {
            foreach (var address in GetIoAddressesAsync(null, new[] { "StartAddress", "IoType", "Length" }).GetAwaiter().GetResult())
            {
                var range = TryCreateHardwareRange(address.Target, address.Attributes, "HardwareAddress");
                if (range != null)
                {
                    yield return range;
                }
            }

            foreach (var telegram in GetDriveTelegramsAsync(null, new[] { "StartAddress", "IoType", "Length" }).GetAwaiter().GetResult())
            {
                foreach (var address in telegram.Addresses)
                {
                    var range = TryCreateHardwareRange(
                        address.Target,
                        address.Attributes,
                        $"DriveTelegram[{telegram.DriveObjectIndex}:{telegram.TelegramType}:{telegram.TelegramNumber}]");
                    if (range != null)
                    {
                        yield return range;
                    }
                }
            }
        }

        internal static HardwareTagAddressInfo? TryParseTagAddress(string tagTableName, string tagName, string logicalAddress)
        {
            var match = Regex.Match(logicalAddress ?? string.Empty, @"^%([IQ])([BWDL]?)(\d+)(?:\.\d+)?$", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return null;
            }

            var area = match.Groups[1].Value.ToUpperInvariant();
            var width = match.Groups[2].Value.ToUpperInvariant();
            var start = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
            var length = width switch
            {
                "W" => 2,
                "D" => 4,
                "L" => 8,
                _ => 1
            };

            return new HardwareTagAddressInfo
            {
                TagTableName = tagTableName,
                TagName = tagName,
                LogicalAddress = logicalAddress ?? string.Empty,
                Area = area,
                StartAddress = start,
                Length = length,
                EndAddress = start + length - 1
            };
        }

        private static HardwareAddressRangeInfo? TryCreateHardwareRange(
            HardwareObjectRef target,
            IEnumerable<HardwareAttributeInfo> attributes,
            string source)
        {
            var values = attributes.ToDictionary(attribute => attribute.Name, attribute => attribute.Value, StringComparer.OrdinalIgnoreCase);
            if (!TryParseInteger(values.TryGetValue("StartAddress", out var startText) ? startText : null, out var start))
            {
                return null;
            }

            var ioType = values.TryGetValue("IoType", out var ioTypeText) ? ioTypeText : null;
            var area = NormalizeIoArea(ioType);
            if (area == null)
            {
                return null;
            }

            var length = TryParseInteger(values.TryGetValue("Length", out var lengthText) ? lengthText : null, out var parsedLength)
                ? Math.Max(1, parsedLength)
                : 1;

            return new HardwareAddressRangeInfo
            {
                Target = CloneRef(target),
                Source = source,
                Area = area,
                StartAddress = start,
                Length = length,
                EndAddress = start + length - 1,
                IoType = ioType
            };
        }

        private static IEnumerable<HardwareTagAddressRangeInfo> BuildUnmatchedRanges(IEnumerable<HardwareTagAddressInfo> tags)
        {
            return tags
                .GroupBy(tag => new { tag.TagTableName, tag.Area })
                .Select(group => new HardwareTagAddressRangeInfo
                {
                    TagTableName = group.Key.TagTableName,
                    Area = group.Key.Area,
                    StartAddress = group.Min(tag => tag.StartAddress),
                    EndAddress = group.Max(tag => tag.EndAddress),
                    TagCount = group.Count(),
                    ExampleTags = group
                        .OrderBy(tag => tag.StartAddress)
                        .ThenBy(tag => tag.TagName, StringComparer.OrdinalIgnoreCase)
                        .Take(8)
                        .Select(tag => $"{tag.TagName}={tag.LogicalAddress}")
                        .ToList()
                })
                .OrderBy(range => range.TagTableName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(range => range.Area, StringComparer.OrdinalIgnoreCase)
                .ThenBy(range => range.StartAddress);
        }

        private static IEnumerable<HardwareObjectRef> FindHardwareTargets(
            IEnumerable<Device> devices,
            string searchText,
            IEnumerable<HardwareCatalogItemInfo> catalogMatches)
        {
            var catalogTokens = new HashSet<string>(
                catalogMatches
                    .Select(match => match.TypeIdentifier)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Cast<string>(),
                StringComparer.OrdinalIgnoreCase);

            foreach (var device in devices)
            {
                var root = new HardwareObjectRef { DeviceName = device.Name, ExpectedTypeIdentifier = device.TypeIdentifier };
                if (MatchesHardwareHint(device.Name, device.TypeIdentifier, searchText, catalogTokens))
                {
                    yield return root;
                }

                foreach (var target in FindHardwareTargetsRecursive(device.DeviceItems.Cast<DeviceItem>(), root, searchText, catalogTokens))
                {
                    yield return target;
                }
            }
        }

        private static IEnumerable<HardwareObjectRef> FindHardwareTargetsRecursive(
            IEnumerable<DeviceItem> items,
            HardwareObjectRef parent,
            string searchText,
            HashSet<string> catalogTypeIdentifiers)
        {
            foreach (var item in items)
            {
                var child = ChildRef(parent, item.Name, item.TypeIdentifier);
                if (MatchesHardwareHint(item.Name, item.TypeIdentifier, searchText, catalogTypeIdentifiers))
                {
                    yield return child;
                }

                foreach (var nested in FindHardwareTargetsRecursive(item.DeviceItems.Cast<DeviceItem>(), child, searchText, catalogTypeIdentifiers))
                {
                    yield return nested;
                }
            }
        }

        private static bool MatchesHardwareHint(
            string? name,
            string? typeIdentifier,
            string searchText,
            HashSet<string> catalogTypeIdentifiers)
        {
            return (!string.IsNullOrWhiteSpace(name) && name!.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                (!string.IsNullOrWhiteSpace(typeIdentifier) &&
                    (typeIdentifier!.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    catalogTypeIdentifiers.Contains(typeIdentifier!)));
        }

        private static void AddAuditWarnings(HardwareTagMappingAuditResult result)
        {
            if (result.UnmatchedTagCount > 0)
            {
                result.Warnings.Add($"{result.UnmatchedTagCount} PLC tag address(es) do not overlap discovered hardware IO ranges.");
            }

            if (result.CatalogMatches.Count > 0 &&
                result.MatchingHardwareTargets.Count == 0 &&
                result.MatchingConnectedIoDevices.Count == 0)
            {
                result.Warnings.Add($"Catalog contains match(es) for '{result.CatalogSearchText}', but no configured hardware device/item matched that hint.");
            }
        }

        private IEnumerable<HardwareConnectedIoDeviceInfo> FindMatchingConnectedIoDevices(
            string searchText,
            IEnumerable<HardwareCatalogItemInfo> catalogMatches)
        {
            var catalogTokens = new HashSet<string>(
                catalogMatches
                    .Select(match => match.TypeIdentifier)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Cast<string>(),
                StringComparer.OrdinalIgnoreCase);

            foreach (var network in GetNetworksAsync().GetAwaiter().GetResult())
            {
                foreach (var connected in network.ConnectedIoDevices)
                {
                    if (!MatchesHardwareHint(connected.OwnerName, connected.OwnerTypeIdentifier, searchText, catalogTokens))
                    {
                        continue;
                    }

                    yield return connected;
                }

                foreach (var connector in network.IoConnectors)
                {
                    if (!MatchesHardwareHint(connector.OwnerName, connector.OwnerTypeIdentifier, searchText, catalogTokens))
                    {
                        continue;
                    }

                    yield return new HardwareConnectedIoDeviceInfo
                    {
                        NetworkTarget = CloneRef(network.Target),
                        ConnectorIndex = connector.Index,
                        ConnectedIoSystemName = connector.ConnectedIoSystemName,
                        OwnerName = connector.OwnerName,
                        OwnerTypeIdentifier = connector.OwnerTypeIdentifier,
                        InterfaceOperatingMode = connector.InterfaceOperatingMode,
                        Nodes = network.Nodes,
                        Connector = connector
                    };
                }
            }
        }

        private static string? NormalizeIoArea(string? ioType)
        {
            if (string.IsNullOrWhiteSpace(ioType))
            {
                return null;
            }

            if (ioType!.IndexOf("Input", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "I";
            }

            if (ioType.IndexOf("Output", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Q";
            }

            return null;
        }

        private static bool TryParseInteger(string? value, out int parsed)
        {
            parsed = 0;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var match = Regex.Match(value!, @"-?\d+");
            return match.Success && int.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);
        }

        private static bool RangesOverlap(int leftStart, int leftEnd, int rightStart, int rightEnd)
        {
            return leftStart <= rightEnd && rightStart <= leftEnd;
        }

        private static string FormatRange(HardwareAddressRangeInfo range)
        {
            var path = range.Target.ItemPath == null || range.Target.ItemPath.Length == 0
                ? range.Target.DeviceName
                : range.Target.DeviceName + "/" + string.Join("/", range.Target.ItemPath);
            return $"{range.Area}{range.StartAddress}-{range.EndAddress} {path} [{range.Source}]";
        }

        private sealed class PlcTagSnapshot
        {
            public string TagTableName { get; set; } = string.Empty;
            public string TagName { get; set; } = string.Empty;
            public string LogicalAddress { get; set; } = string.Empty;
        }

        private sealed class ResolvedTarget
        {
            public ResolvedTarget(HardwareObject obj, HardwareObjectRef reference)
            {
                Object = obj;
                Reference = reference;
            }

            public HardwareObject Object { get; }
            public HardwareObjectRef Reference { get; }
        }
    }
}
