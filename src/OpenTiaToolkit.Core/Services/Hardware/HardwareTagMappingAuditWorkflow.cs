using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Services.PlcTags;
using OpenTiaToolkit.Core.Services.Hardware.Patching;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using OpenTiaToolkit.Core.Services.V21;

namespace OpenTiaToolkit.Core.Services.Hardware
{
    internal sealed class HardwareTagMappingAuditWorkflow
    {
        private readonly HardwareEngineeringContext _context;
        private readonly HardwareTopologyReader _topology;
        private readonly HardwareDriveReader _drives;
        private readonly V21HardwareAdapter _adapter;
        private readonly V21SoftwareDiscovery _software;
        private readonly IPlcTagCatalogReader _tagCatalogReader;
        private readonly ILogger? _logger;

        public HardwareTagMappingAuditWorkflow(
            HardwareEngineeringContext context,
            HardwareTopologyReader topology,
            HardwareDriveReader drives,
            V21HardwareAdapter adapter,
            V21OpennessAdapter opennessAdapter,
            IPlcTagCatalogReader tagCatalogReader,
            ILogger? logger)
        {
            _context = context;
            _topology = topology;
            _drives = drives;
            _adapter = adapter;
            _software = opennessAdapter.Software;
            _tagCatalogReader = tagCatalogReader;
            _logger = logger;
        }

