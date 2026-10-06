using OpenTiaToolkit.Core.Models;
using Microsoft.Extensions.Logging;
using Siemens.Engineering;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.UI.Base;
using Siemens.Engineering.HmiUnified.UI.Screens;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using static OpenTiaToolkit.Core.Services.HmiJsonImportPrimitives;

namespace OpenTiaToolkit.Core.Services
{
    internal sealed class HmiJsonCompositeImportCoordinator
    {
        private readonly V21UnifiedHmiAdapter _adapter;

        internal HmiJsonCompositeImportCoordinator(V21UnifiedHmiAdapter adapter)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        }

        internal void ProcessEventHandlers(
            string scope,
            string screenPath,
            string? itemName,
            IReadOnlyList<HmiJsonEventHandler>? eventHandlers,
            object? owner,
            Type ownerType,
            HmiJsonFilterOptions filters,
            bool dryRun,
            HmiJsonImportResult result)
        {
            if (eventHandlers == null)
            {
                return;
            }

            if (!HmiJsonFilterMatcher.ShouldIncludeCompositeProperty(EventHandlersPropertyName, filters))
            {
                AddSkippedProperty(result, scope, screenPath, itemName, EventHandlersPropertyName, "Filtered out");
                return;
            }

            var filteredHandlers = eventHandlers
                .Where(handler => HmiJsonFilterMatcher.ShouldIncludeCompositeEntry(EventHandlersPropertyName, handler.EventType, filters))
                .ToList();
            if (eventHandlers.Count > 0 && filteredHandlers.Count == 0)
            {
                AddSkippedProperty(result, scope, screenPath, itemName, EventHandlersPropertyName, "Filtered out");
                return;
            }

            var currentHandlers = new List<HmiJsonEventHandler>();
            if (owner != null)
            {
                if (!_adapter.TryReadEventHandlers(owner, out currentHandlers, out var readError))
                {
                    if (!string.IsNullOrWhiteSpace(readError))
                    {
                        AddFailedProperty(result, scope, screenPath, itemName, EventHandlersPropertyName, readError!);
                        AddWarning(result, $"{scope} '{FormatObjectPath(screenPath, itemName)}': {readError}");
                        return;
                    }
                }
            }

            var desiredHandlers = MergeFilteredEventHandlers(currentHandlers, filteredHandlers, filters);
            var changedKeys = HmiJsonCompositeComparer.GetChangedEventHandlerKeys(currentHandlers, desiredHandlers);
            if (changedKeys.Count == 0)
            {
                result.PropertyUnchangedCount += filteredHandlers.Count;
                return;
            }

            if (!_adapter.TryValidateOrApplyEventHandlers(owner, ownerType, desiredHandlers, apply: !dryRun, out var error))
            {
                foreach (var eventType in changedKeys)
                {
                    AddFailedProperty(result, scope, screenPath, itemName, $"{EventHandlersPropertyName}.{eventType}", error ?? "Event handlers could not be applied.");
                }

                if (!string.IsNullOrWhiteSpace(error))
                {
                    AddWarning(result, $"{scope} '{FormatObjectPath(screenPath, itemName)}': {error}");
                }

                return;
            }

            var currentMap = HmiJsonCompositeComparer.BuildEventHandlerMap(currentHandlers);
            var desiredMap = HmiJsonCompositeComparer.BuildEventHandlerMap(desiredHandlers);
            foreach (var eventType in changedKeys)
            {
                currentMap.TryGetValue(eventType, out var currentHandler);
                desiredMap.TryGetValue(eventType, out var desiredHandler);
                AddPropertyChange(
                    result,
                    scope,
                    screenPath,
                    itemName,
                    $"{EventHandlersPropertyName}.{eventType}",
                    currentHandler == null ? "Create" : desiredHandler == null ? "Delete" : "Update",
                    SerializeCompositeValue(currentHandler),
                    SerializeCompositeValue(desiredHandler));
            }

            result.PropertySetCount += changedKeys.Count;
        }

