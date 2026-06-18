using OpenTiaToolkit.Core.Models;
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
                    HmiTrendBulkResult trendBulk => FormatHmiTrendBulkResult(trendBulk),
                    TagUpdateCommandResult tagUpdate => FormatTagUpdateResult(tagUpdate),
                    TagUpdateResult tagUpdate => FormatTagUpdateResult(tagUpdate),
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
                HmiTrendBulkResult trendBulk => FormatHmiTrendBulkResult(trendBulk),
                CommandArtifactResult artifact => FormatCommandArtifactResult(artifact),
                HmiJsonExportResult jsonExport => FormatHmiJsonExportResult(jsonExport),
                HmiJsonDiffResult jsonDiff => FormatHmiJsonDiffResult(jsonDiff),
                HmiJsonImportResult jsonImport => FormatHmiJsonImportResult(jsonImport),
                IEnumerable<BlockInfo> blocks => FormatBlocks(blocks),
                BlockInfo block => FormatBlockInfo(block),
                BlockInfoDetail blockDetail => FormatBlockInfoDetail(blockDetail),
                FindReplaceCommandResult findReplace => FormatFindReplaceResult(findReplace),
                FindReplaceResult findReplace => FormatFindReplaceResult(findReplace),
                TagUpdateCommandResult tagUpdate => FormatTagUpdateResult(tagUpdate),
                TagUpdateResult tagUpdate => FormatTagUpdateResult(tagUpdate),
                PlcExportCommandResult export => FormatExportResult(export),
                ExportResult export => FormatExportResult(export),
                PlcImportCommandResult import => FormatImportResult(import),
                ImportResult import => FormatImportResult(import),
                // Anonymous objects (status objects, etc.)
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

        private static string FormatCommandArtifactResult(CommandArtifactResult artifact)
        {
            var lines = new List<string>
            {
                "Command Artifact:",
                "",
                $"Output Path:   {artifact.OutputPath}",
                $"Format:        {artifact.Format}",
                $"Data Kind:     {artifact.DataKind}",
                $"Bytes Written: {artifact.BytesWritten}",
                $"Inline Data:   {(artifact.InlineOutputSuppressed ? "Suppressed" : "Included")}"
            };

            if (artifact.ItemCount.HasValue)
            {
                lines.Add($"Item Count:    {artifact.ItemCount.Value}");
            }

            if (!string.IsNullOrWhiteSpace(artifact.Reason))
            {
                lines.Add($"Reason:        {artifact.Reason}");
            }

            if (artifact.Columns.Count > 0)
            {
                lines.Add($"Columns:       {string.Join(", ", artifact.Columns)}");
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

            TreeTextFormatter.Format(
                tree,
                lines,
                node => node.Name,
                node => node.Type,
                node => node.Children);

            return string.Join(Environment.NewLine, lines);
        }

        private static string FormatSoftwareTree(SoftwareTreeNode tree)
        {
            var lines = new List<string>
            {
                "PLC Software Tree:",
                ""
            };

            TreeTextFormatter.Format(
                tree,
                lines,
                node => node.Name,
                node => node.Type,
                node => node.Children);

            return string.Join(Environment.NewLine, lines);
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
                var confirmed = plc.IsConfirmedPlc ? "Yes" : "No";

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

        private static string FormatHmiTrendBulkResult(HmiTrendBulkResult result)
        {
            var lines = new List<string>
            {
                "Unified HMI Trend Batch:",
                "",
                $"Operation: {result.Operation}",
                $"Dry Run:   {(result.DryRun ? "Yes" : "No")}",
                $"Requested: {result.RequestedCount}",
                $"Applied:   {result.AppliedCount}",
                $"Planned:   {result.PlannedCount}",
                $"Blocked:   {result.BlockedCount}",
                $"Not Found: {result.NotFoundCount}",
                $"Failed:    {result.FailedCount}",
                ""
            };

            foreach (var item in result.Results)
            {
                var target = string.IsNullOrWhiteSpace(item.TagPath)
                    ? item.DataLogName
                    : $"{item.TagPath} -> {item.DataLogName}";
                lines.Add($"[{item.Status}] {target}");
                if (!string.IsNullOrWhiteSpace(item.Message))
                {
                    lines.Add($"  {item.Message}");
                }

                foreach (var reference in item.References)
                {
                    lines.Add($"  Referenced by: {reference.TagPath} / {reference.LoggingTagName}");
                }
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
                $"Blocked Reason:        {importResult.BlockedReason ?? "(none)"}",
                $"Transaction Used:      {(importResult.TransactionUsed ? "Yes" : "No")}",
                $"Rolled Back:           {(importResult.RolledBack ? "Yes" : "No")}",
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
        /// Formats a find-replace result object.
        /// </summary>
        private static string FormatFindReplaceResult(FindReplaceResult result)
        {
            return FormatFindReplaceResult(FindReplaceCommandResult.From(result));
        }

        private static string FormatFindReplaceResult(FindReplaceCommandResult result)
        {
            var lines = new List<string>();
            var summary = result.Summary;

            lines.Add("Summary:");
            lines.Add($"  Total Processed: {summary.TotalProcessed}");
            lines.Add($"  Matches Found: {summary.MatchesFound}");
            lines.Add($"  Changes Applied: {summary.ChangesApplied}");
            lines.Add($"  Skipped: {summary.Skipped}");
            lines.Add($"  Warnings: {summary.Warnings}");
            lines.Add($"  Errors: {summary.Errors}");

            if (result.Changes.Count > 0)
            {
                lines.Add("");
                lines.Add("Changes:");
                foreach (var change in result.Changes)
                {
                    lines.Add($"  - {change.Path ?? string.Empty}: {change.Before ?? string.Empty} -> {change.After ?? string.Empty}");
                }
            }

            if (result.Skipped.Count > 0)
            {
                lines.Add("");
                lines.Add("Skipped:");
                foreach (var item in result.Skipped)
                {
                    lines.Add($"  - {item.Path ?? string.Empty}.{item.Name ?? string.Empty}: {item.Reason ?? string.Empty}");
                }
            }

            if (result.Warnings.Count > 0)
            {
                lines.Add("");
                lines.Add("Warnings:");
                foreach (var warning in result.Warnings)
                {
                    if (!string.IsNullOrEmpty(warning.Code))
                    {
                        lines.Add($"  - {warning.Code}: {warning.Message ?? string.Empty} at {warning.Path ?? string.Empty}");
                    }
                    else
                    {
                        lines.Add($"  - {warning.Message ?? string.Empty}");
                    }
                }
            }

            if (result.Errors.Count > 0)
            {
                lines.Add("");
                lines.Add("Errors:");
                foreach (var error in result.Errors)
                {
                    lines.Add($"  - {error.Path ?? string.Empty}.{error.Name ?? string.Empty}: {error.Error ?? string.Empty}");
                    if (!string.IsNullOrEmpty(error.Details))
                    {
                        lines.Add($"    Details: {error.Details}");
                    }
                }
            }

            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>
        /// Formats a tag update result object.
        /// </summary>
        private static string FormatTagUpdateResult(TagUpdateResult result)
        {
            return FormatTagUpdateResult(TagUpdateCommandResult.From(result));
        }

        private static string FormatTagUpdateResult(TagUpdateCommandResult result)
        {
            var lines = new List<string>();
            var summary = result.Summary;

            lines.Add("Summary:");
            lines.Add($"  CSV Rows: {summary.TotalCsvRows}");
            lines.Add($"  Existing Tags Scanned: {summary.ExistingTagsScanned}");
            lines.Add($"  Tags Updated: {summary.TagsUpdated}");
            lines.Add($"  Tags Created: {summary.TagsCreated}");
            lines.Add($"  Tags Deleted: {summary.TagsDeleted}");
            lines.Add($"  Tags Skipped: {summary.TagsSkipped}");

            if (result.Updated.Count > 0)
            {
                lines.Add("");
                lines.Add("Updated Tags:");
                foreach (var entry in result.Updated)
                {
                    var oldAddress = entry.OldAddress ?? string.Empty;
                    if (entry.MatchedBy == "BaseName" && entry.OldName != entry.NewName)
                    {
                        lines.Add($"  - [{entry.TagTableName}] {entry.OldName} -> {entry.NewName} ({oldAddress} -> {entry.NewAddress})");
                    }
                    else
                    {
                        lines.Add($"  - [{entry.TagTableName}] {entry.NewName}: {oldAddress} -> {entry.NewAddress}");
                    }
                }
            }

            if (result.Deleted.Count > 0)
            {
                lines.Add("");
                lines.Add("Deleted Tags:");
                foreach (var entry in result.Deleted)
                {
                    var reason = string.IsNullOrEmpty(entry.Reason) ? string.Empty : $" - {entry.Reason}";
                    lines.Add($"  - [{entry.TagTableName}] {entry.TagName}: {entry.Address ?? string.Empty}{reason}");
                }
            }

            if (result.Created.Count > 0)
            {
                lines.Add("");
                lines.Add("Created Tags:");
                foreach (var entry in result.Created)
                {
                    var comment = string.IsNullOrEmpty(entry.Comment) ? string.Empty : $" - {entry.Comment}";
                    lines.Add($"  - [{entry.TagTableName}] {entry.TagName} ({entry.DataType}): {entry.Address}{comment}");
                }
            }

            if (result.Warnings.Count > 0)
            {
                lines.Add("");
                lines.Add("Warnings:");
                foreach (var warning in result.Warnings)
                {
                    var line = warning.LineNumber.HasValue ? $" (line {warning.LineNumber.Value})" : string.Empty;
                    lines.Add($"  - {warning.Item}{line}: {warning.Reason}");
                }
            }

            if (result.Errors.Count > 0)
            {
                lines.Add("");
                lines.Add("Errors:");
                foreach (var error in result.Errors)
                {
                    var line = error.LineNumber.HasValue ? $" (line {error.LineNumber.Value})" : string.Empty;
                    lines.Add($"  - {error.Item}{line}: {error.Error}");
                    if (!string.IsNullOrEmpty(error.Details))
                    {
                        lines.Add($"    Details: {error.Details}");
                    }
                }
            }

            return string.Join(Environment.NewLine, lines);
        }

        private static string FormatImportResult(ImportResult result)
        {
            return FormatImportResult(new PlcImportCommandResult
            {
                TotalCount = result.TotalCount,
                SuccessCount = result.SuccessCount,
                FailedCount = result.FailedCount,
                ImportedItems = result.ImportedItems,
                FailedItems = result.FailedItems,
                Errors = result.Errors
            });
        }

        private static string FormatImportResult(PlcImportCommandResult result)
        {
            var lines = new List<string>
            {
                "Summary:",
                $"  Total Items: {result.TotalCount}",
                $"  Imported: {result.SuccessCount}",
                $"  Failed: {result.FailedCount}"
            };

            AddImportList(lines, "Imported Items:", result.ImportedItems);
            AddImportList(lines, "Failed Items:", result.FailedItems);
            AddImportList(lines, "Errors:", result.Errors);

            return string.Join(Environment.NewLine, lines);
        }

        private static string FormatExportResult(ExportResult result)
        {
            return FormatExportResult(PlcExportCommandResult.From(result));
        }

        private static string FormatExportResult(PlcExportCommandResult result)
        {
            var lines = new List<string>
            {
                "Summary:",
                $"  Total Items: {result.Summary.Total}",
                $"  Exported: {result.Summary.Succeeded}",
                $"  Failed: {result.Summary.Failed}"
            };

            AddResultList(lines, "Exported Items:", result.Exported);
            AddResultList(lines, "Failed Items:", result.Failed);
            AddResultList(lines, "Errors:", result.Errors);

            return string.Join(Environment.NewLine, lines);
        }

        private static void AddImportList(List<string> lines, string title, IReadOnlyCollection<string> items)
        {
            AddResultList(lines, title, items);
        }

        private static void AddResultList(List<string> lines, string title, IReadOnlyCollection<string> items)
        {
            if (items.Count == 0)
            {
                return;
            }

            lines.Add("");
            lines.Add(title);
            foreach (var item in items)
            {
                lines.Add($"  - {item}");
            }
        }
    }
}