        public HardwareTagMappingAuditResult Audit(string plcName, string? tagTableFilter, string? catalogSearchText, int maxUnmatchedTags)
        {
            HardwarePatchRules.RequireText(plcName, "plcName");
            var project = _context.RequireProject();
            var result = new HardwareTagMappingAuditResult
            {
                PlcName = plcName,
                TagTableFilter = string.IsNullOrWhiteSpace(tagTableFilter) ? null : tagTableFilter,
                CatalogSearchText = string.IsNullOrWhiteSpace(catalogSearchText) ? null : catalogSearchText
            };
            var devices = ProjectDeviceEnumerator.EnumerateDevices(project).ToList();
            result.HardwareDeviceCount = devices.Count;
            result.HardwareDevices = devices.Select(device => $"{device.Name} ({device.TypeIdentifier})").ToList();

            var catalog = _tagCatalogReader.ReadCatalog(ResolvePlcSoftware(project, plcName));
            var tags = (catalog?.AllActiveTags ?? Enumerable.Empty<PlcTagState>())
                .Where(tag => string.IsNullOrWhiteSpace(tagTableFilter) ||
                    tag.Table.Name.IndexOf(tagTableFilter!, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            result.ScannedTagCount = tags.Count;
            var tagAddresses = tags.Select(tag => HardwareAuditRules.TryParseTagAddress(tag.Table.Name, tag.Name, tag.RawLogicalAddress))
                .Where(tag => tag != null).Cast<HardwareTagAddressInfo>().ToList();
            result.AddressedTagCount = tagAddresses.Count;
            var ranges = CollectHardwareAddressRanges().ToList();
            result.HardwareIoRangeCount = ranges.Count;
            result.HardwareRanges = ranges;

            foreach (var tag in tagAddresses)
            {
                var match = ranges.FirstOrDefault(range => string.Equals(range.Area, tag.Area, StringComparison.OrdinalIgnoreCase)
                    && HardwareAuditRules.RangesOverlap(tag.StartAddress, tag.EndAddress, range.StartAddress, range.EndAddress));
                if (match == null) result.UnmatchedTags.Add(tag);
                else { tag.MatchedHardwareRange = HardwareAuditRules.FormatRange(match); result.MatchedTagCount++; }
            }
            result.UnmatchedTagCount = result.UnmatchedTags.Count;
            result.UnmatchedRanges = HardwareAuditRules.BuildUnmatchedRanges(result.UnmatchedTags).ToList();
            if (maxUnmatchedTags >= 0 && result.UnmatchedTags.Count > maxUnmatchedTags)
                result.UnmatchedTags = result.UnmatchedTags.Take(maxUnmatchedTags).ToList();

            if (!string.IsNullOrWhiteSpace(catalogSearchText))
            {
                result.CatalogMatches = _adapter.FindCatalogItems(_context.RequirePortal(), catalogSearchText!, null).Take(50).ToList();
                result.MatchingHardwareTargets = FindHardwareTargets(devices, catalogSearchText!, result.CatalogMatches).ToList();
                result.MatchingConnectedIoDevices = FindMatchingConnectedIoDevices(catalogSearchText!, result.CatalogMatches).ToList();
            }
            HardwareAuditRules.AddWarnings(result);
            return result;
        }

        private object ResolvePlcSoftware(ProjectBase project, string plcName)
        {
            var session = _context.Session;
            if (session.PlcSoftware != null && string.Equals(session.CurrentPlcName, plcName, StringComparison.OrdinalIgnoreCase))
                return session.PlcSoftware;
            foreach (var device in ProjectDeviceEnumerator.EnumerateDevices(project))
            {
                var items = device.Name.Equals(plcName, StringComparison.OrdinalIgnoreCase)
                    ? device.DeviceItems.Cast<DeviceItem>()
                    : device.DeviceItems.Cast<DeviceItem>().Where(item => item.Name.Equals(plcName, StringComparison.OrdinalIgnoreCase));
                foreach (var item in items)
                {
                    try
                    {
                        var software = _software.GetPlcSoftware(item);
                        if (software == null) continue;
                        session.PlcSoftware = software;
                        session.CurrentPlcDevice = device;
                        session.CurrentPlcDeviceItem = item;
                        session.CurrentPlcName = plcName;
                        return software;
                    }
                    catch (Exception ex) { _logger?.LogDebug(ex, "Failed to inspect PLC software for device item {DeviceItemName}", item.Name); }
                }
            }
            throw new InvalidOperationException($"Could not find PLC software for PLC device '{plcName}'.");
        }

        private IEnumerable<HardwareAddressRangeInfo> CollectHardwareAddressRanges()
        {
            foreach (var address in _topology.GetIoAddresses(null, new[] { "StartAddress", "IoType", "Length" }))
            {
                var range = HardwareAuditRules.TryCreateHardwareRange(address.Target, address.Attributes, "HardwareAddress");
                if (range != null) yield return range;
            }
            foreach (var telegram in _drives.GetDriveTelegrams(null, new[] { "StartAddress", "IoType", "Length" }))
            foreach (var address in telegram.Addresses)
            {
                var range = HardwareAuditRules.TryCreateHardwareRange(address.Target, address.Attributes,
                    $"DriveTelegram[{telegram.DriveObjectIndex}:{telegram.TelegramType}:{telegram.TelegramNumber}]");
                if (range != null) yield return range;
            }
        }

        private static IEnumerable<HardwareObjectRef> FindHardwareTargets(IEnumerable<Device> devices, string search, IEnumerable<HardwareCatalogItemInfo> matches)
        {
            var ids = HardwareAuditRules.CatalogTypeIdentifiers(matches);
            foreach (var device in devices)
            {
                var root = new HardwareObjectRef { DeviceName = device.Name, ExpectedTypeIdentifier = device.TypeIdentifier };
                if (HardwareAuditRules.MatchesHardwareHint(device.Name, device.TypeIdentifier, search, ids)) yield return root;
                foreach (var target in FindHardwareTargets(device.DeviceItems.Cast<DeviceItem>(), root, search, ids)) yield return target;
            }
        }

        private static IEnumerable<HardwareObjectRef> FindHardwareTargets(IEnumerable<DeviceItem> items, HardwareObjectRef parent, string search, HashSet<string> ids)
        {
            foreach (var item in items)
            {
                var child = HardwareReferenceRules.Child(parent, item.Name, item.TypeIdentifier);
                if (HardwareAuditRules.MatchesHardwareHint(item.Name, item.TypeIdentifier, search, ids)) yield return child;
                foreach (var nested in FindHardwareTargets(item.DeviceItems.Cast<DeviceItem>(), child, search, ids)) yield return nested;
            }
        }

        private IEnumerable<HardwareConnectedIoDeviceInfo> FindMatchingConnectedIoDevices(string search, IEnumerable<HardwareCatalogItemInfo> matches)
        {
            var ids = HardwareAuditRules.CatalogTypeIdentifiers(matches);
            foreach (var network in _topology.GetNetworks())
            {
                foreach (var connected in network.ConnectedIoDevices)
                    if (HardwareAuditRules.MatchesHardwareHint(connected.OwnerName, connected.OwnerTypeIdentifier, search, ids)) yield return connected;
                foreach (var connector in network.IoConnectors)
                    if (HardwareAuditRules.MatchesHardwareHint(connector.OwnerName, connector.OwnerTypeIdentifier, search, ids))
                        yield return new HardwareConnectedIoDeviceInfo
                        {
                            NetworkTarget = HardwareReferenceRules.Clone(network.Target), ConnectorIndex = connector.Index,
                            ConnectedIoSystemName = connector.ConnectedIoSystemName, OwnerName = connector.OwnerName,
                            OwnerTypeIdentifier = connector.OwnerTypeIdentifier, InterfaceOperatingMode = connector.InterfaceOperatingMode,
                            Nodes = network.Nodes, Connector = connector
                        };
            }
        }
    }

    internal static class HardwareAuditRules
    {
        public static HardwareTagAddressInfo? TryParseTagAddress(string table, string name, string address)
        {
            var match = Regex.Match(address ?? string.Empty, @"^%([IQ])([BWDL]?)(\d+)(?:\.\d+)?$", RegexOptions.IgnoreCase);
            if (!match.Success) return null;
            var width = match.Groups[2].Value.ToUpperInvariant();
            var start = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
            var length = width == "W" ? 2 : width == "D" ? 4 : width == "L" ? 8 : 1;
            return new HardwareTagAddressInfo
            {
                TagTableName = table, TagName = name, LogicalAddress = address ?? string.Empty,
                Area = match.Groups[1].Value.ToUpperInvariant(), StartAddress = start, Length = length, EndAddress = start + length - 1
            };
        }

        public static HardwareAddressRangeInfo? TryCreateHardwareRange(HardwareObjectRef target, IEnumerable<HardwareAttributeInfo> attributes, string source)
        {
            var values = attributes.ToDictionary(attribute => attribute.Name, attribute => attribute.Value, StringComparer.OrdinalIgnoreCase);
            if (!TryParseInteger(values.TryGetValue("StartAddress", out var startText) ? startText : null, out var start)) return null;
            var ioType = values.TryGetValue("IoType", out var ioTypeText) ? ioTypeText : null;
            var area = NormalizeIoArea(ioType);
            if (area == null) return null;
            var length = TryParseInteger(values.TryGetValue("Length", out var lengthText) ? lengthText : null, out var parsed) ? Math.Max(1, parsed) : 1;
            return new HardwareAddressRangeInfo
            {
                Target = HardwareReferenceRules.Clone(target), Source = source, Area = area, StartAddress = start,
                Length = length, EndAddress = start + length - 1, IoType = ioType
            };
        }

        public static IEnumerable<HardwareTagAddressRangeInfo> BuildUnmatchedRanges(IEnumerable<HardwareTagAddressInfo> tags) => tags
            .GroupBy(tag => new { tag.TagTableName, tag.Area })
            .Select(group => new HardwareTagAddressRangeInfo
            {
                TagTableName = group.Key.TagTableName, Area = group.Key.Area,
                StartAddress = group.Min(tag => tag.StartAddress), EndAddress = group.Max(tag => tag.EndAddress), TagCount = group.Count(),
                ExampleTags = group.OrderBy(tag => tag.StartAddress).ThenBy(tag => tag.TagName, StringComparer.OrdinalIgnoreCase)
                    .Take(8).Select(tag => $"{tag.TagName}={tag.LogicalAddress}").ToList()
            }).OrderBy(range => range.TagTableName, StringComparer.OrdinalIgnoreCase)
              .ThenBy(range => range.Area, StringComparer.OrdinalIgnoreCase).ThenBy(range => range.StartAddress);

        public static HashSet<string> CatalogTypeIdentifiers(IEnumerable<HardwareCatalogItemInfo> matches) => new HashSet<string>(
            matches.Select(match => match.TypeIdentifier).Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>(), StringComparer.OrdinalIgnoreCase);
        public static bool MatchesHardwareHint(string? name, string? type, string search, HashSet<string> ids) =>
            (!string.IsNullOrWhiteSpace(name) && name!.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) ||
            (!string.IsNullOrWhiteSpace(type) && (type!.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 || ids.Contains(type!)));
        public static bool RangesOverlap(int leftStart, int leftEnd, int rightStart, int rightEnd) => leftStart <= rightEnd && rightStart <= leftEnd;
        public static string FormatRange(HardwareAddressRangeInfo range) =>
            $"{range.Area}{range.StartAddress}-{range.EndAddress} {HardwareReferenceRules.FormatPath(range.Target)} [{range.Source}]";
        public static void AddWarnings(HardwareTagMappingAuditResult result)
        {
            if (result.UnmatchedTagCount > 0) result.Warnings.Add($"{result.UnmatchedTagCount} PLC tag address(es) do not overlap discovered hardware IO ranges.");
            if (result.CatalogMatches.Count > 0 && result.MatchingHardwareTargets.Count == 0 && result.MatchingConnectedIoDevices.Count == 0)
                result.Warnings.Add($"Catalog contains match(es) for '{result.CatalogSearchText}', but no configured hardware device/item matched that hint.");
        }
        private static string? NormalizeIoArea(string? value) => string.IsNullOrWhiteSpace(value) ? null :
            value!.IndexOf("Input", StringComparison.OrdinalIgnoreCase) >= 0 ? "I" :
            value.IndexOf("Output", StringComparison.OrdinalIgnoreCase) >= 0 ? "Q" : null;
        private static bool TryParseInteger(string? value, out int parsed)
        {
            parsed = 0;
            if (string.IsNullOrWhiteSpace(value)) return false;
            var match = Regex.Match(value!, @"-?\d+");
            return match.Success && int.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);
        }
    }
}
