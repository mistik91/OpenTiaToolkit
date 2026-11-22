using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace OpenTiaToolkit.Core.Services.AutomationMl;

internal sealed class AmlObject(string key, string kind, string name, IReadOnlyDictionary<string, string> properties)
{
    public string Key { get; } = key;
    public string Kind { get; } = kind;
    public string Name { get; } = name;
    public IReadOnlyDictionary<string, string> Properties { get; } = properties;
}

internal sealed class AmlConnection(string kind, string first, string second)
{
    public string Kind { get; } = kind;
    public string First { get; } = first;
    public string Second { get; } = second;
    internal (string Kind, string First, string Second) Identity => (Kind, First, Second);
}

internal sealed class AmlModel(string? apcVersion, IReadOnlyList<AmlObject> objects,
    IReadOnlyList<AmlConnection> connections, IReadOnlyList<string> diagnostics)
{
    public string? ApcVersion { get; } = apcVersion;
    public IReadOnlyList<AmlObject> Objects { get; } = objects;
    public IReadOnlyList<AmlConnection> Connections { get; } = connections;
    public IReadOnlyList<string> Diagnostics { get; } = diagnostics;

    internal static AmlModel Empty { get; } = new(null, Array.Empty<AmlObject>(),
        Array.Empty<AmlConnection>(), Array.Empty<string>());
}

internal static class AmlReader
{
    private const string RoleLibrary = "AutomationProjectConfigurationRoleClassLib/";
    private const string InterfaceLibrary = "AutomationProjectConfigurationInterfaceClassLib/";
    private static readonly HashSet<string> Roles = new(StringComparer.Ordinal)
    {
        "Device", "DeviceItem", "Subnet", "IoSystem", "IOSystem", "CommunicationInterface",
        "CommunicationPort", "Node", "TagTable", "TagTableGroup", "TagFolder", "Controller", "Connector"
    };
    private static readonly HashSet<string> InterfaceTypes = new(StringComparer.Ordinal)
    {
        "Channel", "Tag", "CommunicationPortInterface", "LogicalEndPoint"
    };

    public static AmlModel Read(Stream input, bool projectSnapshot = false)
    {
        using var reader = XmlReader.Create(input, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 32 * 1024 * 1024
        });
        var root = XDocument.Load(reader).Root;
        if (root?.Name.LocalName != "CAEXFile")
            throw new InvalidDataException("Expected an AutomationML CAEXFile root.");

        var ns = root.Name.Namespace;
        var version = root.Elements(ns + "AdditionalInformation").Elements(ns + "Document")
            .FirstOrDefault(element => Value(element, "DocumentIdentifier") == "AR APC");
        var roots = root.Elements(ns + "InstanceHierarchy").Elements(ns + "InternalElement").ToArray();
        if (!projectSnapshot && version == null && !roots.SelectMany(element => element.DescendantsAndSelf(ns + "InternalElement"))
            .Any(element => RolePaths(element).Contains(RoleLibrary + "AutomationProject")))
            throw new InvalidDataException("The file does not contain an AR APC AutomationProject.");

