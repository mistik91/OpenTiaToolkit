using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace OpenTiaToolkit.Core.Services.AutomationMl;

internal sealed class AmlModuleAddition(string name, string typeIdentifier, int slot, IReadOnlyList<AmlIoAddress> addresses)
{
    public string Name { get; } = name;
    public string TypeIdentifier { get; } = typeIdentifier;
    public int Slot { get; } = slot;
    public IReadOnlyList<AmlIoAddress> Addresses { get; } = addresses;
}

internal sealed class AmlIoAddress(string direction, int start, int? length)
{
    public string Direction { get; } = direction;
    public int Start { get; } = start;
    public int? Length { get; } = length;
}

internal sealed class AmlStationAddition(string name, string deviceType, AmlModuleAddition head,
    IReadOnlyList<AmlModuleAddition> modules, string label, string subnet, string ioSystem, string controller,
    string address, string? subnetMask)
{
    public string Name { get; } = name;
    public string DeviceType { get; } = deviceType;
    public AmlModuleAddition Head { get; } = head;
    public IReadOnlyList<AmlModuleAddition> Modules { get; } = modules;
    public string InterfaceLabel { get; } = label;
    public string Subnet { get; } = subnet;
    public string IoSystem { get; } = ioSystem;
    public string Controller { get; } = controller;
    public string Address { get; } = address;
    public string? SubnetMask { get; } = subnetMask;
}

internal sealed class AmlTagAddition(string device, string table, bool defaultTable, string name, string type, string address)
{
    public string Device { get; } = device;
    public string Table { get; } = table;
    public bool DefaultTable { get; } = defaultTable;
    public string Name { get; } = name;
    public string DataType { get; } = type;
    public string Address { get; } = address;
}

internal sealed class AmlAdditivePlan
{
    public List<AmlStationAddition> Stations { get; } = new();
    public List<AmlTagAddition> Tags { get; } = new();
    public List<AmlDifference> Conflicts { get; } = new();
    public bool CanApply => Conflicts.Count == 0 && (Stations.Count > 0 || Tags.Count > 0);
}

internal static class AmlAdditivePlanner
{
    private static readonly StringComparer IgnoreCase = StringComparer.OrdinalIgnoreCase;

    public static AmlAdditivePlan Create(AmlModel source, AmlModel project, AmlPlan comparison)
    {
        var plan = new AmlAdditivePlan();
        var sourceObjects = source.Objects.ToDictionary(item => item.Key, StringComparer.Ordinal);
        var missing = new HashSet<string>(comparison.Differences.Where(item => item.Change == "MissingObject")
            .Select(item => item.Target), StringComparer.Ordinal);
        var plcOwners = ReadPlcOwnership(project);
        var addedStations = new HashSet<string>(StringComparer.Ordinal);
        var addedTags = new HashSet<string>(StringComparer.Ordinal);
        var supportedLinks = new HashSet<(string, string, string)>();

        var matcher = new AmlObjectMatcher(source, project);
        var matched = new Dictionary<string, AmlObject>(StringComparer.Ordinal);
        foreach (var item in source.Objects.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var current = matcher.Match(item, out _);
            if (current != null) matched.Add(item.Key, current);
        }

        foreach (var item in source.Objects.Where(item => missing.Contains(item.Key)))
        {
            if (item.Kind == "Device" &&
                TryPlanStation(item, source, sourceObjects, matched, project, plan, supportedLinks, plcOwners))
                addedStations.Add(item.Key);

            if (item.Kind == "Tag" && TryPlanTag(item, sourceObjects, matched, project, plan))
                addedTags.Add(item.Key);
        }

        foreach (var difference in comparison.Differences)
        {
            if (difference.Change == "MissingObject" &&
                (addedStations.Contains(Root(difference.Target)) || addedTags.Contains(difference.Target)))
                continue;
            if (difference.Expected is string other)
            {
                if (difference.Change == "MissingNetworkConnection" &&
                    supportedLinks.Contains(("NetworkConnection", difference.Target, other)))
                    continue;
                if (difference.Change == "MissingChannelTag" &&
                    (addedTags.Contains(difference.Target) || addedTags.Contains(other) ||
                     addedStations.Contains(Root(difference.Target)) || addedStations.Contains(Root(other))))
                    continue;
            }

            plan.Conflicts.Add(difference);
        }
        return plan;
    }

