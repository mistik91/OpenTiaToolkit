using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace OpenTiaToolkit.Core.Services.AutomationMl;

internal sealed class AmlDifference(string change, string target, string? property = null,
    string? expected = null, string? actual = null)
{
    public string Change { get; } = change;
    public string Target { get; } = target;
    public string? Property { get; } = property;
    public string? Expected { get; } = expected;
    public string? Actual { get; } = actual;
}

internal sealed class AmlPlan(IReadOnlyList<AmlDifference> differences,
    IReadOnlyList<string> sourceDiagnostics, IReadOnlyList<string> projectDiagnostics,
    IReadOnlyList<AmlDifference> unverifiedCatalogIdentifiers)
{
    public IReadOnlyList<AmlDifference> Differences { get; } = differences;
    public IReadOnlyList<string> SourceDiagnostics { get; } = sourceDiagnostics;
    public IReadOnlyList<string> ProjectDiagnostics { get; } = projectDiagnostics;

    public IReadOnlyList<AmlDifference> UnverifiedCatalogIdentifiers { get; } = unverifiedCatalogIdentifiers;

    public bool Matches => Differences.Count == 0 && SourceDiagnostics.Count == 0 && ProjectDiagnostics.Count == 0;
    public bool FullyVerified => Matches && UnverifiedCatalogIdentifiers.Count == 0;
}

internal static class AmlComparer
{
    private static readonly HashSet<string> Settings = new(StringComparer.Ordinal)
    {
        "TypeIdentifier", "PositionNumber", "StartAddress", "LogicalAddress", "IoType", "DataType",
        "Type", "Number", "Length", "Address", "IpAddress", "NetworkAddress", "SubnetMask", "RouterAddress",
        "DeviceName", "PnDeviceName", "Label", "DeviceNumber", "StationNumber", "UseRouter", "IpProtocolSelection"
    };

    public static AmlPlan Compare(AmlModel source, AmlModel project)
    {
        var differences = new List<AmlDifference>();
        var unverifiedCatalogIdentifiers = new List<AmlDifference>();
        var matcher = new AmlObjectMatcher(source, project);
        foreach (var expected in source.Objects.Where(IsComparable).OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            if (matcher.IsEmptyDefaultTableDirective(expected)) continue;

            var actual = matcher.Match(expected, out var ambiguous);
            if (actual == null)
            {
                differences.Add(new AmlDifference(ambiguous ? "AmbiguousObject" : "MissingObject", expected.Key));
                continue;
            }

            foreach (var property in expected.Properties.Where(property => IsSetting(property.Key)))
            {
                var actualValue = ProjectProperty(property.Key, expected, actual, matcher);
                if (Equivalent(property.Key, property.Value, actualValue, expected, actual, matcher))
                    continue;

                if (property.Key == "TypeIdentifier" && actualValue != null &&
                    IsTolerantOrderNumber(property.Value, actualValue))
                {
                    unverifiedCatalogIdentifiers.Add(new AmlDifference("UnverifiedCatalogIdentifier",
                        expected.Key, property.Key, property.Value, actualValue));
                    continue;
                }

                differences.Add(new AmlDifference("DifferentProperty", expected.Key,
                    property.Key, property.Value, actualValue));
            }
        }

        var actualConnections = new HashSet<(string, string, string)>(project.Connections.Select(link => link.Identity));
        foreach (var link in source.Connections)
        {
            var first = matcher.TranslateEndpoint(link.First);
            var second = matcher.TranslateEndpoint(link.Second);
            if (first != null && second != null)
            {
                if (StringComparer.Ordinal.Compare(first, second) > 0) (first, second) = (second, first);
                if (actualConnections.Contains((link.Kind, first, second))) continue;
            }

            differences.Add(new AmlDifference("Missing" + link.Kind, link.First, expected: link.Second));
        }

        return new AmlPlan(differences, source.Diagnostics, project.Diagnostics, unverifiedCatalogIdentifiers);
    }

    private static string? ProjectProperty(string key, AmlObject source, AmlObject project, AmlObjectMatcher matcher)
    {
        var components = key.Split('/');
        if (components.Length != 3 || components[0] != "Address")
            return AmlObjectMatcher.Property(project, key);

        var direction = AmlObjectMatcher.Property(source, "Address/" + components[1] + "/IoType");
        if (direction == null) return AmlObjectMatcher.Property(project, key);

        var direct = AddressEntries(project, direction).Take(2).ToArray();
        var matches = direct.Length > 0 ? direct : matcher.BuiltInRepresentations(project)
            .SelectMany(child => AddressEntries(child, direction)).Take(2).ToArray();
        if (matches.Length != 1) return null;

        var (item, prefix) = matches[0];
        return AmlObjectMatcher.Property(item, prefix + components[2]);
    }

