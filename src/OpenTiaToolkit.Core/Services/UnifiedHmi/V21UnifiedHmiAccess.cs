using OpenTiaToolkit.Core.Models;
using Microsoft.Extensions.Logging;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.HmiLogging;
using Siemens.Engineering.HmiUnified.HmiLogging.HmiLoggingCommon;
using Siemens.Engineering.HmiUnified.HmiTags;
using Siemens.Engineering.HmiUnified.LoggingTags;
using Siemens.Engineering.HmiUnified.UI.Base;
using Siemens.Engineering.HmiUnified.UI.Dynamization;
using Siemens.Engineering.HmiUnified.UI.Dynamization.Flashing;
using Siemens.Engineering.HmiUnified.UI.Dynamization.Script;
using Siemens.Engineering.HmiUnified.UI.Dynamization.Tag;
using Siemens.Engineering.HmiUnified.UI.Controls;
using Siemens.Engineering.HmiUnified.UI.ScreenGroup;
using Siemens.Engineering.HmiUnified.UI.Screens;
using Siemens.Engineering.HmiUnified.UI.Shapes;
using Siemens.Engineering.HmiUnified.UI.Widgets;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace OpenTiaToolkit.Core.Services
{
    internal sealed class V21UnifiedHmiAccess
    {
        private readonly V21HmiEngineeringPrimitives _primitives;
        private readonly V21HmiItemPropertyAccessor _properties;

        internal V21UnifiedHmiAccess(
            V21HmiEngineeringPrimitives primitives,
            V21HmiItemPropertyAccessor properties)
        {
            _primitives = primitives ?? throw new ArgumentNullException(nameof(primitives));
            _properties = properties ?? throw new ArgumentNullException(nameof(properties));
        }

        public string NormalizeScreenPath(string? screenPath) => V21HmiScreenInventory.NormalizeScreenPath(screenPath);

        public string NormalizeItemTypeName(string? itemType)
        {
            if (string.IsNullOrWhiteSpace(itemType))
            {
                return string.Empty;
            }

            var normalized = itemType!.Contains(".")
                ? itemType.Split('.').Last()
                : itemType.Trim();

            return NormalizeToken(normalized);
        }

        public string NormalizeTagPath(string? tagPath)
        {
            if (string.IsNullOrWhiteSpace(tagPath))
            {
                return string.Empty;
            }

            return string.Join(
                ".",
                tagPath!
                    .Split(new[] { '.', '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(segment => segment.Trim())
                    .Where(segment => !string.IsNullOrWhiteSpace(segment)));
        }

        private static string NormalizeGroupPath(string? groupPath)
        {
            if (string.IsNullOrWhiteSpace(groupPath))
            {
                return string.Empty;
            }

            return string.Join(
                "/",
                groupPath!
                    .Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(segment => segment.Trim())
                    .Where(segment => !string.IsNullOrWhiteSpace(segment)));
        }

        private static string CombinePath(string prefix, string leaf)
        {
            return string.IsNullOrWhiteSpace(prefix) ? leaf : prefix + "/" + leaf;
        }

        private static string NormalizeToken(string value)
        {
            return value.Replace(" ", string.Empty)
                .Replace("-", string.Empty)
                .Replace("_", string.Empty)
                .ToLowerInvariant();
        }

        public bool TryResolveUnifiedHmi(ProjectBase project, string hmiName, out Device? matchedDevice, out DeviceItem? matchedDeviceItem, out HmiSoftware? software)
        {
            matchedDevice = null;
            matchedDeviceItem = null;
            software = null;

            foreach (var device in project.Devices)
            {
                if (string.Equals(device.Name, hmiName, StringComparison.OrdinalIgnoreCase))
                {
                    var exactDeviceMatch = FindUnifiedHmiInDevice(device, null);
                    if (exactDeviceMatch.Software != null)
                    {
                        matchedDevice = device;
                        matchedDeviceItem = exactDeviceMatch.DeviceItem;
                        software = exactDeviceMatch.Software;
                        return true;
                    }
                }

                var recursiveMatch = FindUnifiedHmiInDevice(device, hmiName);
                if (recursiveMatch.Software != null)
                {
                    matchedDevice = device;
                    matchedDeviceItem = recursiveMatch.DeviceItem;
                    software = recursiveMatch.Software;
                    return true;
                }
            }

            return false;
        }

        public IReadOnlyList<HmiScreenInfo> ListScreens(HmiSoftware software, string? groupPath, bool recursive) =>
            V21HmiScreenInventory.ListScreens(software, groupPath, recursive);

        public IReadOnlyList<string> ListScreenPaths(HmiSoftware software) => V21HmiScreenInventory.ListScreenPaths(software);

        public bool TryGetScreen(HmiSoftware software, string screenPath, out HmiScreen? screen, out string normalizedPath)
        {
            screen = null;
            normalizedPath = NormalizeScreenPath(screenPath);
            if (string.IsNullOrEmpty(normalizedPath))
            {
                return false;
            }

            var segments = normalizedPath.Split('/');
            var screenName = segments.Last();
            var groupSegments = segments.Take(segments.Length - 1).ToArray();

            if (groupSegments.Length == 0)
            {
                screen = software.Screens.Find(screenName);
                return screen != null;
            }

            var group = TryGetGroup(software, groupSegments);
            if (group == null)
            {
                return false;
            }

            screen = group.Screens.Find(screenName);
            return screen != null;
        }

        public HmiScreen? EnsureScreen(HmiSoftware software, string screenPath)
        {
            var normalizedPath = NormalizeScreenPath(screenPath);
            if (string.IsNullOrEmpty(normalizedPath))
            {
                return null;
            }

            var segments = normalizedPath.Split('/');
            var screenName = segments.Last();
            var groupSegments = segments.Take(segments.Length - 1).ToArray();

            if (groupSegments.Length == 0)
            {
                return software.Screens.Find(screenName) ?? software.Screens.Create(screenName);
            }

            var group = EnsureGroup(software, groupSegments);
            return group?.Screens.Find(screenName) ?? group?.Screens.Create(screenName);
        }

        public IReadOnlyList<HmiItemTypeInfo> ListSupportedItemTypes(string? filter)
        {
            var normalizedFilter = string.IsNullOrWhiteSpace(filter)
                ? null
                : filter!.Trim();

            return _primitives.ItemTypes.Values
                .Where(descriptor =>
                    normalizedFilter == null ||
                    descriptor.TypeName.IndexOf(normalizedFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    descriptor.Category.IndexOf(normalizedFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    descriptor.Description.IndexOf(normalizedFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    descriptor.Aliases.Any(alias => alias.IndexOf(normalizedFilter, StringComparison.OrdinalIgnoreCase) >= 0))
                .GroupBy(descriptor => descriptor.TypeName, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First().ToPublicInfo())
                .OrderBy(info => info.Type, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public IReadOnlyList<HmiTagInfo> ListTags(HmiSoftware software, string? filter, string? rootPath = null)
        {
            return ListTags(software, new HmiTagBrowseOptions
            {
                Filter = filter,
                RootPath = rootPath
            });
        }

        public IReadOnlyList<HmiTagInfo> ListTags(HmiSoftware software, HmiTagBrowseOptions? options)
        {
            options = options ?? new HmiTagBrowseOptions();
            var normalizedFilter = string.IsNullOrWhiteSpace(options.Filter)
                ? null
                : options.Filter!.Trim();
            var maxResults = options.MaxResults.GetValueOrDefault(0) > 0
                ? options.MaxResults!.Value
                : int.MaxValue;
            var maxDepth = options.MaxDepth.GetValueOrDefault(-1) >= 0
                ? options.MaxDepth!.Value
                : int.MaxValue;
            var normalizedRootPath = NormalizeTagPath(options.RootPath);
            var results = new List<HmiTagInfo>();

            if (string.IsNullOrWhiteSpace(normalizedRootPath))
            {
                CollectTags(software.Tags, string.Empty, normalizedFilter, options, maxResults, maxDepth, 0, results);
            }
            else if (TryGetTag(software, normalizedRootPath, out var rootTag, out var resolvedRootPath) && rootTag != null)
            {
                CollectTag(rootTag, resolvedRootPath, normalizedFilter, options, maxResults, maxDepth, 0, results);
            }

            return results
                .OrderBy(tag => tag.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public bool TryGetTag(HmiSoftware software, string tagPath, out HmiTag? tag, out string normalizedPath)
        {
            tag = null;
            normalizedPath = NormalizeTagPath(tagPath);
            if (string.IsNullOrEmpty(normalizedPath))
            {
                return false;
            }

            var segments = normalizedPath.Split('.');
            tag = software.Tags.Find(segments[0]);
            if (tag == null)
            {
                return false;
            }

            for (int index = 1; index < segments.Length; index++)
            {
                tag = tag.Members.Find(segments[index]);
                if (tag == null)
                {
                    return false;
                }
            }

            return true;
        }

        public HmiScreenItemBase? CreateScreenItem(HmiScreen screen, string itemType, string itemName)
        {
            if (!_properties.TryResolveItemType(itemType, out var descriptor))
            {
                _primitives.Logger?.LogWarning("Unsupported Unified HMI item type: {ItemType}", itemType);
                return null;
            }

            var genericCreate = V21HmiEngineeringPrimitives.CreateScreenItemGenericMethod.MakeGenericMethod(descriptor.ItemType);
            var created = genericCreate.Invoke(screen.ScreenItems, new object[] { itemName });
            return created as HmiScreenItemBase;
        }

        public HmiDataLog EnsureDataLog(HmiSoftware software, string dataLogName, out bool created)
        {
            var existing = software.DataLogs.Find(dataLogName);
            if (existing != null)
            {
                created = false;
                return existing;
            }

            created = true;
            return software.DataLogs.Create(dataLogName);
        }

        public HmiDataLog? FindDataLog(HmiSoftware software, string dataLogName)
        {
            return software.DataLogs.Find(dataLogName);
        }

        public void DeleteDataLog(HmiDataLog dataLog)
        {
            dataLog.Delete();
        }

        public void ConfigureDataLog(HmiDataLog dataLog, int logMaxSize, int durationDays, int segmentMaxSize)
        {
            dataLog.Settings.LogMaxSize = (uint)Math.Max(1, logMaxSize);
            dataLog.Settings.LogTimePeriod.Days = (uint)Math.Max(0, durationDays);
            dataLog.Settings.LogTimePeriod.Hours = 0;
            dataLog.Settings.LogTimePeriod.Minutes = 0;
            dataLog.Settings.LogTimePeriod.Seconds = 0;
            dataLog.Settings.LogTimePeriod.Ticks = 0;
            dataLog.Segment.SegmentMaxSize = (uint)Math.Max(1, segmentMaxSize);
            dataLog.Segment.SegmentStartTime = DateTime.Today;
        }

        public string GetLoggingTagName(string tagPath)
        {
            return NormalizeTagPath(tagPath).Replace(".", "_");
        }

        public HmiLoggingTag EnsureLoggingTag(HmiTag tag, string loggingTagName, out bool created)
        {
            var existing = tag.LoggingTags.Find(loggingTagName);
            if (existing != null)
            {
                created = false;
                return existing;
            }

            created = true;
            return tag.LoggingTags.Create(loggingTagName);
        }

        public HmiLoggingTag? FindLoggingTag(HmiTag tag, string loggingTagName)
        {
            return tag.LoggingTags.Find(loggingTagName);
        }

        public void DeleteLoggingTag(HmiLoggingTag loggingTag)
        {
            loggingTag.Delete();
        }

        public IReadOnlyList<HmiLoggingTagReferenceInfo> ListLoggingTagReferences(HmiSoftware software, string dataLogName)
        {
            return ListLoggingTagReferences(
                software,
                new HashSet<string>(new[] { dataLogName }, StringComparer.OrdinalIgnoreCase));
        }

        public IReadOnlyList<HmiLoggingTagReferenceInfo> ListLoggingTagReferences(
            HmiSoftware software,
            ISet<string> dataLogNames)
        {
            var results = new List<HmiLoggingTagReferenceInfo>();
            CollectLoggingTagReferences(software.Tags, string.Empty, dataLogNames, results);
            return results
                .OrderBy(reference => reference.TagPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(reference => reference.LoggingTagName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public void ConfigureLoggingTag(HmiLoggingTag loggingTag, string dataLogName, HmiLoggingMode loggingMode, string? cycle)
        {
            loggingTag.DataLog = dataLogName;
            loggingTag.LoggingMode = loggingMode;

            if (loggingMode == HmiLoggingMode.Cyclic)
            {
                loggingTag.Cycle = string.IsNullOrWhiteSpace(cycle) ? "T5s" : cycle!.Trim();
            }
        }

        public bool TryApplyGeometry(HmiScreenItemBase item, int? left, int? top, int? width, int? height, out string? error)
        {
            error = null;

            try
            {
                if (left.HasValue)
                {
                    V21HmiEngineeringPrimitives.SetAttributeValue(item, "Left", left.Value);
                }

                if (top.HasValue)
                {
                    V21HmiEngineeringPrimitives.SetAttributeValue(item, "Top", top.Value);
                }

                if (width.HasValue)
                {
                    V21HmiEngineeringPrimitives.SetAttributeValue(item, "Width", (uint)Math.Max(0, width.Value));
                }

                if (height.HasValue)
                {
                    V21HmiEngineeringPrimitives.SetAttributeValue(item, "Height", (uint)Math.Max(0, height.Value));
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public HmiScreenInfo ToScreenInfo(HmiScreen screen, string path, bool includeItems) =>
            V21HmiScreenInventory.ToScreenInfo(screen, path, includeItems);

        public HmiScreenItemInfo ToItemInfo(HmiScreenItemBase item, string screenPath) =>
            V21HmiScreenInventory.ToItemInfo(item, screenPath);

        public HmiTrendInfo ToTrendInfo(
            HmiTag tag,
            string normalizedTagPath,
            HmiLoggingTag loggingTag,
            HmiDataLog dataLog,
            bool dataLogCreated,
            bool loggingTagCreated)
        {
            return new HmiTrendInfo
            {
                TagPath = normalizedTagPath,
                TagName = tag.Name,
                LoggingTagName = loggingTag.Name,
                DataLogName = dataLog.Name,
                LoggingMode = loggingTag.LoggingMode.ToString(),
                Cycle = string.IsNullOrWhiteSpace(loggingTag.Cycle) ? null : loggingTag.Cycle,
                StorageFolder = dataLog.Settings.StorageFolder ?? string.Empty,
                LogMaxSize = (int)dataLog.Settings.LogMaxSize,
                DurationDays = (int)dataLog.Settings.LogTimePeriod.Days,
                SegmentMaxSize = (int)dataLog.Segment.SegmentMaxSize,
                DataLogCreated = dataLogCreated,
                LoggingTagCreated = loggingTagCreated
            };
        }

        private void CollectTags(
            HmiTagComposition tags,
            string prefix,
            string? filter,
            HmiTagBrowseOptions options,
            int maxResults,
            int maxDepth,
            int depth,
            List<HmiTagInfo> results)
        {
            foreach (var tag in SnapshotTags(tags))
            {
                if (results.Count >= maxResults)
                {
                    return;
                }

                var path = string.IsNullOrWhiteSpace(prefix) ? tag.Name : prefix + "." + tag.Name;
                CollectTag(tag, path, filter, options, maxResults, maxDepth, depth, results);
            }
        }

        private void CollectTag(
            HmiTag tag,
            string path,
            string? filter,
            HmiTagBrowseOptions options,
            int maxResults,
            int maxDepth,
            int depth,
            List<HmiTagInfo> results)
        {
            if (results.Count >= maxResults)
            {
                return;
            }

            var pathMatches = filter == null ||
                path.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                tag.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;

            var info = new HmiTagInfo
            {
                Path = path,
                Name = tag.Name
            };

            if (options.IncludeDetails)
            {
                info.DisplayName = V21HmiEngineeringPrimitives.FormatValue(tag.DisplayName);
                info.DataType = tag.DataType?.ToString();
                info.HmiDataType = tag.HmiDataType?.ToString();
                info.TagTableName = tag.TagTableName;
                info.HasMembers = tag.Members.Count > 0;
                info.MemberCount = tag.Members.Count;
            }

            if (options.IncludeLoggingCounts)
            {
                info.LoggingTagCount = tag.LoggingTags.Count;
            }

            var detailMatches = filter != null &&
                ((info.DisplayName?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) ||
                 (info.DataType?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) ||
                 (info.HmiDataType?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) ||
                 (info.TagTableName?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0));

            if (pathMatches || detailMatches)
            {
                results.Add(info);
            }

            if (results.Count < maxResults && depth < maxDepth && tag.Members.Count > 0)
            {
                CollectTags(tag.Members, path, filter, options, maxResults, maxDepth, depth + 1, results);
            }
        }

        private static List<HmiTag> SnapshotTags(HmiTagComposition tags)
        {
            var snapshot = new List<HmiTag>();
            foreach (var tag in tags)
            {
                snapshot.Add(tag);
            }

            return snapshot;
        }

        private static void CollectLoggingTagReferences(
            HmiTagComposition tags,
            string prefix,
            ISet<string> dataLogNames,
            List<HmiLoggingTagReferenceInfo> results)
        {
            foreach (var tag in tags)
            {
                var path = string.IsNullOrWhiteSpace(prefix) ? tag.Name : prefix + "." + tag.Name;
                foreach (var loggingTag in tag.LoggingTags)
                {
                    if (dataLogNames.Contains(loggingTag.DataLog))
                    {
                        results.Add(new HmiLoggingTagReferenceInfo
                        {
                            TagPath = path,
                            LoggingTagName = loggingTag.Name,
                            DataLogName = loggingTag.DataLog
                        });
                    }
                }

                if (tag.Members.Count > 0)
                {
                    CollectLoggingTagReferences(tag.Members, path, dataLogNames, results);
                }
            }
        }

        private static HmiScreenGroup? TryGetGroup(HmiSoftware software, IEnumerable<string> segments) =>
            V21HmiScreenInventory.FindGroup(software, segments);

        private static HmiScreenGroup? EnsureGroup(HmiSoftware software, IEnumerable<string> segments)
        {
            HmiScreenGroup? currentGroup = null;

            foreach (var segment in segments)
            {
                currentGroup = currentGroup == null
                    ? software.ScreenGroups.Find(segment) ?? software.ScreenGroups.Create(segment)
                    : currentGroup.Groups.Find(segment) ?? currentGroup.Groups.Create(segment);
            }

            return currentGroup;
        }

        private static (DeviceItem? DeviceItem, HmiSoftware? Software) FindUnifiedHmiInDevice(Device device, string? requestedName)
        {
            foreach (DeviceItem deviceItem in device.DeviceItems)
            {
                var resolved = FindUnifiedHmiRecursive(deviceItem, requestedName);
                if (resolved.Software != null)
                {
                    return resolved;
                }
            }

            return default;
        }

        private static (DeviceItem? DeviceItem, HmiSoftware? Software) FindUnifiedHmiRecursive(DeviceItem deviceItem, string? requestedName)
        {
            if (string.IsNullOrWhiteSpace(requestedName) ||
                string.Equals(deviceItem.Name, requestedName, StringComparison.OrdinalIgnoreCase))
            {
                var softwareContainer = deviceItem.GetService<SoftwareContainer>();
                if (softwareContainer?.Software is HmiSoftware containerSoftware)
                {
                    return (deviceItem, containerSoftware);
                }
            }

            foreach (DeviceItem child in deviceItem.DeviceItems)
            {
                var resolved = FindUnifiedHmiRecursive(child, requestedName);
                if (resolved.Software != null)
                {
                    return resolved;
                }
            }

            return default;
        }

    }
}