        internal void ProcessPropertyEventHandlers(
            string scope,
            string screenPath,
            string? itemName,
            IReadOnlyList<HmiJsonPropertyEventHandler>? propertyEventHandlers,
            object? owner,
            Type ownerType,
            HmiJsonFilterOptions filters,
            bool dryRun,
            HmiJsonImportResult result)
        {
            if (propertyEventHandlers == null)
            {
                return;
            }

            if (!HmiJsonFilterMatcher.ShouldIncludeCompositeProperty(PropertyEventHandlersPropertyName, filters))
            {
                AddSkippedProperty(result, scope, screenPath, itemName, PropertyEventHandlersPropertyName, "Filtered out");
                return;
            }

            var filteredHandlers = propertyEventHandlers
                .Where(handler =>
                {
                    var key = HmiJsonCompositeComparer.GetPropertyEventHandlerKey(handler.PropertyName, handler.EventType);
                    return key != null &&
                           HmiJsonFilterMatcher.ShouldIncludeCompositeEntry(
                               PropertyEventHandlersPropertyName,
                               HmiJsonCompositeComparer.FormatPropertyEventHandlerKey(key),
                               filters);
                })
                .ToList();
            if (propertyEventHandlers.Count > 0 && filteredHandlers.Count == 0)
            {
                AddSkippedProperty(result, scope, screenPath, itemName, PropertyEventHandlersPropertyName, "Filtered out");
                return;
            }

            var currentHandlers = new List<HmiJsonPropertyEventHandler>();
            if (owner != null)
            {
                if (!_adapter.TryReadPropertyEventHandlers(owner, out currentHandlers, out var readError))
                {
                    if (!string.IsNullOrWhiteSpace(readError))
                    {
                        AddFailedProperty(result, scope, screenPath, itemName, PropertyEventHandlersPropertyName, readError!);
                        AddWarning(result, $"{scope} '{FormatObjectPath(screenPath, itemName)}': {readError}");
                        return;
                    }
                }
            }

            var desiredHandlers = MergeFilteredPropertyEventHandlers(currentHandlers, filteredHandlers, filters);
            var changedKeys = HmiJsonCompositeComparer.GetChangedPropertyEventHandlerKeys(currentHandlers, desiredHandlers);
            if (changedKeys.Count == 0)
            {
                result.PropertyUnchangedCount += filteredHandlers.Count;
                return;
            }

            if (!_adapter.TryValidateOrApplyPropertyEventHandlers(owner, ownerType, desiredHandlers, apply: !dryRun, out var error))
            {
                foreach (var key in changedKeys)
                {
                    AddFailedProperty(
                        result,
                        scope,
                        screenPath,
                        itemName,
                        $"{PropertyEventHandlersPropertyName}.{HmiJsonCompositeComparer.FormatPropertyEventHandlerKey(key)}",
                        error ?? "Property event handlers could not be applied.");
                }

                if (!string.IsNullOrWhiteSpace(error))
                {
                    AddWarning(result, $"{scope} '{FormatObjectPath(screenPath, itemName)}': {error}");
                }

                return;
            }

            var currentMap = HmiJsonCompositeComparer.BuildPropertyEventHandlerMap(currentHandlers);
            var desiredMap = HmiJsonCompositeComparer.BuildPropertyEventHandlerMap(desiredHandlers);
            foreach (var key in changedKeys)
            {
                currentMap.TryGetValue(key, out var currentHandler);
                desiredMap.TryGetValue(key, out var desiredHandler);
                AddPropertyChange(
                    result,
                    scope,
                    screenPath,
                    itemName,
                    $"{PropertyEventHandlersPropertyName}.{HmiJsonCompositeComparer.FormatPropertyEventHandlerKey(key)}",
                    currentHandler == null ? "Create" : desiredHandler == null ? "Delete" : "Update",
                    SerializeCompositeValue(currentHandler),
                    SerializeCompositeValue(desiredHandler));
            }

            result.PropertySetCount += changedKeys.Count;
        }

