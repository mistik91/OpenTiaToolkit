using OpenTiaToolkit.Core.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Services
{
    /// <summary>
    /// Creates minimal Unified HMI JSON patch documents from two exports.
    /// </summary>
    internal sealed class HmiJsonDiffService
    {
        public async Task<HmiJsonDiffResult?> CreatePatchAsync(
            string basePath,
            string modifiedPath,
            string patchPath,
            HmiJsonFilterOptions? filters)
        {
            return await Task.Run(() =>
            {
                var effectiveFilters = HmiJsonFilterMatcher.Normalize(filters);
                var baseDocument = HmiJsonDocumentStore.LoadDocument(basePath);
                var modifiedDocument = HmiJsonDocumentStore.LoadDocument(modifiedPath);

                var patchSummary = new HmiJsonPatchSummary();
                var patchDocument = new HmiJsonDocument
                {
                    DocumentKind = "Patch",
                    SchemaVersion = modifiedDocument.SchemaVersion,
                    ExportedAtUtc = baseDocument.ExportedAtUtc,
                    PatchGeneratedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                    HmiName = modifiedDocument.HmiName ?? baseDocument.HmiName,
                    Filters = effectiveFilters,
                    PatchSummary = patchSummary,
                    Warnings = baseDocument.Warnings
                        .Concat(modifiedDocument.Warnings)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList()
                };

                var baseScreens = baseDocument.Screens
                    .Where(screen => HmiJsonFilterMatcher.ShouldIncludeScreen(screen.Path, effectiveFilters))
                    .ToDictionary(screen => screen.Path, screen => screen, StringComparer.OrdinalIgnoreCase);
                var modifiedScreens = modifiedDocument.Screens
                    .Where(screen => HmiJsonFilterMatcher.ShouldIncludeScreen(screen.Path, effectiveFilters))
                    .ToDictionary(screen => screen.Path, screen => screen, StringComparer.OrdinalIgnoreCase);

                foreach (var screenPath in baseScreens.Keys
                    .Union(modifiedScreens.Keys, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    baseScreens.TryGetValue(screenPath, out var baseScreen);
                    modifiedScreens.TryGetValue(screenPath, out var modifiedScreen);

                    if (baseScreen == null && modifiedScreen != null)
                    {
                        patchSummary.ScreenCreateCount++;
                        patchSummary.ItemCreateCount += modifiedScreen.Items.Count;
                        patchSummary.PropertyChangeCount += modifiedScreen.Properties.Count;
                        patchSummary.PropertyChangeCount += modifiedScreen.EventHandlers?.Count ?? 0;
                        patchSummary.PropertyChangeCount += modifiedScreen.PropertyEventHandlers?.Count ?? 0;
                        patchSummary.PropertyChangeCount += modifiedScreen.Dynamizations?.Count ?? 0;
                        patchSummary.PropertyChangeCount += modifiedScreen.Items.Sum(item =>
                            item.Properties.Count +
                            (item.EventHandlers?.Count ?? 0) +
                            (item.PropertyEventHandlers?.Count ?? 0) +
                            (item.Dynamizations?.Count ?? 0) +
                            (item.Interface?.Count ?? 0));
                        patchDocument.Screens.Add(CloneScreen(modifiedScreen, action: "Create"));
                        continue;
                    }

                    if (baseScreen != null && modifiedScreen == null)
                    {
                        patchSummary.ScreenDeleteCount++;
                        patchDocument.Screens.Add(new HmiJsonScreen
                        {
                            Path = baseScreen.Path,
                            Name = baseScreen.Name,
                            Action = "Delete"
                        });
                        continue;
                    }

                    var screenPatch = BuildScreenPatch(baseScreen!, modifiedScreen!, effectiveFilters, patchSummary);
                    if (screenPatch != null)
                    {
                        patchDocument.Screens.Add(screenPatch);
                    }
                }

                patchDocument.Screens = patchDocument.Screens
                    .OrderBy(screen => screen.Path, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var outputFile = HmiJsonDocumentStore.ResolvePatchFilePath(patchPath);
                HmiJsonDocumentStore.SaveDocument(patchDocument, outputFile);

                return new HmiJsonDiffResult
                {
                    OutputPath = outputFile,
                    ScreenCreateCount = patchSummary.ScreenCreateCount,
                    ScreenUpdateCount = patchSummary.ScreenUpdateCount,
                    ScreenDeleteCount = patchSummary.ScreenDeleteCount,
                    ItemCreateCount = patchSummary.ItemCreateCount,
                    ItemUpdateCount = patchSummary.ItemUpdateCount,
                    ItemDeleteCount = patchSummary.ItemDeleteCount,
                    ItemRecreateCount = patchSummary.ItemRecreateCount,
                    PropertyChangeCount = patchSummary.PropertyChangeCount,
                    WarningCount = patchDocument.Warnings.Count
                };
            });
        }

        private static HmiJsonScreen? BuildScreenPatch(
            HmiJsonScreen baseScreen,
            HmiJsonScreen modifiedScreen,
            HmiJsonFilterOptions filters,
            HmiJsonPatchSummary patchSummary)
        {
            var changedScreenProperties = DiffProperties(baseScreen.Properties, modifiedScreen.Properties, filters);
            var changedScreenEventHandlerKeys = HmiJsonFilterMatcher.ShouldIncludeCompositeProperty("EventHandlers", filters)
                ? HmiJsonCompositeComparer.GetChangedEventHandlerKeys(baseScreen.EventHandlers, modifiedScreen.EventHandlers)
                : new List<string>();
            var changedScreenPropertyEventHandlerKeys = HmiJsonFilterMatcher.ShouldIncludeCompositeProperty("PropertyEventHandlers", filters)
                ? HmiJsonCompositeComparer.GetChangedPropertyEventHandlerKeys(baseScreen.PropertyEventHandlers, modifiedScreen.PropertyEventHandlers)
                : new List<string>();
            var changedScreenDynamizationProperties = HmiJsonFilterMatcher.ShouldIncludeCompositeProperty("Dynamizations", filters)
                ? HmiJsonCompositeComparer.GetChangedDynamizationProperties(baseScreen.Dynamizations, modifiedScreen.Dynamizations)
                : new List<string>();
            var itemPatches = new List<HmiJsonItem>();

            var baseItems = baseScreen.Items
                .Where(item => HmiJsonFilterMatcher.ShouldIncludeItem(item.Name, filters))
                .ToDictionary(item => item.Name, item => item, StringComparer.OrdinalIgnoreCase);
            var modifiedItems = modifiedScreen.Items
                .Where(item => HmiJsonFilterMatcher.ShouldIncludeItem(item.Name, filters))
                .ToDictionary(item => item.Name, item => item, StringComparer.OrdinalIgnoreCase);

            foreach (var itemName in baseItems.Keys
                .Union(modifiedItems.Keys, StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            {
                baseItems.TryGetValue(itemName, out var baseItem);
                modifiedItems.TryGetValue(itemName, out var modifiedItem);

                if (baseItem == null && modifiedItem != null)
                {
                    patchSummary.ItemCreateCount++;
                    itemPatches.Add(CloneItem(modifiedItem, action: "Create"));
                    continue;
                }

                if (baseItem != null && modifiedItem == null)
                {
                    patchSummary.ItemDeleteCount++;
                    itemPatches.Add(new HmiJsonItem
                    {
                        Name = baseItem.Name,
                        Type = baseItem.Type,
                        Action = "Delete"
                    });
                    continue;
                }

                if (!string.Equals(baseItem!.Type, modifiedItem!.Type, StringComparison.OrdinalIgnoreCase))
                {
                    patchSummary.ItemRecreateCount++;
                    itemPatches.Add(CloneItem(modifiedItem, action: "Recreate"));
                    continue;
                }

                var changedItemProperties = DiffProperties(baseItem.Properties, modifiedItem.Properties, filters);
                var changedItemEventHandlerKeys = HmiJsonFilterMatcher.ShouldIncludeCompositeProperty("EventHandlers", filters)
                    ? HmiJsonCompositeComparer.GetChangedEventHandlerKeys(baseItem.EventHandlers, modifiedItem.EventHandlers)
                    : new List<string>();
                var changedItemPropertyEventHandlerKeys = HmiJsonFilterMatcher.ShouldIncludeCompositeProperty("PropertyEventHandlers", filters)
                    ? HmiJsonCompositeComparer.GetChangedPropertyEventHandlerKeys(baseItem.PropertyEventHandlers, modifiedItem.PropertyEventHandlers)
                    : new List<string>();
                var changedItemDynamizationProperties = HmiJsonFilterMatcher.ShouldIncludeCompositeProperty("Dynamizations", filters)
                    ? HmiJsonCompositeComparer.GetChangedDynamizationProperties(baseItem.Dynamizations, modifiedItem.Dynamizations)
                    : new List<string>();
                var changedItemInterfaceProperties = HmiJsonFilterMatcher.ShouldIncludeCompositeProperty("Interface", filters)
                    ? HmiJsonCompositeComparer.GetChangedInterfaceProperties(baseItem.Interface, modifiedItem.Interface)
                    : new List<string>();
                if (changedItemProperties.Count == 0 &&
                    changedItemEventHandlerKeys.Count == 0 &&
                    changedItemPropertyEventHandlerKeys.Count == 0 &&
                    changedItemDynamizationProperties.Count == 0 &&
                    changedItemInterfaceProperties.Count == 0)
                {
                    continue;
                }

                patchSummary.ItemUpdateCount++;
                patchSummary.PropertyChangeCount += changedItemProperties.Count;
                patchSummary.PropertyChangeCount += changedItemEventHandlerKeys.Count;
                patchSummary.PropertyChangeCount += changedItemPropertyEventHandlerKeys.Count;
                patchSummary.PropertyChangeCount += changedItemDynamizationProperties.Count;
                patchSummary.PropertyChangeCount += changedItemInterfaceProperties.Count;
                itemPatches.Add(new HmiJsonItem
                {
                    Name = modifiedItem.Name,
                    Type = modifiedItem.Type,
                    Action = "Update",
                    Properties = changedItemProperties,
                    EventHandlers = changedItemEventHandlerKeys.Count > 0
                        ? HmiJsonCompositeComparer.CloneEventHandlers(modifiedItem.EventHandlers) ?? new List<HmiJsonEventHandler>()
                        : null,
                    PropertyEventHandlers = changedItemPropertyEventHandlerKeys.Count > 0
                        ? HmiJsonCompositeComparer.ClonePropertyEventHandlers(modifiedItem.PropertyEventHandlers) ?? new List<HmiJsonPropertyEventHandler>()
                        : null,
                    Dynamizations = changedItemDynamizationProperties.Count > 0
                        ? HmiJsonCompositeComparer.CloneDynamizations(modifiedItem.Dynamizations) ?? new List<HmiJsonDynamization>()
                        : null,
                    Interface = changedItemInterfaceProperties.Count > 0
                        ? HmiJsonCompositeComparer.CloneInterfaceBindings(modifiedItem.Interface) ?? new List<HmiJsonInterfaceBinding>()
                        : null
                });
            }

            if (changedScreenProperties.Count == 0 &&
                changedScreenEventHandlerKeys.Count == 0 &&
                changedScreenPropertyEventHandlerKeys.Count == 0 &&
                changedScreenDynamizationProperties.Count == 0 &&
                itemPatches.Count == 0)
            {
                return null;
            }

            patchSummary.ScreenUpdateCount++;
            patchSummary.PropertyChangeCount += changedScreenProperties.Count;
            patchSummary.PropertyChangeCount += changedScreenEventHandlerKeys.Count;
            patchSummary.PropertyChangeCount += changedScreenPropertyEventHandlerKeys.Count;
            patchSummary.PropertyChangeCount += changedScreenDynamizationProperties.Count;

            return new HmiJsonScreen
            {
                Path = modifiedScreen.Path,
                Name = modifiedScreen.Name,
                Action = "Update",
                Properties = changedScreenProperties,
                EventHandlers = changedScreenEventHandlerKeys.Count > 0
                    ? HmiJsonCompositeComparer.CloneEventHandlers(modifiedScreen.EventHandlers) ?? new List<HmiJsonEventHandler>()
                    : null,
                PropertyEventHandlers = changedScreenPropertyEventHandlerKeys.Count > 0
                    ? HmiJsonCompositeComparer.ClonePropertyEventHandlers(modifiedScreen.PropertyEventHandlers) ?? new List<HmiJsonPropertyEventHandler>()
                    : null,
                Dynamizations = changedScreenDynamizationProperties.Count > 0
                    ? HmiJsonCompositeComparer.CloneDynamizations(modifiedScreen.Dynamizations) ?? new List<HmiJsonDynamization>()
                    : null,
                Items = itemPatches
            };
        }

        private static Dictionary<string, string?> DiffProperties(
            IReadOnlyDictionary<string, string?> baseProperties,
            IReadOnlyDictionary<string, string?> modifiedProperties,
            HmiJsonFilterOptions filters)
        {
            var changedProperties = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            foreach (var propertyName in modifiedProperties.Keys
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            {
                if (!HmiJsonFilterMatcher.ShouldIncludeProperty(propertyName, filters))
                {
                    continue;
                }

                baseProperties.TryGetValue(propertyName, out var baseValue);
                modifiedProperties.TryGetValue(propertyName, out var modifiedValue);
                if (string.Equals(baseValue, modifiedValue, StringComparison.Ordinal))
                {
                    continue;
                }

                changedProperties[propertyName] = modifiedValue;
            }

            return changedProperties;
        }

        private static HmiJsonScreen CloneScreen(HmiJsonScreen source, string action)
        {
            return new HmiJsonScreen
            {
                Path = source.Path,
                Name = source.Name,
                Action = action,
                Properties = new Dictionary<string, string?>(source.Properties, StringComparer.OrdinalIgnoreCase),
                EventHandlers = HmiJsonCompositeComparer.CloneEventHandlers(source.EventHandlers),
                PropertyEventHandlers = HmiJsonCompositeComparer.ClonePropertyEventHandlers(source.PropertyEventHandlers),
                Dynamizations = HmiJsonCompositeComparer.CloneDynamizations(source.Dynamizations),
                Items = source.Items.Select(item => CloneItem(item, action: "Create")).ToList()
            };
        }

        private static HmiJsonItem CloneItem(HmiJsonItem source, string action)
        {
            return new HmiJsonItem
            {
                Name = source.Name,
                Type = source.Type,
                Action = action,
                Properties = new Dictionary<string, string?>(source.Properties, StringComparer.OrdinalIgnoreCase),
                EventHandlers = HmiJsonCompositeComparer.CloneEventHandlers(source.EventHandlers),
                PropertyEventHandlers = HmiJsonCompositeComparer.ClonePropertyEventHandlers(source.PropertyEventHandlers),
                Dynamizations = HmiJsonCompositeComparer.CloneDynamizations(source.Dynamizations),
                Interface = HmiJsonCompositeComparer.CloneInterfaceBindings(source.Interface)
            };
        }
    }
}
