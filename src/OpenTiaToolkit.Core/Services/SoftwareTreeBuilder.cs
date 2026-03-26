using OpenTiaToolkit.Core.Abstractions;
using System;
using System.Collections.Generic;

namespace OpenTiaToolkit.Core.Services
{
    /// <summary>
    /// Builds PLC software tree nodes from the reflected TIA software object model.
    /// </summary>
    internal sealed class SoftwareTreeBuilder
    {
        private static readonly SoftwareContainerDefinition[] RootContainers =
        {
            new SoftwareContainerDefinition("Blocks", "BlockContainer", static plcSoftware => TiaReflectionHelper.GetBlockGroup(plcSoftware), new[] { "Blocks" }),
            new SoftwareContainerDefinition("Types", "TypeContainer", static plcSoftware => TiaReflectionHelper.GetTypeGroup(plcSoftware), new[] { "Types", "Documents" }),
            new SoftwareContainerDefinition("Tag Tables", "TagTableContainer", static plcSoftware => TiaReflectionHelper.GetTagTableGroup(plcSoftware), new[] { "TagTables" }),
            new SoftwareContainerDefinition("Source Files", "ExternalSourceContainer", static plcSoftware => TiaReflectionHelper.GetExternalSourceGroup(plcSoftware), new[] { "ExternalSources" })
        };

        public SoftwareTreeNode Build(object plcSoftware, string? plcName, int? maxDepth = null)
        {
            if (plcSoftware == null) throw new ArgumentNullException(nameof(plcSoftware));

            var rootName = TiaReflectionHelper.GetName(plcSoftware) ?? plcName ?? "PLC";
            var rootPath = rootName;
            var children = new List<SoftwareTreeNode>();

            foreach (var container in RootContainers)
            {
                AddContainerNode(
                    children,
                    container.DisplayName,
                    container.ContainerType,
                    rootPath,
                    container.ResolveGroup(plcSoftware),
                    maxDepth,
                    1,
                    container.ItemCollectionPropertyNames);
            }

            return new SoftwareTreeNode(rootName, "PlcSoftware", rootPath, children);
        }

        private void AddContainerNode(
            List<SoftwareTreeNode> target,
            string displayName,
            string containerType,
            string rootPath,
            object? group,
            int? maxDepth,
            int currentDepth,
            IReadOnlyList<string> itemCollectionPropertyNames)
        {
            if (group == null)
            {
                return;
            }

            var path = $"{rootPath}/{displayName}";
            var children = maxDepth.HasValue && currentDepth >= maxDepth.Value
                ? Array.Empty<SoftwareTreeNode>()
                : BuildGroupChildren(group, path, maxDepth, currentDepth + 1, itemCollectionPropertyNames);

            target.Add(new SoftwareTreeNode(displayName, containerType, path, children));
        }

        private IReadOnlyList<SoftwareTreeNode> BuildGroupChildren(
            object group,
            string parentPath,
            int? maxDepth,
            int currentDepth,
            IReadOnlyList<string> itemCollectionPropertyNames)
        {
            var children = new List<SoftwareTreeNode>();

            foreach (var propertyName in itemCollectionPropertyNames)
            {
                var items = TiaReflectionHelper.GetEnumerableProperty(group, propertyName);
                if (items == null)
                {
                    continue;
                }

                foreach (var item in items)
                {
                    children.Add(BuildLeafNode(item, parentPath));
                }
            }

            var groups = TiaReflectionHelper.GetGroups(group);
            if (groups == null)
            {
                return children;
            }

            foreach (var childGroup in groups)
            {
                children.Add(BuildGroupNode(childGroup, parentPath, maxDepth, currentDepth, itemCollectionPropertyNames));
            }

            return children;
        }

        private SoftwareTreeNode BuildGroupNode(
            object group,
            string parentPath,
            int? maxDepth,
            int currentDepth,
            IReadOnlyList<string> itemCollectionPropertyNames)
        {
            var name = TiaReflectionHelper.GetName(group) ?? TiaReflectionHelper.GetTypeName(group) ?? "Group";
            var path = $"{parentPath}/{name}";
            var children = maxDepth.HasValue && currentDepth >= maxDepth.Value
                ? Array.Empty<SoftwareTreeNode>()
                : BuildGroupChildren(group, path, maxDepth, currentDepth + 1, itemCollectionPropertyNames);

            return new SoftwareTreeNode(
                name,
                TiaReflectionHelper.GetTypeName(group) ?? "Group",
                path,
                children);
        }

        private static SoftwareTreeNode BuildLeafNode(object item, string parentPath)
        {
            var name = TiaReflectionHelper.GetName(item) ?? TiaReflectionHelper.GetTypeName(item) ?? "Item";
            var path = $"{parentPath}/{name}";
            var nodeType = GetLeafType(item);
            return new SoftwareTreeNode(name, nodeType, path, Array.Empty<SoftwareTreeNode>());
        }

        private static string GetLeafType(object item)
        {
            var explicitType = TiaReflectionHelper.GetProperty(item, "Type")?.ToString();
            if (!string.IsNullOrWhiteSpace(explicitType))
            {
                return explicitType!;
            }

            var programmingLanguage = TiaReflectionHelper.GetProperty(item, "ProgrammingLanguage")?.ToString();
            if (!string.IsNullOrWhiteSpace(programmingLanguage))
            {
                return $"{TiaReflectionHelper.GetTypeName(item) ?? "Item"} ({programmingLanguage})";
            }

            return TiaReflectionHelper.GetTypeName(item) ?? "Item";
        }

        private sealed class SoftwareContainerDefinition
        {
            public SoftwareContainerDefinition(
                string displayName,
                string containerType,
                Func<object, object?> resolveGroup,
                IReadOnlyList<string> itemCollectionPropertyNames)
            {
                DisplayName = displayName;
                ContainerType = containerType;
                ResolveGroup = resolveGroup;
                ItemCollectionPropertyNames = itemCollectionPropertyNames;
            }

            public string DisplayName { get; }

            public string ContainerType { get; }

            public Func<object, object?> ResolveGroup { get; }

            public IReadOnlyList<string> ItemCollectionPropertyNames { get; }
        }
    }
}
