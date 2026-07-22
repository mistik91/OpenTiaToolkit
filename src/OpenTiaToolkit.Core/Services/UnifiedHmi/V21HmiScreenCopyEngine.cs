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

namespace OpenTiaToolkit.Core.Services
{
    internal sealed class V21HmiScreenCopyEngine
    {
        private readonly V21UnifiedHmiAccess _access;

        internal V21HmiScreenCopyEngine(V21UnifiedHmiAccess access)
        {
            _access = access ?? throw new ArgumentNullException(nameof(access));
        }

        internal bool TryCopyScreenFromTemplate(
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

            if (!_access.TryGetScreen(software, templateScreenPath, out var templateScreen, out var normalizedTemplatePath) || templateScreen == null)
            {
                error = $"Template screen '{templateScreenPath}' was not found.";
                return false;
            }

            var normalizedTargetPath = _access.NormalizeScreenPath(targetScreenPath);
            if (string.IsNullOrWhiteSpace(normalizedTargetPath))
            {
                error = "Target screen path cannot be empty.";
                return false;
            }

            if (_access.TryGetScreen(software, normalizedTargetPath, out _, out _))
            {
                error = $"Target screen '{normalizedTargetPath}' already exists.";
                return false;
            }

            HmiScreen? targetScreen = null;

            try
            {
                targetScreen = _access.EnsureScreen(software, normalizedTargetPath);
                if (targetScreen == null)
                {
                    error = $"Failed to create target screen '{normalizedTargetPath}'.";
                    return false;
                }

                var warnings = new List<string>();
                TryCopyDirectProperty(templateScreen, targetScreen, "BackGraphic", warnings);
                CopyPropertyGraph(templateScreen, targetScreen, ScreenCopyRootPropertyExclusions, findText, replaceText, 0, new HashSet<object>(ReferenceEqualityComparer.Instance), warnings);
                CopyEventHandlers(templateScreen, targetScreen, findText, replaceText, warnings);

                foreach (var templateItem in templateScreen.ScreenItems.Cast<HmiScreenItemBase>())
                {
                    var targetItem = _access.CreateScreenItem(targetScreen, templateItem.GetType().Name, templateItem.Name);
                    if (targetItem == null)
                    {
                        throw new InvalidOperationException($"Item type '{templateItem.GetType().Name}' is not supported for screen copy.");
                    }

                    CopyPropertyGraph(templateItem, targetItem, ItemCopyRootPropertyExclusions, findText, replaceText, 0, new HashSet<object>(ReferenceEqualityComparer.Instance), warnings);
                    CopyEventHandlers(templateItem, targetItem, findText, replaceText, warnings);
                }

                result = new HmiScreenCopyResult
                {
                    TemplatePath = normalizedTemplatePath,
                    TargetPath = normalizedTargetPath,
                    ReplacementApplied = !string.IsNullOrWhiteSpace(findText),
                    CopiedItemCount = targetScreen.ScreenItems.Count,
                    CopiedScreenEventHandlerCount = CountEventHandlers(targetScreen),
                    CopiedItemEventHandlerCount = targetScreen.ScreenItems.Cast<HmiScreenItemBase>().Sum(CountEventHandlers),
                    Screen = _access.ToScreenInfo(targetScreen, normalizedTargetPath, includeItems: true)
                };
                result.Warnings.AddRange(warnings);
                return true;
            }
            catch (Exception ex)
            {
                TryDelete(targetScreen);
                error = ex.InnerException?.Message ?? ex.Message;
                return false;
            }
        }

        internal static void TryCopyDirectProperty(object source, object target, string propertyName, List<string>? warnings = null)
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
            catch (Exception ex)
            {
                warnings?.Add($"Failed to copy direct property '{propertyName}' from {source.GetType().Name} to {target.GetType().Name}: {FormatExceptionMessage(ex)}");
            }
        }

