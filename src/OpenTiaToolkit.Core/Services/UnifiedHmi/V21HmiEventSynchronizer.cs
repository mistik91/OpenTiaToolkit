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
using static OpenTiaToolkit.Core.Services.V21HmiEngineeringPrimitives;
using static OpenTiaToolkit.Core.Services.V21HmiDynamizationSynchronizer;
using static OpenTiaToolkit.Core.Services.V21HmiMappingTableSynchronizer;

namespace OpenTiaToolkit.Core.Services
{
    internal static class V21HmiEventSynchronizer
    {
        internal static bool TryGetNamedComposition(object? source, string propertyName, out object composition)
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

        internal static List<HmiJsonEventHandler> ReadEventHandlersFromComposition(object composition)
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

        internal static List<HmiJsonInterfaceBinding> ReadInterfaceBindingsFromComposition(object composition)
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

        internal static HmiJsonInterfaceBinding? TryReadInterfaceBindingExport(object? binding)
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

        internal static List<HmiJsonDynamization>? TryReadDynamizationsFromOwner(object? owner)
        {
            if (!TryGetNamedComposition(owner, "Dynamizations", out var composition))
            {
                return null;
            }

            var dynamizations = ReadSupportedDynamizationsFromComposition(composition);
            return dynamizations.Count > 0 ? dynamizations : null;
        }

        internal static HmiJsonEventHandler? TryReadEventHandlerExport(object? eventHandler)
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

        internal static List<HmiJsonPropertyEventHandler> ReadPropertyEventHandlersFromComposition(object composition)
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

        internal static HmiJsonPropertyEventHandler? TryReadPropertyEventHandlerExport(object? eventHandler)
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

        internal static HmiJsonScriptPayload? ReadScriptPayload(object? script)
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

        internal static bool TryResolveEventHandlerEnumType(object? owner, Type ownerType, out Type enumType)
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

        internal static bool TryResolveEventHandlerEnumTypeFromCompositionType(Type? compositionType, out Type enumType)
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

        internal static bool TryValidateEventHandlerDefinitions(
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

        internal static bool TryValidateEventHandler(
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

            if (string.IsNullOrWhiteSpace(handler?.Script?.ScriptCode))
            {
                error = $"Event handler '{eventType}' requires non-empty ScriptCode.";
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

        internal static bool TrySynchronizeEventHandlers(
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

        internal static Dictionary<string, (object Raw, HmiJsonEventHandler? Export, string EventType)> ReadRawEventHandlers(object composition)
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

        internal static object CreateEventHandlerObject(object composition, string eventType, Type eventTypeEnum)
        {
            var createMethod = ResolveSingleCreateMethod(
                composition.GetType(),
                "EventHandlers.Create(enum)",
                eventTypeEnum);

            var enumValue = Enum.Parse(eventTypeEnum, eventType, ignoreCase: true);
            return createMethod.Invoke(composition, new[] { enumValue })
                ?? throw new InvalidOperationException($"Failed to create event handler '{eventType}'.");
        }

        internal static void ApplyEventHandlerExport(object targetHandler, HmiJsonEventHandler desired)
        {
            ApplyScriptPayload(TryReadPropertyValue(targetHandler, "Script"), desired.Script);
        }

        internal static bool TryResolvePropertyEventHandlerEnumType(object? owner, Type ownerType, out Type enumType)
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

        internal static bool TryResolvePropertyEventHandlerEnumTypeFromCompositionType(Type? compositionType, out Type enumType)
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

        internal static bool TryValidatePropertyEventHandlerDefinitions(
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

        internal static bool TryValidatePropertyEventHandler(
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

            if (string.IsNullOrWhiteSpace(handler?.Script?.ScriptCode))
            {
                error = $"Property event handler '{propertyName}/{eventType}' requires non-empty ScriptCode.";
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

        internal static bool TrySynchronizePropertyEventHandlers(
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

        internal static Dictionary<string, (object Raw, HmiJsonPropertyEventHandler? Export, string Key)> ReadRawPropertyEventHandlers(object composition)
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

        internal static object CreatePropertyEventHandlerObject(
            object composition,
            string propertyName,
            string eventType,
            Type eventTypeEnum)
        {
            var createMethod = ResolveSingleCreateMethod(
                composition.GetType(),
                "PropertyEventHandlers.Create(string, enum)",
                typeof(string),
                eventTypeEnum);

            var enumValue = Enum.Parse(eventTypeEnum, eventType, ignoreCase: true);
            return createMethod.Invoke(composition, new[] { propertyName, enumValue })
                ?? throw new InvalidOperationException($"Failed to create property event handler '{propertyName}/{eventType}'.");
        }

        internal static void ApplyPropertyEventHandlerExport(object targetHandler, HmiJsonPropertyEventHandler desired)
        {
            ApplyScriptPayload(TryReadPropertyValue(targetHandler, "Script"), desired.Script);
        }

        internal static void ApplyScriptPayload(object? targetScript, HmiJsonScriptPayload? desired)
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

    }
}
