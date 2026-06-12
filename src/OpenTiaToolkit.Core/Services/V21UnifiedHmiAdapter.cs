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
    /// <summary>
    /// Centralizes V21 Unified HMI-specific operations and conversions.
    /// </summary>
    internal sealed class V21UnifiedHmiAdapter
    {
        private static readonly MethodInfo CreateScreenItemGenericMethod = typeof(HmiScreenItemBaseComposition)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(method =>
                string.Equals(method.Name, "Create", StringComparison.Ordinal) &&
                method.IsGenericMethodDefinition &&
                method.GetParameters().Length == 1 &&
                method.GetParameters()[0].ParameterType == typeof(string));
        private static readonly Lazy<Dictionary<string, Type>> DynamicItemTypes = new Lazy<Dictionary<string, Type>>(BuildDynamicItemTypeMap);
        private static readonly HashSet<string> ScreenCopyRootPropertyExclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Name",
            "Parent",
            "EventHandlers",
            "ScreenItems"
        };
        private static readonly HashSet<string> ItemCopyRootPropertyExclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Name",
            "Parent",
            "EventHandlers"
        };
        private static readonly HashSet<string> AttributeCopyExclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Name"
        };
        private static readonly HashSet<string> EmptyRootPropertyExclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, HmiItemTypeDescriptor> _itemTypes;
        private readonly ILogger? _logger;

        public V21UnifiedHmiAdapter(ILogger? logger)
        {
            _logger = logger;
            _itemTypes = BuildItemTypeMap();
        }

        public string NormalizeScreenPath(string? screenPath)
        {
            if (string.IsNullOrWhiteSpace(screenPath))
            {
                return string.Empty;
            }

            return string.Join(
                "/",
                screenPath!
                    .Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(segment => segment.Trim())
                    .Where(segment => !string.IsNullOrWhiteSpace(segment)));
        }

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

        public IReadOnlyList<HmiScreenInfo> ListScreens(HmiSoftware software, string? groupPath, bool recursive)
        {
            var normalizedGroupPath = NormalizeGroupPath(groupPath);
            var results = new List<HmiScreenInfo>();

            if (string.IsNullOrEmpty(normalizedGroupPath))
            {
                foreach (var screen in software.Screens)
                {
                    results.Add(ToScreenInfo(screen, screen.Name, includeItems: false));
                }

                foreach (var group in software.ScreenGroups)
                {
                    CollectScreens(group, group.Name, recursive, results);
                }
            }
            else
            {
                var group = TryGetGroup(software, normalizedGroupPath.Split('/'));
                if (group == null)
                {
                    return Array.Empty<HmiScreenInfo>();
                }

                foreach (var screen in group.Screens)
                {
                    results.Add(ToScreenInfo(screen, CombinePath(normalizedGroupPath, screen.Name), includeItems: false));
                }

                if (recursive)
                {
                    foreach (var childGroup in group.Groups)
                    {
                        CollectScreens(childGroup, CombinePath(normalizedGroupPath, childGroup.Name), recursive, results);
                    }
                }
            }

            return results
                .OrderBy(screen => screen.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

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

            return _itemTypes.Values
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
            if (!TryResolveItemType(itemType, out var descriptor))
            {
                _logger?.LogWarning("Unsupported Unified HMI item type: {ItemType}", itemType);
                return null;
            }

            var genericCreate = CreateScreenItemGenericMethod.MakeGenericMethod(descriptor.ItemType);
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
                    SetAttributeValue(item, "Left", left.Value);
                }

                if (top.HasValue)
                {
                    SetAttributeValue(item, "Top", top.Value);
                }

                if (width.HasValue)
                {
                    SetAttributeValue(item, "Width", (uint)Math.Max(0, width.Value));
                }

                if (height.HasValue)
                {
                    SetAttributeValue(item, "Height", (uint)Math.Max(0, height.Value));
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public HmiScreenInfo ToScreenInfo(HmiScreen screen, string path, bool includeItems)
        {
            var items = includeItems
                ? screen.ScreenItems.Cast<HmiScreenItemBase>().Select(item => ToItemInfo(item, path)).ToList()
                : new List<HmiScreenItemInfo>();

            var normalizedPath = NormalizeScreenPath(path);
            var slashIndex = normalizedPath.LastIndexOf('/');

            return new HmiScreenInfo
            {
                Name = screen.Name,
                Path = normalizedPath,
                GroupPath = slashIndex >= 0 ? normalizedPath.Substring(0, slashIndex) : null,
                Width = (int)screen.Width,
                Height = (int)screen.Height,
                Enabled = screen.Enabled,
                ItemCount = screen.ScreenItems.Count,
                Items = items
            };
        }

        public HmiScreenItemInfo ToItemInfo(HmiScreenItemBase item, string screenPath)
        {
            return new HmiScreenItemInfo
            {
                ScreenPath = NormalizeScreenPath(screenPath),
                Name = item.Name,
                Type = item.GetType().Name,
                Left = TryReadIntAttribute(item, "Left"),
                Top = TryReadIntAttribute(item, "Top"),
                Width = TryReadIntAttribute(item, "Width"),
                Height = TryReadIntAttribute(item, "Height")
            };
        }

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

        public bool TryCopyScreenFromTemplate(
            HmiSoftware software,
            string templateScreenPath,
            string targetScreenPath,
            string? findText,
            string? replaceText,
            out HmiScreenCopyResult? result,
            out string? error)
        {
            result = null;
            error = null;

            if (!TryGetScreen(software, templateScreenPath, out var templateScreen, out var normalizedTemplatePath) || templateScreen == null)
            {
                error = $"Template screen '{templateScreenPath}' was not found.";
                return false;
            }

            var normalizedTargetPath = NormalizeScreenPath(targetScreenPath);
            if (string.IsNullOrWhiteSpace(normalizedTargetPath))
            {
                error = "Target screen path cannot be empty.";
                return false;
            }

            if (TryGetScreen(software, normalizedTargetPath, out _, out _))
            {
                error = $"Target screen '{normalizedTargetPath}' already exists.";
                return false;
            }

            HmiScreen? targetScreen = null;

            try
            {
                targetScreen = EnsureScreen(software, normalizedTargetPath);
                if (targetScreen == null)
                {
                    error = $"Failed to create target screen '{normalizedTargetPath}'.";
                    return false;
                }

                TryCopyDirectProperty(templateScreen, targetScreen, "BackGraphic");
                CopyPropertyGraph(templateScreen, targetScreen, ScreenCopyRootPropertyExclusions, findText, replaceText, 0, new HashSet<object>(ReferenceEqualityComparer.Instance));
                CopyEventHandlers(templateScreen, targetScreen, findText, replaceText);

                foreach (var templateItem in templateScreen.ScreenItems.Cast<HmiScreenItemBase>())
                {
                    var targetItem = CreateScreenItem(targetScreen, templateItem.GetType().Name, templateItem.Name);
                    if (targetItem == null)
                    {
                        throw new InvalidOperationException($"Item type '{templateItem.GetType().Name}' is not supported for screen copy.");
                    }

                    CopyPropertyGraph(templateItem, targetItem, ItemCopyRootPropertyExclusions, findText, replaceText, 0, new HashSet<object>(ReferenceEqualityComparer.Instance));
                    CopyEventHandlers(templateItem, targetItem, findText, replaceText);
                }

                result = new HmiScreenCopyResult
                {
                    TemplatePath = normalizedTemplatePath,
                    TargetPath = normalizedTargetPath,
                    ReplacementApplied = !string.IsNullOrWhiteSpace(findText),
                    CopiedItemCount = targetScreen.ScreenItems.Count,
                    CopiedScreenEventHandlerCount = CountEventHandlers(targetScreen),
                    CopiedItemEventHandlerCount = targetScreen.ScreenItems.Cast<HmiScreenItemBase>().Sum(CountEventHandlers),
                    Screen = ToScreenInfo(targetScreen, normalizedTargetPath, includeItems: true)
                };

                return true;
            }
            catch (Exception ex)
            {
                TryDelete(targetScreen);
                error = ex.InnerException?.Message ?? ex.Message;
                return false;
            }
        }

        private static void CollectScreens(HmiScreenGroup group, string groupPath, bool recursive, List<HmiScreenInfo> results)
        {
            foreach (var screen in group.Screens)
            {
                results.Add(new HmiScreenInfo
                {
                    Name = screen.Name,
                    Path = CombinePath(groupPath, screen.Name),
                    GroupPath = groupPath,
                    Width = (int)screen.Width,
                    Height = (int)screen.Height,
                    Enabled = screen.Enabled,
                    ItemCount = screen.ScreenItems.Count
                });
            }

            if (!recursive)
            {
                return;
            }

            foreach (var childGroup in group.Groups)
            {
                CollectScreens(childGroup, CombinePath(groupPath, childGroup.Name), recursive, results);
            }
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
                info.DisplayName = FormatValue(tag.DisplayName);
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

        private static HmiScreenGroup? TryGetGroup(HmiSoftware software, IEnumerable<string> segments)
        {
            HmiScreenGroup? currentGroup = null;

            foreach (var segment in segments)
            {
                currentGroup = currentGroup == null
                    ? software.ScreenGroups.Find(segment)
                    : currentGroup.Groups.Find(segment);

                if (currentGroup == null)
                {
                    return null;
                }
            }

            return currentGroup;
        }

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

        public IReadOnlyList<HmiItemPropertyInfo> GetItemProperties(HmiScreenItemBase item, IEnumerable<string>? propertyNames)
        {
            var result = new Dictionary<string, HmiItemPropertyInfo>(StringComparer.OrdinalIgnoreCase);

            if (propertyNames != null)
            {
                foreach (var propertyName in propertyNames.Where(name => !string.IsNullOrWhiteSpace(name)))
                {
                    if (TryReadPropertyOrAttribute(item, propertyName.Trim(), out var property))
                    {
                        result[property.Name] = property;
                    }
                }

                return result.Values.OrderBy(property => property.Name, StringComparer.OrdinalIgnoreCase).ToList();
            }

            foreach (var property in ReadBrowsableProperties(item))
            {
                result[property.Name] = property;
            }

            foreach (var property in ReadAttributeProperties(item))
            {
                result[property.Name] = property;
            }

            return result.Values.OrderBy(property => property.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public bool TrySetItemProperty(HmiScreenItemBase item, string propertyName, string value, HmiSoftware software, out string? error)
        {
            error = null;

            try
            {
                if (propertyName.IndexOf("BackColor", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    TryEnsureSolidBackFillPattern(item);
                }

                if (TrySetPropertyPathValue(item, propertyName.Trim(), value, software))
                {
                    return true;
                }

                error = $"Property or attribute '{propertyName}' was not found or is not writable.";
                return false;
            }
            catch (Exception ex)
            {
                error = ex.InnerException?.Message ?? ex.Message;
                return false;
            }
        }

        public bool TryValidateItemProperty(HmiScreenItemBase item, string propertyName, string value, HmiSoftware software, out string? error)
        {
            error = null;

            try
            {
                var propertyPath = propertyName.Trim();
                var resolvedProperty = ResolveProperty(item, propertyPath);
                if (resolvedProperty != null)
                {
                    if (IsMultilingualText(resolvedProperty.PropertyInfo.PropertyType))
                    {
                        if (resolvedProperty.PropertyInfo.GetValue(resolvedProperty.Target) == null)
                        {
                            error = $"Multilingual text property '{propertyPath}' is not initialized.";
                            return false;
                        }

                        return true;
                    }

                    if (!resolvedProperty.PropertyInfo.CanWrite)
                    {
                        error = $"Property '{propertyPath}' is read-only.";
                        return false;
                    }

                    if ((Nullable.GetUnderlyingType(resolvedProperty.PropertyInfo.PropertyType) ?? resolvedProperty.PropertyInfo.PropertyType) == typeof(string))
                    {
                        NormalizeStringPropertyValue(resolvedProperty.Target, resolvedProperty.PropertyInfo, value);
                        return true;
                    }

                    ConvertStringToType(value, resolvedProperty.PropertyInfo.PropertyType, software);
                    return true;
                }

                var resolvedAttribute = ResolveAttribute(item, propertyPath);
                if (resolvedAttribute == null)
                {
                    error = $"Property or attribute '{propertyName}' was not found or is not writable.";
                    return false;
                }

                if (resolvedAttribute.AttributeInfo.AccessMode != EngineeringAttributeAccessMode.Write &&
                    resolvedAttribute.AttributeInfo.AccessMode != EngineeringAttributeAccessMode.ReadWrite)
                {
                    error = $"Attribute '{propertyPath}' is not writable.";
                    return false;
                }

                ConvertStringToAttributeValue(
                    value,
                    resolvedAttribute.Target,
                    resolvedAttribute.AttributeInfo,
                    software);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.InnerException?.Message ?? ex.Message;
                return false;
            }
        }

        public HmiFaceplateInterfaceProbeResult ProbeFaceplateInterface(
            string screenPath,
            HmiScreenItemBase item,
            string? propertyName,
            bool write,
            string? value,
            bool restore)
        {
            var targetProperty = NormalizeOptionalString(propertyName);
            var result = new HmiFaceplateInterfaceProbeResult
            {
                ScreenPath = screenPath,
                ItemName = TryReadStringProperty(item, "Name") ?? string.Empty,
                ItemType = item.GetType().FullName ?? item.GetType().Name,
                ContainedType = TryReadStringProperty(item, "ContainedType"),
                TargetProperty = targetProperty,
                WriteRequested = write,
                WriteValue = value,
                RestoreRequested = write && restore
            };

            if (!TryGetNamedComposition(item, "Interface", out var composition) || composition is not IEnumerable enumerable)
            {
                result.WriteError = "Interface is not available or is not enumerable.";
                return result;
            }

            object? targetEntry = null;
            object? originalRawValue = null;

            foreach (var entry in enumerable.Cast<object>())
            {
                var entryName = NormalizeOptionalString(TryReadStringProperty(entry, "PropertyName"));
                if (string.IsNullOrWhiteSpace(entryName))
                {
                    continue;
                }

                var valueProperty = ResolvePublicInstanceProperty(entry.GetType(), "Value");
                var rawValue = valueProperty?.CanRead == true ? TryReadPropertyValue(entry, "Value") : null;
                var probeEntry = new HmiFaceplateInterfaceProbeEntry
                {
                    PropertyName = entryName!,
                    Value = NormalizeOptionalString(FormatValue(rawValue)),
                    EntryType = entry.GetType().FullName ?? entry.GetType().Name,
                    ValuePropertyType = valueProperty?.PropertyType.FullName,
                    ValueDeclaringType = valueProperty?.DeclaringType?.FullName,
                    ValueCanRead = valueProperty?.CanRead == true,
                    ValueCanWrite = valueProperty?.CanWrite == true,
                    IsTarget = !string.IsNullOrWhiteSpace(targetProperty) &&
                        entryName!.Equals(targetProperty, StringComparison.OrdinalIgnoreCase)
                };
                result.Entries.Add(probeEntry);

                if (probeEntry.IsTarget)
                {
                    targetEntry = entry;
                    originalRawValue = rawValue;
                }
            }

            result.Entries = result.Entries
                .OrderBy(entry => entry.PropertyName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!write)
            {
                return result;
            }

            if (string.IsNullOrWhiteSpace(targetProperty))
            {
                result.WriteError = "A property name is required when write is true.";
                return result;
            }

            if (targetEntry == null)
            {
                result.WriteError = $"Interface property '{targetProperty}' was not found.";
                return result;
            }

            var targetProbeEntry = result.Entries.First(entry => entry.IsTarget);
            try
            {
                SetObjectPropertyValueFlexible(targetEntry, "Value", value ?? string.Empty);
                result.WriteSucceeded = true;
                targetProbeEntry.ValueAfterWrite = NormalizeOptionalString(FormatValue(TryReadPropertyValue(targetEntry, "Value")));
            }
            catch (Exception ex)
            {
                result.WriteError = FormatExceptionMessage(ex);
                return result;
            }

            if (restore)
            {
                try
                {
                    SetObjectPropertyValueFlexible(targetEntry, "Value", originalRawValue ?? string.Empty);
                    result.RestoreSucceeded = true;
                    targetProbeEntry.ValueAfterRestore = NormalizeOptionalString(FormatValue(TryReadPropertyValue(targetEntry, "Value")));
                }
                catch (Exception ex)
                {
                    result.RestoreError = FormatExceptionMessage(ex);
                }
            }

            return result;
        }

        public bool TryResolveItemClrType(string itemType, out Type resolvedType)
        {
            if (TryResolveItemType(itemType, out var descriptor))
            {
                resolvedType = descriptor.ItemType;
                return true;
            }

            resolvedType = null!;
            return false;
        }

        public bool TryReadEventHandlers(object owner, out List<HmiJsonEventHandler> eventHandlers, out string? error)
        {
            eventHandlers = new List<HmiJsonEventHandler>();
            error = null;

            if (!TryGetNamedComposition(owner, "EventHandlers", out var composition))
            {
                return false;
            }

            try
            {
                eventHandlers = ReadEventHandlersFromComposition(composition);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.InnerException?.Message ?? ex.Message;
                eventHandlers = new List<HmiJsonEventHandler>();
                return false;
            }
        }

        public bool TryReadPropertyEventHandlers(object owner, out List<HmiJsonPropertyEventHandler> propertyEventHandlers, out string? error)
        {
            propertyEventHandlers = new List<HmiJsonPropertyEventHandler>();
            error = null;

            if (!TryGetNamedComposition(owner, "PropertyEventHandlers", out var composition))
            {
                return false;
            }

            try
            {
                propertyEventHandlers = ReadPropertyEventHandlersFromComposition(composition);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.InnerException?.Message ?? ex.Message;
                propertyEventHandlers = new List<HmiJsonPropertyEventHandler>();
                return false;
            }
        }

        public bool TryReadDynamizations(object owner, out List<HmiJsonDynamization> dynamizations, out string? error)
        {
            dynamizations = new List<HmiJsonDynamization>();
            error = null;

            if (!TryGetNamedComposition(owner, "Dynamizations", out var composition))
            {
                return false;
            }

            try
            {
                dynamizations = ReadSupportedDynamizationsFromComposition(composition);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.InnerException?.Message ?? ex.Message;
                dynamizations = new List<HmiJsonDynamization>();
                return false;
            }
        }

        public bool TryReadInterfaceBindings(object owner, out List<HmiJsonInterfaceBinding> bindings, out string? error)
        {
            bindings = new List<HmiJsonInterfaceBinding>();
            error = null;

            if (!TryGetNamedComposition(owner, "Interface", out var composition))
            {
                return false;
            }

            try
            {
                bindings = ReadInterfaceBindingsFromComposition(composition);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.InnerException?.Message ?? ex.Message;
                bindings = new List<HmiJsonInterfaceBinding>();
                return false;
            }
        }

        public bool TryValidateOrApplyEventHandlers(
            object? owner,
            Type ownerType,
            IReadOnlyList<HmiJsonEventHandler>? desiredEventHandlers,
            bool apply,
            out string? error)
        {
            error = null;

            var desired = HmiJsonCompositeComparer.CloneEventHandlers(desiredEventHandlers)?.ToList() ?? new List<HmiJsonEventHandler>();
            if (!TryValidateEventHandlerDefinitions(desired, out error))
            {
                return false;
            }

            if (!TryResolveEventHandlerEnumType(owner, ownerType, out var enumType))
            {
                error = $"EventHandlers is not available on type '{ownerType.Name}'.";
                return false;
            }

            foreach (var handler in desired)
            {
                if (!TryValidateEventHandler(handler, enumType, out error))
                {
                    return false;
                }
            }

            if (!apply)
            {
                return true;
            }

            if (owner == null)
            {
                error = "The event handler owner is not available.";
                return false;
            }

            if (!TryGetNamedComposition(owner, "EventHandlers", out var composition))
            {
                error = $"EventHandlers is not available on {owner.GetType().Name}.";
                return false;
            }

            return TrySynchronizeEventHandlers(composition, desired, enumType, out error);
        }

        public bool TryValidateOrApplyPropertyEventHandlers(
            object? owner,
            Type ownerType,
            IReadOnlyList<HmiJsonPropertyEventHandler>? desiredPropertyEventHandlers,
            bool apply,
            out string? error)
        {
            error = null;

            var desired = HmiJsonCompositeComparer.ClonePropertyEventHandlers(desiredPropertyEventHandlers)?.ToList()
                          ?? new List<HmiJsonPropertyEventHandler>();
            if (!TryValidatePropertyEventHandlerDefinitions(desired, out error))
            {
                return false;
            }

            if (!TryResolvePropertyEventHandlerEnumType(owner, ownerType, out var enumType))
            {
                error = $"PropertyEventHandlers is not available on type '{ownerType.Name}'.";
                return false;
            }

            foreach (var handler in desired)
            {
                if (!TryValidatePropertyEventHandler(handler, enumType, out error))
                {
                    return false;
                }
            }

            if (!apply)
            {
                return true;
            }

            if (owner == null)
            {
                error = "The property event handler owner is not available.";
                return false;
            }

            if (!TryGetNamedComposition(owner, "PropertyEventHandlers", out var composition))
            {
                error = $"PropertyEventHandlers is not available on {owner.GetType().Name}.";
                return false;
            }

            return TrySynchronizePropertyEventHandlers(composition, desired, enumType, out error);
        }

        public bool TryValidateOrApplyDynamizations(
            object? owner,
            Type ownerType,
            IReadOnlyList<HmiJsonDynamization>? desiredDynamizations,
            bool apply,
            out string? error)
        {
            error = null;

            var desired = HmiJsonCompositeComparer.CloneDynamizations(desiredDynamizations)?.ToList() ?? new List<HmiJsonDynamization>();
            if (!TryValidateDynamizationDefinitions(desired, out error))
            {
                return false;
            }

            foreach (var dynamization in desired)
            {
                if (!TryValidateDynamizationTarget(owner, ownerType, dynamization, out error) ||
                    !TryValidateDynamizationDefinition(dynamization, out error))
                {
                    return false;
                }
            }

            if (!apply)
            {
                return true;
            }

            if (owner == null)
            {
                error = "The dynamization owner is not available.";
                return false;
            }

            if (!TryGetNamedComposition(owner, "Dynamizations", out var composition))
            {
                error = $"Dynamizations is not available on {owner.GetType().Name}.";
                return false;
            }

            return TrySynchronizeDynamizations(composition, desired, out error);
        }

        public bool TryValidateOrApplyInterfaceBindings(
            object? owner,
            Type ownerType,
            IReadOnlyList<HmiJsonInterfaceBinding>? desiredBindings,
            bool apply,
            out string? error)
        {
            error = null;

            var desired = HmiJsonCompositeComparer.CloneInterfaceBindings(desiredBindings)?.ToList()
                          ?? new List<HmiJsonInterfaceBinding>();
            if (!TryValidateInterfaceBindingDefinitions(desired, out error))
            {
                return false;
            }

            if (ResolvePublicInstanceProperty(ownerType, "Interface") == null)
            {
                error = $"Interface is not available on type '{ownerType.Name}'.";
                return false;
            }

            if (owner != null && TryGetNamedComposition(owner, "Interface", out var validationComposition))
            {
                var availableBindings = ReadRawInterfaceBindings(validationComposition);
                foreach (var binding in desired)
                {
                    var propertyName = NormalizeOptionalString(binding.PropertyName);
                    if (!string.IsNullOrWhiteSpace(propertyName) &&
                        !availableBindings.ContainsKey(propertyName!))
                    {
                        error = $"Interface property '{propertyName}' was not found.";
                        return false;
                    }

                    if (!TryValidateDynamizationDefinitions(binding.Dynamizations ?? new List<HmiJsonDynamization>(), out error))
                    {
                        error = $"Interface.{propertyName}.Dynamizations: {error}";
                        return false;
                    }
                }
            }

            if (!apply)
            {
                return true;
            }

            if (owner == null)
            {
                error = "The interface owner is not available.";
                return false;
            }

            if (!TryGetNamedComposition(owner, "Interface", out var composition))
            {
                error = $"Interface is not available on {owner.GetType().Name}.";
                return false;
            }

            return TrySynchronizeInterfaceBindings(composition, desired, out error);
        }

        private static bool TryGetNamedComposition(object? source, string propertyName, out object composition)
        {
            composition = null!;
            var value = TryReadPropertyValue(source, propertyName);
            if (value == null)
            {
                return false;
            }

            composition = value;
            return true;
        }

        private static List<HmiJsonEventHandler> ReadEventHandlersFromComposition(object composition)
        {
            if (composition is not IEnumerable enumerable)
            {
                throw new InvalidOperationException("EventHandlers composition is not enumerable.");
            }

            return enumerable
                .Cast<object>()
                .Select(TryReadEventHandlerExport)
                .Where(handler => handler != null)
                .OrderBy(handler => handler!.EventType, StringComparer.OrdinalIgnoreCase)
                .Cast<HmiJsonEventHandler>()
                .ToList();
        }

        private static List<HmiJsonInterfaceBinding> ReadInterfaceBindingsFromComposition(object composition)
        {
            if (composition is not IEnumerable enumerable)
            {
                throw new InvalidOperationException("Interface composition is not enumerable.");
            }

            return enumerable
                .Cast<object>()
                .Select(TryReadInterfaceBindingExport)
                .Where(binding => binding != null)
                .OrderBy(binding => binding!.PropertyName, StringComparer.OrdinalIgnoreCase)
                .Cast<HmiJsonInterfaceBinding>()
                .ToList();
        }

        private static HmiJsonInterfaceBinding? TryReadInterfaceBindingExport(object? binding)
        {
            var propertyName = NormalizeOptionalString(TryReadStringProperty(binding, "PropertyName"));
            if (string.IsNullOrWhiteSpace(propertyName))
            {
                return null;
            }

            var rawValue = TryReadPropertyValue(binding, "Value");
            return new HmiJsonInterfaceBinding
            {
                PropertyName = propertyName!,
                ValueType = rawValue?.GetType().Name,
                Value = NormalizeOptionalString(FormatValue(rawValue)),
                Dynamizations = TryReadDynamizationsFromOwner(binding)
            };
        }

        private static List<HmiJsonDynamization>? TryReadDynamizationsFromOwner(object? owner)
        {
            if (!TryGetNamedComposition(owner, "Dynamizations", out var composition))
            {
                return null;
            }

            var dynamizations = ReadSupportedDynamizationsFromComposition(composition);
            return dynamizations.Count > 0 ? dynamizations : null;
        }

        private static HmiJsonEventHandler? TryReadEventHandlerExport(object? eventHandler)
        {
            var eventType = NormalizeOptionalString(TryReadStringProperty(eventHandler, "EventType"));
            if (string.IsNullOrWhiteSpace(eventType))
            {
                return null;
            }

            return new HmiJsonEventHandler
            {
                EventType = eventType!,
                Script = ReadScriptPayload(TryReadPropertyValue(eventHandler, "Script"))
            };
        }

        private static List<HmiJsonPropertyEventHandler> ReadPropertyEventHandlersFromComposition(object composition)
        {
            if (composition is not IEnumerable enumerable)
            {
                throw new InvalidOperationException("PropertyEventHandlers composition is not enumerable.");
            }

            return enumerable
                .Cast<object>()
                .Select(TryReadPropertyEventHandlerExport)
                .Where(handler => handler != null)
                .OrderBy(handler => handler!.PropertyName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(handler => handler!.EventType, StringComparer.OrdinalIgnoreCase)
                .Cast<HmiJsonPropertyEventHandler>()
                .ToList();
        }

        private static HmiJsonPropertyEventHandler? TryReadPropertyEventHandlerExport(object? eventHandler)
        {
            var propertyName = NormalizeOptionalString(TryReadStringProperty(eventHandler, "PropertyName"));
            var eventType = NormalizeOptionalString(TryReadStringProperty(eventHandler, "EventType"));
            if (string.IsNullOrWhiteSpace(propertyName) || string.IsNullOrWhiteSpace(eventType))
            {
                return null;
            }

            return new HmiJsonPropertyEventHandler
            {
                PropertyName = propertyName!,
                EventType = eventType!,
                Script = ReadScriptPayload(TryReadPropertyValue(eventHandler, "Script"))
            };
        }

        private static HmiJsonScriptPayload? ReadScriptPayload(object? script)
        {
            if (script == null)
            {
                return null;
            }

            var payload = new HmiJsonScriptPayload
            {
                Async = TryReadNullableBoolProperty(script, "Async"),
                GlobalDefinitionAreaScriptCode = TryReadStringProperty(script, "GlobalDefinitionAreaScriptCode"),
                ScriptCode = TryReadStringProperty(script, "ScriptCode")
            };

            return payload.Async.HasValue ||
                   !string.IsNullOrWhiteSpace(payload.GlobalDefinitionAreaScriptCode) ||
                   !string.IsNullOrWhiteSpace(payload.ScriptCode)
                ? payload
                : null;
        }

        private static bool TryResolveEventHandlerEnumType(object? owner, Type ownerType, out Type enumType)
        {
            enumType = null!;

            if (owner != null &&
                TryGetNamedComposition(owner, "EventHandlers", out var ownerComposition) &&
                TryResolveEventHandlerEnumTypeFromCompositionType(ownerComposition.GetType(), out enumType))
            {
                return true;
            }

            var property = ResolvePublicInstanceProperty(ownerType, "EventHandlers");
            return property != null && TryResolveEventHandlerEnumTypeFromCompositionType(property.PropertyType, out enumType);
        }

        private static bool TryResolveEventHandlerEnumTypeFromCompositionType(Type? compositionType, out Type enumType)
        {
            enumType = null!;

            var createMethod = compositionType?
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(method =>
                    string.Equals(method.Name, "Create", StringComparison.OrdinalIgnoreCase) &&
                    method.GetParameters().Length == 1 &&
                    method.GetParameters()[0].ParameterType.IsEnum);
            if (createMethod == null)
            {
                return false;
            }

            enumType = createMethod.GetParameters()[0].ParameterType;
            return true;
        }

        private static bool TryValidateEventHandlerDefinitions(
            IEnumerable<HmiJsonEventHandler> eventHandlers,
            out string? error)
        {
            error = null;
            var seenEventTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var handler in eventHandlers ?? Enumerable.Empty<HmiJsonEventHandler>())
            {
                var eventType = NormalizeOptionalString(handler?.EventType);
                if (string.IsNullOrWhiteSpace(eventType))
                {
                    error = "EventHandlers entries require an EventType value.";
                    return false;
                }

                if (!seenEventTypes.Add(eventType!))
                {
                    error = $"EventHandlers contains a duplicate event type '{eventType}'.";
                    return false;
                }
            }

            return true;
        }

        private static bool TryValidateEventHandler(
            HmiJsonEventHandler handler,
            Type eventTypeEnum,
            out string? error)
        {
            error = null;

            var eventType = NormalizeOptionalString(handler?.EventType);
            if (string.IsNullOrWhiteSpace(eventType))
            {
                error = "EventType is required.";
                return false;
            }

            try
            {
                Enum.Parse(eventTypeEnum, eventType, ignoreCase: true);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.InnerException?.Message ?? ex.Message;
                return false;
            }
        }

        private static bool TrySynchronizeEventHandlers(
            object composition,
            IReadOnlyList<HmiJsonEventHandler> desiredEventHandlers,
            Type eventTypeEnum,
            out string? error)
        {
            error = null;

            try
            {
                var desired = HmiJsonCompositeComparer.CloneEventHandlers(desiredEventHandlers)?.ToList() ?? new List<HmiJsonEventHandler>();
                var currentAll = ReadRawEventHandlers(composition);
                var current = currentAll.Values
                    .Where(entry => entry.Export != null)
                    .Select(entry => entry.Export!)
                    .OrderBy(entry => entry.EventType, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (HmiJsonCompositeComparer.AreEventHandlerCollectionsEquivalent(current, desired))
                {
                    return true;
                }

                var desiredMap = HmiJsonCompositeComparer.BuildEventHandlerMap(desired);
                foreach (var currentEntry in currentAll.Values
                    .Where(entry => entry.Export != null)
                    .OrderByDescending(entry => entry.EventType, StringComparer.OrdinalIgnoreCase))
                {
                    if (!desiredMap.ContainsKey(currentEntry.EventType))
                    {
                        DeleteEngineeringObject(currentEntry.Raw);
                    }
                }

                foreach (var handler in desired)
                {
                    var eventType = NormalizeOptionalString(handler.EventType)!;
                    currentAll.TryGetValue(eventType, out var currentEntry);
                    var targetHandler = currentEntry.Raw ?? CreateEventHandlerObject(composition, eventType, eventTypeEnum);
                    ApplyEventHandlerExport(targetHandler, handler);
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.InnerException?.Message ?? ex.Message;
                return false;
            }
        }

        private static Dictionary<string, (object Raw, HmiJsonEventHandler? Export, string EventType)> ReadRawEventHandlers(object composition)
        {
            var result = new Dictionary<string, (object Raw, HmiJsonEventHandler? Export, string EventType)>(StringComparer.OrdinalIgnoreCase);
            if (composition is not IEnumerable enumerable)
            {
                return result;
            }

            foreach (var entry in enumerable.Cast<object>())
            {
                var export = TryReadEventHandlerExport(entry);
                var eventType = NormalizeOptionalString(export?.EventType ?? TryReadStringProperty(entry, "EventType"));
                if (string.IsNullOrWhiteSpace(eventType))
                {
                    continue;
                }

                result[eventType!] = (entry, export, eventType!);
            }

            return result;
        }

        private static object CreateEventHandlerObject(object composition, string eventType, Type eventTypeEnum)
        {
            var createMethod = composition.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(method =>
                    string.Equals(method.Name, "Create", StringComparison.OrdinalIgnoreCase) &&
                    method.GetParameters().Length == 1 &&
                    method.GetParameters()[0].ParameterType == eventTypeEnum);
            if (createMethod == null)
            {
                throw new InvalidOperationException("EventHandlers.Create(enum) is not available.");
            }

            var enumValue = Enum.Parse(eventTypeEnum, eventType, ignoreCase: true);
            return createMethod.Invoke(composition, new[] { enumValue })
                ?? throw new InvalidOperationException($"Failed to create event handler '{eventType}'.");
        }

        private static void ApplyEventHandlerExport(object targetHandler, HmiJsonEventHandler desired)
        {
            ApplyScriptPayload(TryReadPropertyValue(targetHandler, "Script"), desired.Script);
        }

        private static bool TryResolvePropertyEventHandlerEnumType(object? owner, Type ownerType, out Type enumType)
        {
            enumType = null!;

            if (owner != null &&
                TryGetNamedComposition(owner, "PropertyEventHandlers", out var ownerComposition) &&
                TryResolvePropertyEventHandlerEnumTypeFromCompositionType(ownerComposition.GetType(), out enumType))
            {
                return true;
            }

            var property = ResolvePublicInstanceProperty(ownerType, "PropertyEventHandlers");
            return property != null && TryResolvePropertyEventHandlerEnumTypeFromCompositionType(property.PropertyType, out enumType);
        }

        private static bool TryResolvePropertyEventHandlerEnumTypeFromCompositionType(Type? compositionType, out Type enumType)
        {
            enumType = null!;

            var createMethod = compositionType?
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(method =>
                    string.Equals(method.Name, "Create", StringComparison.OrdinalIgnoreCase) &&
                    method.GetParameters().Length == 2 &&
                    method.GetParameters()[0].ParameterType == typeof(string) &&
                    method.GetParameters()[1].ParameterType.IsEnum);
            if (createMethod == null)
            {
                return false;
            }

            enumType = createMethod.GetParameters()[1].ParameterType;
            return true;
        }

        private static bool TryValidatePropertyEventHandlerDefinitions(
            IEnumerable<HmiJsonPropertyEventHandler> propertyEventHandlers,
            out string? error)
        {
            error = null;
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var handler in propertyEventHandlers ?? Enumerable.Empty<HmiJsonPropertyEventHandler>())
            {
                var propertyName = NormalizeOptionalString(handler?.PropertyName);
                var eventType = NormalizeOptionalString(handler?.EventType);
                if (string.IsNullOrWhiteSpace(propertyName) || string.IsNullOrWhiteSpace(eventType))
                {
                    error = "PropertyEventHandlers entries require PropertyName and EventType values.";
                    return false;
                }

                var key = HmiJsonCompositeComparer.GetPropertyEventHandlerKey(propertyName, eventType);
                if (!string.IsNullOrWhiteSpace(key) && !seenKeys.Add(key!))
                {
                    error = $"PropertyEventHandlers contains a duplicate handler '{propertyName}/{eventType}'.";
                    return false;
                }
            }

            return true;
        }

        private static bool TryValidatePropertyEventHandler(
            HmiJsonPropertyEventHandler handler,
            Type eventTypeEnum,
            out string? error)
        {
            error = null;

            var propertyName = NormalizeOptionalString(handler?.PropertyName);
            if (string.IsNullOrWhiteSpace(propertyName))
            {
                error = "PropertyName is required.";
                return false;
            }

            var eventType = NormalizeOptionalString(handler?.EventType);
            if (string.IsNullOrWhiteSpace(eventType))
            {
                error = "EventType is required.";
                return false;
            }

            try
            {
                Enum.Parse(eventTypeEnum, eventType, ignoreCase: true);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.InnerException?.Message ?? ex.Message;
                return false;
            }
        }

        private static bool TrySynchronizePropertyEventHandlers(
            object composition,
            IReadOnlyList<HmiJsonPropertyEventHandler> desiredPropertyEventHandlers,
            Type eventTypeEnum,
            out string? error)
        {
            error = null;

            try
            {
                var desired = HmiJsonCompositeComparer.ClonePropertyEventHandlers(desiredPropertyEventHandlers)?.ToList()
                              ?? new List<HmiJsonPropertyEventHandler>();
                var currentAll = ReadRawPropertyEventHandlers(composition);
                var current = currentAll.Values
                    .Where(entry => entry.Export != null)
                    .Select(entry => entry.Export!)
                    .OrderBy(entry => entry.PropertyName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(entry => entry.EventType, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (HmiJsonCompositeComparer.ArePropertyEventHandlerCollectionsEquivalent(current, desired))
                {
                    return true;
                }

                var desiredMap = HmiJsonCompositeComparer.BuildPropertyEventHandlerMap(desired);
                foreach (var currentEntry in currentAll.Values
                    .Where(entry => entry.Export != null)
                    .OrderByDescending(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
                {
                    if (!desiredMap.ContainsKey(currentEntry.Key))
                    {
                        DeleteEngineeringObject(currentEntry.Raw);
                    }
                }

                foreach (var handler in desired)
                {
                    var propertyName = NormalizeOptionalString(handler.PropertyName)!;
                    var eventType = NormalizeOptionalString(handler.EventType)!;
                    var key = HmiJsonCompositeComparer.GetPropertyEventHandlerKey(propertyName, eventType)!;
                    currentAll.TryGetValue(key, out var currentEntry);
                    var targetHandler = currentEntry.Raw ?? CreatePropertyEventHandlerObject(composition, propertyName, eventType, eventTypeEnum);
                    ApplyPropertyEventHandlerExport(targetHandler, handler);
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.InnerException?.Message ?? ex.Message;
                return false;
            }
        }

        private static Dictionary<string, (object Raw, HmiJsonPropertyEventHandler? Export, string Key)> ReadRawPropertyEventHandlers(object composition)
        {
            var result = new Dictionary<string, (object Raw, HmiJsonPropertyEventHandler? Export, string Key)>(StringComparer.OrdinalIgnoreCase);
            if (composition is not IEnumerable enumerable)
            {
                return result;
            }

            foreach (var entry in enumerable.Cast<object>())
            {
                var export = TryReadPropertyEventHandlerExport(entry);
                var key = export == null
                    ? null
                    : HmiJsonCompositeComparer.GetPropertyEventHandlerKey(export.PropertyName, export.EventType);
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                result[key!] = (entry, export, key!);
            }

            return result;
        }

        private static object CreatePropertyEventHandlerObject(
            object composition,
            string propertyName,
            string eventType,
            Type eventTypeEnum)
        {
            var createMethod = composition.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(method =>
                    string.Equals(method.Name, "Create", StringComparison.OrdinalIgnoreCase) &&
                    method.GetParameters().Length == 2 &&
                    method.GetParameters()[0].ParameterType == typeof(string) &&
                    method.GetParameters()[1].ParameterType == eventTypeEnum);
            if (createMethod == null)
            {
                throw new InvalidOperationException("PropertyEventHandlers.Create(string, enum) is not available.");
            }

            var enumValue = Enum.Parse(eventTypeEnum, eventType, ignoreCase: true);
            return createMethod.Invoke(composition, new[] { propertyName, enumValue })
                ?? throw new InvalidOperationException($"Failed to create property event handler '{propertyName}/{eventType}'.");
        }

        private static void ApplyPropertyEventHandlerExport(object targetHandler, HmiJsonPropertyEventHandler desired)
        {
            ApplyScriptPayload(TryReadPropertyValue(targetHandler, "Script"), desired.Script);
        }

        private static void ApplyScriptPayload(object? targetScript, HmiJsonScriptPayload? desired)
        {
            if (desired == null)
            {
                return;
            }

            if (targetScript == null)
            {
                throw new InvalidOperationException("Script is not available.");
            }

            if (desired.Async.HasValue)
            {
                SetObjectPropertyValueFlexible(targetScript, "Async", desired.Async.Value);
            }

            if (desired.GlobalDefinitionAreaScriptCode != null)
            {
                SetObjectPropertyValueFlexible(targetScript, "GlobalDefinitionAreaScriptCode", desired.GlobalDefinitionAreaScriptCode);
            }

            if (desired.ScriptCode != null)
            {
                SetObjectPropertyValueFlexible(targetScript, "ScriptCode", desired.ScriptCode);
            }
        }

        private static List<HmiJsonDynamization> ReadSupportedDynamizationsFromComposition(object composition)
        {
            if (composition is not IEnumerable enumerable)
            {
                throw new InvalidOperationException("Dynamizations composition is not enumerable.");
            }

            return enumerable
                .Cast<object>()
                .Select(TryReadDynamizationExport)
                .Where(dynamization => dynamization != null)
                .OrderBy(dynamization => dynamization!.Property, StringComparer.OrdinalIgnoreCase)
                .Cast<HmiJsonDynamization>()
                .ToList();
        }

        private static HmiJsonDynamization? TryReadDynamizationExport(object? dynamization)
        {
            var propertyName = NormalizeOptionalString(TryReadStringProperty(dynamization, "PropertyName"));
            if (string.IsNullOrWhiteSpace(propertyName))
            {
                return null;
            }

            var kind = NormalizeDynamizationKind(
                TryReadStringProperty(dynamization, "DynamizationType"),
                dynamization?.GetType().Name);
            if (ResolveDynamizationClrType(kind) == null)
            {
                return null;
            }

            var export = new HmiJsonDynamization
            {
                Property = propertyName!,
                Type = kind
            };

            switch (kind)
            {
                case "Tag":
                    export.Binding = CreateBinding(
                        tag: TryReadStringProperty(dynamization, "Tag"),
                        readOnly: TryReadNullableBoolProperty(dynamization, "ReadOnly"),
                        useIndirectAddressing: TryReadNullableBoolProperty(dynamization, "UseIndirectAddressing"),
                        resourceList: null,
                        dynamicTagName: null);
                    export.ValueConverter = ReadValueConverterExport(TryReadPropertyValue(dynamization, "ValueConverter"));
                    break;
                case "Script":
                    export.Script = ReadScriptDynamizationExport(dynamization);
                    break;
                case "Flashing":
                    export.Flashing = ReadFlashingDynamizationExport(dynamization);
                    break;
                case "Expression":
                    export.ValueConverter = ReadValueConverterExport(TryReadPropertyValue(dynamization, "ValueConverter"));
                    break;
                case "ResourceList":
                    export.Binding = CreateBinding(
                        tag: TryReadStringProperty(dynamization, "Tag"),
                        readOnly: null,
                        useIndirectAddressing: null,
                        resourceList: TryReadStringProperty(dynamization, "ResourceList"),
                        dynamicTagName: null);
                    break;
                case "TagParameter":
                    export.Binding = CreateBinding(
                        tag: null,
                        readOnly: null,
                        useIndirectAddressing: null,
                        resourceList: null,
                        dynamicTagName: TryReadStringProperty(dynamization, "DynamicTagName"));
                    break;
            }

            return export;
        }

        private static HmiJsonDynamizationBinding? CreateBinding(
            string? tag,
            bool? readOnly,
            bool? useIndirectAddressing,
            string? resourceList,
            string? dynamicTagName)
        {
            var binding = new HmiJsonDynamizationBinding
            {
                Tag = NormalizeOptionalString(tag),
                ReadOnly = readOnly,
                UseIndirectAddressing = useIndirectAddressing,
                ResourceList = NormalizeOptionalString(resourceList),
                DynamicTagName = NormalizeOptionalString(dynamicTagName)
            };

            return HasMeaningfulBinding(binding) ? binding : null;
        }

        private static HmiJsonScriptDynamization? ReadScriptDynamizationExport(object? dynamization)
        {
            if (dynamization == null)
            {
                return null;
            }

            var export = new HmiJsonScriptDynamization
            {
                Async = TryReadNullableBoolProperty(dynamization, "Async"),
                GlobalDefinitionAreaScriptCode = TryReadStringProperty(dynamization, "GlobalDefinitionAreaScriptCode"),
                ScriptCode = TryReadStringProperty(dynamization, "ScriptCode"),
                Trigger = ReadScriptTriggerExport(TryReadPropertyValue(dynamization, "Trigger"))
            };

            return export.Async.HasValue ||
                   !string.IsNullOrWhiteSpace(export.GlobalDefinitionAreaScriptCode) ||
                   !string.IsNullOrWhiteSpace(export.ScriptCode) ||
                   export.Trigger != null
                ? export
                : null;
        }

        private static HmiJsonScriptTrigger? ReadScriptTriggerExport(object? trigger)
        {
            if (trigger == null)
            {
                return null;
            }

            var export = new HmiJsonScriptTrigger
            {
                Type = TryReadStringProperty(trigger, "Type"),
                CustomDuration = TryReadStringProperty(trigger, "CustomDuration"),
                Tags = ReadStringListValue(TryReadPropertyValue(trigger, "Tags"))
            };

            return !string.IsNullOrWhiteSpace(export.Type) ||
                   !string.IsNullOrWhiteSpace(export.CustomDuration) ||
                   (export.Tags?.Count ?? 0) > 0
                ? export
                : null;
        }

        private static HmiJsonFlashingDynamization? ReadFlashingDynamizationExport(object? dynamization)
        {
            if (dynamization == null)
            {
                return null;
            }

            var export = new HmiJsonFlashingDynamization
            {
                Color = TryReadStringProperty(dynamization, "Color"),
                AlternateColor = TryReadStringProperty(dynamization, "AlternateColor"),
                FlashingCondition = TryReadStringProperty(dynamization, "FlashingCondition"),
                FlashingRate = TryReadStringProperty(dynamization, "FlashingRate")
            };

            return !string.IsNullOrWhiteSpace(export.Color) ||
                   !string.IsNullOrWhiteSpace(export.AlternateColor) ||
                   !string.IsNullOrWhiteSpace(export.FlashingCondition) ||
                   !string.IsNullOrWhiteSpace(export.FlashingRate)
                ? export
                : null;
        }

        private static HmiJsonValueConverter? ReadValueConverterExport(object? valueConverter)
        {
            if (valueConverter == null)
            {
                return null;
            }

            var export = new HmiJsonValueConverter
            {
                IsFormulaSelected = TryReadNullableBoolProperty(valueConverter, "IsFormulaSelected"),
                Formula = TryReadStringProperty(valueConverter, "Formula"),
                MappingTable = ReadMappingTableExport(TryReadPropertyValue(valueConverter, "MappingTable"))
            };

            return export.IsFormulaSelected.HasValue ||
                   !string.IsNullOrWhiteSpace(export.Formula) ||
                   HasMeaningfulMappingTable(export.MappingTable)
                ? export
                : null;
        }

        private static HmiJsonMappingTable? ReadMappingTableExport(object? mappingTable)
        {
            if (mappingTable == null)
            {
                return null;
            }

            var entries = ReadMappingTableEntries(TryReadPropertyValue(mappingTable, "Entries"));
            var export = new HmiJsonMappingTable
            {
                ConditionType = TryReadStringProperty(mappingTable, "ConditionType"),
                Entries = entries.Count > 0 ? entries : null
            };

            return HasMeaningfulMappingTable(export) ? export : null;
        }

        private static List<HmiJsonMappingTableEntry> ReadMappingTableEntries(object? entriesComposition)
        {
            var entries = new List<HmiJsonMappingTableEntry>();
            if (entriesComposition == null)
            {
                return entries;
            }

            if (entriesComposition is not IEnumerable enumerable)
            {
                throw new InvalidOperationException("MappingTable entries composition is not enumerable.");
            }

            foreach (var entry in enumerable.Cast<object>())
            {
                var entryType = NormalizeMappingEntryKind(entry.GetType().Name);
                if (string.IsNullOrWhiteSpace(entryType))
                {
                    continue;
                }

                var export = new HmiJsonMappingTableEntry
                {
                    EntryType = entryType,
                    Value = TryReadConvertedStringProperty(entry, "Value"),
                    AlternateValue = TryReadConvertedStringProperty(entry, "AlternateValue"),
                    Flashing = TryReadNullableBoolProperty(entry, "Flashing"),
                    FlashingRate = TryReadStringProperty(entry, "FlashingRate")
                };

                switch (entryType)
                {
                    case "Simple":
                        export.Condition = TryReadConvertedStringProperty(entry, "Condition");
                        break;
                    case "Range":
                        export.From = TryReadConvertedStringProperty(entry, "From");
                        export.To = TryReadConvertedStringProperty(entry, "To");
                        export.RangeType = TryReadStringProperty(entry, "RangeType");
                        break;
                    case "Bitmask":
                        export.BitDynamizationType = TryReadStringProperty(entry, "BitDynamizationType");
                        export.Condition = TryReadConvertedStringProperty(entry, "Condition");
                        export.Relevant = TryReadConvertedStringProperty(entry, "Relevant");
                        break;
                }

                entries.Add(export);
            }

            return entries;
        }

        private static bool TryValidateDynamizationDefinitions(
            IEnumerable<HmiJsonDynamization> dynamizations,
            out string? error)
        {
            error = null;
            var seenProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var dynamization in dynamizations ?? Enumerable.Empty<HmiJsonDynamization>())
            {
                if (dynamization == null)
                {
                    continue;
                }

                var currentDynamization = dynamization;

                var propertyName = NormalizeOptionalString(currentDynamization.Property);
                if (string.IsNullOrWhiteSpace(propertyName))
                {
                    error = "Dynamizations entries require a Property value.";
                    return false;
                }

                if (!seenProperties.Add(propertyName!))
                {
                    error = $"Dynamizations contains a duplicate property '{propertyName}'.";
                    return false;
                }

                if (ResolveDynamizationClrType(currentDynamization.Type) == null)
                {
                    error = $"Unsupported dynamization type '{currentDynamization.Type}' for property '{propertyName}'.";
                    return false;
                }
            }

            return true;
        }

        private static bool TryValidateInterfaceBindingDefinitions(
            IEnumerable<HmiJsonInterfaceBinding> bindings,
            out string? error)
        {
            error = null;
            var seenProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var binding in bindings ?? Enumerable.Empty<HmiJsonInterfaceBinding>())
            {
                var propertyName = NormalizeOptionalString(binding?.PropertyName);
                if (string.IsNullOrWhiteSpace(propertyName))
                {
                    error = "Interface entries require a PropertyName value.";
                    return false;
                }

                if (!seenProperties.Add(propertyName!))
                {
                    error = $"Interface contains a duplicate property '{propertyName}'.";
                    return false;
                }

                if (!TryValidateDynamizationDefinitions(binding?.Dynamizations ?? new List<HmiJsonDynamization>(), out error))
                {
                    error = $"Interface.{propertyName}.Dynamizations: {error}";
                    return false;
                }
            }

            return true;
        }

        private static bool TryValidateDynamizationTarget(
            object? owner,
            Type ownerType,
            HmiJsonDynamization dynamization,
            out string? error)
        {
            error = null;

            if (ResolvePublicInstanceProperty(ownerType, "Dynamizations") == null)
            {
                error = $"Dynamizations is not available on type '{ownerType.Name}'.";
                return false;
            }

            if (owner == null)
            {
                return true;
            }

            if (ResolveProperty(owner, dynamization.Property) != null || ResolveAttribute(owner, dynamization.Property) != null)
            {
                return true;
            }

            error = $"Property or attribute '{dynamization.Property}' was not found on type '{owner.GetType().Name}'.";
            return false;
        }

        private static bool TryValidateDynamizationDefinition(HmiJsonDynamization dynamization, out string? error)
        {
            error = null;
            var dynamizationType = ResolveDynamizationClrType(dynamization.Type);
            if (dynamizationType == null)
            {
                error = $"Unsupported dynamization type '{dynamization.Type}'.";
                return false;
            }

            switch (NormalizeDynamizationKind(dynamization.Type))
            {
                case "Tag":
                case "Expression":
                    if (!TryValidateValueConverterDefinition(dynamization.ValueConverter, out error))
                    {
                        error = $"Property '{dynamization.Property}': {error}";
                        return false;
                    }
                    break;
            }

            return true;
        }

        private static bool TryValidateValueConverterDefinition(HmiJsonValueConverter? valueConverter, out string? error)
        {
            error = null;
            if (valueConverter?.MappingTable == null)
            {
                return true;
            }

            var mappingTable = valueConverter.MappingTable;
            if (!TryValidateObjectPropertyValue(typeof(MappingTable), "ConditionType", mappingTable.ConditionType, out error))
            {
                return false;
            }

            foreach (var entry in mappingTable.Entries ?? Enumerable.Empty<HmiJsonMappingTableEntry>())
            {
                var entryType = ResolveMappingTableEntryClrType(entry?.EntryType);
                if (entryType == null)
                {
                    error = $"Unsupported mapping table entry type '{entry?.EntryType}'.";
                    return false;
                }

                if (!TryValidateObjectPropertyValue(entryType, "Value", entry?.Value, out error) ||
                    !TryValidateObjectPropertyValue(entryType, "AlternateValue", entry?.AlternateValue, out error) ||
                    !TryValidateObjectPropertyValue(entryType, "Flashing", entry?.Flashing, out error) ||
                    !TryValidateObjectPropertyValue(entryType, "FlashingRate", entry?.FlashingRate, out error))
                {
                    return false;
                }

                switch (NormalizeMappingEntryKind(entry?.EntryType))
                {
                    case "Simple":
                        if (!TryValidateObjectPropertyValue(entryType, "Condition", entry?.Condition, out error))
                        {
                            return false;
                        }
                        break;
                    case "Range":
                        if (!TryValidateObjectPropertyValue(entryType, "From", entry?.From, out error) ||
                            !TryValidateObjectPropertyValue(entryType, "To", entry?.To, out error) ||
                            !TryValidateObjectPropertyValue(entryType, "RangeType", entry?.RangeType, out error))
                        {
                            return false;
                        }
                        break;
                    case "Bitmask":
                        if (!TryValidateObjectPropertyValue(entryType, "BitDynamizationType", entry?.BitDynamizationType, out error) ||
                            !TryValidateObjectPropertyValue(entryType, "Condition", entry?.Condition, out error) ||
                            !TryValidateObjectPropertyValue(entryType, "Relevant", entry?.Relevant, out error))
                        {
                            return false;
                        }
                        break;
                }
            }

            return true;
        }

        private static bool TrySynchronizeDynamizations(
            object composition,
            IReadOnlyList<HmiJsonDynamization> desiredDynamizations,
            out string? error)
        {
            error = null;

            try
            {
                var desired = HmiJsonCompositeComparer.CloneDynamizations(desiredDynamizations)?.ToList() ?? new List<HmiJsonDynamization>();
                var currentAll = ReadRawDynamizations(composition);
                var current = currentAll.Values
                    .Where(entry => entry.Export != null)
                    .Select(entry => entry.Export!)
                    .OrderBy(entry => entry.Property, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (HmiJsonCompositeComparer.AreDynamizationCollectionsEquivalent(current, desired))
                {
                    return true;
                }

                var desiredMap = HmiJsonCompositeComparer.BuildDynamizationMap(desired);
                foreach (var currentEntry in currentAll.Values
                    .Where(entry => entry.Export != null)
                    .OrderByDescending(entry => entry.Property, StringComparer.OrdinalIgnoreCase))
                {
                    if (!desiredMap.ContainsKey(currentEntry.Property))
                    {
                        DeleteEngineeringObject(currentEntry.Raw);
                    }
                }

                foreach (var dynamization in desired)
                {
                    currentAll.TryGetValue(dynamization.Property, out var currentEntry);
                    var targetDynamization = currentEntry.Raw;
                    var currentKind = NormalizeDynamizationKind(currentEntry.Export?.Type, currentEntry.Raw?.GetType().Name);
                    var desiredKind = NormalizeDynamizationKind(dynamization.Type);

                    if (targetDynamization != null &&
                        !string.Equals(currentKind, desiredKind, StringComparison.OrdinalIgnoreCase))
                    {
                        DeleteEngineeringObject(targetDynamization);
                        targetDynamization = null;
                    }

                    targetDynamization ??= CreateDynamizationObject(composition, dynamization);
                    ApplyDynamizationExport(targetDynamization, dynamization);
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.InnerException?.Message ?? ex.Message;
                return false;
            }
        }

        private static bool TrySynchronizeInterfaceBindings(
            object composition,
            IReadOnlyList<HmiJsonInterfaceBinding> desiredBindings,
            out string? error)
        {
            error = null;

            try
            {
                var desired = HmiJsonCompositeComparer.CloneInterfaceBindings(desiredBindings)?.ToList()
                              ?? new List<HmiJsonInterfaceBinding>();
                var currentAll = ReadRawInterfaceBindings(composition);
                var current = currentAll.Values
                    .Where(entry => entry.Export != null)
                    .Select(entry => entry.Export!)
                    .OrderBy(entry => entry.PropertyName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (HmiJsonCompositeComparer.AreInterfaceBindingCollectionsEquivalent(current, desired))
                {
                    return true;
                }

                var changedProperties = HmiJsonCompositeComparer.GetChangedInterfaceProperties(current, desired);
                foreach (var binding in desired.Where(binding =>
                    changedProperties.Contains(binding.PropertyName ?? string.Empty, StringComparer.OrdinalIgnoreCase)))
                {
                    var propertyName = NormalizeOptionalString(binding.PropertyName)!;
                    if (!currentAll.TryGetValue(propertyName, out var currentEntry) || currentEntry.Raw == null)
                    {
                        throw new InvalidOperationException($"Interface property '{propertyName}' was not found.");
                    }

                    var valueChanged = !string.Equals(
                        NormalizeOptionalString(currentEntry.Export?.Value),
                        NormalizeOptionalString(binding.Value),
                        StringComparison.Ordinal);

                    try
                    {
                        if (valueChanged)
                        {
                            SetObjectPropertyValueFlexible(currentEntry.Raw, "Value", binding.Value ?? string.Empty);
                        }
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException(
                            $"Interface property '{propertyName}' could not be written on {currentEntry.Raw.GetType().Name}: {FormatExceptionMessage(ex)}",
                            ex);
                    }

                    if (!TryGetNamedComposition(currentEntry.Raw, "Dynamizations", out var dynamizationComposition))
                    {
                        if ((binding.Dynamizations?.Count ?? 0) > 0)
                        {
                            throw new InvalidOperationException($"Interface property '{propertyName}' does not expose Dynamizations.");
                        }

                        continue;
                    }

                    if (!TrySynchronizeDynamizations(dynamizationComposition, binding.Dynamizations ?? new List<HmiJsonDynamization>(), out var dynamizationError))
                    {
                        throw new InvalidOperationException($"Interface property '{propertyName}' dynamizations could not be written: {dynamizationError}");
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                error = FormatExceptionMessage(ex);
                return false;
            }
        }

        private static Dictionary<string, (object Raw, HmiJsonInterfaceBinding? Export, string PropertyName)> ReadRawInterfaceBindings(object composition)
        {
            var result = new Dictionary<string, (object Raw, HmiJsonInterfaceBinding? Export, string PropertyName)>(StringComparer.OrdinalIgnoreCase);
            if (composition is not IEnumerable enumerable)
            {
                return result;
            }

            foreach (var entry in enumerable.Cast<object>())
            {
                var propertyName = NormalizeOptionalString(TryReadStringProperty(entry, "PropertyName"));
                if (string.IsNullOrWhiteSpace(propertyName))
                {
                    continue;
                }

                result[propertyName!] = (entry, TryReadInterfaceBindingExport(entry), propertyName!);
            }

            return result;
        }

        private static Dictionary<string, (object Raw, HmiJsonDynamization? Export, string Property)> ReadRawDynamizations(object composition)
        {
            var result = new Dictionary<string, (object Raw, HmiJsonDynamization? Export, string Property)>(StringComparer.OrdinalIgnoreCase);
            if (composition is not IEnumerable enumerable)
            {
                return result;
            }

            foreach (var entry in enumerable.Cast<object>())
            {
                var propertyName = NormalizeOptionalString(TryReadStringProperty(entry, "PropertyName"));
                if (string.IsNullOrWhiteSpace(propertyName))
                {
                    continue;
                }

                result[propertyName!] = (entry, TryReadDynamizationExport(entry), propertyName!);
            }

            return result;
        }

        private static Type? ResolveDynamizationClrType(string? kind)
        {
            switch (NormalizeDynamizationKind(kind))
            {
                case "Tag":
                    return typeof(TagDynamization);
                case "Script":
                    return typeof(ScriptDynamization);
                case "Flashing":
                    return typeof(FlashingDynamization);
                case "Expression":
                    return typeof(ExpressionDynamization);
                case "ResourceList":
                    return typeof(ResourceListDynamization);
                case "TagParameter":
                    return typeof(TagParameterDynamization);
                default:
                    return null;
            }
        }

        private static string NormalizeDynamizationKind(params string?[] candidates)
        {
            foreach (var candidate in candidates.Where(candidate => !string.IsNullOrWhiteSpace(candidate)))
            {
                var trimmed = candidate!.Trim();
                if (trimmed.EndsWith("Dynamization", StringComparison.OrdinalIgnoreCase))
                {
                    trimmed = trimmed.Substring(0, trimmed.Length - "Dynamization".Length);
                }

                switch (trimmed.ToLowerInvariant())
                {
                    case "tag":
                        return "Tag";
                    case "script":
                        return "Script";
                    case "flashing":
                        return "Flashing";
                    case "expression":
                        return "Expression";
                    case "resourcelist":
                        return "ResourceList";
                    case "tagparameter":
                        return "TagParameter";
                }
            }

            return string.Empty;
        }

        private static object CreateDynamizationObject(object composition, HmiJsonDynamization dynamization)
        {
            var createMethod = composition.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(method =>
                    string.Equals(method.Name, "Create", StringComparison.OrdinalIgnoreCase) &&
                    method.IsGenericMethodDefinition &&
                    method.GetParameters().Length == 1 &&
                    method.GetParameters()[0].ParameterType == typeof(string));
            if (createMethod == null)
            {
                throw new InvalidOperationException("Dynamizations.Create<T>(string) is not available.");
            }

            var dynamizationType = ResolveDynamizationClrType(dynamization.Type)
                ?? throw new InvalidOperationException($"Unsupported dynamization type '{dynamization.Type}'.");
            return createMethod.MakeGenericMethod(dynamizationType).Invoke(composition, new object[] { dynamization.Property })
                ?? throw new InvalidOperationException($"Failed to create dynamization '{dynamization.Property}'.");
        }

        private static void ApplyDynamizationExport(object targetDynamization, HmiJsonDynamization desired)
        {
            switch (NormalizeDynamizationKind(desired.Type, targetDynamization.GetType().Name))
            {
                case "Tag":
                    if (desired.Binding?.Tag != null)
                    {
                        SetObjectPropertyValueFlexible(targetDynamization, "Tag", desired.Binding.Tag);
                    }

                    if (desired.Binding?.ReadOnly.HasValue == true)
                    {
                        SetObjectPropertyValueFlexible(targetDynamization, "ReadOnly", desired.Binding.ReadOnly.Value);
                    }

                    if (desired.Binding?.UseIndirectAddressing.HasValue == true)
                    {
                        SetObjectPropertyValueFlexible(targetDynamization, "UseIndirectAddressing", desired.Binding.UseIndirectAddressing.Value);
                    }

                    ApplyValueConverterExport(TryReadPropertyValue(targetDynamization, "ValueConverter"), desired.ValueConverter);
                    break;
                case "Script":
                    ApplyScriptDynamizationExport(targetDynamization, desired.Script);
                    break;
                case "Flashing":
                    ApplyFlashingDynamizationExport(targetDynamization, desired.Flashing);
                    break;
                case "Expression":
                    ApplyValueConverterExport(TryReadPropertyValue(targetDynamization, "ValueConverter"), desired.ValueConverter);
                    break;
                case "ResourceList":
                    if (desired.Binding?.ResourceList != null)
                    {
                        SetObjectPropertyValueFlexible(targetDynamization, "ResourceList", desired.Binding.ResourceList);
                    }

                    if (desired.Binding?.Tag != null)
                    {
                        SetObjectPropertyValueFlexible(targetDynamization, "Tag", desired.Binding.Tag);
                    }
                    break;
                case "TagParameter":
                    if (desired.Binding?.DynamicTagName != null)
                    {
                        SetObjectPropertyValueFlexible(targetDynamization, "DynamicTagName", desired.Binding.DynamicTagName);
                    }
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported dynamization type '{desired.Type}'.");
            }
        }

        private static void ApplyScriptDynamizationExport(object targetDynamization, HmiJsonScriptDynamization? desired)
        {
            if (desired == null)
            {
                return;
            }

            if (desired.Async.HasValue)
            {
                SetObjectPropertyValueFlexible(targetDynamization, "Async", desired.Async.Value);
            }

            if (desired.GlobalDefinitionAreaScriptCode != null)
            {
                SetObjectPropertyValueFlexible(targetDynamization, "GlobalDefinitionAreaScriptCode", desired.GlobalDefinitionAreaScriptCode);
            }

            if (desired.ScriptCode != null)
            {
                SetObjectPropertyValueFlexible(targetDynamization, "ScriptCode", desired.ScriptCode);
            }

            ApplyScriptTriggerExport(TryReadPropertyValue(targetDynamization, "Trigger"), desired.Trigger);
        }

        private static void ApplyScriptTriggerExport(object? targetTrigger, HmiJsonScriptTrigger? desired)
        {
            if (desired == null)
            {
                return;
            }

            if (targetTrigger == null)
            {
                throw new InvalidOperationException("Trigger is not available.");
            }

            if (desired.Type != null)
            {
                SetObjectPropertyValueFlexible(targetTrigger, "Type", desired.Type);
            }

            if (desired.CustomDuration != null)
            {
                SetObjectPropertyValueFlexible(targetTrigger, "CustomDuration", desired.CustomDuration);
            }

            if (desired.Tags != null)
            {
                SetObjectPropertyValueRaw(targetTrigger, "Tags", desired.Tags.ToArray());
            }
        }

        private static void ApplyFlashingDynamizationExport(object targetDynamization, HmiJsonFlashingDynamization? desired)
        {
            if (desired == null)
            {
                return;
            }

            if (desired.Color != null)
            {
                SetObjectPropertyValueFlexible(targetDynamization, "Color", desired.Color);
            }

            if (desired.AlternateColor != null)
            {
                SetObjectPropertyValueFlexible(targetDynamization, "AlternateColor", desired.AlternateColor);
            }

            if (desired.FlashingCondition != null)
            {
                SetObjectPropertyValueFlexible(targetDynamization, "FlashingCondition", desired.FlashingCondition);
            }

            if (desired.FlashingRate != null)
            {
                SetObjectPropertyValueFlexible(targetDynamization, "FlashingRate", desired.FlashingRate);
            }
        }

        private static void ApplyValueConverterExport(object? valueConverter, HmiJsonValueConverter? desired)
        {
            if (desired == null)
            {
                return;
            }

            if (valueConverter == null)
            {
                throw new InvalidOperationException("ValueConverter is not available.");
            }

            if (desired.IsFormulaSelected == true)
            {
                SetObjectPropertyValueFlexible(valueConverter, "IsFormulaSelected", true);
            }

            if (desired.Formula != null)
            {
                SetObjectPropertyValueFlexible(valueConverter, "Formula", desired.Formula);
            }

            ApplyMappingTableExport(TryReadPropertyValue(valueConverter, "MappingTable"), desired.MappingTable);

            if (desired.IsFormulaSelected == false)
            {
                SetObjectPropertyValueFlexible(valueConverter, "IsFormulaSelected", false);
            }
        }

        private static void ApplyMappingTableExport(object? mappingTable, HmiJsonMappingTable? desired)
        {
            if (mappingTable == null)
            {
                if (desired == null)
                {
                    return;
                }

                throw new InvalidOperationException("MappingTable is not available.");
            }

            var entriesComposition = TryReadPropertyValue(mappingTable, "Entries");
            if (entriesComposition == null)
            {
                throw new InvalidOperationException("MappingTable entries are not available.");
            }

            SetObjectPropertyValueFlexible(mappingTable, "ConditionType", desired?.ConditionType ?? "None");
            DeleteEngineeringCompositionEntries(entriesComposition);

            foreach (var entry in desired?.Entries ?? Enumerable.Empty<HmiJsonMappingTableEntry>())
            {
                var targetEntry = CreateMappingTableEntry(entriesComposition, entry);
                ApplyMappingTableEntryExport(targetEntry, entry);
            }
        }

        private static object CreateMappingTableEntry(object entriesComposition, HmiJsonMappingTableEntry entry)
        {
            var entryKind = NormalizeMappingEntryKind(entry?.EntryType);
            if (string.IsNullOrWhiteSpace(entryKind))
            {
                throw new InvalidOperationException($"Unsupported mapping table entry type '{entry?.EntryType}'.");
            }

            if (string.Equals(entryKind, "Bitmask", StringComparison.OrdinalIgnoreCase))
            {
                var bitmaskCreate = entriesComposition.GetType().GetMethod("Create", new[] { typeof(BitDynamizationType) });
                if (bitmaskCreate != null)
                {
                    var bitTypeName = !string.IsNullOrWhiteSpace(entry?.BitDynamizationType)
                        ? entry!.BitDynamizationType!
                        : BitDynamizationType.SingleBit.ToString();
                    var bitType = Enum.Parse(typeof(BitDynamizationType), bitTypeName, ignoreCase: true);
                    return bitmaskCreate.Invoke(entriesComposition, new[] { bitType })
                        ?? throw new InvalidOperationException($"Failed to create mapping table entry '{entryKind}'.");
                }
            }

            var genericCreate = entriesComposition.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(method =>
                    string.Equals(method.Name, "Create", StringComparison.OrdinalIgnoreCase) &&
                    method.IsGenericMethodDefinition &&
                    method.GetParameters().Length == 0);
            if (genericCreate == null)
            {
                throw new InvalidOperationException("MappingTable entries Create<T>() is not available.");
            }

            var entryType = ResolveMappingTableEntryClrType(entryKind)
                ?? throw new InvalidOperationException($"Unsupported mapping table entry type '{entryKind}'.");
            return genericCreate.MakeGenericMethod(entryType).Invoke(entriesComposition, null)
                ?? throw new InvalidOperationException($"Failed to create mapping table entry '{entryKind}'.");
        }

        private static void ApplyMappingTableEntryExport(object targetEntry, HmiJsonMappingTableEntry desired)
        {
            if (desired.Value != null)
            {
                SetObjectPropertyValueFlexible(targetEntry, "Value", desired.Value);
            }

            if (desired.AlternateValue != null)
            {
                SetObjectPropertyValueFlexible(targetEntry, "AlternateValue", desired.AlternateValue);
            }

            if (desired.Flashing.HasValue)
            {
                SetObjectPropertyValueFlexible(targetEntry, "Flashing", desired.Flashing.Value);
            }

            if (desired.FlashingRate != null)
            {
                SetObjectPropertyValueFlexible(targetEntry, "FlashingRate", desired.FlashingRate);
            }

            switch (NormalizeMappingEntryKind(desired.EntryType))
            {
                case "Simple":
                    if (desired.Condition != null)
                    {
                        SetObjectPropertyValueFlexible(targetEntry, "Condition", desired.Condition);
                    }
                    break;
                case "Range":
                    if (desired.From != null)
                    {
                        SetObjectPropertyValueFlexible(targetEntry, "From", desired.From);
                    }

                    if (desired.To != null)
                    {
                        SetObjectPropertyValueFlexible(targetEntry, "To", desired.To);
                    }

                    if (desired.RangeType != null)
                    {
                        SetObjectPropertyValueFlexible(targetEntry, "RangeType", desired.RangeType);
                    }
                    break;
                case "Bitmask":
                    if (desired.BitDynamizationType != null)
                    {
                        SetObjectPropertyValueFlexible(targetEntry, "BitDynamizationType", desired.BitDynamizationType);
                    }

                    if (desired.Condition != null)
                    {
                        SetObjectPropertyValueFlexible(targetEntry, "Condition", desired.Condition);
                    }

                    if (desired.Relevant != null)
                    {
                        SetObjectPropertyValueFlexible(targetEntry, "Relevant", desired.Relevant);
                    }
                    break;
            }
        }

        private static void DeleteEngineeringCompositionEntries(object composition)
        {
            if (composition is not IEnumerable enumerable)
            {
                return;
            }

            foreach (var entry in enumerable.Cast<object>().ToList())
            {
                DeleteEngineeringObject(entry);
            }
        }

        private static bool HasMeaningfulMappingTable(HmiJsonMappingTable? mappingTable)
        {
            return mappingTable != null &&
                   ((!string.IsNullOrWhiteSpace(mappingTable.ConditionType) &&
                     !mappingTable.ConditionType!.Equals("None", StringComparison.OrdinalIgnoreCase)) ||
                    (mappingTable.Entries?.Count ?? 0) > 0);
        }

        private static string NormalizeMappingEntryKind(string? typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
            {
                return string.Empty;
            }

            var trimmed = typeName!.Trim();
            if (trimmed.StartsWith("MappingTableEntry", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring("MappingTableEntry".Length);
            }

            switch (trimmed.ToLowerInvariant())
            {
                case "simple":
                    return "Simple";
                case "range":
                    return "Range";
                case "bitmask":
                    return "Bitmask";
                default:
                    return string.Empty;
            }
        }

        private static Type? ResolveMappingTableEntryClrType(string? kind)
        {
            switch (NormalizeMappingEntryKind(kind))
            {
                case "Simple":
                    return typeof(MappingTableEntrySimple);
                case "Range":
                    return typeof(MappingTableEntryRange);
                case "Bitmask":
                    return typeof(MappingTableEntryBitmask);
                default:
                    return null;
            }
        }

        private static bool HasMeaningfulBinding(HmiJsonDynamizationBinding? binding)
        {
            return binding != null &&
                   (!string.IsNullOrWhiteSpace(binding.Tag) ||
                    binding.ReadOnly.HasValue ||
                    binding.UseIndirectAddressing.HasValue ||
                    !string.IsNullOrWhiteSpace(binding.ResourceList) ||
                    !string.IsNullOrWhiteSpace(binding.DynamicTagName));
        }

        private static List<string>? ReadStringListValue(object? value)
        {
            if (value == null)
            {
                return null;
            }

            if (value is string stringValue)
            {
                var normalized = NormalizeOptionalString(stringValue);
                return normalized == null ? null : new List<string> { normalized };
            }

            if (value is not IEnumerable enumerable)
            {
                return null;
            }

            var result = enumerable
                .Cast<object>()
                .Select(entry => NormalizeOptionalString(Convert.ToString(entry, CultureInfo.InvariantCulture)))
                .Where(entry => !string.IsNullOrWhiteSpace(entry))
                .Cast<string>()
                .ToList();
            return result.Count > 0 ? result : null;
        }

        private static string? TryReadStringProperty(object? source, string propertyName)
        {
            return NormalizeOptionalString(FormatValue(TryReadPropertyValue(source, propertyName)));
        }

        private static string? TryReadConvertedStringProperty(object? source, string propertyName)
        {
            return TryReadStringProperty(source, propertyName);
        }

        private static bool? TryReadNullableBoolProperty(object? source, string propertyName)
        {
            var value = TryReadPropertyValue(source, propertyName);
            if (value == null)
            {
                return null;
            }

            if (value is bool boolValue)
            {
                return boolValue;
            }

            return bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out var parsed)
                ? parsed
                : null;
        }

        private static string? NormalizeOptionalString(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value?.Trim();
        }

        private static string FormatExceptionMessage(Exception ex)
        {
            var messages = new List<string>();
            for (var current = ex; current != null; current = current.InnerException)
            {
                if (!string.IsNullOrWhiteSpace(current.Message) &&
                    !messages.Contains(current.Message, StringComparer.Ordinal))
                {
                    messages.Add(current.Message);
                }
            }

            return messages.Count == 0 ? ex.GetType().Name : string.Join(" | Inner: ", messages);
        }

        private static void DeleteEngineeringObject(object target)
        {
            var method = target.GetType().GetMethod("Delete", BindingFlags.Public | BindingFlags.Instance);
            if (method == null)
            {
                throw new InvalidOperationException($"Type '{target.GetType().Name}' does not expose Delete().");
            }

            method.Invoke(target, null);
        }

        private static void SetObjectPropertyValueFlexible(object targetObject, string propertyName, object rawValue)
        {
            if (targetObject == null || string.IsNullOrWhiteSpace(propertyName))
            {
                throw new InvalidOperationException("Invalid property target.");
            }

            var property = ResolvePublicInstanceProperty(targetObject.GetType(), propertyName);

            if (property != null && property.CanWrite)
            {
                if (property.PropertyType.IsInstanceOfType(rawValue))
                {
                    property.SetValue(targetObject, rawValue);
                    return;
                }

                if (rawValue is string stringValue)
                {
                    property.SetValue(targetObject, ConvertInvariantStringToType(stringValue, property.PropertyType));
                    return;
                }

                property.SetValue(
                    targetObject,
                    Convert.ChangeType(rawValue, Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType, CultureInfo.InvariantCulture));
                return;
            }

            if (property != null)
            {
                if (rawValue is string stringValue)
                {
                    SetAttributeValue(targetObject, propertyName, ConvertInvariantStringToType(stringValue, property.PropertyType));
                    return;
                }

                if (property.PropertyType.IsInstanceOfType(rawValue))
                {
                    SetAttributeValue(targetObject, propertyName, rawValue);
                    return;
                }

                SetAttributeValue(
                    targetObject,
                    propertyName,
                    Convert.ChangeType(rawValue, Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType, CultureInfo.InvariantCulture));
                return;
            }

            SetAttributeValue(targetObject, propertyName, rawValue);
        }

        private static void SetObjectPropertyValueRaw(object targetObject, string propertyName, object rawValue)
        {
            if (targetObject == null || string.IsNullOrWhiteSpace(propertyName))
            {
                throw new InvalidOperationException("Invalid property target.");
            }

            var property = ResolvePublicInstanceProperty(targetObject.GetType(), propertyName);

            if (property != null && property.CanWrite)
            {
                if (property.PropertyType.IsInstanceOfType(rawValue))
                {
                    property.SetValue(targetObject, rawValue);
                    return;
                }

                if (property.PropertyType == typeof(string[]) && rawValue is IEnumerable<string> stringValues)
                {
                    property.SetValue(targetObject, stringValues.ToArray());
                    return;
                }
            }

            SetAttributeValue(targetObject, propertyName, rawValue);
        }

        private static bool TryValidateObjectPropertyValue(Type targetType, string propertyName, object? rawValue, out string? error)
        {
            error = null;
            if (rawValue == null)
            {
                return true;
            }

            var property = ResolvePublicInstanceProperty(targetType, propertyName);
            if (property == null)
            {
                return true;
            }

            try
            {
                if (property.PropertyType.IsInstanceOfType(rawValue))
                {
                    return true;
                }

                if (rawValue is string stringValue)
                {
                    ConvertInvariantStringToType(stringValue, property.PropertyType);
                    return true;
                }

                Convert.ChangeType(rawValue, Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType, CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception ex)
            {
                error = $"{propertyName}: {ex.InnerException?.Message ?? ex.Message}";
                return false;
            }
        }

        private static object ConvertInvariantStringToType(string rawValue, Type targetType)
        {
            return EngineeringStringConverter.ConvertInvariant(rawValue, targetType, ConvertHmiPrimitiveString);
        }

        private IReadOnlyList<HmiItemPropertyInfo> ReadBrowsableProperties(object target)
        {
            var properties = new List<HmiItemPropertyInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var property in target.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!property.CanRead || property.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                if (!TryFormatPropertyValue(target, property, out var value))
                {
                    continue;
                }

                if (!seen.Add(property.Name))
                {
                    continue;
                }

                properties.Add(new HmiItemPropertyInfo
                {
                    Name = property.Name,
                    Value = value,
                    ValueType = property.PropertyType.Name,
                    IsWritable = property.CanWrite || IsMultilingualText(property.PropertyType),
                    Source = "Property"
                });
            }

            return properties;
        }

        private IReadOnlyList<HmiItemPropertyInfo> ReadAttributeProperties(object target)
        {
            var properties = new List<HmiItemPropertyInfo>();

            foreach (var info in GetAttributeInfos(target))
            {
                try
                {
                    var value = GetAttributeValue(target, info.Name);
                    properties.Add(new HmiItemPropertyInfo
                    {
                        Name = info.Name,
                        Value = FormatValue(value),
                        ValueType = info.SupportedTypes?.FirstOrDefault()?.Name ?? "Attribute",
                        IsWritable = info.AccessMode == EngineeringAttributeAccessMode.Write ||
                                     info.AccessMode == EngineeringAttributeAccessMode.ReadWrite,
                        Source = "Attribute"
                    });
                }
                catch
                {
                    // Ignore individual attribute read failures.
                }
            }

            return properties;
        }

        private bool TryReadPropertyOrAttribute(object target, string propertyName, out HmiItemPropertyInfo property)
        {
            var resolvedProperty = ResolveProperty(target, propertyName);
            if (resolvedProperty != null && TryFormatPropertyValue(resolvedProperty.Target, resolvedProperty.PropertyInfo, out var propertyValue))
            {
                property = new HmiItemPropertyInfo
                {
                    Name = propertyName,
                    Value = propertyValue,
                    ValueType = resolvedProperty.PropertyInfo.PropertyType.Name,
                    IsWritable = resolvedProperty.PropertyInfo.CanWrite || IsMultilingualText(resolvedProperty.PropertyInfo.PropertyType),
                    Source = "Property"
                };

                return true;
            }

            var resolvedAttribute = ResolveAttribute(target, propertyName);
            if (resolvedAttribute != null)
            {
                property = new HmiItemPropertyInfo
                {
                    Name = propertyName,
                    Value = FormatValue(GetAttributeValue(resolvedAttribute.Target, resolvedAttribute.AttributeInfo.Name)),
                    ValueType = resolvedAttribute.AttributeInfo.SupportedTypes?.FirstOrDefault()?.Name ?? "Attribute",
                    IsWritable = resolvedAttribute.AttributeInfo.AccessMode == EngineeringAttributeAccessMode.Write ||
                                 resolvedAttribute.AttributeInfo.AccessMode == EngineeringAttributeAccessMode.ReadWrite,
                    Source = "Attribute"
                };
                return true;
            }

            property = null!;
            return false;
        }

        private bool TrySetPropertyPathValue(object target, string propertyPath, string rawValue, HmiSoftware software)
        {
            var resolvedProperty = ResolveProperty(target, propertyPath);
            if (resolvedProperty != null)
            {
                if (IsMultilingualText(resolvedProperty.PropertyInfo.PropertyType))
                {
                    var multilingualText = resolvedProperty.PropertyInfo.GetValue(resolvedProperty.Target) as MultilingualText;
                    if (multilingualText == null)
                    {
                        throw new InvalidOperationException($"Multilingual text property '{propertyPath}' is not initialized.");
                    }

                    SetMultilingualText(multilingualText, rawValue);
                    return true;
                }

                if (!resolvedProperty.PropertyInfo.CanWrite)
                {
                    throw new InvalidOperationException($"Property '{propertyPath}' is read-only.");
                }

                if ((Nullable.GetUnderlyingType(resolvedProperty.PropertyInfo.PropertyType) ?? resolvedProperty.PropertyInfo.PropertyType) == typeof(string))
                {
                    resolvedProperty.PropertyInfo.SetValue(
                        resolvedProperty.Target,
                        NormalizeStringPropertyValue(resolvedProperty.Target, resolvedProperty.PropertyInfo, rawValue));
                    return true;
                }

                var convertedValue = ConvertStringToType(rawValue, resolvedProperty.PropertyInfo.PropertyType, software);
                resolvedProperty.PropertyInfo.SetValue(resolvedProperty.Target, convertedValue);
                return true;
            }

            var resolvedAttribute = ResolveAttribute(target, propertyPath);
            if (resolvedAttribute == null)
            {
                return false;
            }

            if (resolvedAttribute.AttributeInfo.AccessMode != EngineeringAttributeAccessMode.Write &&
                resolvedAttribute.AttributeInfo.AccessMode != EngineeringAttributeAccessMode.ReadWrite)
            {
                throw new InvalidOperationException($"Attribute '{propertyPath}' is not writable.");
            }

            var convertedAttributeValue = ConvertStringToAttributeValue(
                rawValue,
                resolvedAttribute.Target,
                resolvedAttribute.AttributeInfo,
                software);
            SetAttributeValue(resolvedAttribute.Target, resolvedAttribute.AttributeInfo.Name, convertedAttributeValue);
            return true;
        }

        private static void TryEnsureSolidBackFillPattern(object target)
        {
            var property = ResolvePublicInstanceProperty(target.GetType(), "BackFillPattern");
            if (property == null || !property.CanWrite || !property.PropertyType.IsEnum)
            {
                return;
            }

            var solidValue = Enum.GetNames(property.PropertyType)
                .FirstOrDefault(name => string.Equals(name, "Solid", StringComparison.OrdinalIgnoreCase));
            if (solidValue == null)
            {
                return;
            }

            property.SetValue(target, Enum.Parse(property.PropertyType, solidValue, ignoreCase: true));
        }

        private static ResolvedProperty? ResolveProperty(object target, string propertyPath)
        {
            var segments = propertyPath.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                return null;
            }

            object currentTarget = target;
            for (int index = 0; index < segments.Length; index++)
            {
                var segment = segments[index];
                var property = currentTarget.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(candidate => string.Equals(candidate.Name, segment, StringComparison.OrdinalIgnoreCase));

                if (property == null)
                {
                    return null;
                }

                var isLeaf = index == segments.Length - 1;
                if (isLeaf)
                {
                    return new ResolvedProperty(currentTarget, property);
                }

                currentTarget = property.GetValue(currentTarget)
                    ?? throw new InvalidOperationException($"Nested property '{string.Join(".", segments.Take(index + 1))}' is null.");
            }

            return null;
        }

        private static ResolvedAttribute? ResolveAttribute(object target, string attributePath)
        {
            var segments = attributePath.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                return null;
            }

            object currentTarget = target;
            for (int index = 0; index < segments.Length - 1; index++)
            {
                var property = currentTarget.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(candidate => string.Equals(candidate.Name, segments[index], StringComparison.OrdinalIgnoreCase));
                if (property == null || !property.CanRead)
                {
                    return null;
                }

                currentTarget = property.GetValue(currentTarget)
                    ?? throw new InvalidOperationException($"Nested property '{string.Join(".", segments.Take(index + 1))}' is null.");
            }

            var attributeName = segments.Last();
            var attributeInfo = GetAttributeInfos(currentTarget)
                .FirstOrDefault(info => string.Equals(info.Name, attributeName, StringComparison.OrdinalIgnoreCase));
            return attributeInfo == null ? null : new ResolvedAttribute(currentTarget, attributeInfo);
        }

        private static bool TryFormatPropertyValue(object target, PropertyInfo property, out string? value)
        {
            value = null;

            object? rawValue;
            try
            {
                rawValue = property.GetValue(target);
            }
            catch
            {
                return false;
            }

            value = FormatValue(rawValue);
            return value != null;
        }

        private static string? FormatValue(object? value)
        {
            if (value == null)
            {
                return null;
            }

            if (value is string stringValue)
            {
                return UnifiedRichTextCodec.Decode(stringValue);
            }

            if (value is MultilingualText multilingualText)
            {
                return ReadMultilingualText(multilingualText);
            }

            if (value is Color color)
            {
                return ColorTranslator.ToHtml(color);
            }

            if (value is Enum enumValue)
            {
                return enumValue.ToString();
            }

            if (value is bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal)
            {
                return Convert.ToString(value, CultureInfo.InvariantCulture);
            }

            return value.ToString();
        }

        private object ConvertStringToAttributeValue(string rawValue, object target, EngineeringAttributeInfo attributeInfo, HmiSoftware software)
        {
            var supportedType = attributeInfo.SupportedTypes?.FirstOrDefault() ?? typeof(string);
            var convertedValue = ConvertStringToType(rawValue, supportedType, software);
            if (convertedValue is string stringValue)
            {
                return NormalizeStringAttributeValue(target, attributeInfo.Name, stringValue);
            }

            return convertedValue;
        }

        private object ConvertStringToType(string rawValue, Type targetType, HmiSoftware software)
        {
            return EngineeringStringConverter.ConvertInvariant(
                rawValue,
                targetType,
                (underlyingType, value) =>
                {
                    var hmiValue = ConvertHmiPrimitiveString(underlyingType, value);
                    if (hmiValue != null)
                    {
                        return hmiValue;
                    }

                    if (typeof(HmiScreen).IsAssignableFrom(underlyingType))
                    {
                        if (!TryGetScreen(software, value, out var screen, out _) || screen == null)
                        {
                            throw new InvalidOperationException($"Referenced screen '{value}' was not found.");
                        }

                        return screen;
                    }

                    return null;
                });
        }

        private static object? ConvertHmiPrimitiveString(Type underlyingType, string rawValue)
        {
            return underlyingType == typeof(Color) ? ParseColor(rawValue) : null;
        }

        private static void SetMultilingualText(MultilingualText multilingualText, string value)
        {
            var items = multilingualText.Items.Cast<MultilingualTextItem>().ToList();
            if (items.Count == 0)
            {
                throw new InvalidOperationException("The multilingual text value is not initialized.");
            }

            foreach (var item in items)
            {
                item.Text = UnifiedRichTextCodec.Encode(value);
            }
        }

        private static string? ReadMultilingualText(MultilingualText multilingualText)
        {
            return multilingualText.Items
                .Cast<MultilingualTextItem>()
                .Select(item => UnifiedRichTextCodec.Decode(item.Text))
                .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text))
                ?? string.Empty;
        }

        private static IEnumerable<EngineeringAttributeInfo> GetAttributeInfos(object target)
        {
            var method = target.GetType().GetMethod("GetAttributeInfos", Type.EmptyTypes);
            var value = method?.Invoke(target, null);
            if (value is IEnumerable<EngineeringAttributeInfo> typedInfos)
            {
                return typedInfos;
            }

            if (value is IEnumerable fallbackInfos)
            {
                return fallbackInfos.Cast<object>().OfType<EngineeringAttributeInfo>().ToList();
            }

            return Array.Empty<EngineeringAttributeInfo>();
        }

        private static object? GetAttributeValue(object target, string attributeName)
        {
            var method = target.GetType().GetMethod("GetAttribute", new[] { typeof(string) });
            return method?.Invoke(target, new object[] { attributeName });
        }

        private static void SetAttributeValue(object target, string attributeName, object value)
        {
            var method = target.GetType().GetMethod("SetAttribute", new[] { typeof(string), typeof(object) });
            if (method == null)
            {
                throw new InvalidOperationException($"Target type '{target.GetType().Name}' does not support SetAttribute.");
            }

            method.Invoke(target, new[] { (object)attributeName, value });
        }

        private static int? TryReadIntAttribute(object target, string attributeName)
        {
            try
            {
                var value = GetAttributeValue(target, attributeName);
                if (value == null)
                {
                    return null;
                }

                if (value is uint uintValue)
                {
                    return unchecked((int)uintValue);
                }

                if (value is int intValue)
                {
                    return intValue;
                }

                return int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private bool TryResolveItemType(string itemType, out HmiItemTypeDescriptor descriptor)
        {
            if (_itemTypes.TryGetValue(NormalizeItemTypeName(itemType), out descriptor))
            {
                return true;
            }

            if (DynamicItemTypes.Value.TryGetValue(NormalizeItemTypeName(itemType), out var dynamicType))
            {
                descriptor = new HmiItemTypeDescriptor(dynamicType, dynamicType.Name, "Advanced", dynamicType.Name, 120, 40);
                return true;
            }

            descriptor = null!;
            return false;
        }

        private static Dictionary<string, Type> BuildDynamicItemTypeMap()
        {
            IEnumerable<Type> types;

            try
            {
                types = typeof(HmiScreenItemBase).Assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(type => type != null)!;
            }

            return types
                .Where(type =>
                    type != null &&
                    !type.IsAbstract &&
                    typeof(HmiScreenItemBase).IsAssignableFrom(type) &&
                    !type.IsGenericTypeDefinition)
                .ToDictionary(type => NormalizeToken(type.Name), type => type, StringComparer.OrdinalIgnoreCase);
        }

        private static void TryCopyDirectProperty(object source, object target, string propertyName)
        {
            var sourceProperty = ResolvePublicInstanceProperty(source.GetType(), propertyName);
            var targetProperty = ResolvePublicInstanceProperty(target.GetType(), propertyName);
            if (sourceProperty == null || targetProperty == null || !sourceProperty.CanRead || !targetProperty.CanWrite)
            {
                return;
            }

            try
            {
                var value = sourceProperty.GetValue(source);
                if (value != null)
                {
                    targetProperty.SetValue(target, value);
                }
            }
            catch
            {
                // Ignore best-effort direct copy failures.
            }
        }

        private static void CopyWritableAttributes(object source, object target, string? findText, string? replaceText)
        {
            foreach (var attribute in GetAttributeInfos(source))
            {
                if (AttributeCopyExclusions.Contains(attribute.Name) ||
                    (attribute.AccessMode != EngineeringAttributeAccessMode.Write &&
                     attribute.AccessMode != EngineeringAttributeAccessMode.ReadWrite))
                {
                    continue;
                }

                object? value;
                try
                {
                    value = GetAttributeValue(source, attribute.Name);
                }
                catch
                {
                    continue;
                }

                if (value == null || value is IEnumerable and not string)
                {
                    continue;
                }

                try
                {
                    var copiedValue = ApplyReplacementToValue(value, findText, replaceText);
                    if (copiedValue is string stringValue)
                    {
                        copiedValue = NormalizeStringAttributeValue(target, attribute.Name, stringValue);
                    }

                    SetAttributeValue(target, attribute.Name, copiedValue);
                }
                catch
                {
                    // Ignore individual attribute copy failures.
                }
            }
        }

        private static void CopyPropertyGraph(
            object source,
            object target,
            HashSet<string> rootExclusions,
            string? findText,
            string? replaceText,
            int depth,
            HashSet<object> visited)
        {
            if (depth > 4 || !visited.Add(source))
            {
                return;
            }

            CopyWritableAttributes(source, target, findText, replaceText);

            foreach (var property in source.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!property.CanRead ||
                    property.GetIndexParameters().Length > 0 ||
                    property.Name.Equals("Parent", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Equals("EventHandlers", StringComparison.OrdinalIgnoreCase) ||
                    (depth == 0 && rootExclusions.Contains(property.Name)))
                {
                    continue;
                }

                object? sourceValue;
                try
                {
                    sourceValue = property.GetValue(source);
                }
                catch
                {
                    continue;
                }

                if (sourceValue == null)
                {
                    continue;
                }

                if (property.PropertyType != typeof(string) && typeof(IEnumerable).IsAssignableFrom(property.PropertyType))
                {
                    TryCopyComposition(
                        sourceValue,
                        TryReadPropertyValue(target, property.Name),
                        findText,
                        replaceText,
                        depth + 1,
                        visited);
                    continue;
                }

                if (IsMultilingualText(property.PropertyType))
                {
                    try
                    {
                        if (property.GetValue(target) is MultilingualText targetText &&
                            sourceValue is MultilingualText sourceText)
                        {
                            CopyMultilingualText(sourceText, targetText, findText, replaceText);
                        }
                    }
                    catch
                    {
                        // Ignore best-effort multilingual copy failures.
                    }

                    continue;
                }

                if (TryAssignPropertyValue(target, property, sourceValue, findText, replaceText))
                {
                    continue;
                }

                if (depth >= 4 || property.PropertyType == typeof(string) || property.PropertyType.IsValueType)
                {
                    continue;
                }

                object? targetValue;
                try
                {
                    targetValue = property.GetValue(target);
                }
                catch
                {
                    continue;
                }

                if (targetValue == null)
                {
                    continue;
                }

                CopyPropertyGraph(sourceValue, targetValue, rootExclusions, findText, replaceText, depth + 1, visited);
            }
        }

        private static bool TryAssignPropertyValue(object target, PropertyInfo property, object sourceValue, string? findText, string? replaceText)
        {
            if (!property.CanWrite)
            {
                return false;
            }

            var targetProperty = ResolvePublicInstanceProperty(target.GetType(), property.Name);
            if (targetProperty == null || !targetProperty.CanWrite)
            {
                return false;
            }

            var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (!IsTerminalCopyType(propertyType))
            {
                return false;
            }

            try
            {
                var assignedValue = ApplyReplacementToValue(sourceValue, findText, replaceText);
                if (assignedValue is string stringValue && propertyType == typeof(string))
                {
                    assignedValue = NormalizeStringPropertyValue(target, targetProperty, stringValue);
                }

                targetProperty.SetValue(target, assignedValue);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsTerminalCopyType(Type propertyType)
        {
            return propertyType == typeof(string) ||
                   propertyType == typeof(bool) ||
                   propertyType == typeof(byte) ||
                   propertyType == typeof(sbyte) ||
                   propertyType == typeof(short) ||
                   propertyType == typeof(ushort) ||
                   propertyType == typeof(int) ||
                   propertyType == typeof(uint) ||
                   propertyType == typeof(long) ||
                   propertyType == typeof(ulong) ||
                   propertyType == typeof(float) ||
                   propertyType == typeof(double) ||
                   propertyType == typeof(decimal) ||
                   propertyType == typeof(TimeSpan) ||
                   propertyType == typeof(DateTime) ||
                   propertyType == typeof(Color) ||
                   propertyType.IsEnum ||
                   typeof(HmiScreen).IsAssignableFrom(propertyType);
        }

        private static object ApplyReplacementToValue(object value, string? findText, string? replaceText)
        {
            if (value is string stringValue)
            {
                return ApplyReplacement(stringValue, findText, replaceText);
            }

            return value;
        }

        private static string ApplyReplacement(string value, string? findText, string? replaceText)
        {
            if (string.IsNullOrEmpty(findText))
            {
                return value;
            }

            return value.Replace(findText, replaceText ?? string.Empty);
        }

        private static void CopyMultilingualText(MultilingualText source, MultilingualText target, string? findText, string? replaceText)
        {
            var sourceItems = source.Items.Cast<MultilingualTextItem>().ToList();
            var targetItems = target.Items.Cast<MultilingualTextItem>().ToList();
            var count = Math.Min(sourceItems.Count, targetItems.Count);

            for (var index = 0; index < count; index++)
            {
                targetItems[index].Text = UnifiedRichTextCodec.Encode(
                    ApplyReplacement(UnifiedRichTextCodec.Decode(sourceItems[index].Text), findText, replaceText));
            }
        }

        private static void TryCopyComposition(
            object? sourceComposition,
            object? targetComposition,
            string? findText,
            string? replaceText,
            int depth,
            HashSet<object> visited)
        {
            if (depth > 4 ||
                sourceComposition is not IEnumerable sourceEnumerable ||
                targetComposition is not IEnumerable)
            {
                return;
            }

            var sourceItems = sourceEnumerable.Cast<object>().ToList();
            if (sourceItems.Count == 0)
            {
                return;
            }

            EnsureCompositionItems(targetComposition, sourceItems.Count, sourceItems);
            var targetItems = ((IEnumerable)targetComposition).Cast<object>().ToList();
            if (targetItems.Count == 0)
            {
                return;
            }

            var assignedTargets = new HashSet<object>(ReferenceEqualityComparer.Instance);
            foreach (var sourceItem in sourceItems)
            {
                var targetItem = FindMatchingCompositionItem(sourceItem, targetItems, assignedTargets);
                if (targetItem == null)
                {
                    continue;
                }

                assignedTargets.Add(targetItem);
                CopyPropertyGraph(sourceItem, targetItem, EmptyRootPropertyExclusions, findText, replaceText, depth, visited);
                CopyEventHandlers(sourceItem, targetItem, findText, replaceText);
            }
        }

        private static void EnsureCompositionItems(object targetComposition, int requiredCount, IReadOnlyList<object> sourceItems)
        {
            var targetItems = ((IEnumerable)targetComposition).Cast<object>().ToList();
            if (targetItems.Count >= requiredCount)
            {
                return;
            }

            var createMethods = targetComposition.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(method => string.Equals(method.Name, "Create", StringComparison.Ordinal))
                .ToList();
            if (createMethods.Count == 0)
            {
                return;
            }

            for (var index = targetItems.Count; index < requiredCount; index++)
            {
                if (!TryCreateCompositionItem(targetComposition, sourceItems[index], createMethods))
                {
                    return;
                }
            }
        }

        private static bool TryCreateCompositionItem(object targetComposition, object sourceItem, IReadOnlyList<MethodInfo> createMethods)
        {
            var sourceName = TryReadPropertyValue(sourceItem, "Name") as string;

            foreach (var method in createMethods.OrderBy(method => method.GetParameters().Length))
            {
                var parameters = method.GetParameters();

                try
                {
                    if (parameters.Length == 0)
                    {
                        method.Invoke(targetComposition, Array.Empty<object>());
                        return true;
                    }

                    if (parameters.Length == 1 &&
                        parameters[0].ParameterType == typeof(string) &&
                        !string.IsNullOrWhiteSpace(sourceName))
                    {
                        method.Invoke(targetComposition, new object[] { sourceName! });
                        return true;
                    }
                }
                catch
                {
                    // Ignore best-effort composition creation failures.
                }
            }

            return false;
        }

        private static object? FindMatchingCompositionItem(
            object sourceItem,
            IReadOnlyList<object> targetItems,
            HashSet<object> assignedTargets)
        {
            var sourceName = TryReadPropertyValue(sourceItem, "Name") as string;
            if (!string.IsNullOrWhiteSpace(sourceName))
            {
                foreach (var targetItem in targetItems)
                {
                    if (assignedTargets.Contains(targetItem))
                    {
                        continue;
                    }

                    var targetName = TryReadPropertyValue(targetItem, "Name") as string;
                    if (string.Equals(sourceName, targetName, StringComparison.OrdinalIgnoreCase))
                    {
                        return targetItem;
                    }
                }
            }

            return targetItems.FirstOrDefault(targetItem => !assignedTargets.Contains(targetItem));
        }

        private static int CountEventHandlers(object owner)
        {
            var eventHandlers = TryReadPropertyValue(owner, "EventHandlers");
            if (eventHandlers == null)
            {
                return 0;
            }

            var countProperty = eventHandlers.GetType().GetProperty("Count", BindingFlags.Public | BindingFlags.Instance);
            if (countProperty?.GetValue(eventHandlers) is int count)
            {
                return count;
            }

            return eventHandlers is IEnumerable enumerable
                ? enumerable.Cast<object>().Count()
                : 0;
        }

        private static void CopyEventHandlers(object sourceOwner, object targetOwner, string? findText, string? replaceText)
        {
            var sourceHandlers = TryReadPropertyValue(sourceOwner, "EventHandlers");
            var targetHandlers = TryReadPropertyValue(targetOwner, "EventHandlers");
            if (sourceHandlers is not IEnumerable sourceEnumerable || targetHandlers == null)
            {
                return;
            }

            var createMethod = targetHandlers.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(method =>
                    string.Equals(method.Name, "Create", StringComparison.Ordinal) &&
                    method.GetParameters().Length == 1);
            if (createMethod == null)
            {
                return;
            }

            foreach (var sourceHandler in sourceEnumerable.Cast<object>())
            {
                var eventType = TryReadPropertyValue(sourceHandler, "EventType");
                if (eventType == null)
                {
                    continue;
                }

                object? targetHandler;
                try
                {
                    targetHandler = createMethod.Invoke(targetHandlers, new[] { eventType });
                }
                catch
                {
                    continue;
                }

                CopyScript(
                    TryReadPropertyValue(sourceHandler, "Script"),
                    TryReadPropertyValue(targetHandler, "Script"),
                    findText,
                    replaceText);
            }
        }

        private static void CopyScript(object? sourceScript, object? targetScript, string? findText, string? replaceText)
        {
            if (sourceScript == null || targetScript == null)
            {
                return;
            }

            TryCopyScriptProperty(sourceScript, targetScript, "Async", null, null);
            TryCopyScriptProperty(sourceScript, targetScript, "GlobalDefinitionAreaScriptCode", findText, replaceText);
            TryCopyScriptProperty(sourceScript, targetScript, "ScriptCode", findText, replaceText);
        }

        private static void TryCopyScriptProperty(object source, object target, string propertyName, string? findText, string? replaceText)
        {
            var sourceProperty = ResolvePublicInstanceProperty(source.GetType(), propertyName);
            var targetProperty = ResolvePublicInstanceProperty(target.GetType(), propertyName);
            if (sourceProperty == null || targetProperty == null || !sourceProperty.CanRead || !targetProperty.CanWrite)
            {
                return;
            }

            try
            {
                var value = sourceProperty.GetValue(source);
                if (value is string stringValue)
                {
                    targetProperty.SetValue(target, ApplyReplacement(stringValue, findText, replaceText));
                    return;
                }

                if (value != null)
                {
                    targetProperty.SetValue(target, value);
                }
            }
            catch
            {
                // Ignore best-effort script copy failures.
            }
        }

        private static object? TryReadPropertyValue(object? source, string propertyName)
        {
            if (source == null)
            {
                return null;
            }

            var property = ResolvePublicInstanceProperty(source.GetType(), propertyName);
            if (property == null || !property.CanRead)
            {
                return null;
            }

            try
            {
                return property.GetValue(source);
            }
            catch
            {
                return null;
            }
        }

        private static PropertyInfo? ResolvePublicInstanceProperty(Type type, string propertyName)
        {
            if (type == null || string.IsNullOrWhiteSpace(propertyName))
            {
                return null;
            }

            var candidates = type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (candidates.Count == 0)
            {
                return null;
            }

            var exact = candidates
                .Where(property => property.Name.Equals(propertyName, StringComparison.Ordinal))
                .ToList();
            if (exact.Count == 1)
            {
                return exact[0];
            }

            if (exact.Count > 1)
            {
                candidates = exact;
            }

            return candidates
                .OrderBy(property => GetInheritanceDistance(type, property.DeclaringType))
                .ThenByDescending(property => property.CanRead)
                .ThenByDescending(property => property.CanWrite)
                .FirstOrDefault();
        }

        private static int GetInheritanceDistance(Type sourceType, Type? declaringType)
        {
            var distance = 0;
            for (var current = sourceType; current != null; current = current.BaseType)
            {
                if (current == declaringType)
                {
                    return distance;
                }

                distance++;
            }

            return int.MaxValue;
        }

        private static void TryDelete(object? target)
        {
            if (target == null)
            {
                return;
            }

            try
            {
                target.GetType().GetMethod("Delete", BindingFlags.Public | BindingFlags.Instance)?.Invoke(target, null);
            }
            catch
            {
                // Ignore cleanup failures after a failed copy.
            }
        }

        private static bool IsMultilingualText(Type type)
        {
            return type == typeof(MultilingualText);
        }

        private static string NormalizeStringPropertyValue(object target, PropertyInfo property, string value)
        {
            if (property.PropertyType != typeof(string))
            {
                return value;
            }

            try
            {
                return property.GetValue(target) is string currentValue && UnifiedRichTextCodec.LooksLikeRichText(currentValue)
                    ? UnifiedRichTextCodec.Encode(value)
                    : value;
            }
            catch
            {
                return value;
            }
        }

        private static string NormalizeStringAttributeValue(object target, string attributeName, string value)
        {
            try
            {
                return GetAttributeValue(target, attributeName) is string currentValue && UnifiedRichTextCodec.LooksLikeRichText(currentValue)
                    ? UnifiedRichTextCodec.Encode(value)
                    : value;
            }
            catch
            {
                return value;
            }
        }

        private static Color ParseColor(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException("Color value cannot be empty.");
            }

            if (value.StartsWith("#", StringComparison.Ordinal))
            {
                return ColorTranslator.FromHtml(value);
            }

            var namedColor = Color.FromName(value);
            if (namedColor.IsKnownColor || namedColor.IsNamedColor || namedColor.A > 0)
            {
                return namedColor;
            }

            throw new InvalidOperationException($"Unsupported color value '{value}'.");
        }

        private sealed class ResolvedProperty
        {
            public ResolvedProperty(object target, PropertyInfo propertyInfo)
            {
                Target = target;
                PropertyInfo = propertyInfo;
            }

            public object Target { get; }

            public PropertyInfo PropertyInfo { get; }
        }

        private sealed class ResolvedAttribute
        {
            public ResolvedAttribute(object target, EngineeringAttributeInfo attributeInfo)
            {
                Target = target;
                AttributeInfo = attributeInfo;
            }

            public object Target { get; }

            public EngineeringAttributeInfo AttributeInfo { get; }
        }

        private static Dictionary<string, HmiItemTypeDescriptor> BuildItemTypeMap()
        {
            var descriptors = new[]
            {
                new HmiItemTypeDescriptor(typeof(HmiButton), "HmiButton", "Controls", "Button control for user interaction", 120, 40, "button"),
                new HmiItemTypeDescriptor(typeof(HmiText), "HmiText", "Controls", "Static text widget", 180, 40, "text"),
                new HmiItemTypeDescriptor(typeof(HmiTextBox), "HmiTextBox", "Controls", "Text box control", 180, 40, "textbox", "text-box"),
                new HmiItemTypeDescriptor(typeof(HmiLabel), "HmiLabel", "Controls", "Static text label", 180, 40, "label"),
                new HmiItemTypeDescriptor(typeof(HmiIOField), "HmiIOField", "Controls", "Input/output field", 150, 30, "iofield", "io-field"),
                new HmiItemTypeDescriptor(typeof(HmiSymbolicIOField), "HmiSymbolicIOField", "Controls", "Symbolic input/output field", 150, 30, "symboliciofield", "symbolic-io-field"),
                new HmiItemTypeDescriptor(typeof(HmiCheckBoxGroup), "HmiCheckBoxGroup", "Controls", "Check box group", 160, 50, "checkboxgroup", "check-box-group"),
                new HmiItemTypeDescriptor(typeof(HmiRadioButtonGroup), "HmiRadioButtonGroup", "Controls", "Radio button group", 160, 50, "radiobuttongroup", "radio-button-group"),
                new HmiItemTypeDescriptor(typeof(HmiRectangle), "HmiRectangle", "Shapes", "Rectangle shape", 100, 100, "rectangle"),
                new HmiItemTypeDescriptor(typeof(HmiCircle), "HmiCircle", "Shapes", "Circle shape", 100, 100, "circle"),
                new HmiItemTypeDescriptor(typeof(HmiScreenWindow), "HmiScreenWindow", "Containers", "Embedded screen window", 320, 240, "screenwindow", "screen-window"),
                new HmiItemTypeDescriptor(typeof(HmiFaceplateContainer), "HmiFaceplateContainer", "Containers", "Faceplate container", 220, 140, "faceplatecontainer", "faceplate-container"),
                new HmiItemTypeDescriptor(typeof(HmiTrendControl), "HmiTrendControl", "Diagnostics", "Trend control", 400, 300, "trendcontrol", "trend-control"),
                new HmiItemTypeDescriptor(typeof(HmiFunctionTrendControl), "HmiFunctionTrendControl", "Diagnostics", "Function trend control", 400, 300, "functiontrendcontrol", "function-trend-control"),
                new HmiItemTypeDescriptor(typeof(HmiTrendCompanion), "HmiTrendCompanion", "Diagnostics", "Trend companion control", 400, 300, "trendcompanion", "trend-companion")
            };

            var map = new Dictionary<string, HmiItemTypeDescriptor>(StringComparer.OrdinalIgnoreCase);
            foreach (var descriptor in descriptors)
            {
                map[descriptor.TypeKey] = descriptor;
                foreach (var alias in descriptor.Aliases)
                {
                    map[alias] = descriptor;
                }
            }

            return map;
        }

        private sealed class HmiItemTypeDescriptor
        {
            public HmiItemTypeDescriptor(Type itemType, string typeName, string category, string description, int defaultWidth, int defaultHeight, params string[] aliases)
            {
                ItemType = itemType;
                TypeName = typeName;
                Category = category;
                Description = description;
                DefaultWidth = defaultWidth;
                DefaultHeight = defaultHeight;
                Aliases = aliases
                    .Concat(new[] { typeName })
                    .Select(NormalizeToken)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                TypeKey = NormalizeToken(typeName);
            }

            public Type ItemType { get; }

            public string TypeName { get; }

            public string Category { get; }

            public string Description { get; }

            public int DefaultWidth { get; }

            public int DefaultHeight { get; }

            public string[] Aliases { get; }

            public string TypeKey { get; }

            public HmiItemTypeInfo ToPublicInfo()
            {
                return new HmiItemTypeInfo
                {
                    Type = TypeName,
                    Category = Category,
                    Description = Description,
                    DefaultWidth = DefaultWidth,
                    DefaultHeight = DefaultHeight,
                    Aliases = Aliases
                        .Where(alias => !string.Equals(alias, TypeKey, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(alias => alias, StringComparer.OrdinalIgnoreCase)
                        .ToList()
                };
            }
        }

    }
}