        var objects = new Dictionary<string, AmlObject>(StringComparer.Ordinal);
        var endpoints = new Dictionary<string, (string Key, string Kind)>(StringComparer.OrdinalIgnoreCase);
        var diagnostics = new List<string>();
        var elementIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in roots.SelectMany(element => element.DescendantsAndSelf(ns + "InternalElement")))
        {
            var role = Role(element);
            var parentKey = ObjectPath(element);
            var owner = Value(element, "ID");
            if (!string.IsNullOrEmpty(owner) && !elementIds.Add(owner!))
                diagnostics.Add($"Duplicate internal element ID on '{Value(element, "Name")}'.");
            if (role != null)
                AddObject(element, parentKey, role, objects, diagnostics);

            var channels = new Dictionary<string, XElement>(StringComparer.Ordinal);
            foreach (var port in element.Elements(ns + "ExternalInterface"))
            {
                var kind = InterfaceKind(port);
                if (kind == null) continue;

                var name = Value(port, "Name");
                var id = Value(port, "ID");
                if (name == null || owner == null)
                {
                    diagnostics.Add("An interface is missing its name or owner ID.");
                    continue;
                }

                var key = parentKey + "/" + Segment(kind, EndpointName(kind, port));
                var repeatedChannel = kind == "Channel" && channels.TryGetValue(key, out var previous) &&
                    string.Equals(Value(previous, "Name"), name, StringComparison.Ordinal) &&
                    XNode.DeepEquals(ChannelDefinition(previous), ChannelDefinition(port));
                if (!repeatedChannel)
                {
                    if (kind == "Channel")
                    {
                        if (!channels.ContainsKey(key)) channels.Add(key, port);
                        AddObject(port, key, kind, objects, diagnostics);
                    }
                    if (kind == "Tag") AddObject(port, key, kind, objects, diagnostics);
                    AddEndpoint(owner + ":" + name, key, kind, endpoints, diagnostics);
                }
                if (id != null)
                    AddEndpoint(id, key, kind, endpoints, diagnostics);
            }
        }

        var connections = new Dictionary<(string, string, string), AmlConnection>();
        foreach (var link in roots.SelectMany(element => element.Descendants(ns + "InternalLink")))
        {
            var first = Value(link, "RefPartnerSideA");
            var second = Value(link, "RefPartnerSideB");
            if (first == null || second == null ||
                !endpoints.TryGetValue(NormalizeReference(first), out var a) ||
                !endpoints.TryGetValue(NormalizeReference(second), out var b))
            {
                diagnostics.Add($"Unresolved internal link '{Value(link, "Name")}'.");
                continue;
            }

            var kind = a.Kind == "Tag" || b.Kind == "Tag" ? "ChannelTag"
                : a.Kind == "CommunicationPortInterface" && b.Kind == "CommunicationPortInterface"
                    ? "PortConnection" : "NetworkConnection";
            if (StringComparer.Ordinal.Compare(a.Key, b.Key) > 0)
                (a, b) = (b, a);
            var connection = new AmlConnection(kind, a.Key, b.Key);
            connections[connection.Identity] = connection;
        }

        return new AmlModel(version == null ? null : Value(version, "Version"),
            objects.Values.OrderBy(item => item.Key, StringComparer.Ordinal).ToArray(),
            connections.Values.OrderBy(link => link.Kind, StringComparer.Ordinal)
                .ThenBy(link => link.First, StringComparer.Ordinal).ThenBy(link => link.Second, StringComparer.Ordinal).ToArray(),
            diagnostics.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static XElement ChannelDefinition(XElement channel)
    {
        var definition = new XElement(channel);
        definition.Attribute("ID")?.Remove();
        return definition;
    }

    private static string NormalizeReference(string reference)
    {
        // CAEX 2.15 uses brackets around both fields when the interface name contains a colon.
        var separator = reference.IndexOf("]:[", StringComparison.Ordinal);
        return separator > 1 && reference.StartsWith("[", StringComparison.Ordinal) &&
            reference.EndsWith("]", StringComparison.Ordinal)
            ? reference.Substring(1, separator - 1) + ":" + reference.Substring(separator + 3, reference.Length - separator - 4)
            : reference;
    }

    private static void AddEndpoint(string reference, string key, string kind,
        Dictionary<string, (string Key, string Kind)> endpoints, List<string> diagnostics)
    {
        if (endpoints.ContainsKey(reference))
            diagnostics.Add($"Duplicate interface reference '{reference}'.");
        else
            endpoints.Add(reference, (key, kind));
    }

    private static void AddObject(XElement element, string key, string kind,
        Dictionary<string, AmlObject> objects, List<string> diagnostics)
    {
        if (objects.ContainsKey(key))
        {
            diagnostics.Add($"Ambiguous {kind} identity '{key}'.");
            return;
        }

        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var attribute in element.Elements(element.Name.Namespace + "Attribute"))
            ReadProperty(attribute, string.Empty, properties, diagnostics);
        objects.Add(key, new AmlObject(key, kind, Value(element, "Name") ?? string.Empty, properties));
    }

    private static void ReadProperty(XElement attribute, string parent, Dictionary<string, string> properties,
        List<string> diagnostics)
    {
        var name = Value(attribute, "Name");
        if (string.IsNullOrWhiteSpace(name)) return;

        var key = parent + Uri.EscapeDataString(name!);
        var value = attribute.Element(attribute.Name.Namespace + "Value")?.Value;
        if (!string.IsNullOrWhiteSpace(value))
        {
            if (properties.ContainsKey(key))
                diagnostics.Add($"Duplicate property '{key}'.");
            else
                properties.Add(key, value!.Trim());
        }

        foreach (var child in attribute.Elements(attribute.Name.Namespace + "Attribute"))
            ReadProperty(child, key + "/", properties, diagnostics);
    }

    private static string ObjectPath(XElement element) => string.Join("/", element.AncestorsAndSelf()
        .Reverse().Where(item => item.Name == element.Name)
        .Select(item => (Element: item, Role: Role(item)))
        .Where(item => item.Role != null)
        .Select(item => Segment(item.Role!, ObjectName(item.Role!, item.Element))));

    private static string ObjectName(string kind, XElement item)
    {
        var name = Value(item, "Name") ?? string.Empty;
        if (kind == "DeviceItem")
        {
            var position = AttributeValue(item, "PositionNumber");
            var itemType = AttributeValue(item, "DeviceItemType");
            // Accessory positions can overlap the positions of installable hardware.
            if (position != null)
                return itemType == "Accessory" ? "Accessory:" + name : "Slot:" + position;
        }

        if (kind == "CommunicationPort")
        {
            var label = AttributeValue(item, "Label");
            var position = AttributeValue(item, "PositionNumber");
            if (label != null && item.ElementsBeforeSelf(item.Name).Concat(item.ElementsAfterSelf(item.Name))
                .Any(sibling => Role(sibling) == "CommunicationPort" && AttributeValue(sibling, "Label") == label))
                return label + ":" + (position ?? name);

            return label ?? position ?? name;
        }

        return name;
    }

    private static string EndpointName(string kind, XElement item) => kind switch
    {
        "Channel" => (AttributeValue(item, "Type") ?? "") + ":" + (AttributeValue(item, "IoType") ?? "") + ":" +
            (AttributeValue(item, "Number") ?? Value(item, "Name")),
        "LogicalEndPoint" or "CommunicationPortInterface" => string.Empty,
        _ => Value(item, "Name") ?? string.Empty
    };

    private static string? AttributeValue(XElement item, string key) =>
        item.Elements(item.Name.Namespace + "Attribute").FirstOrDefault(attribute => Value(attribute, "Name") == key)
            ?.Element(item.Name.Namespace + "Value")?.Value.Trim();

    private static string? Role(XElement element) => RolePaths(element)
        .Where(path => path.StartsWith(RoleLibrary, StringComparison.Ordinal))
        .Select(path => path.Substring(RoleLibrary.Length))
        .FirstOrDefault(Roles.Contains);

    private static IEnumerable<string> RolePaths(XElement element) =>
        element.Elements(element.Name.Namespace + "SupportedRoleClass")
            .Select(role => Value(role, "RefRoleClassPath"))
            .Concat(element.Elements(element.Name.Namespace + "RoleRequirements")
                .Select(role => Value(role, "RefBaseRoleClassPath")))
            .OfType<string>();

    private static string? InterfaceKind(XElement element)
    {
        var path = Value(element, "RefBaseClassPath") ?? string.Empty;
        if (!path.StartsWith(InterfaceLibrary, StringComparison.Ordinal) &&
            !path.StartsWith("CommunicationInterfaceClassLib/", StringComparison.Ordinal))
            return null;

        var kind = path.Substring(path.LastIndexOf('/') + 1);
        return InterfaceTypes.Contains(kind) ? kind : null;
    }

    private static string Segment(string kind, string name) => kind + ":" + Uri.EscapeDataString(name);
    private static string? Value(XElement node, string attribute) => (string?)node.Attribute(attribute);
}
