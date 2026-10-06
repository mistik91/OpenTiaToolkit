using OpenTiaToolkit.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTiaToolkit.Core.Services
{
    internal static class HmiJsonCompositeMergeRules
    {
        internal static List<HmiJsonEventHandler> MergeEventHandlers(
            IReadOnlyList<HmiJsonEventHandler> currentHandlers,
            IReadOnlyList<HmiJsonEventHandler> filteredHandlers,
            HmiJsonFilterOptions filters)
        {
            if (!ShouldPreserveUnfilteredEntries(HmiJsonImportPrimitives.EventHandlersPropertyName, filters))
            {
                return HmiJsonCompositeComparer.CloneEventHandlers(filteredHandlers) ?? new List<HmiJsonEventHandler>();
            }

            var merged = HmiJsonCompositeComparer.BuildEventHandlerMap(currentHandlers)
                .Where(entry => !HmiJsonFilterMatcher.ShouldIncludeCompositeEntry(HmiJsonImportPrimitives.EventHandlersPropertyName, entry.Key, filters))
                .Select(entry => entry.Value)
                .Concat(filteredHandlers);
            return HmiJsonCompositeComparer.CloneEventHandlers(merged) ?? new List<HmiJsonEventHandler>();
        }

        internal static List<HmiJsonPropertyEventHandler> MergePropertyEventHandlers(
            IReadOnlyList<HmiJsonPropertyEventHandler> currentHandlers,
            IReadOnlyList<HmiJsonPropertyEventHandler> filteredHandlers,
            HmiJsonFilterOptions filters)
        {
            if (!ShouldPreserveUnfilteredEntries(HmiJsonImportPrimitives.PropertyEventHandlersPropertyName, filters))
            {
                return HmiJsonCompositeComparer.ClonePropertyEventHandlers(filteredHandlers) ?? new List<HmiJsonPropertyEventHandler>();
            }

            var merged = HmiJsonCompositeComparer.BuildPropertyEventHandlerMap(currentHandlers)
                .Where(entry => !HmiJsonFilterMatcher.ShouldIncludeCompositeEntry(
                    HmiJsonImportPrimitives.PropertyEventHandlersPropertyName,
                    HmiJsonCompositeComparer.FormatPropertyEventHandlerKey(entry.Key),
                    filters))
                .Select(entry => entry.Value)
                .Concat(filteredHandlers);
            return HmiJsonCompositeComparer.ClonePropertyEventHandlers(merged) ?? new List<HmiJsonPropertyEventHandler>();
        }

        internal static List<HmiJsonDynamization> MergeDynamizations(
            IReadOnlyList<HmiJsonDynamization> currentDynamizations,
            IReadOnlyList<HmiJsonDynamization> filteredDynamizations,
            HmiJsonFilterOptions filters)
        {
            if (!ShouldPreserveUnfilteredEntries(HmiJsonImportPrimitives.DynamizationsPropertyName, filters))
            {
                return HmiJsonCompositeComparer.CloneDynamizations(filteredDynamizations) ?? new List<HmiJsonDynamization>();
            }

            var merged = HmiJsonCompositeComparer.BuildDynamizationMap(currentDynamizations)
                .Where(entry => !HmiJsonFilterMatcher.ShouldIncludeCompositeEntry(HmiJsonImportPrimitives.DynamizationsPropertyName, entry.Key, filters))
                .Select(entry => entry.Value)
                .Concat(filteredDynamizations);
            return HmiJsonCompositeComparer.CloneDynamizations(merged) ?? new List<HmiJsonDynamization>();
        }

        internal static List<HmiJsonInterfaceBinding> MergeInterfaceBindings(
            IReadOnlyList<HmiJsonInterfaceBinding> currentBindings,
            IReadOnlyList<HmiJsonInterfaceBinding> filteredBindings,
            HmiJsonFilterOptions filters)
        {
            if (!ShouldPreserveUnfilteredEntries(HmiJsonImportPrimitives.InterfacePropertyName, filters))
            {
                return HmiJsonCompositeComparer.CloneInterfaceBindings(filteredBindings) ?? new List<HmiJsonInterfaceBinding>();
            }

            var merged = HmiJsonCompositeComparer.BuildInterfaceBindingMap(currentBindings)
                .Where(entry => !HmiJsonFilterMatcher.ShouldIncludeCompositeEntry(HmiJsonImportPrimitives.InterfacePropertyName, entry.Key, filters))
                .Select(entry => entry.Value)
                .Concat(filteredBindings);
            return HmiJsonCompositeComparer.CloneInterfaceBindings(merged) ?? new List<HmiJsonInterfaceBinding>();
        }

        internal static bool ShouldPreserveUnfilteredEntries(string propertyName, HmiJsonFilterOptions filters)
        {
            return filters.Properties.Count > 0 &&
                   !filters.Properties.Contains(propertyName, StringComparer.OrdinalIgnoreCase);
        }
    }
}
