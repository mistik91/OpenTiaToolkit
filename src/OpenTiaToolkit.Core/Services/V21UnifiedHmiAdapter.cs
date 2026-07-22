using OpenTiaToolkit.Core.Models;
using Microsoft.Extensions.Logging;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.HmiLogging;
using Siemens.Engineering.HmiUnified.HmiTags;
using Siemens.Engineering.HmiUnified.LoggingTags;
using Siemens.Engineering.HmiUnified.UI.Base;
using Siemens.Engineering.HmiUnified.UI.Screens;
using System;
using System.Collections.Generic;

namespace OpenTiaToolkit.Core.Services
{
    /// <summary>
    /// Compatibility facade for the focused Siemens V21 Unified HMI access components.
    /// </summary>
    internal sealed class V21UnifiedHmiAdapter
    {
        private readonly V21UnifiedHmiAccess _access;
        private readonly V21HmiCompositeAccess _composites;
        private readonly V21HmiScreenCopyEngine _screenCopy;

        public V21UnifiedHmiAdapter(ILogger? logger)
        {
            var primitives = new V21HmiEngineeringPrimitives(logger);
            var properties = new V21HmiItemPropertyAccessor(primitives);
            _access = new V21UnifiedHmiAccess(primitives, properties);
            _composites = new V21HmiCompositeAccess(properties);
            _screenCopy = new V21HmiScreenCopyEngine(_access);
        }

