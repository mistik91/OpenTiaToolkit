using System;
using System.Collections.Generic;
using System.Linq;
using OpenTiaToolkit.Core.Services.AutomationMl;
using OpenTiaToolkit.Core.Services.PlcTags;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Tags;

namespace OpenTiaToolkit.Core.Services.V21;

internal sealed class V21AutomationMlAdditions(V21HardwareNetworkAccess networks, IPlcTagRepository tags, V21HardwareCatalogAccess catalog)
{
    public IReadOnlyDictionary<string, string> ResolveCatalog(TiaPortal portal, AmlAdditivePlan plan)
    {
        var selections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var identifier in plan.Stations.SelectMany(station => station.Modules.Concat(new[] { station.Head }))
            .Select(module => module.TypeIdentifier).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var orderNumber = identifier.Substring("OrderNumber:".Length).Split('/')[0];
            var candidates = catalog.FindCatalogItems(portal, orderNumber, null)
                .Select(entry => entry.TypeIdentifier).Where(candidate => CatalogMatches(identifier, candidate))
                .OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(CatalogVersion).ThenBy(candidate => candidate, StringComparer.Ordinal).ToArray();
            if (candidates.Length == 0)
                throw new InvalidOperationException($"Catalog type '{identifier}' is unavailable.");
            selections.Add(identifier, candidates[0]);
        }
        return selections;
    }

    private static Version CatalogVersion(string identifier)
    {
        var suffix = identifier.LastIndexOf("/V", StringComparison.OrdinalIgnoreCase);
        return suffix >= 0 && Version.TryParse(identifier.Substring(suffix + 2), out var version)
            ? version : new Version(0, 0);
    }

    public void Apply(ProjectBase project, AmlAdditivePlan plan, IReadOnlyDictionary<string, string> catalogSelection)
    {
        // Resolve and validate existing PLC tables before any equipment is created.
        var tagTargets = plan.Tags.Select(tag => (Tag: tag, Table: ResolveTagTable(project, tag))).ToArray();
        foreach (var station in plan.Stations)
            AddStation(project, station, catalogSelection);

        foreach (var (tag, table) in tagTargets)
        {
            if (!tags.TryCreateTag(table, tag.Name, tag.DataType, tag.Address, out _, out var reason))
                throw new InvalidOperationException($"TIA Portal could not create PLC tag '{tag.Name}': {reason}");
        }
    }

    private void AddStation(ProjectBase project, AmlStationAddition station, IReadOnlyDictionary<string, string> catalogSelection)
    {
        if (Devices(project).Any(item => Equal(item.Name, station.Name)))
            throw new InvalidOperationException($"Station '{station.Name}' already exists.");

        var subnet = FindOne(project.Subnets.Where(item => Equal(item.Name, station.Subnet)),
            $"subnet '{station.Subnet}'");
        var controller = FindOne(Devices(project).Where(item => Equal(item.Name, station.Controller)),
            $"PLC station '{station.Controller}'");
        var ioSystem = FindOne(Items(controller.DeviceItems).Distinct().Select(item => item.GetService<NetworkInterface>())
            .OfType<NetworkInterface>().SelectMany(feature => feature.IoControllers)
            .Select(item => item.IoSystem).OfType<IoSystem>().Where(item => Equal(item.Name, station.IoSystem)),
            $"I/O system '{station.IoSystem}' on '{station.Controller}'");

        var device = project.Devices.CreateWithItem(catalogSelection[station.Head.TypeIdentifier], station.Head.Name, station.Name);
        if (!Equal(device.TypeIdentifier, station.DeviceType))
            throw new InvalidOperationException($"Catalog type created unexpected device family '{device.TypeIdentifier}'.");

        var headRack = FindOne(device.DeviceItems.Where(item => item.PositionNumber == 0 && IsRack(item, station.DeviceType)), $"main rack for '{station.Name}'");
        var head = FindOne(device.DeviceItems.Concat(headRack.DeviceItems).Distinct().Where(item => item.PositionNumber == station.Head.Slot &&
            Equal(item.Name, station.Head.Name) && CatalogMatches(station.Head.TypeIdentifier, item.TypeIdentifier)), $"head module for '{station.Name}'");
        var installedModules = new List<(DeviceItem Item, AmlModuleAddition Module)> { (head, station.Head) };
        foreach (var module in station.Modules)
        {
            var present = headRack.DeviceItems.FirstOrDefault(item => item.PositionNumber == module.Slot);
            if (present != null)
            {
                if (!CatalogMatches(module.TypeIdentifier, present.TypeIdentifier))
                    throw new InvalidOperationException($"Slot {module.Slot} of new station '{station.Name}' is occupied by '{present.Name}'.");
                installedModules.Add((present, module));
                continue;
            }

            var moduleType = catalogSelection[module.TypeIdentifier];
            if (!headRack.CanPlugNew(moduleType, module.Name, module.Slot))
                throw new InvalidOperationException($"Cannot plug '{module.Name}' in slot {module.Slot} of '{station.Name}'.");
            var installed = headRack.PlugNew(moduleType, module.Name, module.Slot);
            installedModules.Add((installed, module));
        }

        var feature = FindOne(Items(device.DeviceItems).Distinct().Select(item => item.GetService<NetworkInterface>())
            .OfType<NetworkInterface>().Where(item => item.IoConnectors.Count > 0 && item.Nodes.Count > 0),
            $"PROFINET interface '{station.InterfaceLabel}' for '{station.Name}'");
        var node = FindOne(feature.Nodes, $"network node for '{station.Name}'");
        // The supported remote I/O catalog families use the primary PROFINET connector.
        // The transaction readback verifies the requested subnet and I/O system.
        var connector = feature.IoConnectors[0];
        if (node.ConnectedSubnet != null || feature.IoConnectors.Any(item => item.ConnectedToIoSystem != null))
            throw new InvalidOperationException($"The new station '{station.Name}' has unexpected network membership.");

        networks.ConnectNodeToSubnet(node, subnet);
        node.SetAttribute("Address", station.Address);
        if (station.SubnetMask != null && !Equal(node.GetAttribute("SubnetMask") as string, station.SubnetMask))
            node.SetAttribute("SubnetMask", station.SubnetMask);
        networks.ConnectIoConnector(connector, ioSystem);
        // Joining an I/O system can assign addresses. Write the requested ranges in the owning PLC's address space.
        foreach (var (item, module) in installedModules)
            ConfigureAddresses(item, module);
    }

    private static void ConfigureAddresses(DeviceItem item, AmlModuleAddition module)
    {
        foreach (var requested in module.Addresses)
        {
            var address = FindOne(Items(new[] { item }).SelectMany(candidate => candidate.Addresses).Distinct()
                .Where(candidate => candidate.IoType.ToString() == requested.Direction),
                $"{requested.Direction} address for '{module.Name}'");
            if (address.StartAddress != requested.Start)
                address.StartAddress = requested.Start;
        }
    }

    private object ResolveTagTable(ProjectBase project, AmlTagAddition addition)
    {
        var device = FindOne(Devices(project).Where(item => Equal(item.Name, addition.Device)),
            $"PLC station '{addition.Device}'");
        var plc = FindOne(Items(device.DeviceItems).Distinct().Select(item => item.GetService<SoftwareContainer>()?.Software)
            .OfType<PlcSoftware>(), $"PLC software for '{addition.Device}'");
        var tables = tags.GetTagTables(plc) ?? throw new InvalidOperationException($"PLC tags for '{addition.Device}' could not be read.");

        if (!AmlAdditivePlanner.TryTagRange(addition.Address, out var proposedRange))
            throw new InvalidOperationException($"The address for '{addition.Name}' is unsupported.");

        foreach (var table in tables)
        {
            var existing = tags.GetTags(table) ?? throw new InvalidOperationException($"PLC tag table '{tags.GetName(table)}' could not be read.");
            foreach (var tag in existing)
            {
                var address = tags.GetLogicalAddress(tag);
                if (Equal(tags.GetName(tag), addition.Name) ||
                    address != null && AmlAdditivePlanner.TryTagRange(address, out var range) && proposedRange.Overlaps(range))
                    throw new InvalidOperationException($"PLC tag '{addition.Name}' conflicts with existing tag '{tags.GetName(tag)}'.");
            }
        }

        return FindOne(tables.Where(table =>
            addition.DefaultTable ? table is PlcTagTable typed && typed.IsDefault : Equal(tags.GetName(table), addition.Table)),
            $"tag table '{addition.Table}' on '{addition.Device}'");
    }

    private static bool IsRack(DeviceItem item, string deviceType)
    {
        var identifier = item.TypeIdentifier?.Replace(" ", string.Empty);
        return identifier?.StartsWith("System:Rack.", StringComparison.OrdinalIgnoreCase) == true ||
            (deviceType == "System:Device.S71500" &&
             identifier?.StartsWith("OrderNumber:6ES7590-", StringComparison.OrdinalIgnoreCase) == true);
    }

    private static bool CatalogMatches(string expected, string? actual)
    {
        if (actual == null) return false;
        var expectedType = expected.Replace(" ", string.Empty);
        var actualType = actual.Replace(" ", string.Empty);
        return Equal(expectedType, actualType) ||
            !expectedType.Contains("/") && actualType.StartsWith(expectedType + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool Equal(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<Device> Devices(ProjectBase project) =>
        ProjectDeviceEnumerator.EnumerateDevices(project).Concat(project.UngroupedDevicesGroup.Devices).Distinct();

    private static IEnumerable<DeviceItem> Items(IEnumerable<DeviceItem> items)
    {
        foreach (var item in items)
        {
            yield return item;
            foreach (var child in Items(item.DeviceItems)) yield return child;
        }
    }

    private static T FindOne<T>(IEnumerable<T> values, string description)
    {
        var candidates = values.Take(2).ToArray();
        if (candidates.Length != 1)
            throw new InvalidOperationException($"Expected one {description}, found {(candidates.Length == 0 ? "none" : "multiple")}.");
        return candidates[0];
    }
}
