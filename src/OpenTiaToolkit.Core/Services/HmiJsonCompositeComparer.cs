using OpenTiaToolkit.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTiaToolkit.Core.Services
{
    /// <summary>
    /// Shared clone, comparison, and change-detection helpers for composite Unified HMI JSON payloads.
    /// </summary>
    internal static class HmiJsonCompositeComparer
    {
        public static List<HmiJsonEventHandler>? CloneEventHandlers(IEnumerable<HmiJsonEventHandler>? eventHandlers)
        {
            if (eventHandlers == null)
            {
                return null;
            }

            return eventHandlers
                .Where(handler => handler != null)
                .Select(handler => new HmiJsonEventHandler
                {
                    EventType = handler.EventType,
                    Script = CloneScriptPayload(handler.Script)
                })
                .OrderBy(handler => handler.EventType, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static List<HmiJsonPropertyEventHandler>? ClonePropertyEventHandlers(IEnumerable<HmiJsonPropertyEventHandler>? propertyEventHandlers)
        {
            if (propertyEventHandlers == null)
            {
                return null;
            }

            return propertyEventHandlers
                .Where(handler => handler != null)
                .Select(handler => new HmiJsonPropertyEventHandler
                {
                    PropertyName = handler.PropertyName,
                    EventType = handler.EventType,
                    Script = CloneScriptPayload(handler.Script)
                })
                .OrderBy(handler => handler.PropertyName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(handler => handler.EventType, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static List<HmiJsonDynamization>? CloneDynamizations(IEnumerable<HmiJsonDynamization>? dynamizations)
        {
            if (dynamizations == null)
            {
                return null;
            }

            return dynamizations
                .Where(dynamization => dynamization != null)
                .Select(dynamization => new HmiJsonDynamization
                {
                    Property = dynamization.Property,
                    Type = dynamization.Type,
                    Binding = dynamization.Binding == null
                        ? null
                        : new HmiJsonDynamizationBinding
                        {
                            Tag = dynamization.Binding.Tag,
                            ReadOnly = dynamization.Binding.ReadOnly,
                            UseIndirectAddressing = dynamization.Binding.UseIndirectAddressing,
                            ResourceList = dynamization.Binding.ResourceList,
                            DynamicTagName = dynamization.Binding.DynamicTagName
                        },
                    Script = dynamization.Script == null
                        ? null
                        : new HmiJsonScriptDynamization
                        {
                            Async = dynamization.Script.Async,
                            GlobalDefinitionAreaScriptCode = dynamization.Script.GlobalDefinitionAreaScriptCode,
                            ScriptCode = dynamization.Script.ScriptCode,
                            Trigger = dynamization.Script.Trigger == null
                                ? null
                                : new HmiJsonScriptTrigger
                                {
                                    Type = dynamization.Script.Trigger.Type,
                                    CustomDuration = dynamization.Script.Trigger.CustomDuration,
                                    Tags = dynamization.Script.Trigger.Tags?.ToList()
                                }
                        },
                    Flashing = dynamization.Flashing == null
                        ? null
                        : new HmiJsonFlashingDynamization
                        {
                            Color = dynamization.Flashing.Color,
                            AlternateColor = dynamization.Flashing.AlternateColor,
                            FlashingCondition = dynamization.Flashing.FlashingCondition,
                            FlashingRate = dynamization.Flashing.FlashingRate
                        },
                    ValueConverter = dynamization.ValueConverter == null
                        ? null
                        : CloneValueConverter(dynamization.ValueConverter)
                })
                .OrderBy(dynamization => dynamization.Property, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static List<HmiJsonInterfaceBinding>? CloneInterfaceBindings(IEnumerable<HmiJsonInterfaceBinding>? bindings)
        {
            if (bindings == null)
            {
                return null;
            }

            return bindings
                .Where(binding => binding != null)
                .Select(binding => new HmiJsonInterfaceBinding
                {
                    PropertyName = binding.PropertyName,
                    ValueType = binding.ValueType,
                    Value = binding.Value,
                    Dynamizations = CloneDynamizations(binding.Dynamizations)
                })
                .OrderBy(binding => binding.PropertyName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static List<string> GetChangedEventHandlerKeys(
            IEnumerable<HmiJsonEventHandler>? current,
            IEnumerable<HmiJsonEventHandler>? desired)
        {
            var currentMap = BuildEventHandlerMap(current);
            var desiredMap = BuildEventHandlerMap(desired);

            return currentMap.Keys
                .Union(desiredMap.Keys, StringComparer.OrdinalIgnoreCase)
                .Where(key =>
                {
                    currentMap.TryGetValue(key, out var currentHandler);
                    desiredMap.TryGetValue(key, out var desiredHandler);
                    return !AreEventHandlersEquivalent(currentHandler, desiredHandler);
                })
                .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static List<string> GetChangedPropertyEventHandlerKeys(
            IEnumerable<HmiJsonPropertyEventHandler>? current,
            IEnumerable<HmiJsonPropertyEventHandler>? desired)
        {
            var currentMap = BuildPropertyEventHandlerMap(current);
            var desiredMap = BuildPropertyEventHandlerMap(desired);

            return currentMap.Keys
                .Union(desiredMap.Keys, StringComparer.OrdinalIgnoreCase)
                .Where(key =>
                {
                    currentMap.TryGetValue(key, out var currentHandler);
                    desiredMap.TryGetValue(key, out var desiredHandler);
                    return !ArePropertyEventHandlersEquivalent(currentHandler, desiredHandler);
                })
                .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static List<string> GetChangedDynamizationProperties(
            IEnumerable<HmiJsonDynamization>? current,
            IEnumerable<HmiJsonDynamization>? desired)
        {
            var currentMap = BuildDynamizationMap(current);
            var desiredMap = BuildDynamizationMap(desired);

            return currentMap.Keys
                .Union(desiredMap.Keys, StringComparer.OrdinalIgnoreCase)
                .Where(key =>
                {
                    currentMap.TryGetValue(key, out var currentDynamization);
                    desiredMap.TryGetValue(key, out var desiredDynamization);
                    return !AreDynamizationsEquivalent(currentDynamization, desiredDynamization);
                })
                .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static List<string> GetChangedInterfaceProperties(
            IEnumerable<HmiJsonInterfaceBinding>? current,
            IEnumerable<HmiJsonInterfaceBinding>? desired)
        {
            var currentMap = BuildInterfaceBindingMap(current);
            var desiredMap = BuildInterfaceBindingMap(desired);

            return currentMap.Keys
                .Union(desiredMap.Keys, StringComparer.OrdinalIgnoreCase)
                .Where(key =>
                {
                    currentMap.TryGetValue(key, out var currentBinding);
                    desiredMap.TryGetValue(key, out var desiredBinding);
                    return !AreInterfaceBindingsEquivalent(currentBinding, desiredBinding);
                })
                .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static Dictionary<string, HmiJsonEventHandler> BuildEventHandlerMap(IEnumerable<HmiJsonEventHandler>? eventHandlers)
        {
            var map = new Dictionary<string, HmiJsonEventHandler>(StringComparer.OrdinalIgnoreCase);
            foreach (var handler in eventHandlers ?? Enumerable.Empty<HmiJsonEventHandler>())
            {
                var key = NormalizeString(handler?.EventType);
                if (!string.IsNullOrWhiteSpace(key) && handler != null)
                {
                    map[key!] = handler;
                }
            }

            return map;
        }

        public static Dictionary<string, HmiJsonPropertyEventHandler> BuildPropertyEventHandlerMap(IEnumerable<HmiJsonPropertyEventHandler>? propertyEventHandlers)
        {
            var map = new Dictionary<string, HmiJsonPropertyEventHandler>(StringComparer.OrdinalIgnoreCase);
            foreach (var handler in propertyEventHandlers ?? Enumerable.Empty<HmiJsonPropertyEventHandler>())
            {
                var key = GetPropertyEventHandlerKey(handler?.PropertyName, handler?.EventType);
                if (!string.IsNullOrWhiteSpace(key) && handler != null)
                {
                    map[key!] = handler;
                }
            }

            return map;
        }

        public static string? GetPropertyEventHandlerKey(string? propertyName, string? eventType)
        {
            var normalizedPropertyName = NormalizeString(propertyName);
            var normalizedEventType = NormalizeString(eventType);
            return string.IsNullOrWhiteSpace(normalizedPropertyName) || string.IsNullOrWhiteSpace(normalizedEventType)
                ? null
                : normalizedPropertyName + "\u001f" + normalizedEventType;
        }

        public static string FormatPropertyEventHandlerKey(string key)
        {
            return key.Replace("\u001f", ".");
        }

        public static Dictionary<string, HmiJsonDynamization> BuildDynamizationMap(IEnumerable<HmiJsonDynamization>? dynamizations)
        {
            var map = new Dictionary<string, HmiJsonDynamization>(StringComparer.OrdinalIgnoreCase);
            foreach (var dynamization in dynamizations ?? Enumerable.Empty<HmiJsonDynamization>())
            {
                var key = NormalizeString(dynamization?.Property);
                if (!string.IsNullOrWhiteSpace(key) && dynamization != null)
                {
                    map[key!] = dynamization;
                }
            }

            return map;
        }

        public static Dictionary<string, HmiJsonInterfaceBinding> BuildInterfaceBindingMap(IEnumerable<HmiJsonInterfaceBinding>? bindings)
        {
            var map = new Dictionary<string, HmiJsonInterfaceBinding>(StringComparer.OrdinalIgnoreCase);
            foreach (var binding in bindings ?? Enumerable.Empty<HmiJsonInterfaceBinding>())
            {
                var key = NormalizeString(binding?.PropertyName);
                if (!string.IsNullOrWhiteSpace(key) && binding != null)
                {
                    map[key!] = binding;
                }
            }

            return map;
        }

        public static bool AreEventHandlerCollectionsEquivalent(
            IEnumerable<HmiJsonEventHandler>? left,
            IEnumerable<HmiJsonEventHandler>? right)
        {
            var leftMap = BuildEventHandlerMap(left);
            var rightMap = BuildEventHandlerMap(right);
            if (leftMap.Count != rightMap.Count)
            {
                return false;
            }

            foreach (var entry in rightMap)
            {
                if (!leftMap.TryGetValue(entry.Key, out var leftHandler) ||
                    !AreEventHandlersEquivalent(leftHandler, entry.Value))
                {
                    return false;
                }
            }

            return true;
        }

        public static bool AreDynamizationCollectionsEquivalent(
            IEnumerable<HmiJsonDynamization>? left,
            IEnumerable<HmiJsonDynamization>? right)
        {
            var leftMap = BuildDynamizationMap(left);
            var rightMap = BuildDynamizationMap(right);
            if (leftMap.Count != rightMap.Count)
            {
                return false;
            }

            foreach (var entry in rightMap)
            {
                if (!leftMap.TryGetValue(entry.Key, out var leftDynamization) ||
                    !AreDynamizationsEquivalent(leftDynamization, entry.Value))
                {
                    return false;
                }
            }

            return true;
        }

        public static bool ArePropertyEventHandlerCollectionsEquivalent(
            IEnumerable<HmiJsonPropertyEventHandler>? left,
            IEnumerable<HmiJsonPropertyEventHandler>? right)
        {
            var leftMap = BuildPropertyEventHandlerMap(left);
            var rightMap = BuildPropertyEventHandlerMap(right);
            if (leftMap.Count != rightMap.Count)
            {
                return false;
            }

            foreach (var entry in rightMap)
            {
                if (!leftMap.TryGetValue(entry.Key, out var leftHandler) ||
                    !ArePropertyEventHandlersEquivalent(leftHandler, entry.Value))
                {
                    return false;
                }
            }

            return true;
        }

        public static bool AreInterfaceBindingCollectionsEquivalent(
            IEnumerable<HmiJsonInterfaceBinding>? left,
            IEnumerable<HmiJsonInterfaceBinding>? right)
        {
            var leftMap = BuildInterfaceBindingMap(left);
            var rightMap = BuildInterfaceBindingMap(right);
            if (leftMap.Count != rightMap.Count)
            {
                return false;
            }

            foreach (var entry in rightMap)
            {
                if (!leftMap.TryGetValue(entry.Key, out var leftBinding) ||
                    !AreInterfaceBindingsEquivalent(leftBinding, entry.Value))
                {
                    return false;
                }
            }

            return true;
        }

        public static bool AreEventHandlersEquivalent(HmiJsonEventHandler? left, HmiJsonEventHandler? right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null)
            {
                return false;
            }

            return StringsEqual(left.EventType, right.EventType) &&
                   Nullable.Equals(left.Script?.Async, right.Script?.Async) &&
                   StringsEqual(left.Script?.GlobalDefinitionAreaScriptCode, right.Script?.GlobalDefinitionAreaScriptCode) &&
                   StringsEqual(left.Script?.ScriptCode, right.Script?.ScriptCode);
        }

        public static bool ArePropertyEventHandlersEquivalent(HmiJsonPropertyEventHandler? left, HmiJsonPropertyEventHandler? right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null)
            {
                return false;
            }

            return StringsEqual(left.PropertyName, right.PropertyName) &&
                   StringsEqual(left.EventType, right.EventType) &&
                   Nullable.Equals(left.Script?.Async, right.Script?.Async) &&
                   StringsEqual(left.Script?.GlobalDefinitionAreaScriptCode, right.Script?.GlobalDefinitionAreaScriptCode) &&
                   StringsEqual(left.Script?.ScriptCode, right.Script?.ScriptCode);
        }

        public static bool AreDynamizationsEquivalent(HmiJsonDynamization? left, HmiJsonDynamization? right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null)
            {
                return false;
            }

            return StringsEqual(left.Property, right.Property) &&
                   StringsEqual(left.Type, right.Type) &&
                   StringsEqual(left.Binding?.Tag, right.Binding?.Tag) &&
                   Nullable.Equals(left.Binding?.ReadOnly, right.Binding?.ReadOnly) &&
                   Nullable.Equals(left.Binding?.UseIndirectAddressing, right.Binding?.UseIndirectAddressing) &&
                   StringsEqual(left.Binding?.ResourceList, right.Binding?.ResourceList) &&
                   StringsEqual(left.Binding?.DynamicTagName, right.Binding?.DynamicTagName) &&
                   Nullable.Equals(left.Script?.Async, right.Script?.Async) &&
                   StringsEqual(left.Script?.GlobalDefinitionAreaScriptCode, right.Script?.GlobalDefinitionAreaScriptCode) &&
                   StringsEqual(left.Script?.ScriptCode, right.Script?.ScriptCode) &&
                   StringsEqual(left.Script?.Trigger?.Type, right.Script?.Trigger?.Type) &&
                   StringsEqual(left.Script?.Trigger?.CustomDuration, right.Script?.Trigger?.CustomDuration) &&
                   StringListsEqual(left.Script?.Trigger?.Tags, right.Script?.Trigger?.Tags) &&
                   StringsEqual(left.Flashing?.Color, right.Flashing?.Color) &&
                   StringsEqual(left.Flashing?.AlternateColor, right.Flashing?.AlternateColor) &&
                   StringsEqual(left.Flashing?.FlashingCondition, right.Flashing?.FlashingCondition) &&
                   StringsEqual(left.Flashing?.FlashingRate, right.Flashing?.FlashingRate) &&
                   Nullable.Equals(left.ValueConverter?.IsFormulaSelected, right.ValueConverter?.IsFormulaSelected) &&
                   StringsEqual(left.ValueConverter?.Formula, right.ValueConverter?.Formula) &&
                   AreMappingTablesEquivalent(left.ValueConverter?.MappingTable, right.ValueConverter?.MappingTable);
        }

        public static bool AreInterfaceBindingsEquivalent(HmiJsonInterfaceBinding? left, HmiJsonInterfaceBinding? right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null)
            {
                return false;
            }

            return StringsEqual(left.PropertyName, right.PropertyName) &&
                   StringsEqual(left.ValueType, right.ValueType) &&
                   StringsEqual(left.Value, right.Value) &&
                   AreDynamizationCollectionsEquivalent(left.Dynamizations, right.Dynamizations);
        }

        private static HmiJsonScriptPayload? CloneScriptPayload(HmiJsonScriptPayload? script)
        {
            return script == null
                ? null
                : new HmiJsonScriptPayload
                {
                    Async = script.Async,
                    GlobalDefinitionAreaScriptCode = script.GlobalDefinitionAreaScriptCode,
                    ScriptCode = script.ScriptCode
                };
        }

        private static HmiJsonValueConverter CloneValueConverter(HmiJsonValueConverter valueConverter)
        {
            return new HmiJsonValueConverter
            {
                IsFormulaSelected = valueConverter.IsFormulaSelected,
                Formula = valueConverter.Formula,
                MappingTable = CloneMappingTable(valueConverter.MappingTable)
            };
        }

        private static HmiJsonMappingTable? CloneMappingTable(HmiJsonMappingTable? mappingTable)
        {
            return mappingTable == null
                ? null
                : new HmiJsonMappingTable
                {
                    ConditionType = mappingTable.ConditionType,
                    Entries = mappingTable.Entries?
                        .Select(entry => new HmiJsonMappingTableEntry
                        {
                            EntryType = entry.EntryType,
                            Condition = entry.Condition,
                            From = entry.From,
                            To = entry.To,
                            RangeType = entry.RangeType,
                            BitDynamizationType = entry.BitDynamizationType,
                            Relevant = entry.Relevant,
                            Value = entry.Value,
                            AlternateValue = entry.AlternateValue,
                            Flashing = entry.Flashing,
                            FlashingRate = entry.FlashingRate
                        })
                        .ToList()
                };
        }

        private static bool AreMappingTablesEquivalent(HmiJsonMappingTable? left, HmiJsonMappingTable? right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null)
            {
                return false;
            }

            var leftEntries = left.Entries ?? new List<HmiJsonMappingTableEntry>();
            var rightEntries = right.Entries ?? new List<HmiJsonMappingTableEntry>();
            if (!StringsEqual(left.ConditionType, right.ConditionType) || leftEntries.Count != rightEntries.Count)
            {
                return false;
            }

            for (var index = 0; index < rightEntries.Count; index++)
            {
                if (!AreMappingTableEntriesEquivalent(leftEntries[index], rightEntries[index]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool AreMappingTableEntriesEquivalent(HmiJsonMappingTableEntry? left, HmiJsonMappingTableEntry? right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null)
            {
                return false;
            }

            return StringsEqual(left.EntryType, right.EntryType) &&
                   StringsEqual(left.Condition, right.Condition) &&
                   StringsEqual(left.From, right.From) &&
                   StringsEqual(left.To, right.To) &&
                   StringsEqual(left.RangeType, right.RangeType) &&
                   StringsEqual(left.BitDynamizationType, right.BitDynamizationType) &&
                   StringsEqual(left.Relevant, right.Relevant) &&
                   StringsEqual(left.Value, right.Value) &&
                   StringsEqual(left.AlternateValue, right.AlternateValue) &&
                   Nullable.Equals(left.Flashing, right.Flashing) &&
                   StringsEqual(left.FlashingRate, right.FlashingRate);
        }

        private static bool StringListsEqual(IReadOnlyList<string>? left, IReadOnlyList<string>? right)
        {
            var leftValues = (left ?? Array.Empty<string>())
                .Select(NormalizeString)
                .ToList();
            var rightValues = (right ?? Array.Empty<string>())
                .Select(NormalizeString)
                .ToList();

            if (leftValues.Count != rightValues.Count)
            {
                return false;
            }

            for (var index = 0; index < leftValues.Count; index++)
            {
                if (!string.Equals(leftValues[index], rightValues[index], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool StringsEqual(string? left, string? right)
        {
            return string.Equals(NormalizeString(left), NormalizeString(right), StringComparison.Ordinal);
        }

        private static string? NormalizeString(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? null
                : value?.Trim();
        }
    }
}