        internal static void CopyWritableAttributes(object source, object target, string? findText, string? replaceText, List<string>? warnings)
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
                catch (Exception ex)
                {
                    warnings?.Add($"Failed to read attribute '{attribute.Name}' from {source.GetType().Name}: {FormatExceptionMessage(ex)}");
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
                catch (Exception ex)
                {
                    warnings?.Add($"Failed to copy attribute '{attribute.Name}' from {source.GetType().Name} to {target.GetType().Name}: {FormatExceptionMessage(ex)}");
                }
            }
        }

        internal static void CopyPropertyGraph(
            object source,
            object target,
            HashSet<string> rootExclusions,
            string? findText,
            string? replaceText,
            int depth,
            HashSet<object> visited,
            List<string>? warnings = null)
        {
            if (depth > 4 || !visited.Add(source))
            {
                return;
            }

            CopyWritableAttributes(source, target, findText, replaceText, warnings);

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
                        visited,
                        warnings);
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
                    catch (Exception ex)
                    {
                        warnings?.Add($"Failed to copy multilingual property '{property.Name}' from {source.GetType().Name} to {target.GetType().Name}: {FormatExceptionMessage(ex)}");
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

                CopyPropertyGraph(sourceValue, targetValue, rootExclusions, findText, replaceText, depth + 1, visited, warnings);
            }
        }

        internal static bool TryAssignPropertyValue(object target, PropertyInfo property, object sourceValue, string? findText, string? replaceText)
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

        internal static bool IsTerminalCopyType(Type propertyType)
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

        internal static object ApplyReplacementToValue(object value, string? findText, string? replaceText)
        {
            if (value is string stringValue)
            {
                return ApplyReplacement(stringValue, findText, replaceText);
            }

            return value;
        }

        internal static string ApplyReplacement(string value, string? findText, string? replaceText)
        {
            if (string.IsNullOrEmpty(findText))
            {
                return value;
            }

            return value.Replace(findText, replaceText ?? string.Empty);
        }

        internal static void CopyMultilingualText(MultilingualText source, MultilingualText target, string? findText, string? replaceText)
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

        internal static void TryCopyComposition(
            object? sourceComposition,
            object? targetComposition,
            string? findText,
            string? replaceText,
            int depth,
            HashSet<object> visited,
            List<string>? warnings = null)
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

            EnsureCompositionItems(targetComposition, sourceItems.Count, sourceItems, warnings);
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
                CopyPropertyGraph(sourceItem, targetItem, EmptyRootPropertyExclusions, findText, replaceText, depth, visited, warnings);
                CopyEventHandlers(sourceItem, targetItem, findText, replaceText, warnings);
            }
        }

        internal static void EnsureCompositionItems(object targetComposition, int requiredCount, IReadOnlyList<object> sourceItems, List<string>? warnings)
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
                if (!TryCreateCompositionItem(targetComposition, sourceItems[index], createMethods, warnings))
                {
                    return;
                }
            }
        }

        internal static bool TryCreateCompositionItem(object targetComposition, object sourceItem, IReadOnlyList<MethodInfo> createMethods, List<string>? warnings)
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
                catch (Exception ex)
                {
                    warnings?.Add($"Failed to create composition item on {targetComposition.GetType().Name}: {FormatExceptionMessage(ex)}");
                }
            }

            return false;
        }

        internal static object? FindMatchingCompositionItem(
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

        internal static int CountEventHandlers(object owner)
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

        internal static void CopyEventHandlers(object sourceOwner, object targetOwner, string? findText, string? replaceText, List<string>? warnings = null)
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
                catch (Exception ex)
                {
                    warnings?.Add($"Failed to create event handler on {targetOwner.GetType().Name}: {FormatExceptionMessage(ex)}");
                    continue;
                }

                CopyScript(
                    TryReadPropertyValue(sourceHandler, "Script"),
                    TryReadPropertyValue(targetHandler, "Script"),
                    findText,
                    replaceText,
                    warnings);
            }
        }

        internal static void CopyScript(object? sourceScript, object? targetScript, string? findText, string? replaceText, List<string>? warnings = null)
        {
            if (sourceScript == null || targetScript == null)
            {
                return;
            }

            TryCopyScriptProperty(sourceScript, targetScript, "Async", null, null, warnings);
            TryCopyScriptProperty(sourceScript, targetScript, "GlobalDefinitionAreaScriptCode", findText, replaceText, warnings);
            TryCopyScriptProperty(sourceScript, targetScript, "ScriptCode", findText, replaceText, warnings);
        }

        internal static void TryCopyScriptProperty(object source, object target, string propertyName, string? findText, string? replaceText, List<string>? warnings = null)
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
            catch (Exception ex)
            {
                warnings?.Add($"Failed to copy script property '{propertyName}' from {source.GetType().Name} to {target.GetType().Name}: {FormatExceptionMessage(ex)}");
            }
        }

    }
}
