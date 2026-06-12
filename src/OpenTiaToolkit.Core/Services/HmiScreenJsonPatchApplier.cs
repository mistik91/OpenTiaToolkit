using OpenTiaToolkit.Core.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenTiaToolkit.Core.Services
{
    internal sealed class HmiScreenJsonPatchApplyResult
    {
        public HmiScreenJsonPatchApplyResult(HmiJsonScreen screen, List<HmiScreenJsonPatchOperationResult> operationResults)
        {
            Screen = screen;
            OperationResults = operationResults;
        }

        public HmiJsonScreen Screen { get; }

        public List<HmiScreenJsonPatchOperationResult> OperationResults { get; }

        public int FailedCount => OperationResults.Count(result => !result.Succeeded);
    }

    internal static class HmiScreenJsonPatchApplier
    {
        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };

        public static HmiScreenJsonPatchApplyResult Apply(
            HmiJsonScreen screen,
            IReadOnlyList<HmiScreenJsonPatchOperation> operations)
        {
            var editedScreen = CloneScreen(screen);
            var operationResults = new List<HmiScreenJsonPatchOperationResult>();

            for (var index = 0; index < operations.Count; index++)
            {
                ApplyOperation(editedScreen, operations[index], index, operationResults);
            }

            return new HmiScreenJsonPatchApplyResult(editedScreen, operationResults);
        }

        public static HmiJsonFilterOptions MergeOperationFilters(
            HmiJsonFilterOptions? filters,
            IReadOnlyList<HmiScreenJsonPatchOperation> operations)
        {
            var normalized = HmiJsonFilterMatcher.Normalize(filters);
            var itemNames = normalized.ItemNames.ToList();
            var properties = normalized.Properties.ToList();

            foreach (var operation in operations)
            {
                AddDistinct(itemNames, ResolveItemName(operation));
                AddDistinct(itemNames, ResolveSourceItemName(operation));
                AddDistinct(itemNames, ResolveTargetItemName(operation));

                foreach (var propertyName in GetTouchedProperties(operation))
                {
                    AddDistinct(properties, propertyName);
                }
            }

            return new HmiJsonFilterOptions
            {
                Screens = normalized.Screens,
                ItemNames = itemNames,
                Properties = properties
            };
        }

        public static List<HmiScreenJsonPatchOperationResult> ValidateOperationFilters(
            HmiJsonFilterOptions filters,
            IReadOnlyList<HmiScreenJsonPatchOperation> operations)
        {
            var results = new List<HmiScreenJsonPatchOperationResult>();

            for (var index = 0; index < operations.Count; index++)
            {
                var operation = operations[index];
                var op = ResolveOperationName(operation);
                foreach (var itemName in GetFilterItemNames(operation).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(itemName) ||
                        filters.ItemNames.Count == 0 ||
                        HmiJsonFilterMatcher.ShouldIncludeItem(itemName, filters))
                    {
                        continue;
                    }

                    results.Add(Failure(
                        index,
                        op,
                        $"Item '{itemName}' is outside the current item-names filter. Re-read or apply with item-names including '{itemName}' so the base hash and diff cover the target item.",
                        itemName,
                        suggestions: BuildItemFilterSuggestions(itemName)));
                }

                var propertyIssueItemName = FirstNonEmpty(
                    ResolveItemName(operation),
                    ResolveTargetItemName(operation),
                    ResolveSourceItemName(operation));
                foreach (var propertyName in GetTouchedProperties(operation)
                    .Where(property => !string.IsNullOrWhiteSpace(property))
                    .Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (filters.Properties.Count == 0 || HmiJsonFilterMatcher.ShouldIncludeProperty(propertyName, filters))
                    {
                        continue;
                    }

                    results.Add(Failure(
                        index,
                        op,
                        $"Property '{propertyName}' is outside the current properties filter. Re-read or apply with properties including '{propertyName}' so the base hash and diff cover the requested change.",
                        propertyIssueItemName,
                        propertyName,
                        BuildPropertyFilterSuggestions(propertyName)));
                }
            }

            return results;
        }

        private static void ApplyOperation(
            HmiJsonScreen screen,
            HmiScreenJsonPatchOperation operation,
            int index,
            List<HmiScreenJsonPatchOperationResult> results)
        {
            var op = ResolveOperationName(operation);
            if (string.IsNullOrWhiteSpace(op))
            {
                results.Add(Failure(index, string.Empty, "The patch operation is missing the op field."));
                return;
            }

            switch (NormalizeOperationName(op))
            {
                case "setproperty":
                    ApplySetProperty(screen, operation, index, op, results);
                    break;
                case "setproperties":
                    ApplySetProperties(screen, operation, index, op, results);
                    break;
                case "moveitem":
                    ApplyLayout(screen, operation, index, op, results, includePosition: true, includeSize: false);
                    break;
                case "resizeitem":
                    ApplyLayout(screen, operation, index, op, results, includePosition: false, includeSize: true);
                    break;
                case "setlayout":
                    ApplyLayout(screen, operation, index, op, results, includePosition: true, includeSize: true);
                    break;
                case "renameitem":
                    ApplyRenameItem(screen, operation, index, op, results);
                    break;
                case "createitem":
                    ApplyCreateItem(screen, operation, index, op, results);
                    break;
                case "deleteitem":
                    ApplyDeleteItem(screen, operation, index, op, results);
                    break;
                case "duplicateitem":
                    ApplyDuplicateItem(screen, operation, index, op, results);
                    break;
                case "seteventhandler":
                    ApplySetEventHandler(screen, operation, index, op, results);
                    break;
                case "removeeventhandler":
                    ApplyRemoveEventHandler(screen, operation, index, op, results);
                    break;
                case "setpropertyeventhandler":
                    ApplySetPropertyEventHandler(screen, operation, index, op, results);
                    break;
                case "removepropertyeventhandler":
                    ApplyRemovePropertyEventHandler(screen, operation, index, op, results);
                    break;
                case "setdynamization":
                    ApplySetDynamization(screen, operation, index, op, results);
                    break;
                case "removedynamization":
                    ApplyRemoveDynamization(screen, operation, index, op, results);
                    break;
                case "setinterface":
                    ApplySetInterface(screen, operation, index, op, results);
                    break;
                default:
                    results.Add(Failure(
                        index,
                        op,
                        "Unsupported patch operation. Supported operations are set-property, set-properties, move-item, resize-item, set-layout, rename-item, create-item, delete-item, duplicate-item, set-event-handler, remove-event-handler, set-property-event-handler, remove-property-event-handler, set-dynamization, remove-dynamization, and set-interface.",
                        suggestions: new List<string>
                        {
                            "Use one of: " + string.Join(", ", HmiScreenJsonAgentSupport.SupportedPatchOperations)
                        }));
                    break;
            }
        }

        private static void ApplySetProperty(
            HmiJsonScreen screen,
            HmiScreenJsonPatchOperation operation,
            int index,
            string op,
            List<HmiScreenJsonPatchOperationResult> results)
        {
            var property = ResolvePropertyName(operation);
            if (string.IsNullOrWhiteSpace(property))
            {
                results.Add(Failure(index, op, "The set-property operation requires property."));
                return;
            }

            if (!TryConvertValue(operation.Value, out var value, out var error))
            {
                results.Add(Failure(index, op, error ?? "The set-property operation requires a scalar value."));
                return;
            }

            ApplyProperty(screen, operation, index, op, property!, value, results);
        }

        private static void ApplySetProperties(
            HmiJsonScreen screen,
            HmiScreenJsonPatchOperation operation,
            int index,
            string op,
            List<HmiScreenJsonPatchOperationResult> results)
        {
            if (operation.Properties == null || operation.Properties.Count == 0)
            {
                results.Add(Failure(index, op, "The set-properties operation requires a non-empty properties object."));
                return;
            }

            foreach (var property in operation.Properties.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(property.Key))
                {
                    results.Add(Failure(index, op, "The set-properties operation contains an empty property name."));
                    continue;
                }

                if (!TryConvertValue(property.Value, out var value, out var error))
                {
                    results.Add(Failure(index, op, error ?? $"Property '{property.Key}' requires a scalar value.", property: property.Key));
                    continue;
                }

                ApplyProperty(screen, operation, index, op, property.Key, value, results);
            }
        }

        private static void ApplyDeleteItem(
            HmiJsonScreen screen,
            HmiScreenJsonPatchOperation operation,
            int index,
            string op,
            List<HmiScreenJsonPatchOperationResult> results)
        {
            var itemName = ResolveItemName(operation);
            if (string.IsNullOrWhiteSpace(itemName))
            {
                results.Add(Failure(index, op, "The delete-item operation requires item or itemName."));
                return;
            }

            if (!TryFindItem(screen, itemName!, out var item, out var suggestions, out var message))
            {
                results.Add(Failure(index, op, message!, itemName, suggestions: suggestions));
                return;
            }

            screen.Items.Remove(item!);
            results.Add(Success(index, op, "item", item!.Name, property: null, previousValue: item.Type, newValue: null));
        }

        private static void ApplyDuplicateItem(
            HmiJsonScreen screen,
            HmiScreenJsonPatchOperation operation,
            int index,
            string op,
            List<HmiScreenJsonPatchOperationResult> results)
        {
            var sourceName = ResolveSourceItemName(operation);
            if (string.IsNullOrWhiteSpace(sourceName))
            {
                results.Add(Failure(index, op, "The duplicate-item operation requires source or sourceItem."));
                return;
            }

            var targetName = ResolveTargetItemName(operation);
            if (string.IsNullOrWhiteSpace(targetName))
            {
                results.Add(Failure(index, op, "The duplicate-item operation requires item, itemName, target, targetItem, or targetName."));
                return;
            }

            if (!TryFindItem(screen, sourceName!, out var sourceItem, out var suggestions, out var message))
            {
                results.Add(Failure(index, op, message!, sourceName, suggestions: suggestions));
                return;
            }

            if (screen.Items.Any(item => item.Name.Equals(targetName!, StringComparison.OrdinalIgnoreCase)))
            {
                results.Add(Failure(index, op, $"Item '{targetName}' already exists on the exported screen.", targetName));
                return;
            }

            var duplicatedItem = CloneItem(sourceItem!);
            duplicatedItem.Name = targetName!;

            if (!TryApplyPropertyOverrides(duplicatedItem.Properties, operation, index, op, targetName, results))
            {
                return;
            }

            AddCreateItemLayoutValue(duplicatedItem.Properties, "Left", operation.Left);
            AddCreateItemLayoutValue(duplicatedItem.Properties, "Top", operation.Top);
            AddCreateItemLayoutValue(duplicatedItem.Properties, "Width", operation.Width);
            AddCreateItemLayoutValue(duplicatedItem.Properties, "Height", operation.Height);

            screen.Items.Add(duplicatedItem);
            results.Add(Success(index, op, "item", targetName, property: null, previousValue: sourceItem!.Name, newValue: duplicatedItem.Type));
            foreach (var property in GetDuplicateOperationOverrideProperties(operation, duplicatedItem)
                .OrderBy(property => property, StringComparer.OrdinalIgnoreCase))
            {
                sourceItem.Properties.TryGetValue(property, out var previousValue);
                duplicatedItem.Properties.TryGetValue(property, out var newValue);
                results.Add(Success(index, op, "item", targetName, property, previousValue, newValue));
            }
        }

        private static void ApplySetEventHandler(
            HmiJsonScreen screen,
            HmiScreenJsonPatchOperation operation,
            int index,
            string op,
            List<HmiScreenJsonPatchOperationResult> results)
        {
            if (!TryResolveCompositeTarget(screen, operation, index, op, results, out var target))
            {
                return;
            }

            var handler = CloneOrBuildEventHandler(operation);
            if (handler == null || string.IsNullOrWhiteSpace(handler.EventType))
            {
                results.Add(Failure(index, op, "The set-event-handler operation requires eventHandler or eventType plus scriptCode.", ResolveItemName(operation), "EventHandlers"));
                return;
            }

            target.EventHandlers ??= new List<HmiJsonEventHandler>();
            target.EventHandlers.RemoveAll(existing => existing.EventType.Equals(handler.EventType, StringComparison.OrdinalIgnoreCase));
            target.EventHandlers.Add(handler);
            target.EventHandlers = target.EventHandlers.OrderBy(existing => existing.EventType, StringComparer.OrdinalIgnoreCase).ToList();
            results.Add(Success(index, op, target.Scope, target.ItemName, "EventHandlers." + handler.EventType, previousValue: null, newValue: handler.EventType));
        }

        private static void ApplyRemoveEventHandler(
            HmiJsonScreen screen,
            HmiScreenJsonPatchOperation operation,
            int index,
            string op,
            List<HmiScreenJsonPatchOperationResult> results)
        {
            if (!TryResolveCompositeTarget(screen, operation, index, op, results, out var target))
            {
                return;
            }

            var eventType = ResolveEventType(operation);
            if (string.IsNullOrWhiteSpace(eventType))
            {
                results.Add(Failure(index, op, "The remove-event-handler operation requires eventType.", ResolveItemName(operation), "EventHandlers"));
                return;
            }

            var existingHandlers = target.EventHandlers ?? new List<HmiJsonEventHandler>();
            var removedCount = existingHandlers.RemoveAll(handler => handler.EventType.Equals(eventType!, StringComparison.OrdinalIgnoreCase));
            if (removedCount == 0)
            {
                results.Add(Failure(index, op, $"Event handler '{eventType}' was not found.", ResolveItemName(operation), "EventHandlers." + eventType));
                return;
            }

            target.EventHandlers = existingHandlers;
            results.Add(Success(index, op, target.Scope, target.ItemName, "EventHandlers." + eventType, previousValue: eventType, newValue: null));
        }

        private static void ApplySetPropertyEventHandler(
            HmiJsonScreen screen,
            HmiScreenJsonPatchOperation operation,
            int index,
            string op,
            List<HmiScreenJsonPatchOperationResult> results)
        {
            if (!TryResolveCompositeTarget(screen, operation, index, op, results, out var target))
            {
                return;
            }

            var handler = CloneOrBuildPropertyEventHandler(operation);
            if (handler == null || string.IsNullOrWhiteSpace(handler.PropertyName) || string.IsNullOrWhiteSpace(handler.EventType))
            {
                results.Add(Failure(index, op, "The set-property-event-handler operation requires propertyEventHandler or property plus eventType plus scriptCode.", ResolveItemName(operation), "PropertyEventHandlers"));
                return;
            }

            target.PropertyEventHandlers ??= new List<HmiJsonPropertyEventHandler>();
            target.PropertyEventHandlers.RemoveAll(existing =>
                existing.PropertyName.Equals(handler.PropertyName, StringComparison.OrdinalIgnoreCase) &&
                existing.EventType.Equals(handler.EventType, StringComparison.OrdinalIgnoreCase));
            target.PropertyEventHandlers.Add(handler);
            target.PropertyEventHandlers = target.PropertyEventHandlers
                .OrderBy(existing => existing.PropertyName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(existing => existing.EventType, StringComparer.OrdinalIgnoreCase)
                .ToList();
            results.Add(Success(index, op, target.Scope, target.ItemName, $"PropertyEventHandlers.{handler.PropertyName}.{handler.EventType}", previousValue: null, newValue: handler.EventType));
        }

        private static void ApplyRemovePropertyEventHandler(
            HmiJsonScreen screen,
            HmiScreenJsonPatchOperation operation,
            int index,
            string op,
            List<HmiScreenJsonPatchOperationResult> results)
        {
            if (!TryResolveCompositeTarget(screen, operation, index, op, results, out var target))
            {
                return;
            }

            var propertyName = ResolvePropertyName(operation);
            var eventType = ResolveEventType(operation);
            if (string.IsNullOrWhiteSpace(propertyName) || string.IsNullOrWhiteSpace(eventType))
            {
                results.Add(Failure(index, op, "The remove-property-event-handler operation requires property and eventType.", ResolveItemName(operation), "PropertyEventHandlers"));
                return;
            }

            var existingHandlers = target.PropertyEventHandlers ?? new List<HmiJsonPropertyEventHandler>();
            var removedCount = existingHandlers.RemoveAll(handler =>
                handler.PropertyName.Equals(propertyName!, StringComparison.OrdinalIgnoreCase) &&
                handler.EventType.Equals(eventType!, StringComparison.OrdinalIgnoreCase));
            if (removedCount == 0)
            {
                results.Add(Failure(index, op, $"Property event handler '{propertyName}.{eventType}' was not found.", ResolveItemName(operation), "PropertyEventHandlers." + propertyName + "." + eventType));
                return;
            }

            target.PropertyEventHandlers = existingHandlers;
            results.Add(Success(index, op, target.Scope, target.ItemName, "PropertyEventHandlers." + propertyName + "." + eventType, previousValue: eventType, newValue: null));
        }

        private static void ApplySetDynamization(
            HmiJsonScreen screen,
            HmiScreenJsonPatchOperation operation,
            int index,
            string op,
            List<HmiScreenJsonPatchOperationResult> results)
        {
            if (!TryResolveCompositeTarget(screen, operation, index, op, results, out var target))
            {
                return;
            }

            var dynamization = CloneOrBuildDynamization(operation);
            if (dynamization == null || string.IsNullOrWhiteSpace(dynamization.Property) || string.IsNullOrWhiteSpace(dynamization.Type))
            {
                results.Add(Failure(index, op, "The set-dynamization operation requires dynamization or property plus dynamizationType/tag.", ResolveItemName(operation), "Dynamizations"));
                return;
            }

            target.Dynamizations ??= new List<HmiJsonDynamization>();
            target.Dynamizations.RemoveAll(existing => existing.Property.Equals(dynamization.Property, StringComparison.OrdinalIgnoreCase));
            target.Dynamizations.Add(dynamization);
            target.Dynamizations = target.Dynamizations.OrderBy(existing => existing.Property, StringComparer.OrdinalIgnoreCase).ToList();
            results.Add(Success(index, op, target.Scope, target.ItemName, "Dynamizations." + dynamization.Property, previousValue: null, newValue: dynamization.Type));
        }

        private static void ApplyRemoveDynamization(
            HmiJsonScreen screen,
            HmiScreenJsonPatchOperation operation,
            int index,
            string op,
            List<HmiScreenJsonPatchOperationResult> results)
        {
            if (!TryResolveCompositeTarget(screen, operation, index, op, results, out var target))
            {
                return;
            }

            var propertyName = ResolvePropertyName(operation);
            if (string.IsNullOrWhiteSpace(propertyName))
            {
                results.Add(Failure(index, op, "The remove-dynamization operation requires property.", ResolveItemName(operation), "Dynamizations"));
                return;
            }

            var existingDynamizations = target.Dynamizations ?? new List<HmiJsonDynamization>();
            var removedCount = existingDynamizations.RemoveAll(dynamization => dynamization.Property.Equals(propertyName!, StringComparison.OrdinalIgnoreCase));
            if (removedCount == 0)
            {
                results.Add(Failure(index, op, $"Dynamization for property '{propertyName}' was not found.", ResolveItemName(operation), "Dynamizations." + propertyName));
                return;
            }

            target.Dynamizations = existingDynamizations;
            results.Add(Success(index, op, target.Scope, target.ItemName, "Dynamizations." + propertyName, previousValue: propertyName, newValue: null));
        }

        private static void ApplySetInterface(
            HmiJsonScreen screen,
            HmiScreenJsonPatchOperation operation,
            int index,
            string op,
            List<HmiScreenJsonPatchOperationResult> results)
        {
            if (!TryResolveCompositeTarget(screen, operation, index, op, results, out var target))
            {
                return;
            }

            if (!string.Equals(target.Scope, "item", StringComparison.OrdinalIgnoreCase))
            {
                results.Add(Failure(index, op, "The set-interface operation requires an item because faceplate Interface belongs to a screen item.", ResolveItemName(operation), "Interface"));
                return;
            }

            var propertyName = ResolveInterfacePropertyName(operation);
            if (string.IsNullOrWhiteSpace(propertyName))
            {
                results.Add(Failure(index, op, "The set-interface operation requires propertyName or property.", ResolveItemName(operation), "Interface"));
                return;
            }

            var hasValue = operation.Value != null;
            var interfaceDynamization = CloneOrBuildInterfaceDynamization(operation, propertyName!);
            if (!hasValue && interfaceDynamization == null)
            {
                results.Add(Failure(index, op, "The set-interface operation requires value and/or a dynamization.", ResolveItemName(operation), "Interface." + propertyName));
                return;
            }

            target.Interface ??= new List<HmiJsonInterfaceBinding>();
            var existing = target.Interface.FirstOrDefault(binding => binding.PropertyName.Equals(propertyName!, StringComparison.OrdinalIgnoreCase));
            var previousValue = existing?.Value;
            string? value = previousValue;
            if (hasValue && !TryConvertValue(operation.Value, out value, out var error))
            {
                results.Add(Failure(index, op, error ?? "The set-interface operation requires a scalar value.", ResolveItemName(operation), "Interface." + propertyName));
                return;
            }

            if (existing == null)
            {
                target.Interface.Add(new HmiJsonInterfaceBinding
                {
                    PropertyName = propertyName!,
                    Value = value
                });
            }
            else
            {
                existing.Value = value;
            }

            var binding = target.Interface.First(existingBinding =>
                existingBinding.PropertyName.Equals(propertyName!, StringComparison.OrdinalIgnoreCase));
            if (interfaceDynamization != null)
            {
                binding.Dynamizations ??= new List<HmiJsonDynamization>();
                binding.Dynamizations.RemoveAll(existingDynamization =>
                    existingDynamization.Property.Equals(interfaceDynamization.Property, StringComparison.OrdinalIgnoreCase));
                binding.Dynamizations.Add(interfaceDynamization);
                binding.Dynamizations = binding.Dynamizations
                    .OrderBy(existingDynamization => existingDynamization.Property, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            target.Interface = target.Interface
                .OrderBy(binding => binding.PropertyName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var resultProperty = interfaceDynamization == null
                ? "Interface." + propertyName
                : "Interface." + propertyName + ".Dynamizations." + interfaceDynamization.Property;
            results.Add(Success(index, op, target.Scope, target.ItemName, resultProperty, previousValue, value));
        }

        private static void ApplyProperty(
            HmiJsonScreen screen,
            HmiScreenJsonPatchOperation operation,
            int index,
            string op,
            string property,
            string? value,
            List<HmiScreenJsonPatchOperationResult> results)
        {
            var itemName = ResolveItemName(operation);
            var scope = string.IsNullOrWhiteSpace(itemName) ? "screen" : "item";
            Dictionary<string, string?> properties;
            string? resolvedItemName = null;

            if (string.IsNullOrWhiteSpace(itemName))
            {
                properties = screen.Properties;
            }
            else if (TryFindItem(screen, itemName!, out var item, out var suggestions, out var message))
            {
                properties = item!.Properties;
                resolvedItemName = item.Name;
            }
            else
            {
                results.Add(Failure(index, op, message!, itemName, property, suggestions));
                return;
            }

            properties.TryGetValue(property, out var previousValue);
            properties[property] = value;
            results.Add(Success(index, op, scope, resolvedItemName, property, previousValue, value));
        }

        private static void ApplyLayout(
            HmiJsonScreen screen,
            HmiScreenJsonPatchOperation operation,
            int index,
            string op,
            List<HmiScreenJsonPatchOperationResult> results,
            bool includePosition,
            bool includeSize)
        {
            var itemName = ResolveItemName(operation);
            if (string.IsNullOrWhiteSpace(itemName))
            {
                results.Add(Failure(index, op, "The layout operation requires item or itemName."));
                return;
            }

            if (!TryFindItem(screen, itemName!, out var item, out var suggestions, out var message))
            {
                results.Add(Failure(index, op, message!, itemName, suggestions: suggestions));
                return;
            }

            var changes = new Dictionary<string, int>();
            if (includePosition)
            {
                AddLayoutValue(changes, "Left", operation.Left);
                AddLayoutValue(changes, "Top", operation.Top);
            }

            if (includeSize)
            {
                AddLayoutValue(changes, "Width", operation.Width);
                AddLayoutValue(changes, "Height", operation.Height);
            }

            if (changes.Count == 0)
            {
                results.Add(Failure(index, op, "The layout operation did not include any applicable Left, Top, Width, or Height values.", item!.Name));
                return;
            }

            foreach (var change in changes.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
            {
                item!.Properties.TryGetValue(change.Key, out var previousValue);
                var newValue = change.Value.ToString(CultureInfo.InvariantCulture);
                item.Properties[change.Key] = newValue;
                results.Add(Success(index, op, "item", item.Name, change.Key, previousValue, newValue));
            }
        }

        private static void ApplyRenameItem(
            HmiJsonScreen screen,
            HmiScreenJsonPatchOperation operation,
            int index,
            string op,
            List<HmiScreenJsonPatchOperationResult> results)
        {
            var itemName = FirstNonEmpty(ResolveItemName(operation), ResolveSourceItemName(operation));
            if (string.IsNullOrWhiteSpace(itemName))
            {
                results.Add(Failure(index, op, "The rename-item operation requires item, itemName, source, or sourceItem."));
                return;
            }

            var newName = ResolveNewName(operation);
            if (string.IsNullOrWhiteSpace(newName))
            {
                results.Add(Failure(index, op, "The rename-item operation requires newName.", itemName));
                return;
            }

            if (!TryFindItem(screen, itemName!, out var item, out var suggestions, out var message))
            {
                results.Add(Failure(index, op, message!, itemName, suggestions: suggestions));
                return;
            }

            var conflictingItem = screen.Items.FirstOrDefault(candidate =>
                !ReferenceEquals(candidate, item) &&
                candidate.Name.Equals(newName!, StringComparison.OrdinalIgnoreCase));
            if (conflictingItem != null)
            {
                results.Add(Failure(index, op, $"Cannot rename item '{item!.Name}' to '{newName}' because another item already has that name.", item.Name));
                return;
            }

            var previousName = item!.Name;
            item.Name = newName!;
            results.Add(Success(index, op, "item", previousName, "Name", previousName, newName));
        }

        private static void ApplyCreateItem(
            HmiJsonScreen screen,
            HmiScreenJsonPatchOperation operation,
            int index,
            string op,
            List<HmiScreenJsonPatchOperationResult> results)
        {
            var itemName = ResolveItemName(operation);
            if (string.IsNullOrWhiteSpace(itemName))
            {
                results.Add(Failure(index, op, "The create-item operation requires item or itemName."));
                return;
            }

            var itemType = ResolveItemType(operation);
            if (string.IsNullOrWhiteSpace(itemType))
            {
                results.Add(Failure(index, op, "The create-item operation requires itemType, item-type, or type.", itemName));
                return;
            }

            if (screen.Items.Any(item => item.Name.Equals(itemName!, StringComparison.OrdinalIgnoreCase)))
            {
                results.Add(Failure(index, op, $"Item '{itemName}' already exists on the exported screen.", itemName));
                return;
            }

            var properties = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            if (!TryApplyPropertyOverrides(properties, operation, index, op, itemName, results))
            {
                return;
            }

            AddCreateItemLayoutValue(properties, "Left", operation.Left);
            AddCreateItemLayoutValue(properties, "Top", operation.Top);
            AddCreateItemLayoutValue(properties, "Width", operation.Width);
            AddCreateItemLayoutValue(properties, "Height", operation.Height);

            if (results.Any(result => result.Index == index && !result.Succeeded))
            {
                return;
            }

            var newItem = new HmiJsonItem
            {
                Name = itemName!,
                Type = itemType!,
                Properties = properties
            };
            screen.Items.Add(newItem);

            results.Add(Success(index, op, "item", itemName, property: null, previousValue: null, newValue: itemType));
            foreach (var property in properties.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
            {
                results.Add(Success(index, op, "item", itemName, property.Key, previousValue: null, property.Value));
            }
        }

        private static IEnumerable<string> GetTouchedProperties(HmiScreenJsonPatchOperation operation)
        {
            switch (NormalizeOperationName(ResolveOperationName(operation)))
            {
                case "setproperty":
                    var propertyName = ResolvePropertyName(operation);
                    if (!string.IsNullOrWhiteSpace(propertyName))
                    {
                        yield return propertyName!;
                    }

                    break;
                case "setproperties":
                    if (operation.Properties != null)
                    {
                        foreach (var property in operation.Properties.Keys.Where(property => !string.IsNullOrWhiteSpace(property)))
                        {
                            yield return property;
                        }
                    }

                    break;
                case "moveitem":
                    if (operation.Left.HasValue)
                    {
                        yield return "Left";
                    }

                    if (operation.Top.HasValue)
                    {
                        yield return "Top";
                    }

                    break;
                case "resizeitem":
                    if (operation.Width.HasValue)
                    {
                        yield return "Width";
                    }

                    if (operation.Height.HasValue)
                    {
                        yield return "Height";
                    }

                    break;
                case "setlayout":
                    if (operation.Left.HasValue)
                    {
                        yield return "Left";
                    }

                    if (operation.Top.HasValue)
                    {
                        yield return "Top";
                    }

                    if (operation.Width.HasValue)
                    {
                        yield return "Width";
                    }

                    if (operation.Height.HasValue)
                    {
                        yield return "Height";
                    }

                    break;
                case "createitem":
                case "duplicateitem":
                    if (operation.Properties != null)
                    {
                        foreach (var property in operation.Properties.Keys.Where(property => !string.IsNullOrWhiteSpace(property)))
                        {
                            yield return property;
                        }
                    }

                    if (operation.Left.HasValue)
                    {
                        yield return "Left";
                    }

                    if (operation.Top.HasValue)
                    {
                        yield return "Top";
                    }

                    if (operation.Width.HasValue)
                    {
                        yield return "Width";
                    }

                    if (operation.Height.HasValue)
                    {
                        yield return "Height";
                    }

                    break;
                case "seteventhandler":
                case "removeeventhandler":
                    yield return "EventHandlers";
                    break;
                case "setpropertyeventhandler":
                case "removepropertyeventhandler":
                    yield return "PropertyEventHandlers";
                    break;
                case "setdynamization":
                case "removedynamization":
                    yield return "Dynamizations";
                    break;
                case "setinterface":
                    yield return "Interface";
                    break;
            }
        }

        private static IEnumerable<string> GetFilterItemNames(HmiScreenJsonPatchOperation operation)
        {
            switch (NormalizeOperationName(ResolveOperationName(operation)))
            {
                case "duplicateitem":
                    var sourceItem = ResolveSourceItemName(operation);
                    if (!string.IsNullOrWhiteSpace(sourceItem))
                    {
                        yield return sourceItem!;
                    }

                    var targetItem = ResolveTargetItemName(operation);
                    if (!string.IsNullOrWhiteSpace(targetItem))
                    {
                        yield return targetItem!;
                    }

                    break;
                case "renameitem":
                    var renameSource = FirstNonEmpty(ResolveItemName(operation), ResolveSourceItemName(operation));
                    if (!string.IsNullOrWhiteSpace(renameSource))
                    {
                        yield return renameSource!;
                    }

                    var renameTarget = ResolveNewName(operation);
                    if (!string.IsNullOrWhiteSpace(renameTarget))
                    {
                        yield return renameTarget!;
                    }

                    break;
                default:
                    var itemName = ResolveItemName(operation);
                    if (!string.IsNullOrWhiteSpace(itemName))
                    {
                        yield return itemName!;
                    }

                    break;
            }
        }

        private static bool TryFindItem(
            HmiJsonScreen screen,
            string itemName,
            out HmiJsonItem? item,
            out List<string> suggestions,
            out string? message)
        {
            item = null;
            suggestions = new List<string>();
            message = null;

            var matches = screen.Items
                .Where(candidate => candidate.Name.Equals(itemName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matches.Count == 1)
            {
                item = matches[0];
                return true;
            }

            if (matches.Count > 1)
            {
                suggestions = matches.Select(candidate => candidate.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                message = $"Item name '{itemName}' is ambiguous on the exported screen.";
                return false;
            }

            suggestions = SuggestItemNames(screen, itemName);
            message = $"Item '{itemName}' was not found on the exported screen.";
            return false;
        }

        private static HmiScreenJsonPatchOperationResult Success(
            int index,
            string op,
            string scope,
            string? itemName,
            string? property,
            string? previousValue,
            string? newValue)
        {
            return new HmiScreenJsonPatchOperationResult
            {
                Index = index,
                Op = op,
                Succeeded = true,
                Scope = scope,
                ItemName = itemName,
                Property = property,
                PreviousValue = previousValue,
                NewValue = newValue
            };
        }

        private static HmiScreenJsonPatchOperationResult Failure(
            int index,
            string op,
            string message,
            string? itemName = null,
            string? property = null,
            List<string>? suggestions = null)
        {
            return new HmiScreenJsonPatchOperationResult
            {
                Index = index,
                Op = op,
                Succeeded = false,
                Scope = string.IsNullOrWhiteSpace(itemName) ? string.Empty : "item",
                ItemName = itemName,
                Property = property,
                Message = message,
                Suggestions = suggestions ?? new List<string>()
            };
        }

        private static bool TryResolveCompositeTarget(
            HmiJsonScreen screen,
            HmiScreenJsonPatchOperation operation,
            int index,
            string op,
            List<HmiScreenJsonPatchOperationResult> results,
            out HmiCompositeTarget target)
        {
            target = null!;
            var itemName = ResolveItemName(operation);
            if (string.IsNullOrWhiteSpace(itemName))
            {
                target = HmiCompositeTarget.ForScreen(screen);
                return true;
            }

            if (!TryFindItem(screen, itemName!, out var item, out var suggestions, out var message))
            {
                results.Add(Failure(index, op, message!, itemName, suggestions: suggestions));
                return false;
            }

            target = HmiCompositeTarget.ForItem(item!);
            return true;
        }

        private static HmiJsonEventHandler? CloneOrBuildEventHandler(HmiScreenJsonPatchOperation operation)
        {
            if (operation.EventHandler != null)
            {
                return Clone(operation.EventHandler);
            }

            var eventType = ResolveEventType(operation);
            if (string.IsNullOrWhiteSpace(eventType) || string.IsNullOrWhiteSpace(operation.ScriptCode))
            {
                return null;
            }

            return new HmiJsonEventHandler
            {
                EventType = eventType!,
                Script = new HmiJsonScriptPayload
                {
                    Async = operation.Async,
                    GlobalDefinitionAreaScriptCode = operation.GlobalDefinitionAreaScriptCode,
                    ScriptCode = operation.ScriptCode
                }
            };
        }

        private static HmiJsonPropertyEventHandler? CloneOrBuildPropertyEventHandler(HmiScreenJsonPatchOperation operation)
        {
            if (operation.PropertyEventHandler != null)
            {
                return Clone(operation.PropertyEventHandler);
            }

            var propertyName = ResolvePropertyName(operation);
            var eventType = ResolveEventType(operation);
            if (string.IsNullOrWhiteSpace(propertyName) ||
                string.IsNullOrWhiteSpace(eventType) ||
                string.IsNullOrWhiteSpace(operation.ScriptCode))
            {
                return null;
            }

            return new HmiJsonPropertyEventHandler
            {
                PropertyName = propertyName!,
                EventType = eventType!,
                Script = new HmiJsonScriptPayload
                {
                    Async = operation.Async,
                    GlobalDefinitionAreaScriptCode = operation.GlobalDefinitionAreaScriptCode,
                    ScriptCode = operation.ScriptCode
                }
            };
        }

        private static HmiJsonDynamization? CloneOrBuildDynamization(HmiScreenJsonPatchOperation operation)
        {
            if (operation.Dynamization != null)
            {
                return Clone(operation.Dynamization);
            }

            var propertyName = ResolvePropertyName(operation);
            var dynamizationType = ResolveDynamizationType(operation);
            var tag = ResolveTag(operation);
            if (string.IsNullOrWhiteSpace(dynamizationType) && !string.IsNullOrWhiteSpace(tag))
            {
                dynamizationType = "Tag";
            }

            if (string.IsNullOrWhiteSpace(dynamizationType) && !string.IsNullOrWhiteSpace(operation.DynamicTagName))
            {
                dynamizationType = "TagParameter";
            }

            if (string.IsNullOrWhiteSpace(dynamizationType) && !string.IsNullOrWhiteSpace(operation.ScriptCode))
            {
                dynamizationType = "Script";
            }

            if (string.IsNullOrWhiteSpace(propertyName) || string.IsNullOrWhiteSpace(dynamizationType))
            {
                return null;
            }

            var dynamization = new HmiJsonDynamization
            {
                Property = propertyName!,
                Type = dynamizationType!
            };

            if (dynamizationType!.Equals("Tag", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(tag))
                {
                    return null;
                }

                dynamization.Binding = new HmiJsonDynamizationBinding
                {
                    Tag = tag
                };
            }
            else if (dynamizationType.Equals("TagParameter", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(operation.DynamicTagName))
                {
                    return null;
                }

                dynamization.Binding = new HmiJsonDynamizationBinding
                {
                    DynamicTagName = operation.DynamicTagName
                };
            }
            else if (dynamizationType.Equals("Script", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(operation.ScriptCode))
                {
                    return null;
                }

                dynamization.Script = new HmiJsonScriptDynamization
                {
                    Async = operation.Async,
                    GlobalDefinitionAreaScriptCode = operation.GlobalDefinitionAreaScriptCode,
                    ScriptCode = operation.ScriptCode
                };
            }

            return dynamization;
        }

        private static HmiJsonDynamization? CloneOrBuildInterfaceDynamization(HmiScreenJsonPatchOperation operation, string interfacePropertyName)
        {
            if (operation.Dynamization == null &&
                string.IsNullOrWhiteSpace(operation.DynamizationType) &&
                string.IsNullOrWhiteSpace(operation.DynamicTagName) &&
                string.IsNullOrWhiteSpace(operation.Tag) &&
                string.IsNullOrWhiteSpace(operation.ScriptCode))
            {
                return null;
            }

            var clone = Clone(operation);
            if (clone.Dynamization == null)
            {
                clone.Property = interfacePropertyName;
            }

            if (clone.Dynamization != null && string.IsNullOrWhiteSpace(clone.Dynamization.Property))
            {
                clone.Dynamization.Property = interfacePropertyName;
            }

            return CloneOrBuildDynamization(clone);
        }

        private static bool TryConvertValue(object? value, out string? converted, out string? error)
        {
            converted = null;
            error = null;

            if (value == null)
            {
                error = "The operation requires a non-null scalar value.";
                return false;
            }

            if (value is JsonElement jsonElement)
            {
                return TryConvertJsonValue(jsonElement, out converted, out error);
            }

            if (value is string stringValue)
            {
                converted = stringValue;
                return true;
            }

            if (value is bool boolValue)
            {
                converted = boolValue.ToString();
                return true;
            }

            if (value is IFormattable formattable)
            {
                converted = formattable.ToString(null, CultureInfo.InvariantCulture);
                return true;
            }

            converted = value.ToString();
            if (converted == null)
            {
                error = "The operation value could not be converted to text.";
                return false;
            }

            return true;
        }

        private static bool TryConvertJsonValue(JsonElement value, out string? converted, out string? error)
        {
            converted = null;
            error = null;

            switch (value.ValueKind)
            {
                case JsonValueKind.String:
                    converted = value.GetString() ?? string.Empty;
                    return true;
                case JsonValueKind.Number:
                    converted = value.GetRawText();
                    return true;
                case JsonValueKind.True:
                    converted = bool.TrueString;
                    return true;
                case JsonValueKind.False:
                    converted = bool.FalseString;
                    return true;
                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                    error = "The operation requires a non-null scalar value.";
                    return false;
                default:
                    error = "The operation value must be a scalar string, number, or boolean.";
                    return false;
            }
        }

        private static string ResolveOperationName(HmiScreenJsonPatchOperation operation)
        {
            return FirstNonEmpty(
                operation.Op,
                GetExtensionString(operation, "operation")) ?? string.Empty;
        }

        private static string? ResolveItemName(HmiScreenJsonPatchOperation operation)
        {
            return FirstNonEmpty(
                operation.Item,
                operation.ItemName,
                GetExtensionString(operation, "item-name"));
        }

        private static string? ResolvePropertyName(HmiScreenJsonPatchOperation operation)
        {
            return FirstNonEmpty(
                operation.Property,
                GetExtensionString(operation, "property-name"));
        }

        private static string? ResolveInterfacePropertyName(HmiScreenJsonPatchOperation operation)
        {
            return FirstNonEmpty(
                GetExtensionString(operation, "propertyName"),
                GetExtensionString(operation, "interfaceProperty"),
                GetExtensionString(operation, "interface-property"),
                operation.Property,
                GetExtensionString(operation, "property-name"));
        }

        private static string? ResolveNewName(HmiScreenJsonPatchOperation operation)
        {
            return FirstNonEmpty(
                operation.NewName,
                operation.TargetName,
                GetExtensionString(operation, "targetName"),
                GetExtensionString(operation, "target-name"),
                GetExtensionString(operation, "new-name"));
        }

        private static string? ResolveSourceItemName(HmiScreenJsonPatchOperation operation)
        {
            return FirstNonEmpty(
                operation.Source,
                operation.SourceItem,
                GetExtensionString(operation, "source-item"));
        }

        private static string? ResolveTargetItemName(HmiScreenJsonPatchOperation operation)
        {
            return FirstNonEmpty(
                operation.Item,
                operation.ItemName,
                operation.Target,
                operation.TargetItem,
                operation.TargetName,
                GetExtensionString(operation, "targetName"),
                GetExtensionString(operation, "item-name"),
                GetExtensionString(operation, "target-name"),
                GetExtensionString(operation, "target-item"));
        }

        private static string? ResolveItemType(HmiScreenJsonPatchOperation operation)
        {
            return FirstNonEmpty(
                operation.ItemType,
                operation.Type,
                GetExtensionString(operation, "item-type"));
        }

        private static string? ResolveEventType(HmiScreenJsonPatchOperation operation)
        {
            return FirstNonEmpty(
                operation.EventType,
                operation.EventHandler?.EventType,
                operation.PropertyEventHandler?.EventType,
                GetExtensionString(operation, "event-type"));
        }

        private static string? ResolveDynamizationType(HmiScreenJsonPatchOperation operation)
        {
            return FirstNonEmpty(
                operation.DynamizationType,
                operation.Dynamization?.Type,
                GetExtensionString(operation, "dynamization-type"),
                GetExtensionString(operation, "dynamic-type"));
        }

        private static string? ResolveTag(HmiScreenJsonPatchOperation operation)
        {
            return FirstNonEmpty(
                operation.Tag,
                operation.Dynamization?.Binding?.Tag,
                GetExtensionString(operation, "tag"));
        }

        private static string? GetExtensionString(HmiScreenJsonPatchOperation operation, string propertyName)
        {
            if (operation.ExtensionData == null)
            {
                return null;
            }

            foreach (var entry in operation.ExtensionData)
            {
                if (!entry.Key.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return entry.Value.ValueKind == JsonValueKind.String
                    ? entry.Value.GetString()
                    : entry.Value.ToString();
            }

            return null;
        }

        private static string? FirstNonEmpty(params string?[] values)
        {
            return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
        }

        private static string NormalizeOperationName(string? op)
        {
            return string.IsNullOrWhiteSpace(op)
                ? string.Empty
                : new string(op!.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        }

        private static void AddLayoutValue(Dictionary<string, int> changes, string propertyName, int? value)
        {
            if (value.HasValue)
            {
                changes[propertyName] = value.Value;
            }
        }

        private static void AddCreateItemLayoutValue(Dictionary<string, string?> properties, string propertyName, int? value)
        {
            if (value.HasValue)
            {
                properties[propertyName] = value.Value.ToString(CultureInfo.InvariantCulture);
            }
        }

        private static bool TryApplyPropertyOverrides(
            Dictionary<string, string?> properties,
            HmiScreenJsonPatchOperation operation,
            int index,
            string op,
            string? itemName,
            List<HmiScreenJsonPatchOperationResult> results)
        {
            if (operation.Properties == null)
            {
                return true;
            }

            var startFailureCount = results.Count(result => result.Index == index && !result.Succeeded);
            foreach (var property in operation.Properties.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(property.Key))
                {
                    results.Add(Failure(index, op, "The operation contains an empty property name.", itemName));
                    continue;
                }

                if (!TryConvertValue(property.Value, out var value, out var error))
                {
                    results.Add(Failure(index, op, error ?? $"Property '{property.Key}' requires a scalar value.", itemName, property.Key));
                    continue;
                }

                properties[property.Key] = value;
            }

            return results.Count(result => result.Index == index && !result.Succeeded) == startFailureCount;
        }

        private static IEnumerable<string> GetDuplicateOperationOverrideProperties(
            HmiScreenJsonPatchOperation operation,
            HmiJsonItem duplicatedItem)
        {
            if (operation.Properties != null)
            {
                foreach (var property in operation.Properties.Keys.Where(property => !string.IsNullOrWhiteSpace(property)))
                {
                    yield return property;
                }
            }

            foreach (var propertyName in new[] { "Left", "Top", "Width", "Height" })
            {
                var hasValue = propertyName switch
                {
                    "Left" => operation.Left.HasValue,
                    "Top" => operation.Top.HasValue,
                    "Width" => operation.Width.HasValue,
                    "Height" => operation.Height.HasValue,
                    _ => false
                };

                if (hasValue || duplicatedItem.Properties.ContainsKey(propertyName))
                {
                    yield return propertyName;
                }
            }
        }

        private static void AddDistinct(List<string> values, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value) &&
                !values.Any(existing => existing.Equals(value, StringComparison.OrdinalIgnoreCase)))
            {
                values.Add(value!.Trim());
            }
        }

        private static List<string> BuildItemFilterSuggestions(string itemName)
        {
            return new List<string>
            {
                $"Add '{itemName}' to item-names.",
                "Re-read the screen with matching item-names before retrying with base-hash."
            };
        }

        private static List<string> BuildPropertyFilterSuggestions(string propertyName)
        {
            var suggestions = new List<string>
            {
                $"Add '{propertyName}' to properties.",
                "Re-read the screen with matching properties before retrying with base-hash."
            };

            if (HmiScreenJsonAgentSupport.CompositePropertyNames.Contains(propertyName, StringComparer.OrdinalIgnoreCase))
            {
                suggestions.Add("Composite edits must filter by the root property name: EventHandlers, PropertyEventHandlers, Dynamizations, or Interface.");
            }

            return suggestions;
        }

        private static List<string> SuggestItemNames(HmiJsonScreen screen, string itemName)
        {
            return screen.Items
                .Select(item => new
                {
                    item.Name,
                    Score = GetSimilarityScore(item.Name, itemName)
                })
                .OrderBy(item => item.Score)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .Select(item => item.Name)
                .ToList();
        }

        private static int GetSimilarityScore(string candidate, string requested)
        {
            if (candidate.IndexOf(requested, StringComparison.OrdinalIgnoreCase) >= 0 ||
                requested.IndexOf(candidate, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 0;
            }

            return GetLevenshteinDistance(
                candidate.ToUpperInvariant(),
                requested.ToUpperInvariant());
        }

        private static int GetLevenshteinDistance(string left, string right)
        {
            var distances = new int[left.Length + 1, right.Length + 1];

            for (var i = 0; i <= left.Length; i++)
            {
                distances[i, 0] = i;
            }

            for (var j = 0; j <= right.Length; j++)
            {
                distances[0, j] = j;
            }

            for (var i = 1; i <= left.Length; i++)
            {
                for (var j = 1; j <= right.Length; j++)
                {
                    var cost = left[i - 1] == right[j - 1] ? 0 : 1;
                    distances[i, j] = Math.Min(
                        Math.Min(distances[i - 1, j] + 1, distances[i, j - 1] + 1),
                        distances[i - 1, j - 1] + cost);
                }
            }

            return distances[left.Length, right.Length];
        }

        private static HmiJsonScreen CloneScreen(HmiJsonScreen screen)
        {
            return JsonSerializer.Deserialize<HmiJsonScreen>(
                JsonSerializer.Serialize(screen, SerializerOptions),
                SerializerOptions) ?? throw new InvalidOperationException("Unable to clone Unified HMI screen JSON.");
        }

        private static HmiJsonItem CloneItem(HmiJsonItem item)
        {
            return JsonSerializer.Deserialize<HmiJsonItem>(
                JsonSerializer.Serialize(item, SerializerOptions),
                SerializerOptions) ?? throw new InvalidOperationException("Unable to clone Unified HMI item JSON.");
        }

        private static T Clone<T>(T source)
        {
            return JsonSerializer.Deserialize<T>(
                JsonSerializer.Serialize(source, SerializerOptions),
                SerializerOptions) ?? throw new InvalidOperationException("Unable to clone Unified HMI JSON payload.");
        }

        private sealed class HmiCompositeTarget
        {
            private readonly HmiJsonScreen? _screen;
            private readonly HmiJsonItem? _item;

            private HmiCompositeTarget(HmiJsonScreen screen)
            {
                _screen = screen;
                Scope = "screen";
            }

            private HmiCompositeTarget(HmiJsonItem item)
            {
                _item = item;
                Scope = "item";
                ItemName = item.Name;
            }

            public string Scope { get; }

            public string? ItemName { get; }

            public List<HmiJsonEventHandler>? EventHandlers
            {
                get => _item != null ? _item.EventHandlers : _screen!.EventHandlers;
                set
                {
                    if (_item != null)
                    {
                        _item.EventHandlers = value;
                    }
                    else
                    {
                        _screen!.EventHandlers = value;
                    }
                }
            }

            public List<HmiJsonPropertyEventHandler>? PropertyEventHandlers
            {
                get => _item != null ? _item.PropertyEventHandlers : _screen!.PropertyEventHandlers;
                set
                {
                    if (_item != null)
                    {
                        _item.PropertyEventHandlers = value;
                    }
                    else
                    {
                        _screen!.PropertyEventHandlers = value;
                    }
                }
            }

            public List<HmiJsonDynamization>? Dynamizations
            {
                get => _item != null ? _item.Dynamizations : _screen!.Dynamizations;
                set
                {
                    if (_item != null)
                    {
                        _item.Dynamizations = value;
                    }
                    else
                    {
                        _screen!.Dynamizations = value;
                    }
                }
            }

            public List<HmiJsonInterfaceBinding>? Interface
            {
                get => _item?.Interface;
                set
                {
                    if (_item != null)
                    {
                        _item.Interface = value;
                    }
                }
            }

            public static HmiCompositeTarget ForScreen(HmiJsonScreen screen)
            {
                return new HmiCompositeTarget(screen);
            }

            public static HmiCompositeTarget ForItem(HmiJsonItem item)
            {
                return new HmiCompositeTarget(item);
            }
        }
    }
}
