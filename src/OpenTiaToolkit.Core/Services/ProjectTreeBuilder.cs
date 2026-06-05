using OpenTiaToolkit.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTiaToolkit.Core.Services
{
    /// <summary>
    /// Builds project tree nodes from the TIA project/device hierarchy.
    /// Extracted from PortalInspectionService so the traversal logic can evolve independently.
    /// </summary>
    internal sealed class ProjectTreeBuilder
    {
        private readonly Type? _plcSoftwareType;

        public ProjectTreeBuilder(Type? plcSoftwareType)
        {
            _plcSoftwareType = plcSoftwareType;
        }

        public ProjectTreeNode Build(object project, int? maxDepth = null)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));

            var projectName = TiaReflectionHelper.GetName(project) ?? "Project";
            return BuildNode(project, projectName, ProjectNodeKind.Project, maxDepth, 0);
        }

        private ProjectTreeNode BuildNode(
            object node,
            string currentPath,
            ProjectNodeKind nodeKind,
            int? maxDepth,
            int currentDepth)
        {
            var nodeName = TiaReflectionHelper.GetName(node) ?? currentPath.Split('/').Last();
            var nodeType = GetNodeType(nodeKind);
            var children = new List<ProjectTreeNode>();

            if (maxDepth.HasValue && currentDepth >= maxDepth.Value)
            {
                return new ProjectTreeNode(nodeName, nodeType, currentPath, children);
            }

            foreach (var child in GetChildren(node, nodeKind))
            {
                var childName = TiaReflectionHelper.GetName(child.Node) ?? child.Kind.ToString();
                var childPath = $"{currentPath}/{childName}";
                children.Add(BuildNode(child.Node, childPath, child.Kind, maxDepth, currentDepth + 1));
            }

            if (nodeKind == ProjectNodeKind.DeviceItem)
            {
                var plcSoftwareNode = TryBuildPlcSoftwareNode(node, currentPath);
                if (plcSoftwareNode != null)
                {
                    children.Add(plcSoftwareNode);
                }
            }

            return new ProjectTreeNode(nodeName, nodeType, currentPath, children);
        }

        private IEnumerable<ProjectNodeChild> GetChildren(object node, ProjectNodeKind nodeKind)
        {
            return nodeKind switch
            {
                ProjectNodeKind.Project => EnumerateChildren(node, "Devices", ProjectNodeKind.Device)
                    .Concat(EnumerateChildren(node, "DeviceGroups", ProjectNodeKind.DeviceGroup)),
                ProjectNodeKind.DeviceGroup => EnumerateChildren(node, "Devices", ProjectNodeKind.Device)
                    .Concat(EnumerateChildren(node, "Groups", ProjectNodeKind.DeviceGroup))
                    .Concat(EnumerateChildren(node, "DeviceGroups", ProjectNodeKind.DeviceGroup)),
                ProjectNodeKind.Device => EnumerateChildren(node, "DeviceItems", ProjectNodeKind.DeviceItem),
                ProjectNodeKind.DeviceItem => EnumerateChildren(node, "DeviceItems", ProjectNodeKind.DeviceItem),
                _ => Array.Empty<ProjectNodeChild>()
            };
        }

        private static IEnumerable<ProjectNodeChild> EnumerateChildren(object node, string propertyName, ProjectNodeKind childKind)
        {
            var children = TiaReflectionHelper.GetEnumerableProperty(node, propertyName);
            if (children == null)
            {
                return Array.Empty<ProjectNodeChild>();
            }

            return children.Select(child => new ProjectNodeChild(child, childKind)).ToArray();
        }

        private ProjectTreeNode? TryBuildPlcSoftwareNode(object deviceItem, string parentPath)
        {
            if (_plcSoftwareType == null)
            {
                return null;
            }

            try
            {
                var getServiceMethod = deviceItem.GetType().GetMethod("GetService");
                if (getServiceMethod == null)
                {
                    return null;
                }

                var genericMethod = getServiceMethod.MakeGenericMethod(_plcSoftwareType);
                var plcSoftware = genericMethod.Invoke(deviceItem, null);
                if (plcSoftware == null)
                {
                    return null;
                }

                return new ProjectTreeNode(
                    "Software",
                    "PlcSoftware",
                    $"{parentPath}/Software",
                    Array.Empty<ProjectTreeNode>());
            }
            catch
            {
                return null;
            }
        }

        private static string GetNodeType(ProjectNodeKind nodeKind)
        {
            return nodeKind switch
            {
                ProjectNodeKind.Project => "Project",
                ProjectNodeKind.DeviceGroup => "DeviceGroup",
                ProjectNodeKind.Device => "Device",
                ProjectNodeKind.DeviceItem => "DeviceItem",
                _ => "Unknown"
            };
        }

        private readonly struct ProjectNodeChild
        {
            public ProjectNodeChild(object node, ProjectNodeKind kind)
            {
                Node = node;
                Kind = kind;
            }

            public object Node { get; }

            public ProjectNodeKind Kind { get; }
        }

        private enum ProjectNodeKind
        {
            Project,
            DeviceGroup,
            Device,
            DeviceItem
        }
    }
}
