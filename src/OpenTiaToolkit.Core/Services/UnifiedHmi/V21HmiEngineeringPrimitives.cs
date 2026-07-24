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
    internal sealed class V21HmiEngineeringPrimitives
    {
        internal static readonly MethodInfo CreateScreenItemGenericMethod = typeof(HmiScreenItemBaseComposition)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(method =>
                string.Equals(method.Name, "Create", StringComparison.Ordinal) &&
                method.IsGenericMethodDefinition &&
                method.GetParameters().Length == 1 &&
                method.GetParameters()[0].ParameterType == typeof(string));
        internal static readonly Lazy<Dictionary<string, Type>> DynamicItemTypes = new Lazy<Dictionary<string, Type>>(BuildDynamicItemTypeMap);
        internal static readonly HashSet<string> ScreenCopyRootPropertyExclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Name",
            "Parent",
            "EventHandlers",
            "ScreenItems"
        };
        internal static readonly HashSet<string> ItemCopyRootPropertyExclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Name",
            "Parent",
            "EventHandlers"
        };
        internal static readonly HashSet<string> AttributeCopyExclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Name"
        };
        internal static readonly HashSet<string> EmptyRootPropertyExclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        internal readonly Dictionary<string, HmiItemTypeDescriptor> ItemTypes;
        internal readonly ILogger? Logger;

        internal V21HmiEngineeringPrimitives(ILogger? logger)
        {
            Logger = logger;
            ItemTypes = BuildItemTypeMap();
        }

        internal static string FormatExceptionMessage(Exception ex)
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

        internal static void DeleteEngineeringObject(object target)
        {
            var method = target.GetType().GetMethod("Delete", BindingFlags.Public | BindingFlags.Instance);
            if (method == null)
            {
                throw new InvalidOperationException($"Type '{target.GetType().Name}' does not expose Delete().");
            }

            method.Invoke(target, null);
        }

        internal static void SetObjectPropertyValueFlexible(object targetObject, string propertyName, object rawValue)
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

        internal static void SetObjectPropertyValueRaw(object targetObject, string propertyName, object rawValue)
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

        internal static bool TryValidateObjectPropertyValue(Type targetType, string propertyName, object? rawValue, out string? error)
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

            if (!property.CanWrite)
            {
                error = $"{propertyName}: property is read-only.";
                return false;
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

        internal static object ConvertInvariantStringToType(string rawValue, Type targetType)
        {
            return EngineeringStringConverter.ConvertInvariant(rawValue, targetType, ConvertHmiPrimitiveString);
        }


        internal static object? TryReadPropertyValue(object? source, string propertyName)
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

        internal static PropertyInfo? ResolvePublicInstanceProperty(Type type, string propertyName)
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

        internal static int GetInheritanceDistance(Type sourceType, Type? declaringType)
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

        internal static void TryDelete(object? target)
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

        internal static bool IsMultilingualText(Type type)
        {
            return type == typeof(MultilingualText);
        }

        internal static string NormalizeStringPropertyValue(object target, PropertyInfo property, string value)
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

        internal static string NormalizeStringAttributeValue(object target, string attributeName, string value)
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

        internal static Color ParseColor(string value)
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

        internal sealed class ResolvedProperty
        {
            public ResolvedProperty(object target, PropertyInfo propertyInfo)
            {
                Target = target;
                PropertyInfo = propertyInfo;
            }

            public object Target { get; }

            public PropertyInfo PropertyInfo { get; }
        }

        internal sealed class ResolvedAttribute
        {
            public ResolvedAttribute(object target, EngineeringAttributeInfo attributeInfo)
            {
                Target = target;
                AttributeInfo = attributeInfo;
            }

            public object Target { get; }

            public EngineeringAttributeInfo AttributeInfo { get; }
        }

        internal static Dictionary<string, HmiItemTypeDescriptor> BuildItemTypeMap()
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
                new HmiItemTypeDescriptor(
                    typeof(HmiFaceplateContainer),
                    "HmiFaceplateContainer",
                    "Containers",
                    "Faceplate container",
                    220,
                    140,
                    new[] { "ContainedType" },
                    "faceplatecontainer",
                    "faceplate-container"),
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


        internal static string NormalizeToken(string value)
        {
            return value.Replace(" ", string.Empty)
                .Replace("-", string.Empty)
                .Replace("_", string.Empty)
                .ToLowerInvariant();
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


        internal static HmiScreenGroup? TryGetGroup(HmiSoftware software, IEnumerable<string> segments)
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


        internal static ResolvedProperty? ResolveProperty(object target, string propertyPath)
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

        internal static ResolvedAttribute? ResolveAttribute(object target, string attributePath)
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


        internal static string? FormatValue(object? value)
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


        internal static object? ConvertHmiPrimitiveString(Type underlyingType, string rawValue)
        {
            return underlyingType == typeof(Color) ? ParseColor(rawValue) : null;
        }


        internal static object? GetAttributeValue(object target, string attributeName)
        {
            var method = target.GetType().GetMethod("GetAttribute", new[] { typeof(string) });
            return method?.Invoke(target, new object[] { attributeName });
        }

        internal static void SetAttributeValue(object target, string attributeName, object value)
        {
            var method = target.GetType().GetMethod("SetAttribute", new[] { typeof(string), typeof(object) });
            if (method == null)
            {
                throw new InvalidOperationException($"Target type '{target.GetType().Name}' does not support SetAttribute.");
            }

            method.Invoke(target, new[] { (object)attributeName, value });
        }


        internal static Dictionary<string, Type> BuildDynamicItemTypeMap()
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

        internal static string? ReadMultilingualText(MultilingualText multilingualText)
        {
            return multilingualText.Items
                .Cast<MultilingualTextItem>()
                .Select(item => UnifiedRichTextCodec.Decode(item.Text))
                .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text))
                ?? string.Empty;
        }

        internal static IEnumerable<EngineeringAttributeInfo> GetAttributeInfos(object target)
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

    }
}