        internal void ProcessDynamizations(
            string scope,
            string screenPath,
            string? itemName,
            IReadOnlyList<HmiJsonDynamization>? dynamizations,
            object? owner,
            Type ownerType,
            HmiJsonFilterOptions filters,
            bool dryRun,
            HmiJsonImportResult result)
        {
            if (dynamizations == null)
            {
                return;
            }

            if (!HmiJsonFilterMatcher.ShouldIncludeCompositeProperty(DynamizationsPropertyName, filters))
            {
                AddSkippedProperty(result, scope, screenPath, itemName, DynamizationsPropertyName, "Filtered out");
                return;
            }

            var filteredDynamizations = dynamizations
                .Where(dynamization => HmiJsonFilterMatcher.ShouldIncludeCompositeEntry(DynamizationsPropertyName, dynamization.Property, filters))
                .ToList();
            if (dynamizations.Count > 0 && filteredDynamizations.Count == 0)
            {
                AddSkippedProperty(result, scope, screenPath, itemName, DynamizationsPropertyName, "Filtered out");
                return;
            }

            var currentDynamizations = new List<HmiJsonDynamization>();
            if (owner != null)
            {
                if (!_adapter.TryReadDynamizations(owner, out currentDynamizations, out var readError))
                {
                    if (!string.IsNullOrWhiteSpace(readError))
                    {
                        AddFailedProperty(result, scope, screenPath, itemName, DynamizationsPropertyName, readError!);
                        AddWarning(result, $"{scope} '{FormatObjectPath(screenPath, itemName)}': {readError}");
                        return;
                    }
                }
            }

            var desiredDynamizations = MergeFilteredDynamizations(currentDynamizations, filteredDynamizations, filters);
            var changedProperties = HmiJsonCompositeComparer.GetChangedDynamizationProperties(currentDynamizations, desiredDynamizations);
            if (changedProperties.Count == 0)
            {
                result.PropertyUnchangedCount += filteredDynamizations.Count;
                return;
            }

            if (!_adapter.TryValidateOrApplyDynamizations(owner, ownerType, desiredDynamizations, apply: !dryRun, out var error))
            {
                foreach (var propertyName in changedProperties)
                {
                    AddFailedProperty(result, scope, screenPath, itemName, $"{DynamizationsPropertyName}.{propertyName}", error ?? "Dynamizations could not be applied.");
                }

                if (!string.IsNullOrWhiteSpace(error))
                {
                    AddWarning(result, $"{scope} '{FormatObjectPath(screenPath, itemName)}': {error}");
                }

                return;
            }

            var currentMap = HmiJsonCompositeComparer.BuildDynamizationMap(currentDynamizations);
            var desiredMap = HmiJsonCompositeComparer.BuildDynamizationMap(desiredDynamizations);
            foreach (var propertyName in changedProperties)
            {
                currentMap.TryGetValue(propertyName, out var currentDynamization);
                desiredMap.TryGetValue(propertyName, out var desiredDynamization);
                AddPropertyChange(
                    result,
                    scope,
                    screenPath,
                    itemName,
                    $"{DynamizationsPropertyName}.{propertyName}",
                    currentDynamization == null ? "Create" : desiredDynamization == null ? "Delete" : "Update",
                    SerializeCompositeValue(currentDynamization),
                    SerializeCompositeValue(desiredDynamization));
            }

            result.PropertySetCount += changedProperties.Count;
        }