    private static bool TryPlanStation(AmlObject device, AmlModel source, Dictionary<string, AmlObject> objects,
        Dictionary<string, AmlObject> matched, AmlModel current, AmlAdditivePlan plan,
        HashSet<(string, string, string)> supportedLinks, ILookup<string, string> plcOwners)
    {
        if (current.Objects.Any(item => item.Kind == "Device" && IgnoreCase.Equals(item.Name, device.Name)) ||
            plan.Stations.Any(station => IgnoreCase.Equals(station.Name, device.Name)))
            return Block(plan, device.Key, "Station name is already in use.");

        var deviceType = Property(device, "TypeIdentifier");
        if (deviceType != "System:Device.ET200SP" && deviceType != "System:Device.S71500")
            return Block(plan, device.Key, "An additive station must be a catalog ET 200SP or ET 200MP remote I/O station.");

        var items = source.Objects.Where(item => Root(item.Key) == device.Key).ToArray();
        if (items.Any(item => item.Properties.Keys.Any(key =>
            key.StartsWith("Failsafe_", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("CustomAttributes/", StringComparison.OrdinalIgnoreCase))))
            return Block(plan, device.Key, "Fail-safe and custom station parameters require review.");

        var racks = items.Where(item => item.Kind == "DeviceItem" && Parent(item.Key) == device.Key).ToArray();
        if (racks.Length != 1 || Property(racks[0], "PositionNumber") != "0")
            return Block(plan, device.Key, "The station must have one main rack at position zero.");

        var rack = racks[0];
        var modules = items.Where(item => item.Kind == "DeviceItem" && Parent(item.Key) == rack.Key)
            .Select(item => (Source: item, Module: ReadModule(item, items))).ToArray();
        if (modules.Any(item => item.Module == null) ||
            modules.Select(item => item.Module!.Slot).Distinct().Count() != modules.Length)
            return Block(plan, device.Key, "Rack modules must have unique slots, concrete catalog order numbers, and usable I/O addresses.");

        var heads = modules.Where(item => item.Module!.TypeIdentifier.Replace(" ", string.Empty)
            .StartsWith("OrderNumber:6ES7155-", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (heads.Length != 1)
            return Block(plan, device.Key, "The station must specify one catalog PROFINET interface module.");
        var head = heads[0];

        var unsupportedItems = items.Where(item => item.Kind == "DeviceItem" && item != rack &&
            Parent(item.Key) != rack.Key && !IsAutomaticBusAdapter(item, head.Source) &&
            (Property(item, "TypeIdentifier") != null || !AmlObjectMatcher.IsBuiltIn(item)));
        if (unsupportedItems.Any() || modules.Any(item => item.Source != head.Source &&
            Property(item.Source, "DeviceItemType") == "Accessory"))
            return Block(plan, device.Key, "Explicit submodules and accessories require review.");

        var interfaces = items.Where(item => item.Kind == "CommunicationInterface").ToArray();
        var nodes = items.Where(item => item.Kind == "Node").ToArray();
        if (interfaces.Length != 1 || nodes.Length != 1 || Parent(nodes[0].Key) != interfaces[0].Key ||
            Property(interfaces[0], "Label") is not string label ||
            Property(nodes[0], "NetworkAddress") is not string address || !IsIpv4(address))
            return Block(plan, device.Key, "The station must specify one PROFINET interface with a static IPv4 address.");

        var node = nodes[0];
        var mask = Property(node, "SubnetMask");
        if (mask != null && !IsIpv4(mask))
            return Block(plan, node.Key, "The specified subnet mask is invalid.");

        if (current.Objects.Any(item => item.Kind == "Node" && Property(item, "NetworkAddress") == address) ||
            plan.Stations.Any(station => station.Address == address))
            return Block(plan, node.Key, "The requested IP address is already in use.");

        if (!TryFindNetwork(source, objects, matched, node.Key, "Subnet", out var subnet, out var subnetLink) ||
            !TryFindNetwork(source, objects, matched, interfaces[0].Key, "IoSystem", out var system, out var systemLink) ||
            !matched.TryGetValue(Root(system!.Key), out var controller) || controller.Kind != "Device")
            return Block(plan, device.Key, "The station must join an existing subnet and an existing PLC I/O system.");

        var siblings = modules.Where(item => item.Source != head.Source)
            .Select(item => item.Module!).OrderBy(item => item.Slot).ToArray();
        if (HasAddressConflict(current, plan, controller, plcOwners, siblings.Concat(new[] { head.Module! })))
            return Block(plan, device.Key, "A requested module I/O range overlaps a range on this PLC or a range whose PLC ownership is unknown.");

        plan.Stations.Add(new AmlStationAddition(device.Name, deviceType, head.Module!, siblings, label,
            subnet!.Name, system.Name, controller.Name, address, mask));
        supportedLinks.Add(subnetLink!.Identity);
        supportedLinks.Add(systemLink!.Identity);
        return true;
    }

    private static AmlModuleAddition? ReadModule(AmlObject item, IReadOnlyList<AmlObject> stationItems)
    {
        var identifier = Property(item, "TypeIdentifier");
        if (identifier == null || !identifier.StartsWith("OrderNumber:", StringComparison.OrdinalIgnoreCase) ||
            identifier.Contains("*") || !TryPosition(Property(item, "PositionNumber"), out var slot))
            return null;

        var addresses = new List<AmlIoAddress>();
        var representations = stationItems.Where(candidate => candidate.Kind == "DeviceItem" &&
            Parent(candidate.Key) == item.Key && candidate.Name == item.Name &&
            AmlObjectMatcher.IsBuiltIn(candidate) && Property(candidate, "TypeIdentifier") == null);
        foreach (var representation in new[] { item }.Concat(representations))
        {
            foreach (var address in ReadAddresses(representation))
            {
                if (address == null || addresses.Any(existing => existing.Direction == address.Direction))
                    return null;
                addresses.Add(address);
            }
        }
        return new AmlModuleAddition(item.Name, identifier, slot, addresses);
    }

    private static bool IsAutomaticBusAdapter(AmlObject item, AmlObject head) =>
        Parent(item.Key) == head.Key && Property(item, "PositionNumber") == "127" &&
        Property(item, "TypeIdentifier")?.Replace(" ", string.Empty)
            .StartsWith("OrderNumber:6ES7193-6A", StringComparison.OrdinalIgnoreCase) == true;

    private static IEnumerable<AmlIoAddress?> ReadAddresses(AmlObject item)
    {
        var groups = item.Properties.Where(property => property.Key.StartsWith("Address/", StringComparison.Ordinal))
            .GroupBy(property => Parent(property.Key));
        foreach (var group in groups)
        {
            var direction = Property(item, group.Key + "/IoType");
            var length = Property(item, group.Key + "/Length");
            if ((direction != "Input" && direction != "Output") ||
                !TryPosition(Property(item, group.Key + "/StartAddress"), out var start) ||
                length != null && (!TryPosition(length, out var width) || width == 0))
                yield return null;
            else
                yield return new AmlIoAddress(direction!, start, length == null ? null : int.Parse(length, CultureInfo.InvariantCulture));
        }
    }

    private static ILookup<string, string> ReadPlcOwnership(AmlModel model)
    {
        var systems = new HashSet<string>(model.Objects.Where(item => item.Kind == "IoSystem" || item.Kind == "IOSystem")
            .Select(item => item.Key), StringComparer.Ordinal);
        var interfaces = new HashSet<string>(model.Objects.Where(item => item.Kind == "CommunicationInterface")
            .Select(item => item.Key), StringComparer.Ordinal);

        var controllers = model.Objects.Where(item => systems.Contains(item.Key) || item.Kind == "TagTable" ||
            item.Kind == "DeviceItem" && IgnoreCase.Equals(Property(item, "DeviceItemType"), "CPU"))
            .Select(item => Root(item.Key)).Distinct(StringComparer.Ordinal);
        var ownership = controllers.Select(controller => (Station: controller, Plc: controller)).ToList();
        foreach (var connection in model.Connections.Where(link => link.Kind == "NetworkConnection"))
        {
            var first = Parent(connection.First);
            var second = Parent(connection.Second);
            if (systems.Contains(second)) (first, second) = (second, first);
            if (systems.Contains(first) && interfaces.Contains(second))
                ownership.Add((Root(second), Root(first)));
        }

        return ownership.Distinct().ToLookup(item => item.Station, item => item.Plc, StringComparer.Ordinal);
    }

    private static bool HasAddressConflict(AmlModel current, AmlAdditivePlan plan, AmlObject controller,
        ILookup<string, string> plcOwners, IEnumerable<AmlModuleAddition> proposed)
    {
        // An I/O address is independent only if CAx establishes a single other owning PLC.
        var occupied = current.Objects.Where(item => item.Kind == "DeviceItem")
            .Where(item =>
            {
                var owners = plcOwners[Root(item.Key)].Take(2).ToArray();
                return owners.Length != 1 || owners[0] == controller.Key;
            })
            .SelectMany(ReadAddresses).OfType<AmlIoAddress>()
            .Concat(plan.Stations.Where(station => IgnoreCase.Equals(station.Controller, controller.Name))
                .SelectMany(station => station.Modules.Concat(new[] { station.Head })).SelectMany(module => module.Addresses))
            .ToList();
        foreach (var address in proposed.SelectMany(module => module.Addresses))
        {
            var start = (long)address.Start * 8;
            var end = start + (address.Length ?? 1) - 1;
            if (occupied.Any(other => other.Direction == address.Direction &&
                start <= (long)other.Start * 8 + (other.Length ?? 1) - 1 && (long)other.Start * 8 <= end))
                return true;
            occupied.Add(address);
        }
        return false;
    }

    private static bool TryFindNetwork(AmlModel source, Dictionary<string, AmlObject> objects,
        Dictionary<string, AmlObject> matched, string owner, string kind, out AmlObject? target, out AmlConnection? link)
    {
        target = null;
        link = null;
        foreach (var candidate in source.Connections.Where(link => link.Kind == "NetworkConnection"))
        {
            var other = Parent(candidate.First) == owner ? Parent(candidate.Second)
                : Parent(candidate.Second) == owner ? Parent(candidate.First) : null;
            if (other == null || !objects.TryGetValue(other, out var expected) || expected.Kind != kind)
                continue;
            if (link != null || !matched.TryGetValue(other, out target))
                return false;
            link = candidate;
        }
        return link != null;
    }

    private static bool TryPlanTag(AmlObject tag, Dictionary<string, AmlObject> sourceObjects,
        Dictionary<string, AmlObject> matched, AmlModel current, AmlAdditivePlan plan)
    {
        if (!sourceObjects.TryGetValue(Parent(tag.Key), out var sourceTable) ||
            !matched.TryGetValue(sourceTable.Key, out var targetTable) ||
            !matched.TryGetValue(Root(tag.Key), out var targetPlc))
            return Block(plan, tag.Key, "Adding a tag requires an existing PLC and tag table.");

        var rawAddress = Property(tag, "LogicalAddress");
        var address = rawAddress == null ? null : AmlComparer.AbsoluteAddress(rawAddress, tag);
        if (address == null || !TryTagRange(address, out var range) || Property(tag, "DataType") is not string dataType)
            return Block(plan, tag.Key, "Adding a tag requires a data type and a supported absolute PLC address.");

        var existing = current.Objects.Where(item => item.Kind == "Tag" && Root(item.Key) == targetPlc.Key);
        foreach (var other in existing)
        {
            var raw = Property(other, "LogicalAddress");
            var otherAddress = raw == null ? null : AmlComparer.AbsoluteAddress(raw, other);
            if (IgnoreCase.Equals(other.Name, tag.Name) || otherAddress != null &&
                TryTagRange(otherAddress, out var otherRange) && range.Overlaps(otherRange))
                return Block(plan, tag.Key, "The tag name or address conflicts with an existing PLC tag.");
        }

        if (plan.Tags.Any(other => IgnoreCase.Equals(other.Device, targetPlc.Name) &&
            (IgnoreCase.Equals(other.Name, tag.Name) || TryTagRange(other.Address, out var otherRange) && range.Overlaps(otherRange))))
            return Block(plan, tag.Key, "The tag name or address conflicts with another planned PLC tag.");

        plan.Tags.Add(new AmlTagAddition(targetPlc.Name, targetTable.Name, Property(sourceTable, "AssignToDefault") == "true",
            tag.Name, dataType, "%" + address));
        return true;
    }

    internal static bool TryTagRange(string address, out (char Area, long First, long End) range)
    {
        range = default;
        var match = Regex.Match(address.ToUpperInvariant(), @"^%?(?<area>[IQM])(?:(?<size>[BWD])(?<byte>[0-9]+)|(?<byte>[0-9]+)\.(?<bit>[0-7]))$");
        if (!match.Success || !TryPosition(match.Groups["byte"].Value, out var start)) return false;

        var bit = match.Groups["bit"].Success ? match.Groups["bit"].Value[0] - '0' : 0;
        var width = match.Groups["size"].Value switch { "B" => 8, "W" => 16, "D" => 32, _ => 1 };
        var first = (long)start * 8 + bit;
        range = (match.Groups["area"].Value[0], first, first + width - 1);
        return true;
    }

    internal static bool Overlaps(this (char Area, long First, long End) range, (char Area, long First, long End) other) =>
        range.Area == other.Area && range.First <= other.End && other.First <= range.End;

    private static bool IsIpv4(string value) => value.Split('.').Length == 4 &&
        System.Net.IPAddress.TryParse(value, out var address) && address.ToString() == value;

    private static bool TryPosition(string? value, out int position) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out position);

    private static bool Block(AmlAdditivePlan plan, string target, string reason)
    {
        plan.Conflicts.Add(new AmlDifference("AdditiveConflict", target, actual: reason));
        return false;
    }

    private static string? Property(AmlObject item, string name) => AmlObjectMatcher.Property(item, name);
    private static string Parent(string key)
    {
        var index = key.LastIndexOf('/');
        return index < 0 ? string.Empty : key.Substring(0, index);
    }
    private static string Root(string key) => key.Split('/')[0];
}
