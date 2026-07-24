using Siemens.Engineering.HmiUnified.UI.Screens;
using System;
using System.Drawing;
using System.Globalization;
using System.Reflection;

namespace OpenTiaToolkit.Core.Services
{
    internal static class HmiJsonScreenPropertyAccessor
    {
        internal static bool TryGetValue(HmiScreen screen, string propertyName, out string? value)
        {
            value = null;
            switch (propertyName)
            {
                case "Width": value = screen.Width.ToString(CultureInfo.InvariantCulture); return true;
                case "Height": value = screen.Height.ToString(CultureInfo.InvariantCulture); return true;
                case "Enabled": value = screen.Enabled.ToString(); return true;
            }

            try
            {
                var property = screen.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (property == null || !property.CanRead) return false;
                var rawValue = property.GetValue(screen);
                if (rawValue == null) return false;
                value = rawValue is string stringValue ? stringValue : Convert.ToString(rawValue, CultureInfo.InvariantCulture);
                return true;
            }
            catch { return false; }
        }

        internal static bool TrySetValue(HmiScreen screen, string propertyName, string value, out string? error)
        {
            error = null;
            try
            {
                switch (propertyName)
                {
                    case "Width": screen.Width = uint.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture); return true;
                    case "Height": screen.Height = uint.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture); return true;
                    case "Enabled": screen.Enabled = bool.Parse(value); return true;
                }

                var property = screen.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (property == null || !property.CanWrite)
                {
                    error = $"Property '{propertyName}' is not writable on screens.";
                    return false;
                }

                property.SetValue(screen, ConvertValue(value, property.PropertyType));
                return true;
            }
            catch (Exception ex)
            {
                error = ex.InnerException?.Message ?? ex.Message;
                return false;
            }
        }

        internal static bool TryValidateValue(HmiScreen screen, string propertyName, string value, out string? error)
        {
            error = null;
            try
            {
                switch (propertyName)
                {
                    case "Width": uint.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture); return true;
                    case "Height": uint.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture); return true;
                    case "Enabled": bool.Parse(value); return true;
                }

                var property = screen.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (property == null || !property.CanWrite)
                {
                    error = $"Property '{propertyName}' is not writable on screens.";
                    return false;
                }

                ConvertValue(value, property.PropertyType);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.InnerException?.Message ?? ex.Message;
                return false;
            }
        }

        private static object ConvertValue(string value, Type propertyType)
        {
            var targetType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
            if (targetType == typeof(bool)) return bool.Parse(value);
            if (targetType == typeof(int)) return int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
            if (targetType == typeof(uint)) return uint.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
            if (targetType == typeof(Color)) return ColorTranslator.FromHtml(value);
            if (targetType.IsEnum) return Enum.Parse(targetType, value, ignoreCase: true);
            return value;
        }
    }
}
