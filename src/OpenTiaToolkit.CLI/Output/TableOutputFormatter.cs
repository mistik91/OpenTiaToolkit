using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands;
using OpenTiaToolkit.Core.Commands.Plc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OpenTiaToolkit.CLI.Output
{
    /// <summary>
    /// Formats command results as human-readable tables for CLI output.
    /// </summary>
    public static class TableOutputFormatter
    {
        /// <summary>
        /// Formats a command result as a table or plain text.
        /// </summary>
        /// <param name="result">The command result to format.</param>
        /// <returns>Formatted string for CLI output.</returns>
        public static string Format(CommandResult result)
        {
            if (!result.Success)
            {
                return FormatError(result.Message, result.Error);
            }

            if (result.Data == null)
            {
                return result.Message;
            }

            // Handle different data types
            return result.Data switch
            {
                string str => str,
                IEnumerable<ProjectInfo> projects => FormatProjects(projects),
                IEnumerable<DeviceInfo> devices => FormatDevices(devices),
                ProjectInfo project => FormatProjectInfo(project),
                DeviceInfo device => FormatDeviceInfo(device),
                ProjectTreeNode tree => FormatProjectTree(tree),
                SoftwareTreeNode softwareTree => FormatSoftwareTree(softwareTree),
                IEnumerable<PlcInfo> plcs => FormatPlcs(plcs),
                IEnumerable<BlockInfo> blocks => FormatBlocks(blocks),
                BlockInfo block => FormatBlockInfo(block),
                BlockInfoDetail blockDetail => FormatBlockInfoDetail(blockDetail),
                // Anonymous objects (status objects, etc.)
                _ when result.Data.GetType().Name.Contains("AnonymousType") => FormatAnonymousObject(result.Data),
                _ => result.ToTable() // Fallback to default formatting
            };
        }

        private static string FormatError(string message, string error)
        {
            var lines = new List<string>
            {
                "Error: " + message
            };

            if (!string.IsNullOrEmpty(error))
            {
                lines.Add($"Details: {error}");
            }

            return string.Join(Environment.NewLine, lines);
        }

        private static string FormatProjects(IEnumerable<ProjectInfo> projects)
        {
            var list = projects.ToList();
            if (list.Count == 0)
            {
                return "No projects found.";
            }

            var lines = new List<string>
            {
                $"Found {list.Count} project(s):",
                "",
                string.Format("{0,-40} {1,-50} {2,-10} {3,-20}", "Name", "Path", "Version", "Created"),
                new string('-', 120)
            };

            foreach (var project in list)
            {
                var name = Truncate(project.Name, 40);
                var path = Truncate(project.Path, 50);
                var version = project.Version ?? "N/A";
                var created = project.Created?.ToString("yyyy-MM-dd HH:mm") ?? "N/A";

                lines.Add($"{name,-40} {path,-50} {version,-10} {created,-20}");
            }

            return string.Join(Environment.NewLine, lines);
        }

        private static string FormatDevices(IEnumerable<DeviceInfo> devices)
        {
            var list = devices.ToList();
            if (list.Count == 0)
            {
                return "No devices found.";
            }

            var lines = new List<string>
            {
                $"Found {list.Count} device(s):",
                "",
                string.Format("{0,-30} {1,-30} {2,-30} {3,-15}", "Name", "Type", "Identification", "IP Address"),
                new string('-', 105)
            };

            foreach (var device in list)
            {
                var name = Truncate(device.Name, 30);
                var type = Truncate(device.Type, 30);
                var identification = Truncate(device.Identification ?? "N/A", 30);
                var ip = device.IpAddress ?? "Not configured";

                lines.Add($"{name,-30} {type,-30} {identification,-30} {ip,-15}");
            }

            return string.Join(Environment.NewLine, lines);
        }

        private static string FormatProjectInfo(ProjectInfo project)
        {
            var lines = new List<string>
            {
                "Project Information:",
                "",
                $"Name:     {project.Name}",
                $"Path:     {project.Path}",
                $"Version:  {project.Version ?? "N/A"}",
                $"Created:  {project.Created?.ToString("yyyy-MM-dd HH:mm:ss") ?? "N/A"}"
            };

            return string.Join(Environment.NewLine, lines);
        }

        private static string FormatDeviceInfo(DeviceInfo device)
        {
            var lines = new List<string>
            {
                "Device Information:",
                "",
                $"Name:           {device.Name}",
                $"Type:           {device.Type}",
                $"Identification: {device.Identification ?? "N/A"}",
                $"IP Address:     {device.IpAddress ?? "Not configured"}"
            };

            return string.Join(Environment.NewLine, lines);
        }

        private static string FormatProjectTree(ProjectTreeNode tree)
        {
            var lines = new List<string>
            {
                "Project Tree:",
                ""
            };

            FormatTreeNode(tree, lines, "", 0);

            return string.Join(Environment.NewLine, lines);
        }

        private static void FormatTreeNode(ProjectTreeNode node, List<string> lines, string prefix, int depth)
        {
            // Limit depth to prevent infinite recursion in case of circular references
            if (depth > 20)
            {
                lines.Add($"{prefix}[Max depth reached]");
                return;
            }

            var typeInfo = string.IsNullOrEmpty(node.Type) || node.Type == "Unknown"
                ? ""
                : $" [{node.Type}]";

            lines.Add($"{prefix}{node.Name}{typeInfo}");

            if (node.Children != null && node.Children.Count > 0)
            {
                var childPrefix = prefix + "  ";
                foreach (var child in node.Children)
                {
                    FormatTreeNode(child, lines, childPrefix, depth + 1);
                }
            }
        }

        private static string FormatSoftwareTree(SoftwareTreeNode tree)
        {
            var lines = new List<string>
            {
                "PLC Software Tree:",
                ""
            };

            FormatSoftwareTreeNode(tree, lines, "", 0);

            return string.Join(Environment.NewLine, lines);
        }

        private static void FormatSoftwareTreeNode(SoftwareTreeNode node, List<string> lines, string prefix, int depth)
        {
            // Limit depth to prevent infinite recursion in case of circular references
            if (depth > 20)
            {
                lines.Add($"{prefix}[Max depth reached]");
                return;
            }

            var typeInfo = string.IsNullOrEmpty(node.Type) || node.Type == "Unknown"
                ? ""
                : $" [{node.Type}]";

            lines.Add($"{prefix}{node.Name}{typeInfo}");

            if (node.Children != null && node.Children.Count > 0)
            {
                var childPrefix = prefix + "  ";
                foreach (var child in node.Children)
                {
                    FormatSoftwareTreeNode(child, lines, childPrefix, depth + 1);
                }
            }
        }

        private static string FormatPlcs(IEnumerable<PlcInfo> plcs)
        {
            var list = plcs.ToList();
            if (list.Count == 0)
            {
                return "No PLC devices found in the project.";
            }

            var lines = new List<string>
            {
                $"Found {list.Count} PLC device(s):",
                "",
                string.Format("{0,-30} {1,-40} {2,-25} {3,-15}", "Name", "Parent Device", "Type", "Confirmed"),
                new string('-', 110)
            };

            foreach (var plc in list)
            {
                var name = Truncate(plc.Name, 30);
                var parent = Truncate(plc.ParentDevice ?? "N/A", 40);
                var type = Truncate(plc.Type ?? "Unknown", 25);
                var confirmed = plc.IsConfirmedPlc ? "✓ Yes" : "No";

                lines.Add($"{name,-30} {parent,-40} {type,-25} {confirmed,-15}");
            }

            return string.Join(Environment.NewLine, lines);
        }

        private static string FormatBlocks(IEnumerable<BlockInfo> blocks)
        {
            var list = blocks.ToList();
            if (list.Count == 0)
            {
                return "No blocks found.";
            }

            var lines = new List<string>
            {
                $"Found {list.Count} block(s):",
                "",
                string.Format("{0,-25} {1,-10} {2,-12} {3,-10} {4,-8} {5,-30}", "Name", "Type", "Number", "Language", "ReadOnly", "Comment"),
                new string('-', 95)
            };

            foreach (var block in list)
            {
                var name = Truncate(block.Name, 25);
                var type = Truncate(block.Type, 10);
                var number = Truncate(block.Number ?? "N/A", 12);
                var language = Truncate(block.Language ?? "N/A", 10);
                var readOnly = block.IsReadOnly ? "Yes" : "No";
                var comment = Truncate(block.Comment ?? "", 30);

                lines.Add($"{name,-25} {type,-10} {number,-12} {language,-10} {readOnly,-8} {comment,-30}");
            }

            return string.Join(Environment.NewLine, lines);
        }

        private static string FormatBlockInfo(BlockInfo block)
        {
            var lines = new List<string>
            {
                "Block Information:",
                "",
                $"Name:      {block.Name}",
                $"Type:      {block.Type}",
                $"Number:    {block.Number ?? "N/A"}",
                $"Language:  {block.Language ?? "N/A"}",
                $"Read-Only: {(block.IsReadOnly ? "Yes" : "No")}",
                $"Comment:   {block.Comment ?? "N/A"}"
            };

            return string.Join(Environment.NewLine, lines);
        }

        private static string FormatBlockInfoDetail(BlockInfoDetail block)
        {
            var lines = new List<string>
            {
                "Detailed Block Information:",
                "",
                $"Name:             {block.Name}",
                $"Type:             {block.Type}",
                $"Number:           {block.Number ?? "N/A"}",
                $"Language:         {block.Language ?? "N/A"}",
                $"Programming Lang: {block.ProgrammingLanguage ?? "N/A"}",
                $"Read-Only:        {(block.IsReadOnly ? "Yes" : "No")}",
                $"Author:           {block.Author ?? "N/A"}",
                $"Memory Size:      {(block.MemorySize > 0 ? block.MemorySize.ToString() : "N/A")} bytes",
                $"Created:          {block.Created ?? "N/A"}",
                $"Modified:         {block.Modified ?? "N/A"}",
                $"Comment:          {block.Comment ?? "N/A"}"
            };

            return string.Join(Environment.NewLine, lines);
        }

        private static string FormatAnonymousObject(object data)
        {
            var lines = new List<string>();

            var properties = data.GetType().GetProperties();
            if (properties.Length == 0)
            {
                return data.ToString() ?? "Empty result";
            }

            var maxNameLength = properties.Max(p => p.Name.Length);

            foreach (var prop in properties)
            {
                var value = prop.GetValue(data);

                // Check if this is a tree node (has name, type, and children properties)
                if (IsTreeNode(value))
                {
                    lines.Add($"{prop.Name,-20} :");
                    FormatAnonymousTreeNode(value, lines, "  ", 0);
                }
                else if (value != null && value.GetType().IsArray)
                {
                    // Check if it's an array of tree nodes
                    var array = (Array)value;
                    if (array.Length > 0 && IsTreeNode(array.GetValue(0)))
                    {
                        lines.Add($"{prop.Name,-20} :");
                        foreach (var item in array)
                        {
                            if (item != null)
                            {
                                FormatAnonymousTreeNode(item, lines, "  ", 0);
                            }
                        }
                    }
                    else
                    {
                        var valueStr = value.ToString() ?? "null";
                        lines.Add($"{prop.Name,-20} : {valueStr}");
                    }
                }
                else
                {
                    var valueStr = value?.ToString() ?? "null";
                    lines.Add($"{prop.Name,-20} : {valueStr}");
                }
            }

            return string.Join(Environment.NewLine, lines);
        }

        private static bool IsTreeNode(object obj)
        {
            if (obj == null) return false;

            var type = obj.GetType();
            var hasName = type.GetProperty("name") != null || type.GetProperty("Name") != null;
            var hasType = type.GetProperty("type") != null || type.GetProperty("Type") != null;
            var hasChildren = type.GetProperty("children") != null || type.GetProperty("Children") != null;
            var hasPath = type.GetProperty("path") != null || type.GetProperty("Path") != null;

            return hasName && hasType && hasChildren && hasPath;
        }

        private static void FormatAnonymousTreeNode(object node, List<string> lines, string indent, int depth)
        {
            if (depth > 20)
            {
                lines.Add($"{indent}[Max depth reached]");
                return;
            }

            var type = node.GetType();

            var nameProp = type.GetProperty("name") ?? type.GetProperty("Name");
            var typeProp = type.GetProperty("type") ?? type.GetProperty("Type");
            var childrenProp = type.GetProperty("children") ?? type.GetProperty("Children");

            var name = nameProp?.GetValue(node)?.ToString() ?? "Unknown";
            var nodeType = typeProp?.GetValue(node)?.ToString() ?? "";

            var typeInfo = string.IsNullOrEmpty(nodeType) || nodeType == "Unknown"
                ? ""
                : $" [{nodeType}]";

            lines.Add($"{indent}{name}{typeInfo}");

            var children = childrenProp?.GetValue(node);
            if (children != null && children.GetType().IsArray)
            {
                var childArray = (Array)children;
                foreach (var child in childArray)
                {
                    if (child != null)
                    {
                        FormatAnonymousTreeNode(child, lines, indent + "  ", depth + 1);
                    }
                }
            }
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            return value.Length <= maxLength
                ? value
                : value.Substring(0, maxLength - 3) + "...";
        }
    }
}