    private static IEnumerable<(AmlObject Item, string Prefix)> AddressEntries(AmlObject item, string direction) =>
        item.Properties.Where(property => property.Value == direction &&
            property.Key.StartsWith("Address/", StringComparison.Ordinal) &&
            property.Key.EndsWith("/IoType", StringComparison.Ordinal))
        .Select(property => (item, property.Key.Substring(0, property.Key.Length - "IoType".Length)));

    private static bool IsComparable(AmlObject item) =>
        item.Kind != "DeviceItem" || !item.Properties.TryGetValue("DeviceItemType", out var type) || type != "Accessory";

    private static bool IsSetting(string key) => key.Split('/').Any(Settings.Contains);

    private static bool Equivalent(string key, string expected, string? actual, AmlObject sourceItem, AmlObject projectItem, AmlObjectMatcher matcher)
    {
        if (key == "PositionNumber" && sourceItem.Kind == "CommunicationPort" && matcher.LegacyReadback &&
            AmlObjectMatcher.IsBuiltIn(sourceItem) && AmlObjectMatcher.IsBuiltIn(projectItem) &&
            AmlObjectMatcher.PortLabel(sourceItem) is string label && label == AmlObjectMatcher.PortLabel(projectItem))
            return true;

        if (actual == null) return false;
        if (key == "DataType" && sourceItem.Kind == "Tag")
            return string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);

        if (key == "Label" && sourceItem.Kind == "CommunicationPort")
            return AmlObjectMatcher.PortLabel(sourceItem) == AmlObjectMatcher.PortLabel(projectItem);

        if (key == "Length" && sourceItem.Kind == "Channel" && matcher.LegacyReadback &&
            AmlObjectMatcher.Property(sourceItem, "Type") == "Analog" && expected == "1" && actual == "16")
            return true;

        if (key == "TypeIdentifier")
        {
            // Legacy EPLAN uses generic station/rack wrappers around concretely ordered modules.
            if (matcher.LegacyReadback && matcher.HasTypedChild(sourceItem) &&
                ((expected == "System:Device.Generic" && actual.StartsWith("System:Device.", StringComparison.Ordinal)) ||
                 (expected == "System:Rack.Generic" && actual.StartsWith("System:Rack.", StringComparison.Ordinal))))
                return true;

            if (expected.StartsWith("OrderNumber:", StringComparison.OrdinalIgnoreCase))
            {
                // Catalog identifiers may insert spacing and add a firmware suffix when none was requested.
                var left = expected.Replace(" ", string.Empty);
                var right = actual.Replace(" ", string.Empty);
                return string.Equals(left, right, StringComparison.OrdinalIgnoreCase)
                    || (!left.Contains("/") && right.StartsWith(left + "/", StringComparison.OrdinalIgnoreCase));
            }
        }

        if (key == "LogicalAddress")
        {
            var sourceAddress = AbsoluteAddress(expected, sourceItem);
            var projectAddress = AbsoluteAddress(actual, projectItem);
            if (sourceAddress != null && projectAddress != null)
                return string.Equals(sourceAddress, projectAddress, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(expected, actual, StringComparison.Ordinal);
    }

    private static bool IsTolerantOrderNumber(string expected, string actual)
    {
        const string prefix = "OrderNumber:";
        if (!expected.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !actual.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            expected.Contains("*") || !actual.Contains("*"))
            return false;

        var exact = expected.Replace(" ", string.Empty).Split('/');
        var tolerant = actual.Replace(" ", string.Empty).Split('/');
        if (tolerant.Skip(1).Any(part => part.Contains("*")) ||
            (exact.Length > 1 && !exact.Skip(1).SequenceEqual(tolerant.Skip(1), StringComparer.OrdinalIgnoreCase)))
            return false;

        var pattern = Regex.Escape(tolerant[0]).Replace("\\*", "[A-Z0-9]");
        return Regex.IsMatch(exact[0], "^" + pattern + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    internal static string? AbsoluteAddress(string value, AmlObject item)
    {
        if (Regex.IsMatch(value, @"^%?[IQM][BWD]?[0-9]+(\.[0-7])?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return value.TrimStart('%');

        // CAx readback can store the I/O direction separately from the address and width.
        if (!Regex.IsMatch(value, @"^(?:[0-9]+\.[0-7]|[BWD][0-9]+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) || !item.Properties.TryGetValue("IoType", out var direction))
            return null;

        return direction switch
        {
            "Input" => "I" + value,
            "Output" => "Q" + value,
            _ => null
        };
    }
}
