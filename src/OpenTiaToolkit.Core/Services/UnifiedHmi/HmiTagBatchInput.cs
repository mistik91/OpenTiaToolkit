using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenTiaToolkit.Core.Models;
using YamlDotNet.RepresentationModel;

namespace OpenTiaToolkit.Core.Services;

internal static class HmiTagBatchInput
{
    internal static IReadOnlyList<HmiTagBatchTag> ReadJson(string text)
    {
        var tags = JsonSerializer.Deserialize<HmiTagBatchTag[]>(text, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
        });
        return Check(tags ?? throw new InvalidDataException("Expected a JSON array of tag specifications."));
    }

    internal static IReadOnlyList<HmiTagBatchTag> ReadWinCcMl(string text)
    {
        var yaml = new YamlStream();
        using (var reader = new StringReader(text))
            yaml.Load(reader);

        if (yaml.Documents.Count != 1 || yaml.Documents[0].RootNode is not YamlMappingNode root ||
            root.Children.Count != 1 || !root.Children.TryGetValue(new YamlScalarNode("SimpleTags"), out var section) ||
            section is not YamlMappingNode simpleTags)
            throw new InvalidDataException("Expected one WinCCML document with a SimpleTags mapping. Structured and array tags are not supported.");

        return Check(simpleTags.Children.Select(entry =>
        {
            if (entry.Key is not YamlScalarNode key || entry.Value is not YamlMappingNode properties)
                throw new InvalidDataException("Expected a tag name and its properties in SimpleTags.");
            var name = key.Value ?? "";
            var connection = Scalar(properties, "Connection");
            if (connection == "<Internal tag>")
                connection = null;
            var mode = Scalar(properties, "AccessMode");
            var controllerType = Scalar(properties, "ControllerDataType");
            if ((connection != null && (mode != "SymbolicAccess" || controllerType == null)) ||
                (connection == null && mode != null && mode != "SymbolicAccess" && mode != "None"))
                throw new InvalidDataException($"Tag '{name}' must be internal or have a symbolic PLC binding with ControllerDataType.");

            return new HmiTagBatchTag
            {
                Name = name,
                DataType = Scalar(properties, "DataType") ?? "",
                Connection = connection,
                PlcTag = Scalar(properties, "PlcTag"),
                InitialValue = Scalar(properties, "InitialValue"),
                WinCcMlControllerDataType = controllerType
            };
        }).ToArray());
    }

    private static string? Scalar(YamlMappingNode properties, string name)
    {
        if (!properties.Children.TryGetValue(new YamlScalarNode(name), out var value))
            return null;
        return value is YamlScalarNode scalar
            ? scalar.Value
            : throw new InvalidDataException($"WinCCML property '{name}' must be a scalar.");
    }

    private static IReadOnlyList<HmiTagBatchTag> Check(IReadOnlyList<HmiTagBatchTag> tags)
    {
        if (tags.Count == 0)
            throw new InvalidDataException("At least one tag is required.");

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in tags)
        {
            if (tag == null || string.IsNullOrWhiteSpace(tag.Name) || string.IsNullOrWhiteSpace(tag.DataType))
                throw new InvalidDataException("Each tag requires a name and data type.");
            if (!names.Add(tag.Name))
                throw new InvalidDataException($"Duplicate tag '{tag.Name}'.");
            if ((tag.Connection == null) != (tag.PlcTag == null) ||
                (tag.Connection != null && (string.IsNullOrWhiteSpace(tag.Connection) || string.IsNullOrWhiteSpace(tag.PlcTag))))
                throw new InvalidDataException($"Tag '{tag.Name}' requires both a connection and a PLC symbol, or neither.");
            if (tag.Connection != null && tag.InitialValue != null)
                throw new InvalidDataException($"Tag '{tag.Name}' specifies an initial value for a PLC-linked tag.");
            if (tag.InitialValue != null)
            {
                if (!string.Equals(tag.DataType, "Bool", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Only Bool initial values are supported. Check tag '{tag.Name}'.");
                switch (tag.InitialValue.ToUpperInvariant())
                {
                    case "TRUE": case "1": tag.InitialValue = "1"; break;
                    case "FALSE": case "0": tag.InitialValue = "0"; break;
                    default: throw new InvalidDataException($"Tag '{tag.Name}' requires a Bool initial value of true, false, 1, or 0.");
                }
            }
        }
        return tags;
    }
}
