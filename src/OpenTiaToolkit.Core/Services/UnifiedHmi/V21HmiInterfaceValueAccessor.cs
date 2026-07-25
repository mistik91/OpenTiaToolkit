using OpenTiaToolkit.Core.Models;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace OpenTiaToolkit.Core.Services
{
    internal static class V21HmiInterfaceValueAccessor
    {
        private static readonly IReadOnlyDictionary<string, Type> KnownValueTypes =
            new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
            {
                ["Boolean"] = typeof(bool),
                ["Bool"] = typeof(bool),
                ["Byte"] = typeof(byte),
                ["SByte"] = typeof(sbyte),
                ["Int16"] = typeof(short),
                ["UInt16"] = typeof(ushort),
                ["Int32"] = typeof(int),
                ["UInt32"] = typeof(uint),
                ["Int64"] = typeof(long),
                ["UInt64"] = typeof(ulong),
                ["Single"] = typeof(float),
                ["Float"] = typeof(float),
                ["Double"] = typeof(double),
                ["Decimal"] = typeof(decimal),
                ["String"] = typeof(string),
                ["TimeSpan"] = typeof(TimeSpan)
            };

        internal static bool TryConvertValue(
            object interfaceEntry,
            HmiJsonInterfaceBinding binding,
            out object? convertedValue,
            out string? error)
        {
            convertedValue = null;
            error = null;

            var valueProperty = V21HmiEngineeringPrimitives.ResolvePublicInstanceProperty(
                interfaceEntry.GetType(),
                "Value");
            if (valueProperty == null || !valueProperty.CanRead)
            {
                error = $"Value is not readable on interface entry type '{interfaceEntry.GetType().Name}'.";
                return false;
            }

            object? currentValue;
            try
            {
                currentValue = valueProperty.GetValue(interfaceEntry);
            }
            catch (Exception ex)
            {
                error = $"The current interface value could not be read: {FormatExceptionMessage(ex)}";
                return false;
            }

            if (!TryResolveTargetType(interfaceEntry, currentValue, binding.ValueType, out var targetType, out error))
            {
                return false;
            }

            try
            {
                convertedValue = EngineeringStringConverter.ConvertInvariant(
                    binding.Value ?? string.Empty,
                    targetType!,
                    V21HmiEngineeringPrimitives.ConvertHmiPrimitiveString);
                return true;
            }
            catch (Exception ex)
            {
                error =
                    $"Value '{binding.Value ?? string.Empty}' could not be converted to {targetType!.Name}: " +
                    FormatExceptionMessage(ex);
                return false;
            }
        }

        internal static bool TryWriteValue(
            object interfaceEntry,
            HmiJsonInterfaceBinding binding,
            out string? error)
        {
            error = null;
            if (!TryConvertValue(interfaceEntry, binding, out var convertedValue, out error))
            {
                return false;
            }

            var valueProperty = V21HmiEngineeringPrimitives.ResolvePublicInstanceProperty(
                interfaceEntry.GetType(),
                "Value");
            if (valueProperty == null || !valueProperty.CanWrite)
            {
                error = $"Value is not writable on interface entry type '{interfaceEntry.GetType().Name}'.";
                return false;
            }

            try
            {
                valueProperty.SetValue(interfaceEntry, convertedValue);
                return true;
            }
            catch (Exception ex)
            {
                error =
                    $"The converted {convertedValue?.GetType().Name ?? "null"} value could not be written: " +
                    FormatExceptionMessage(ex);
                return false;
            }
        }

        internal static bool TryWriteRawValue(
            object interfaceEntry,
            object? value,
            out string? error)
        {
            error = null;
            var valueProperty = V21HmiEngineeringPrimitives.ResolvePublicInstanceProperty(
                interfaceEntry.GetType(),
                "Value");
            if (valueProperty == null || !valueProperty.CanWrite)
            {
                error = $"Value is not writable on interface entry type '{interfaceEntry.GetType().Name}'.";
                return false;
            }

            try
            {
                valueProperty.SetValue(interfaceEntry, value);
                return true;
            }
            catch (Exception ex)
            {
                error = $"The original interface value could not be restored: {FormatExceptionMessage(ex)}";
                return false;
            }
        }

        private static bool TryResolveTargetType(
            object interfaceEntry,
            object? currentValue,
            string? declaredValueType,
            out Type? targetType,
            out string? error)
        {
            targetType = null;
            error = null;

            if (currentValue != null)
            {
                targetType = currentValue.GetType();
                return true;
            }

            var declaredType = ResolveKnownValueType(declaredValueType);
            var supportedTypes = ReadSupportedValueTypes(interfaceEntry);
            if (declaredType != null && supportedTypes.Contains(declaredType))
            {
                targetType = declaredType;
                return true;
            }

            if (supportedTypes.Count == 1)
            {
                targetType = supportedTypes[0];
                return true;
            }

            if (declaredType != null)
            {
                targetType = declaredType;
                return true;
            }

            if (supportedTypes.Count > 1)
            {
                error =
                    "The interface value type is ambiguous. Supported types: " +
                    string.Join(", ", supportedTypes.Select(type => type.Name)) +
                    ".";
                return false;
            }

            error =
                "The interface value type could not be resolved because the current value is null and no " +
                "supported or exported ValueType is available.";
            return false;
        }

        private static Type? ResolveKnownValueType(string? valueType)
        {
            if (string.IsNullOrWhiteSpace(valueType))
            {
                return null;
            }

            var normalized = valueType!.Trim();
            if (normalized.StartsWith("System.", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring("System.".Length);
            }

            return KnownValueTypes.TryGetValue(normalized, out var type) ? type : null;
        }

        private static IReadOnlyList<Type> ReadSupportedValueTypes(object interfaceEntry)
        {
            try
            {
                var getAttributeInfos = interfaceEntry.GetType().GetMethod(
                    "GetAttributeInfos",
                    BindingFlags.Public | BindingFlags.Instance,
                    binder: null,
                    types: Type.EmptyTypes,
                    modifiers: null);
                if (getAttributeInfos?.Invoke(interfaceEntry, null) is not IEnumerable attributeInfos)
                {
                    return Array.Empty<Type>();
                }

                foreach (var attributeInfo in attributeInfos.Cast<object>())
                {
                    var name = attributeInfo.GetType().GetProperty("Name")?.GetValue(attributeInfo) as string;
                    if (!string.Equals(name, "Value", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (attributeInfo.GetType().GetProperty("SupportedTypes")?.GetValue(attributeInfo) is not IEnumerable supportedTypes)
                    {
                        return Array.Empty<Type>();
                    }

                    return supportedTypes.Cast<object>()
                        .OfType<Type>()
                        .Where(type => type != typeof(object))
                        .Distinct()
                        .ToList();
                }
            }
            catch
            {
                // Runtime boxed values and the exported ValueType remain deterministic fallbacks.
            }

            return Array.Empty<Type>();
        }

        private static string FormatExceptionMessage(Exception exception)
        {
            var current = exception;
            while (current is TargetInvocationException && current.InnerException != null)
            {
                current = current.InnerException;
            }

            return current.Message;
        }
    }
}
