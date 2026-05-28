using OpenTiaToolkit.Core.Commands.Hmi;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTiaToolkit.Core.Services
{
    /// <summary>
    /// Normalizes and evaluates Unified HMI JSON filter options.
    /// </summary>
    internal static class HmiJsonFilterMatcher
    {
        public static HmiJsonFilterOptions Normalize(HmiJsonFilterOptions? filters)
        {
            if (filters == null)
            {
                return new HmiJsonFilterOptions();
            }

            return new HmiJsonFilterOptions
            {
                Screens = NormalizeList(filters.Screens, NormalizeScreenPath),
                ItemNames = NormalizeList(filters.ItemNames, NormalizeToken),
                Properties = NormalizeList(filters.Properties, value => value.Trim())
            };
        }

        public static bool ShouldIncludeScreen(string screenPath, HmiJsonFilterOptions filters)
        {
            var normalizedPath = NormalizeScreenPath(screenPath);
            return filters.Screens.Count == 0 ||
                   filters.Screens.Contains(normalizedPath, StringComparer.OrdinalIgnoreCase);
        }

        public static bool ShouldIncludeItem(string itemName, HmiJsonFilterOptions filters)
        {
            var normalizedName = NormalizeToken(itemName);
            return filters.ItemNames.Count == 0 ||
                   filters.ItemNames.Contains(normalizedName, StringComparer.OrdinalIgnoreCase);
        }

        public static bool ShouldIncludeProperty(string propertyName, HmiJsonFilterOptions filters)
        {
            return filters.Properties.Count == 0 ||
                   filters.Properties.Contains(propertyName.Trim(), StringComparer.OrdinalIgnoreCase);
        }

        public static bool ShouldIncludeCompositeProperty(string propertyName, HmiJsonFilterOptions filters)
        {
            if (filters.Properties.Count == 0)
            {
                return true;
            }

            var normalizedPropertyName = propertyName.Trim();
            var prefix = normalizedPropertyName + ".";

            return filters.Properties.Any(filter =>
                filter.Equals(normalizedPropertyName, StringComparison.OrdinalIgnoreCase) ||
                filter.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        public static bool ShouldIncludeCompositeEntry(string propertyName, string entryKey, HmiJsonFilterOptions filters)
        {
            if (filters.Properties.Count == 0)
            {
                return true;
            }

            var normalizedPropertyName = propertyName.Trim();
            var normalizedEntryKey = entryKey.Trim();
            var entryPath = normalizedPropertyName + "." + normalizedEntryKey;
            return filters.Properties.Contains(normalizedPropertyName, StringComparer.OrdinalIgnoreCase) ||
                   filters.Properties.Any(filter =>
                       filter.Equals(entryPath, StringComparison.OrdinalIgnoreCase) ||
                       filter.StartsWith(entryPath + ".", StringComparison.OrdinalIgnoreCase));
        }

        private static IReadOnlyList<string> NormalizeList(IEnumerable<string> values, Func<string, string> normalizer)
        {
            return (values ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => normalizer(value))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string NormalizeScreenPath(string screenPath)
        {
            return string.Join(
                "/",
                screenPath
                    .Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(segment => segment.Trim())
                    .Where(segment => !string.IsNullOrWhiteSpace(segment)));
        }

        private static string NormalizeToken(string value)
        {
            return value.Trim().ToLowerInvariant();
        }
    }
}