        public string NormalizeScreenPath(string? screenPath) => _access.NormalizeScreenPath(screenPath);
        public string NormalizeItemTypeName(string? itemType) => _access.NormalizeItemTypeName(itemType);
        public string NormalizeTagPath(string? tagPath) => _access.NormalizeTagPath(tagPath);
        public bool TryResolveUnifiedHmi(ProjectBase project, string hmiName, out Device? matchedDevice, out DeviceItem? matchedDeviceItem, out HmiSoftware? software) =>
            _access.TryResolveUnifiedHmi(project, hmiName, out matchedDevice, out matchedDeviceItem, out software);
        public IReadOnlyList<HmiScreenInfo> ListScreens(HmiSoftware software, string? groupPath, bool recursive) =>
            _access.ListScreens(software, groupPath, recursive);
        public bool TryGetScreen(HmiSoftware software, string screenPath, out HmiScreen? screen, out string normalizedPath) =>
            _access.TryGetScreen(software, screenPath, out screen, out normalizedPath);
        public HmiScreen? EnsureScreen(HmiSoftware software, string screenPath) => _access.EnsureScreen(software, screenPath);
        public IReadOnlyList<HmiItemTypeInfo> ListSupportedItemTypes(string? filter) => _access.ListSupportedItemTypes(filter);
        public IReadOnlyList<HmiTagInfo> ListTags(HmiSoftware software, string? filter, string? rootPath = null) =>
            _access.ListTags(software, filter, rootPath);
        public IReadOnlyList<HmiTagInfo> ListTags(HmiSoftware software, HmiTagBrowseOptions? options) => _access.ListTags(software, options);
        public bool TryGetTag(HmiSoftware software, string tagPath, out HmiTag? tag, out string normalizedPath) =>
            _access.TryGetTag(software, tagPath, out tag, out normalizedPath);
        public HmiScreenItemBase? CreateScreenItem(HmiScreen screen, string itemType, string itemName) =>
            _access.CreateScreenItem(screen, itemType, itemName);
        public HmiDataLog EnsureDataLog(HmiSoftware software, string dataLogName, out bool created) =>
            _access.EnsureDataLog(software, dataLogName, out created);
        public HmiDataLog? FindDataLog(HmiSoftware software, string dataLogName) => _access.FindDataLog(software, dataLogName);
        public void DeleteDataLog(HmiDataLog dataLog) => _access.DeleteDataLog(dataLog);
        public void ConfigureDataLog(HmiDataLog dataLog, int logMaxSize, int durationDays, int segmentMaxSize) =>
            _access.ConfigureDataLog(dataLog, logMaxSize, durationDays, segmentMaxSize);
        public string GetLoggingTagName(string tagPath) => _access.GetLoggingTagName(tagPath);
        public HmiLoggingTag EnsureLoggingTag(HmiTag tag, string loggingTagName, out bool created) =>
            _access.EnsureLoggingTag(tag, loggingTagName, out created);
        public HmiLoggingTag? FindLoggingTag(HmiTag tag, string loggingTagName) => _access.FindLoggingTag(tag, loggingTagName);
        public void DeleteLoggingTag(HmiLoggingTag loggingTag) => _access.DeleteLoggingTag(loggingTag);
        public IReadOnlyList<HmiLoggingTagReferenceInfo> ListLoggingTagReferences(HmiSoftware software, string dataLogName) =>
            _access.ListLoggingTagReferences(software, dataLogName);
        public IReadOnlyList<HmiLoggingTagReferenceInfo> ListLoggingTagReferences(HmiSoftware software, ISet<string> dataLogNames) =>
            _access.ListLoggingTagReferences(software, dataLogNames);
        public void ConfigureLoggingTag(HmiLoggingTag loggingTag, string dataLogName, HmiLoggingMode loggingMode, string? cycle) =>
            _access.ConfigureLoggingTag(loggingTag, dataLogName, loggingMode, cycle);
        public bool TryApplyGeometry(HmiScreenItemBase item, int? left, int? top, int? width, int? height, out string? error) =>
            _access.TryApplyGeometry(item, left, top, width, height, out error);
        public HmiScreenInfo ToScreenInfo(HmiScreen screen, string path, bool includeItems) => _access.ToScreenInfo(screen, path, includeItems);
        public HmiScreenItemInfo ToItemInfo(HmiScreenItemBase item, string screenPath) => _access.ToItemInfo(item, screenPath);
        public HmiTrendInfo ToTrendInfo(HmiTag tag, string normalizedTagPath, HmiLoggingTag loggingTag, HmiDataLog dataLog, bool dataLogCreated, bool loggingTagCreated) =>
            _access.ToTrendInfo(tag, normalizedTagPath, loggingTag, dataLog, dataLogCreated, loggingTagCreated);
        public bool TryCopyScreenFromTemplate(HmiSoftware software, string templateScreenPath, string targetScreenPath, string? findText, string? replaceText, out HmiScreenCopyResult? result, out string? error) =>
            _screenCopy.TryCopyScreenFromTemplate(software, templateScreenPath, targetScreenPath, findText, replaceText, out result, out error);
        public IReadOnlyList<HmiItemPropertyInfo> GetItemProperties(HmiScreenItemBase item, IEnumerable<string>? propertyNames) =>
            _composites.GetItemProperties(item, propertyNames);
        public bool TrySetItemProperty(HmiScreenItemBase item, string propertyName, string value, HmiSoftware software, out string? error) =>
            _composites.TrySetItemProperty(item, propertyName, value, software, out error);
        public bool TryValidateItemProperty(HmiScreenItemBase item, string propertyName, string value, HmiSoftware software, out string? error) =>
            _composites.TryValidateItemProperty(item, propertyName, value, software, out error);
        public HmiFaceplateInterfaceProbeResult ProbeFaceplateInterface(string screenPath, HmiScreenItemBase item, string? propertyName, bool write, string? value, bool restore) =>
            _composites.ProbeFaceplateInterface(screenPath, item, propertyName, write, value, restore);
        public bool TryResolveItemClrType(string itemType, out Type resolvedType) => _composites.TryResolveItemClrType(itemType, out resolvedType);
        public bool TryReadEventHandlers(object owner, out List<HmiJsonEventHandler> eventHandlers, out string? error) =>
            _composites.TryReadEventHandlers(owner, out eventHandlers, out error);
        public bool TryReadPropertyEventHandlers(object owner, out List<HmiJsonPropertyEventHandler> propertyEventHandlers, out string? error) =>
            _composites.TryReadPropertyEventHandlers(owner, out propertyEventHandlers, out error);
        public bool TryReadDynamizations(object owner, out List<HmiJsonDynamization> dynamizations, out string? error) =>
            _composites.TryReadDynamizations(owner, out dynamizations, out error);
        public bool TryReadInterfaceBindings(object owner, out List<HmiJsonInterfaceBinding> bindings, out string? error) =>
            _composites.TryReadInterfaceBindings(owner, out bindings, out error);
        public bool TryValidateOrApplyEventHandlers(object? owner, Type ownerType, IReadOnlyList<HmiJsonEventHandler>? desiredEventHandlers, bool apply, out string? error) =>
            _composites.TryValidateOrApplyEventHandlers(owner, ownerType, desiredEventHandlers, apply, out error);
        public bool TryValidateOrApplyPropertyEventHandlers(object? owner, Type ownerType, IReadOnlyList<HmiJsonPropertyEventHandler>? desiredPropertyEventHandlers, bool apply, out string? error) =>
            _composites.TryValidateOrApplyPropertyEventHandlers(owner, ownerType, desiredPropertyEventHandlers, apply, out error);
        public bool TryValidateOrApplyDynamizations(object? owner, Type ownerType, IReadOnlyList<HmiJsonDynamization>? desiredDynamizations, bool apply, out string? error) =>
            _composites.TryValidateOrApplyDynamizations(owner, ownerType, desiredDynamizations, apply, out error);
        public bool TryValidateOrApplyInterfaceBindings(object? owner, Type ownerType, IReadOnlyList<HmiJsonInterfaceBinding>? desiredBindings, bool apply, out string? error) =>
            _composites.TryValidateOrApplyInterfaceBindings(owner, ownerType, desiredBindings, apply, out error);
    }
}
