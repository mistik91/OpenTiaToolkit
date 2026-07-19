using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace OpenTiaToolkit.Core.Services.Hardware
{
    internal static class HardwareHexCodec
    {
        public static byte[] Parse(string text)
        {
            var compact = new string((text ?? string.Empty)
                .Where(character => !char.IsWhiteSpace(character) && character != '-' && character != ',').ToArray());
            if (compact.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) compact = compact.Substring(2);
            if (compact.Length == 0 || compact.Length % 2 != 0)
                throw new FormatException("dataHex must contain an even number of hexadecimal digits.");

            var bytes = new byte[compact.Length / 2];
            for (var index = 0; index < bytes.Length; index++)
                bytes[index] = byte.Parse(compact.Substring(index * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return bytes;
        }

        public static string Format(IEnumerable<byte> data) =>
            string.Join(" ", data.Select(value => value.ToString("X2", CultureInfo.InvariantCulture)));
    }

    internal static class HardwareAttributeValueConverter
    {
        public static object ConvertJsonValue(object? value)
        {
            if (!(value is JsonElement element)) return value ?? string.Empty;
            switch (element.ValueKind)
            {
                case JsonValueKind.True: return true;
                case JsonValueKind.False: return false;
                case JsonValueKind.Number:
                    if (element.TryGetInt32(out var intValue)) return intValue;
                    if (element.TryGetInt64(out var longValue)) return longValue;
                    return element.GetDouble();
                case JsonValueKind.String: return element.GetString() ?? string.Empty;
                default: return element.ToString();
            }
        }

        public static bool ExpectedValueMatches(object? actual, object? expected) => string.Equals(
            System.Convert.ToString(actual, CultureInfo.InvariantCulture),
            System.Convert.ToString(expected, CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
    }
}
