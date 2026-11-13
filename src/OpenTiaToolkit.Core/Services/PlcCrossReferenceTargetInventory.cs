using OpenTiaToolkit.Core.Services.V21;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTiaToolkit.Core.Services
{
    /// <summary>Target identities captured for one impact-analysis invocation.</summary>
    internal static class PlcCrossReferenceTargetInventory
    {
        internal static IReadOnlyList<Target> Read(object software, V21PlcTagStructureAccess tags, V21EngineeringObjectAccess objects) =>
            Enumerate(software, tags, objects).ToArray();

        internal static IEnumerable<Target> Find(string name, IReadOnlyList<Target> all)
        {
            var exact = all.Where(target => string.Equals(target.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var target in exact.Count > 0
                ? exact
                : all.Where(target => target.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                yield return target;
            }
        }

        private static IEnumerable<Target> Enumerate(object plcSoftware, V21PlcTagStructureAccess tagsAccess, V21EngineeringObjectAccess objects)
        {
            var tagTables = tagsAccess.GetTagTables(plcSoftware);
            if (tagTables != null)
            {
                foreach (var tagTable in tagTables)
                {
                    var tableName = objects.GetName(tagTable) ?? string.Empty;
                    var tags = tagsAccess.GetTags(tagTable);
                    if (tags == null) continue;
                    foreach (var tag in tags)
                    {
                        var name = objects.GetName(tag);
                        if (!string.IsNullOrWhiteSpace(name))
                            yield return new Target(name!, "Tag", "Tag Tables/" + tableName + "/" + name, tag);
                    }
                }
            }

            foreach (var block in EnumerateRecursive(TiaReflectionHelper.GetBlockGroup(plcSoftware), "Blocks"))
            {
                var name = GetString(block, "Name");
                if (!string.IsNullOrWhiteSpace(name))
                    yield return new Target(name!, "Block", "Program Blocks/" + name, block);
            }
            foreach (var type in EnumerateRecursive(TiaReflectionHelper.GetTypeGroup(plcSoftware), "Types"))
            {
                var name = GetString(type, "Name");
                if (!string.IsNullOrWhiteSpace(name))
                    yield return new Target(name!, "Type", "PLC Data Types/" + name, type);
            }
            foreach (var source in EnumerateRecursive(TiaReflectionHelper.GetExternalSourceGroup(plcSoftware), "ExternalSources"))
            {
                var name = GetString(source, "Name");
                if (!string.IsNullOrWhiteSpace(name))
                    yield return new Target(name!, "ExternalSource", "Source Files/" + name, source);
            }
        }

        private static IEnumerable<object> EnumerateRecursive(object? group, string collectionName)
        {
            if (group == null) yield break;
            var items = TiaReflectionHelper.GetEnumerableProperty(group, collectionName);
            if (items != null)
                foreach (var item in items)
                    yield return item;
            var groups = TiaReflectionHelper.GetGroups(group);
            if (groups == null) yield break;
            foreach (var nested in groups)
                foreach (var item in EnumerateRecursive(nested, collectionName))
                    yield return item;
        }

        private static string? GetString(object? value, params string[] propertyNames)
        {
            if (value == null) return null;
            foreach (var propertyName in propertyNames)
            {
                var propertyValue = TiaReflectionHelper.GetProperty(value, propertyName);
                if (propertyValue != null) return propertyValue.ToString();
            }
            return value is string text ? text : null;
        }

        internal sealed class Target
        {
            public Target(string name, string itemType, string path, object item)
            {
                Name = name;
                ItemType = itemType;
                Path = path;
                Item = item;
            }

            public string Name { get; }
            public string ItemType { get; }
            public string Path { get; }
            public object Item { get; }
        }
    }
}