        internal void ProcessInterfaceBindings(
            string scope,
            string screenPath,
            string? itemName,
            IReadOnlyList<HmiJsonInterfaceBinding>? bindings,
            object? owner,
            Type ownerType,
            HmiSoftware software,
            HmiJsonFilterOptions filters,
            bool dryRun,
            HmiJsonImportResult result)
        {
            if (bindings == null)
            {
                return;
            }

            if (!HmiJsonFilterMatcher.ShouldIncludeCompositeProperty(InterfacePropertyName, filters))
            {
                AddSkippedProperty(result, scope, screenPath, itemName, InterfacePropertyName, "Filtered out");
                return;
            }

            var filteredBindings = bindings
                .Where(binding => HmiJsonFilterMatcher.ShouldIncludeCompositeEntry(InterfacePropertyName, binding.PropertyName, filters))
                .ToList();
            if (bindings.Count > 0 && filteredBindings.Count == 0)
            {
                AddSkippedProperty(result, scope, screenPath, itemName, InterfacePropertyName, "Filtered out");
                return;
            }

            if (!dryRun && string.Equals(scope, "Item", StringComparison.OrdinalIgnoreCase))
            {
                owner = ReacquireItemOwnerForInterfaceWrite(software, screenPath, itemName, result);
                ownerType = owner?.GetType() ?? ownerType;
            }

            var currentBindings = new List<HmiJsonInterfaceBinding>();
            if (owner != null)
            {
                if (!_adapter.TryReadInterfaceBindings(owner, out currentBindings, out var readError))
                {
                    if (!string.IsNullOrWhiteSpace(readError))
                    {
                        AddFailedProperty(result, scope, screenPath, itemName, InterfacePropertyName, readError!);
                        AddWarning(result, $"{scope} '{FormatObjectPath(screenPath, itemName)}': {readError}");
                        return;
                    }
                }
            }

            var desiredBindings = MergeFilteredInterfaceBindings(currentBindings, filteredBindings, filters);
            var changedProperties = HmiJsonCompositeComparer.GetChangedInterfaceProperties(currentBindings, desiredBindings);
            if (changedProperties.Count == 0)
            {
                result.PropertyUnchangedCount += filteredBindings.Count;
                return;
            }

            if (!_adapter.TryValidateOrApplyInterfaceBindings(owner, ownerType, desiredBindings, apply: !dryRun, out var error))
            {
                foreach (var propertyName in changedProperties)
                {
                    AddFailedProperty(result, scope, screenPath, itemName, $"{InterfacePropertyName}.{propertyName}", error ?? "Interface bindings could not be applied.");
                }

                if (!string.IsNullOrWhiteSpace(error))
                {
                    AddWarning(result, $"{scope} '{FormatObjectPath(screenPath, itemName)}': {error}");
                }

                return;
            }

            var currentMap = HmiJsonCompositeComparer.BuildInterfaceBindingMap(currentBindings);
            var desiredMap = HmiJsonCompositeComparer.BuildInterfaceBindingMap(desiredBindings);
            foreach (var propertyName in changedProperties)
            {
                currentMap.TryGetValue(propertyName, out var currentBinding);
                desiredMap.TryGetValue(propertyName, out var desiredBinding);
                AddPropertyChange(
                    result,
                    scope,
                    screenPath,
                    itemName,
                    $"{InterfacePropertyName}.{propertyName}",
                    currentBinding == null ? "Create" : desiredBinding == null ? "Delete" : "Update",
                    SerializeCompositeValue(currentBinding),
                    SerializeCompositeValue(desiredBinding));
            }

            result.PropertySetCount += changedProperties.Count;
        }

        private object? ReacquireItemOwnerForInterfaceWrite(
            HmiSoftware software,
            string screenPath,
            string? itemName,
            HmiJsonImportResult result)
        {
            if (string.IsNullOrWhiteSpace(itemName))
            {
                return null;
            }

            try
            {
                if (!_adapter.TryGetScreen(software, screenPath, out var screen, out var normalizedPath) || screen == null)
                {
                    AddWarning(result, $"Item '{FormatObjectPath(screenPath, itemName)}': screen could not be reacquired before Interface write.");
                    return null;
                }

                var item = screen.ScreenItems.Find(itemName);
                if (item == null)
                {
                    AddWarning(result, $"Item '{FormatObjectPath(normalizedPath, itemName)}': item could not be reacquired before Interface write.");
                }

                return item;
            }
            catch (Exception ex)
            {
                AddWarning(result, $"Item '{FormatObjectPath(screenPath, itemName)}': item reacquire before Interface write failed: {FormatExceptionMessage(ex)}");
                return null;
            }
        }

    }
}
