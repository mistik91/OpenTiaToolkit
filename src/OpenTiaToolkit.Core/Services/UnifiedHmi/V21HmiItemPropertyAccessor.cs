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
    internal sealed class V21HmiItemPropertyAccessor
    {
        private readonly V21HmiEngineeringPrimitives _primitives;

        internal V21HmiItemPropertyAccessor(V21HmiEngineeringPrimitives primitives)
        {
            _primitives = primitives ?? throw new ArgumentNullException(nameof(primitives));
        }

        internal IReadOnlyList<HmiItemPropertyInfo> ReadBrowsableProperties(object target)
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

        internal IReadOnlyList<HmiItemPropertyInfo> ReadAttributeProperties(object target)
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
                catch (Exception ex)
                {
                    _primitives.Logger?.LogDebug(
                        ex,
                        "Failed to read Unified HMI attribute {AttributeName} on {TargetType}",
                        info.Name,
                        target.GetType().FullName);
                }
            }

            return properties;
        }

        internal bool TryReadPropertyOrAttribute(object target, string propertyName, out HmiItemPropertyInfo property)
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

        internal bool TrySetPropertyPathValue(object target, string propertyPath, string rawValue, HmiSoftware software)
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

        internal static void TryEnsureSolidBackFillPattern(object target)
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

        internal static bool TryFormatPropertyValue(object target, PropertyInfo property, out string? value)
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

        internal object ConvertStringToAttributeValue(string rawValue, object target, EngineeringAttributeInfo attributeInfo, HmiSoftware software)
        {
            var supportedType = attributeInfo.SupportedTypes?.FirstOrDefault() ?? typeof(string);
            var convertedValue = ConvertStringToType(rawValue, supportedType, software);
            if (convertedValue is string stringValue)
            {
                return NormalizeStringAttributeValue(target, attributeInfo.Name, stringValue);
            }

            return convertedValue;
        }

        internal object ConvertStringToType(string rawValue, Type targetType, HmiSoftware software)
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
                        if (!_primitives.TryGetScreen(software, value, out var screen, out _) || screen == null)
                        {
                            throw new InvalidOperationException($"Referenced screen '{value}' was not found.");
                        }

                        return screen;
                    }

                    return null;
                });
        }

        internal static object? ConvertHmiPrimitiveString(Type underlyingType, string rawValue)
        {
            return underlyingType == typeof(Color) ? ParseColor(rawValue) : null;
        }

        internal static void SetMultilingualText(MultilingualText multilingualText, string value)
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

        internal static int? TryReadIntAttribute(object target, string attributeName)
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

        internal bool TryResolveItemType(string itemType, out HmiItemTypeDescriptor descriptor)
        {
            if (_primitives.ItemTypes.TryGetValue(_primitives.NormalizeItemTypeName(itemType), out descriptor))
            {
                return true;
            }

            if (DynamicItemTypes.Value.TryGetValue(_primitives.NormalizeItemTypeName(itemType), out var dynamicType))
            {
                descriptor = new HmiItemTypeDescriptor(dynamicType, dynamicType.Name, "Advanced", dynamicType.Name, 120, 40);
                return true;
            }

            descriptor = null!;
            return false;
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

    }
}
