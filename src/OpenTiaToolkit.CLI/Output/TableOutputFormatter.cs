using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands;
using OpenTiaToolkit.Core.Commands.Hmi;
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
                var errorText = FormatError(result.Message, result.Error);
                if (result.Data == null)
                {
                    return errorText;
                }

                var detailText = result.Data switch
                {
                    HmiDataLogDeleteResult dataLogDelete => FormatHmiDataLogDeleteResult(dataLogDelete),
                    HmiLoggingTagDeleteResult loggingTagDelete => FormatHmiLoggingTagDeleteResult(loggingTagDelete),
                    _ when IsTagUpdateResult(result.Data) => FormatTagUpdateResult(result.Data),
                    _ => string.Empty
                };

                return string.IsNullOrWhiteSpace(detailText)
                    ? errorText
                    : errorText + Environment.NewLine + Environment.NewLine + detailText;
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
                IEnumerable<HmiInfo> hmis => FormatHmis(hmis),
                ProjectInfo project => FormatProjectInfo(project),
                DeviceInfo device => FormatDeviceInfo(device),
                ProjectTreeNode tree => FormatProjectTree(tree),
                SoftwareTreeNode softwareTree => FormatSoftwareTree(softwareTree),
                IEnumerable<PlcInfo> plcs => FormatPlcs(plcs),
                IEnumerable<HmiTagInfo> tags => FormatHmiTags(tags),
                IEnumerable<HmiScreenInfo> screens => FormatHmiScreens(screens),
                HmiScreenInfo screen => FormatHmiScreenInfo(screen),
                HmiScreenItemInfo screenItem => FormatHmiScreenItemInfo(screenItem),
                HmiScreenCopyResult screenCopy => FormatHmiScreenCopyResult(screenCopy),
                IEnumerable<HmiItemTypeInfo> itemTypes => FormatHmiItemTypes(itemTypes),
                IEnumerable<HmiItemPropertyInfo> itemProperties => FormatHmiItemProperties(itemProperties),
                HmiTrendInfo trendInfo => FormatHmiTrendInfo(trendInfo),
                HmiLoggingTagDeleteResult loggingTagDelete => FormatHmiLoggingTagDeleteResult(loggingTagDelete),
                HmiDataLogDeleteResult dataLogDelete => FormatHmiDataLogDeleteResult(dataLogDelete),
                HmiJsonExportResult jsonExport => FormatHmiJsonExportResult(jsonExport),
                HmiJsonDiffResult jsonDiff => FormatHmiJsonDiffResult(jsonDiff),
                HmiJsonImportResult jsonImport => FormatHmiJsonImportResult(jsonImport),
                IEnumerable<BlockInfo> blocks => FormatBlocks(blocks),
                BlockInfo block => FormatBlockInfo(block),
                BlockInfoDetail blockDetail => FormatBlockInfoDetail(blockDetail),
                // Anonymous objects (status objects, etc.)
                _ when IsFindReplaceResult(result.Data) => FormatFindReplaceResult(result.Data),
                _ when IsTagUpdateResult(result.Data) => FormatTagUpdateResult(result.Data),
                _ when IsImportResult(result.Data) => FormatImportResult(result.Data),
                _ when result.Data.GetType().Name.Contains("AnonymousType") => FormatAnonymousObject(result.Data),
                _ => result.ToTable() // Fallback to default formatting
            };
        }

        private static string FormatError(string message, string? error)
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

        private static string FormatHmis(IEnumerable<HmiInfo> hmis)
        {
            var list = hmis.ToList();
            if (list.Count == 0)
            {
                return "No HMI devices found in the project.";
            }

            var lines = new List<string>
            {
                $"Found {list.Count} HMI device(s):",
                "",
                string.Format("{0,-30} {1,-25} {2,-30} {3,-12}", "Name", "Type", "Parent Device", "Confirmed"),
                new string('-', 100)
            };

            foreach (var hmi in list)
            {
                lines.Add(string.Format(
                    "{0,-30} {1,-25} {2,-30} {3,-12}",
                    Truncate(hmi.Name, 30),
                    Truncate(hmi.Type ?? "Unknown", 25),
                    Truncate(hmi.ParentDevice ?? "N/A", 30),
                    hmi.IsConfirmedHmi ? "Yes" : "No"));
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

        private static string FormatHmiScreens(IEnumerable<HmiScreenInfo> screens)
        {
            var list = screens.ToList();
            if (list.Count == 0)
            {
                return "No Unified HMI screens found.";
            }

            var lines = new List<string>
            {
                $"Found {list.Count} screen(s):",
                "",
                string.Format("{0,-45} {1,-10} {2,-10} {3,-8} {4,-10}", "Path", "Width", "Height", "Items", "Enabled"),
                new string('-', 90)
            };

            foreach (var screen in list)
            {
                lines.Add(string.Format(
                    "{0,-45} {1,-10} {2,-10} {3,-8} {4,-10}",
                    Truncate(screen.Path, 45),
                    screen.Width,
                    screen.Height,
                    screen.ItemCount,
                    screen.Enabled ? "Yes" : "No"));
            }

            return string.Join(Environment.NewLine, lines);
        }

        private static string FormatHmiTags(IEnumerable<HmiTagInfo> tags)
        {
            var list = tags.ToList();
            if (list.Count == 0)
            {
                return "No Unified HMI tags found.";
            }

            var lines = new List<string>
            {
                $"Found {list.Count} tag(s):",
                "",
                string.Format("{0,-45} {1,-16} {2,-18} {3,-8} {4,-8}", "Path", "Data Type", "Tag Table", "Members", "Logs"),
                new string('-', 100)
            };

            foreach (var tag in list)
            {
                lines.Add(string.Format(
                    "{0,-45} {1,-16} {2,-18} {3,-8} {4,-8}",
                    Truncate(tag.Path, 45),
                    Truncate(tag.DataType ?? tag.HmiDataType ?? "N/A", 16),
                    Truncate(tag.TagTableName ?? "N/A", 18),
                    tag.MemberCount,
                    tag.LoggingTagCount));
            }

            return string.Join(Environment.NewLine, lines);
        }

        private static string FormatHmiScreenInfo(HmiScreenInfo screen)
        {
            var lines = new List<string>
            {
                "Unified HMI Screen:",
                "",
                $"Name:      {screen.Name}",
                $"Path:      {screen.Path}",
                $"Group:     {screen.GroupPath ?? "(root)"}",
                $"Size:      {screen.Width}x{screen.Height}",
                $"Enabled:   {(screen.Enabled ? "Yes" : "No")}",
                $"Items:     {screen.ItemCount}"
            };

            if (screen.Items.Count > 0)
            {
                lines.Add("");
                lines.Add("Items:");
                foreach (var item in screen.Items)
                {
                    lines.Add($"  - {item.Name} [{item.Type}] ({item.Left ?? 0}, {item.Top ?? 0}) {item.Width ?? 0}x{item.Height ?? 0}");
                }
            }

            return string.Join(Environment.NewLine, lines);
        }

        private static string FormatHmiScreenItemInfo(HmiScreenItemInfo item)
        {
            var lines = new List<string>
            {
                "Unified HMI Item:",
                "",
                $"Name:       {item.Name}",
                $"Type:       {item.Type}",
                $"Screen:     {item.ScreenPath}",
                $"Position:   ({item.Left ?? 0}, {item.Top ?? 0})",
                $"Size:       {item.Width ?? 0}x{item.Height ?? 0}"
            };

            return string.Join(Environment.NewLine, lines);
        }

        private static string FormatHmiScreenCopyResult(HmiScreenCopyResult copy)
        {
            var lines = new List<string>
            {
                "Unified HMI Screen Copy:",
                "",
                $"Template:              {copy.TemplatePath}",
                $"Target:                {copy.TargetPath}",
                $"Replacement Applied:   {(copy.ReplacementApplied ? "Yes" : "No")}",
                $"Copied Items:          {copy.CopiedItemCount}",
                $"Screen Event Handlers: {copy.CopiedScreenEventHandlerCount}",
                $"Item Event Handlers:   {copy.CopiedItemEventHandlerCount}",
                "",
                FormatHmiScreenInfo(copy.Screen)
            };

            return string.Join(Environment.NewLine, lines);
        }

        private static string FormatHmiItemTypes(IEnumerable<HmiItemTypeInfo> itemTypes)
        {
            var list = itemTypes.ToList();
            if (list.Count == 0)
            {
                return "No supported Unified HMI item types found.";
            }

            var lines = new List<string>
            {
                $"Found {list.Count} supported item type(s):",
                "",
                string.Format("{0,-28} {1,-14} {2,-12} {3,-25}", "Type", "Category", "Default Size", "Aliases"),
                new string('-', 85)
            };

            foreach (var itemType in list)
            {
                lines.Add(string.Format(
                    "{0,-28} {1,-14} {2,-12} {3,-25}",
                    Truncate(itemType.Type, 28),
                    Truncate(itemType.Category, 14),
                    $"{itemType.DefaultWidth}x{itemType.DefaultHeight}",
                    Truncate(string.Join(", ", itemType.Aliases), 25)));
            }

            return string.Join(Environment.NewLine, lines);
        }

        private static string FormatHmiItemProperties(IEnumerable<HmiItemPropertyInfo> itemProperties)
        {
            var list = itemProperties.ToList();
            if (list.Count == 0)
            {
                return "No Unified HMI item properties found.";
            }

            var lines = new List<string>
            {
                $"Found {list.Count} property value(s):",
                "",
                string.Format("{0,-28} {1,-30} {2,-18} {3,-10} {4,-12}", "Name", "Value", "Type", "Writable", "Source"),
                new string('-', 105)
            };

            foreach (var property in list)
            {
                lines.Add(string.Format(
                    "{0,-28} {1,-30} {2,-18} {3,-10} {4,-12}",
                    Truncate(property.Name, 28),
                    Truncate(property.Value ?? string.Empty, 30),
                    Truncate(property.ValueType, 18),
                    property.IsWritable ? "Yes" : "No",
                    Truncate(property.Source, 12)));
            }

            return string.Join(Environment.NewLine, lines);
        }

        private static string FormatHmiTrendInfo(HmiTrendInfo trendInfo)
        {
            var lines = new List<string>
            {
                "Unified HMI Trend:",
                "",
                $"Tag Path:         {trendInfo.TagPath}",
                $"Tag Name:         {trendInfo.TagName}",
                $"Logging Tag:      {trendInfo.LoggingTagName}",
                $"Data Log:         {trendInfo.DataLogName}",
                $"Logging Mode:     {trendInfo.LoggingMode}",
                $"Cycle:            {trendInfo.Cycle ?? "(not used)"}",
                $"Storage Folder:   {trendInfo.StorageFolder}",
                $"Log Max Size:     {trendInfo.LogMaxSize}",
                $"Duration Days:    {trendInfo.DurationDays}",
                $"Segment Max Size: {trendInfo.SegmentMaxSize}",
                $"Data Log Created: {(trendInfo.DataLogCreated ? "Yes" : "No")}",
                $"Logging Tag Created: {(trendInfo.LoggingTagCreated ? "Yes" : "No")}"
            };

            return string.Join(Environment.NewLine, lines);
        }

        private static string FormatHmiLoggingTagDeleteResult(HmiLoggingTagDeleteResult result)
        {
            var lines = new List<string>
            {
                "Unified HMI Logging Tag Removal:",
                "",
                $"Tag Path:    {result.TagPath}",
                $"Logging Tag: {result.LoggingTagName}",
                $"Data Log:    {result.DataLogName}",
                $"Deleted:     {(result.Deleted ? "Yes" : "No")}"
            };

            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                lines.Add($"Reason:      {result.Error}");
            }

            return string.Join(Environment.NewLine, lines);
        }

        private static string FormatHmiDataLogDeleteResult(HmiDataLogDeleteResult result)
        {
            var lines = new List<string>
            {
                "Unified HMI Data Log Deletion:",
                "",
                $"Data Log:   {result.DataLogName}",
                $"Deleted:    {(result.Deleted ? "Yes" : "No")}",
                $"References: {result.References.Count}"
            };

            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                lines.Add($"Reason:     {result.Error}");
            }

            foreach (var reference in result.References)
            {
                lines.Add($"- {reference.TagPath} / {reference.LoggingTagName}");
            }

            return string.Join(Environment.NewLine, lines);
        }

        private static string FormatHmiJsonExportResult(HmiJsonExportResult exportResult)
        {
            var lines = new List<string>
            {
                "Unified HMI JSON Export:",
                "",
                $"Output Path:   {exportResult.OutputPath}",
                $"Screens:       {exportResult.ScreenCount}",
                $"Items:         {exportResult.ItemCount}",
                $"Warnings:      {exportResult.WarningCount}"
            };

            return string.Join(Environment.NewLine, lines);
        }

        private static string FormatHmiJsonDiffResult(HmiJsonDiffResult diffResult)
        {
            var lines = new List<string>
            {
                "Unified HMI JSON Patch:",
                "",
                $"Output Path:       {diffResult.OutputPath}",
                $"Screen Creates:    {diffResult.ScreenCreateCount}",
                $"Screen Updates:    {diffResult.ScreenUpdateCount}",
                $"Screen Deletes:    {diffResult.ScreenDeleteCount}",
                $"Item Creates:      {diffResult.ItemCreateCount}",
                $"Item Updates:      {diffResult.ItemUpdateCount}",
                $"Item Deletes:      {diffResult.ItemDeleteCount}",
                $"Item Recreates:    {diffResult.ItemRecreateCount}",
                $"Property Changes:  {diffResult.PropertyChangeCount}",
                $"Warnings:          {diffResult.WarningCount}"
            };

            return string.Join(Environment.NewLine, lines);
        }

        private static string FormatHmiJsonImportResult(HmiJsonImportResult importResult)
        {
            var lines = new List<string>
            {
                importResult.DryRun ? "Unified HMI JSON Preflight:" : "Unified HMI JSON Import:",
                "",
                $"Input Path:            {importResult.InputPath}",
                $"Report Path:           {importResult.ReportPath ?? "(not written)"}",
                $"Safety Blocked:        {(importResult.SafetyBlocked ? "Yes" : "No")}",
                $"Safety Message:        {importResult.SafetyMessage ?? "(none)"}",
                $"Backup Path:           {importResult.BackupPath ?? "(none)"}",
                $"Screens Processed:     {importResult.ScreenCount}",
                $"Screens Created:       {importResult.ScreenCreated}",
                $"Screens Deleted:       {importResult.ScreenDeleted}",
                $"Items Processed:       {importResult.ItemCount}",
                $"Items Created:         {importResult.ItemCreated}",
                $"Items Deleted:         {importResult.ItemDeleted}",
                $"Items Recreated:       {importResult.ItemRecreated}",
                $"Properties Applied:    {importResult.PropertySetCount}",
                $"Properties Unchanged:  {importResult.PropertyUnchangedCount}",
                $"Properties Skipped:    {importResult.PropertySkippedCount}",
                $"Properties Failed:     {importResult.PropertyFailedCount}",
                $"Warnings:              {importResult.WarningCount}"
            };

            if (importResult.Warnings.Count > 0)
            {
                lines.Add("");
                lines.Add("Warnings:");
                foreach (var warning in importResult.Warnings.Take(5))
                {
                    lines.Add("  - " + warning);
                }

                if (importResult.Warnings.Count > 5)
                {
                    lines.Add($"  - ... {importResult.Warnings.Count - 5} more");
                }
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
                else if (value != null && (value.GetType().IsArray || IsGenericList(value)))
                {
                    // Check if it's a collection of tree nodes
                    var collection = (System.Collections.IEnumerable)value;
                    var itemList = new System.Collections.Generic.List<object>();
                    foreach (var item in collection)
                    {
                        itemList.Add(item);
                    }

                    if (itemList.Count > 0 && IsTreeNode(itemList[0]))
                    {
                        lines.Add($"{prop.Name,-20} :");
                        foreach (var item in itemList)
                        {
                            if (item != null)
                            {
                                FormatAnonymousTreeNode(item, lines, "  ", 0);
                            }
                        }
                    }
                    else if (itemList.Count > 0 && itemList[0] is CompilationDiagnostic)
                    {
                        lines.Add($"{prop.Name,-20} :");
                        foreach (var item in itemList.Cast<CompilationDiagnostic>())
                        {
                            lines.Add($"  - {FormatCompilationDiagnostic(item)}");
                        }
                    }
                    else
                    {
                        // Format as a list of items
                        if (itemList.Count > 0)
                        {
                            lines.Add($"{prop.Name,-20} :");
                            foreach (var item in itemList)
                            {
                                lines.Add($"  - {item}");
                            }
                        }
                        else
                        {
                            lines.Add($"{prop.Name,-20} : (empty)");
                        }
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

        private static string FormatCompilationDiagnostic(CompilationDiagnostic diagnostic)
        {
            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(diagnostic.Severity))
            {
                parts.Add(diagnostic.Severity);
            }

            if (!string.IsNullOrWhiteSpace(diagnostic.ErrorCode))
            {
                parts.Add(diagnostic.ErrorCode!);
            }

            var location = string.Join(
                " / ",
                new[] { diagnostic.Path, diagnostic.Name }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));

            if (!string.IsNullOrWhiteSpace(location))
            {
                parts.Add(location);
            }

            if (!string.IsNullOrWhiteSpace(diagnostic.Description))
            {
                parts.Add(diagnostic.Description!);
            }

            return parts.Count == 0
                ? "Compiler diagnostic without exposed details"
                : string.Join(": ", parts);
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

        private static bool IsGenericList(object obj)
        {
            if (obj == null) return false;

            var type = obj.GetType();

            // Check if it implements IEnumerable (but not string)
            if (type == typeof(string)) return false;

            var implementsIEnumerable = type.GetInterface("System.Collections.IEnumerable") != null;
            return implementsIEnumerable && type.IsClass;
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

        /// <summary>
        /// Detects if the anonymous object is a find-replace result.
        /// </summary>
        private static bool IsFindReplaceResult(object data)
        {
            if (data == null) return false;

            var type = data.GetType();
            if (!type.Name.Contains("AnonymousType")) return false;

            // Check for signature properties of find-replace results
            var hasSummary = type.GetProperty("summary") != null;
            var hasChanges = type.GetProperty("changes") != null;
            var hasSkipped = type.GetProperty("skipped") != null;
            var hasWarnings = type.GetProperty("warnings") != null;
            var hasErrors = type.GetProperty("errors") != null;

            return hasSummary && hasChanges && hasSkipped && hasWarnings && hasErrors;
        }

        /// <summary>
        /// Detects if the anonymous object is a tag update result.
        /// </summary>
        private static bool IsTagUpdateResult(object data)
        {
            if (data == null) return false;

            var type = data.GetType();
            if (!type.Name.Contains("AnonymousType")) return false;

            // Check for signature properties of tag update results
            var hasSummary = type.GetProperty("summary") != null;
            var hasUpdated = type.GetProperty("updated") != null;
            var hasCreated = type.GetProperty("created") != null;
            var hasWarnings = type.GetProperty("warnings") != null;
            var hasErrors = type.GetProperty("errors") != null;

            return hasSummary && hasUpdated && hasCreated && hasWarnings && hasErrors;
        }

        /// <summary>
        /// Detects if the anonymous object is an import result.
        /// </summary>
        private static bool IsImportResult(object data)
        {
            if (data == null) return false;

            var type = data.GetType();
            if (!type.Name.Contains("AnonymousType")) return false;

            // Check for signature properties of import results
            var hasTotalCount = type.GetProperty("TotalCount") != null;
            var hasSuccessCount = type.GetProperty("SuccessCount") != null;
            var hasFailedCount = type.GetProperty("FailedCount") != null;
            var hasImportedItems = type.GetProperty("ImportedItems") != null;
            var hasFailedItems = type.GetProperty("FailedItems") != null;
            var hasErrors = type.GetProperty("Errors") != null;

            return hasTotalCount && hasSuccessCount && hasFailedCount;
        }

        /// <summary>
        /// Formats a find-replace result object.
        /// </summary>
        private static string FormatFindReplaceResult(object data)
        {
            var lines = new List<string>();

            var summaryProp = data.GetType().GetProperty("summary");
            var changesProp = data.GetType().GetProperty("changes");
            var skippedProp = data.GetType().GetProperty("skipped");
            var warningsProp = data.GetType().GetProperty("warnings");
            var errorsProp = data.GetType().GetProperty("errors");

            var summary = summaryProp?.GetValue(data);
            var changes = changesProp?.GetValue(data);
            var skipped = skippedProp?.GetValue(data);
            var warnings = warningsProp?.GetValue(data);
            var errors = errorsProp?.GetValue(data);

            // Format summary
            if (summary != null)
            {
                var totalProcessed = summary.GetType().GetProperty("TotalProcessed")?.GetValue(summary);
                var matchesFound = summary.GetType().GetProperty("MatchesFound")?.GetValue(summary);
                var changesApplied = summary.GetType().GetProperty("ChangesApplied")?.GetValue(summary);
                var skippedCount = summary.GetType().GetProperty("Skipped")?.GetValue(summary);
                var warningsCount = summary.GetType().GetProperty("Warnings")?.GetValue(summary);
                var errorsCount = summary.GetType().GetProperty("Errors")?.GetValue(summary);

                lines.Add($"Summary:");
                lines.Add($"  Total Processed: {totalProcessed ?? 0}");
                lines.Add($"  Matches Found: {matchesFound ?? 0}");
                lines.Add($"  Changes Applied: {changesApplied ?? 0}");
                lines.Add($"  Skipped: {skippedCount ?? 0}");
                lines.Add($"  Warnings: {warningsCount ?? 0}");
                lines.Add($"  Errors: {errorsCount ?? 0}");
            }

            // Format changes
            if (changes != null && changes.GetType().IsGenericType && changes.GetType().GetGenericTypeDefinition() == typeof(List<>))
            {
                var changeList = (System.Collections.IList)changes;
                if (changeList.Count > 0)
                {
                    lines.Add("");
                    lines.Add("Changes:");
                    foreach (var change in changeList)
                    {
                        if (change != null)
                        {
                            var path = change.GetType().GetProperty("Path")?.GetValue(change)?.ToString() ?? "";
                            var before = change.GetType().GetProperty("Before")?.GetValue(change)?.ToString() ?? "";
                            var after = change.GetType().GetProperty("After")?.GetValue(change)?.ToString() ?? "";
                            lines.Add($"  - {path}: {before} -> {after}");
                        }
                    }
                }
            }

            // Format skipped items
            if (skipped != null && skipped.GetType().IsGenericType && skipped.GetType().GetGenericTypeDefinition() == typeof(List<>))
            {
                var skippedList = (System.Collections.IList)skipped;
                if (skippedList.Count > 0)
                {
                    lines.Add("");
                    lines.Add("Skipped:");
                    foreach (var item in skippedList)
                    {
                        if (item != null)
                        {
                            var path = item.GetType().GetProperty("Path")?.GetValue(item)?.ToString() ?? "";
                            var name = item.GetType().GetProperty("Name")?.GetValue(item)?.ToString() ?? "";
                            var reason = item.GetType().GetProperty("Reason")?.GetValue(item)?.ToString() ?? "";
                            lines.Add($"  - {path}.{name}: {reason}");
                        }
                    }
                }
            }

            // Format warnings
            if (warnings != null && warnings.GetType().IsGenericType && warnings.GetType().GetGenericTypeDefinition() == typeof(List<>))
            {
                var warningsList = (System.Collections.IList)warnings;
                if (warningsList.Count > 0)
                {
                    lines.Add("");
                    lines.Add("Warnings:");
                    foreach (var warning in warningsList)
                    {
                        if (warning != null)
                        {
                            var code = warning.GetType().GetProperty("Code")?.GetValue(warning)?.ToString() ?? "";
                            var message = warning.GetType().GetProperty("Message")?.GetValue(warning)?.ToString() ?? "";
                            var path = warning.GetType().GetProperty("Path")?.GetValue(warning)?.ToString() ?? "";
                            if (!string.IsNullOrEmpty(code))
                            {
                                lines.Add($"  - {code}: {message} at {path}");
                            }
                            else
                            {
                                lines.Add($"  - {message}");
                            }
                        }
                    }
                }
            }

            // Format errors
            if (errors != null && errors.GetType().IsGenericType && errors.GetType().GetGenericTypeDefinition() == typeof(List<>))
            {
                var errorsList = (System.Collections.IList)errors;
                if (errorsList.Count > 0)
                {
                    lines.Add("");
                    lines.Add("Errors:");
                    foreach (var error in errorsList)
                    {
                        if (error != null)
                        {
                            var path = error.GetType().GetProperty("Path")?.GetValue(error)?.ToString() ?? "";
                            var name = error.GetType().GetProperty("Name")?.GetValue(error)?.ToString() ?? "";
                            var errorMessage = error.GetType().GetProperty("Error")?.GetValue(error)?.ToString() ?? "";
                            var details = error.GetType().GetProperty("Details")?.GetValue(error)?.ToString() ?? "";
                            lines.Add($"  - {path}.{name}: {errorMessage}");
                            if (!string.IsNullOrEmpty(details))
                            {
                                lines.Add($"    Details: {details}");
                            }
                        }
                    }
                }
            }

            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>
        /// Formats a tag update result object.
        /// </summary>
        private static string FormatTagUpdateResult(object data)
        {
            var lines = new List<string>();

            var summaryProp = data.GetType().GetProperty("summary");
            var updatedProp = data.GetType().GetProperty("updated");
            var createdProp = data.GetType().GetProperty("created");
            var deletedProp = data.GetType().GetProperty("deleted");
            var warningsProp = data.GetType().GetProperty("warnings");
            var errorsProp = data.GetType().GetProperty("errors");

            var summary = summaryProp?.GetValue(data);
            var updated = updatedProp?.GetValue(data);
            var created = createdProp?.GetValue(data);
            var deleted = deletedProp?.GetValue(data);
            var warnings = warningsProp?.GetValue(data);
            var errors = errorsProp?.GetValue(data);

            // Format summary
            if (summary != null)
            {
                var totalCsvRows = summary.GetType().GetProperty("TotalCsvRows")?.GetValue(summary);
                var existingTagsScanned = summary.GetType().GetProperty("ExistingTagsScanned")?.GetValue(summary);
                var tagsUpdated = summary.GetType().GetProperty("TagsUpdated")?.GetValue(summary);
                var tagsCreated = summary.GetType().GetProperty("TagsCreated")?.GetValue(summary);
                var tagsDeleted = summary.GetType().GetProperty("TagsDeleted")?.GetValue(summary);
                var tagsSkipped = summary.GetType().GetProperty("TagsSkipped")?.GetValue(summary);

                lines.Add("Summary:");
                lines.Add($"  CSV Rows: {totalCsvRows ?? 0}");
                lines.Add($"  Existing Tags Scanned: {existingTagsScanned ?? 0}");
                lines.Add($"  Tags Updated: {tagsUpdated ?? 0}");
                lines.Add($"  Tags Created: {tagsCreated ?? 0}");
                lines.Add($"  Tags Deleted: {tagsDeleted ?? 0}");
                lines.Add($"  Tags Skipped: {tagsSkipped ?? 0}");
            }

            // Format updated tags
            if (updated != null && updated.GetType().IsGenericType && updated.GetType().GetGenericTypeDefinition() == typeof(List<>))
            {
                var updatedList = (System.Collections.IList)updated;
                if (updatedList.Count > 0)
                {
                    lines.Add("");
                    lines.Add("Updated Tags:");
                    foreach (var entry in updatedList)
                    {
                        if (entry != null)
                        {
                            var tagTableName = entry.GetType().GetProperty("TagTableName")?.GetValue(entry)?.ToString() ?? "";
                            var matchedBy = entry.GetType().GetProperty("MatchedBy")?.GetValue(entry)?.ToString() ?? "";
                            var oldName = entry.GetType().GetProperty("OldName")?.GetValue(entry)?.ToString() ?? "";
                            var newName = entry.GetType().GetProperty("NewName")?.GetValue(entry)?.ToString() ?? "";
                            var oldAddress = entry.GetType().GetProperty("OldAddress")?.GetValue(entry)?.ToString() ?? "";
                            var newAddress = entry.GetType().GetProperty("NewAddress")?.GetValue(entry)?.ToString() ?? "";

                            if (matchedBy == "BaseName" && oldName != newName)
                            {
                                lines.Add($"  - [{tagTableName}] {oldName} -> {newName} ({oldAddress} -> {newAddress})");
                            }
                            else
                            {
                                lines.Add($"  - [{tagTableName}] {newName}: {oldAddress} -> {newAddress}");
                            }
                        }
                    }
                }
            }

            // Format deleted tags
            if (deleted != null && deleted.GetType().IsGenericType && deleted.GetType().GetGenericTypeDefinition() == typeof(List<>))
            {
                var deletedList = (System.Collections.IList)deleted;
                if (deletedList.Count > 0)
                {
                    lines.Add("");
                    lines.Add("Deleted Tags:");
                    foreach (var entry in deletedList)
                    {
                        if (entry != null)
                        {
                            var tagTableName = entry.GetType().GetProperty("TagTableName")?.GetValue(entry)?.ToString() ?? "";
                            var tagName = entry.GetType().GetProperty("TagName")?.GetValue(entry)?.ToString() ?? "";
                            var address = entry.GetType().GetProperty("Address")?.GetValue(entry)?.ToString() ?? "";
                            var reason = entry.GetType().GetProperty("Reason")?.GetValue(entry)?.ToString();
                            var reasonStr = string.IsNullOrEmpty(reason) ? "" : $" - {reason}";
                            lines.Add($"  - [{tagTableName}] {tagName}: {address}{reasonStr}");
                        }
                    }
                }
            }

            // Format created tags
            if (created != null && created.GetType().IsGenericType && created.GetType().GetGenericTypeDefinition() == typeof(List<>))
            {
                var createdList = (System.Collections.IList)created;
                if (createdList.Count > 0)
                {
                    lines.Add("");
                    lines.Add("Created Tags:");
                    foreach (var entry in createdList)
                    {
                        if (entry != null)
                        {
                            var tagTableName = entry.GetType().GetProperty("TagTableName")?.GetValue(entry)?.ToString() ?? "";
                            var tagName = entry.GetType().GetProperty("TagName")?.GetValue(entry)?.ToString() ?? "";
                            var dataType = entry.GetType().GetProperty("DataType")?.GetValue(entry)?.ToString() ?? "";
                            var address = entry.GetType().GetProperty("Address")?.GetValue(entry)?.ToString() ?? "";
                            var comment = entry.GetType().GetProperty("Comment")?.GetValue(entry)?.ToString();
                            var commentStr = string.IsNullOrEmpty(comment) ? "" : $" - {comment}";
                            lines.Add($"  - [{tagTableName}] {tagName} ({dataType}): {address}{commentStr}");
                        }
                    }
                }
            }

            // Format warnings
            if (warnings != null && warnings.GetType().IsGenericType && warnings.GetType().GetGenericTypeDefinition() == typeof(List<>))
            {
                var warningsList = (System.Collections.IList)warnings;
                if (warningsList.Count > 0)
                {
                    lines.Add("");
                    lines.Add("Warnings:");
                    foreach (var warning in warningsList)
                    {
                        if (warning != null)
                        {
                            var item = warning.GetType().GetProperty("Item")?.GetValue(warning)?.ToString() ?? "";
                            var reason = warning.GetType().GetProperty("Reason")?.GetValue(warning)?.ToString() ?? "";
                            var lineNumber = warning.GetType().GetProperty("LineNumber")?.GetValue(warning);
                            var lineStr = lineNumber != null ? $" (line {lineNumber})" : "";
                            lines.Add($"  - {item}{lineStr}: {reason}");
                        }
                    }
                }
            }

            // Format errors
            if (errors != null && errors.GetType().IsGenericType && errors.GetType().GetGenericTypeDefinition() == typeof(List<>))
            {
                var errorsList = (System.Collections.IList)errors;
                if (errorsList.Count > 0)
                {
                    lines.Add("");
                    lines.Add("Errors:");
                    foreach (var error in errorsList)
                    {
                        if (error != null)
                        {
                            var item = error.GetType().GetProperty("Item")?.GetValue(error)?.ToString() ?? "";
                            var errorMessage = error.GetType().GetProperty("Error")?.GetValue(error)?.ToString() ?? "";
                            var details = error.GetType().GetProperty("Details")?.GetValue(error)?.ToString();
                            var lineNumber = error.GetType().GetProperty("LineNumber")?.GetValue(error);
                            var lineStr = lineNumber != null ? $" (line {lineNumber})" : "";
                            lines.Add($"  - {item}{lineStr}: {errorMessage}");
                            if (!string.IsNullOrEmpty(details))
                            {
                                lines.Add($"    Details: {details}");
                            }
                        }
                    }
                }
            }

            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>
        /// Formats an import result object.
        /// </summary>
        private static string FormatImportResult(object data)
        {
            var lines = new List<string>();

            var totalCountProp = data.GetType().GetProperty("TotalCount");
            var successCountProp = data.GetType().GetProperty("SuccessCount");
            var failedCountProp = data.GetType().GetProperty("FailedCount");
            var importedItemsProp = data.GetType().GetProperty("ImportedItems");
            var failedItemsProp = data.GetType().GetProperty("FailedItems");
            var errorsProp = data.GetType().GetProperty("Errors");

            var totalCount = totalCountProp?.GetValue(data);
            var successCount = successCountProp?.GetValue(data);
            var failedCount = failedCountProp?.GetValue(data);
            var importedItems = importedItemsProp?.GetValue(data);
            var failedItems = failedItemsProp?.GetValue(data);
            var errors = errorsProp?.GetValue(data);

            // Format summary
            lines.Add("Summary:");
            lines.Add($"  Total Items: {totalCount ?? 0}");
            lines.Add($"  Imported: {successCount ?? 0}");
            lines.Add($"  Failed: {failedCount ?? 0}");

            // Format imported items
            if (importedItems != null && importedItems.GetType().IsGenericType && importedItems.GetType().GetGenericTypeDefinition() == typeof(List<>))
            {
                var importedList = (System.Collections.IList)importedItems;
                if (importedList.Count > 0)
                {
                    lines.Add("");
                    lines.Add("Imported Items:");
                    foreach (var item in importedList)
                    {
                        if (item != null)
                        {
                            lines.Add($"  - {item}");
                        }
                    }
                }
            }

            // Format failed items
            if (failedItems != null && failedItems.GetType().IsGenericType && failedItems.GetType().GetGenericTypeDefinition() == typeof(List<>))
            {
                var failedList = (System.Collections.IList)failedItems;
                if (failedList.Count > 0)
                {
                    lines.Add("");
                    lines.Add("Failed Items:");
                    foreach (var item in failedList)
                    {
                        if (item != null)
                        {
                            lines.Add($"  - {item}");
                        }
                    }
                }
            }

            // Format errors
            if (errors != null && errors.GetType().IsGenericType && errors.GetType().GetGenericTypeDefinition() == typeof(List<>))
            {
                var errorsList = (System.Collections.IList)errors;
                if (errorsList.Count > 0)
                {
                    lines.Add("");
                    lines.Add("Errors:");
                    foreach (var error in errorsList)
                    {
                        if (error != null)
                        {
                            lines.Add($"  - {error}");
                        }
                    }
                }
            }

            return string.Join(Environment.NewLine, lines);
        }
    }
}
