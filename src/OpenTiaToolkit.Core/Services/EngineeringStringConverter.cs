using System;
using System.Drawing;
using System.Globalization;

namespace OpenTiaToolkit.Core.Services
{
    internal static class EngineeringStringConverter
    {
        public static object ConvertInvariant(
            string rawValue,
            Type targetType,
            Func<Type, string, object?>? customConverter = null)
        {
            var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

            var customValue = customConverter?.Invoke(underlyingType, rawValue);
            if (customValue != null)
            {
                return customValue;
            }

            if (underlyingType == typeof(string) || underlyingType == typeof(object))
            {
                return rawValue;
            }

            if (underlyingType == typeof(bool))
            {
                return bool.Parse(rawValue);
            }

            if (underlyingType == typeof(byte))
            {
                return byte.Parse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture);
            }

            if (underlyingType == typeof(sbyte))
            {
                return sbyte.Parse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture);
            }

            if (underlyingType == typeof(short))
            {
                return short.Parse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture);
            }

            if (underlyingType == typeof(ushort))
            {
                return ushort.Parse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture);
            }

            if (underlyingType == typeof(int))
            {
                return int.Parse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture);
            }

            if (underlyingType == typeof(uint))
            {
                return uint.Parse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture);
            }

            if (underlyingType == typeof(long))
            {
                return long.Parse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture);
            }

            if (underlyingType == typeof(ulong))
            {
                return ulong.Parse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture);
            }

            if (underlyingType == typeof(float))
            {
                return float.Parse(rawValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture);
            }

            if (underlyingType == typeof(double))
            {
                return double.Parse(rawValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture);
            }

            if (underlyingType == typeof(decimal))
            {
                return decimal.Parse(rawValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture);
            }

            if (underlyingType == typeof(TimeSpan))
            {
                return TimeSpan.Parse(rawValue, CultureInfo.InvariantCulture);
            }

            if (underlyingType == typeof(Color))
            {
                return ParseColor(rawValue);
            }

            if (underlyingType.IsEnum)
            {
                return Enum.Parse(underlyingType, rawValue, ignoreCase: true);
            }

            return Convert.ChangeType(rawValue, underlyingType, CultureInfo.InvariantCulture);
        }

        private static Color ParseColor(string rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return Color.Empty;
            }

            var value = rawValue.Trim();
            if (value.StartsWith("#", StringComparison.Ordinal))
            {
                return ColorTranslator.FromHtml(value);
            }

            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(value.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hexColor))
            {
                return Color.FromArgb(hexColor);
            }

            return Color.FromName(value);
        }
    }
}
