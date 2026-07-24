using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace OpenTiaToolkit.Core.Services
{
    internal static class PlcTransferRules
    {
        public static Regex? TryCreateRegex(string? filterRegex, List<string> errors)
        {
            if (string.IsNullOrEmpty(filterRegex)) return null;
            try
            {
                return new Regex(filterRegex, RegexOptions.IgnoreCase);
            }
            catch (Exception)
            {
                errors.Add($"Invalid regex filter: {filterRegex}");
                return null;
            }
        }

        public static IEnumerable<object> EnumerateBlocks(object group) =>
            Enumerate(group, "Blocks");

        public static IEnumerable<object> EnumerateTypes(object group) =>
            Enumerate(group, "Types");

        private static IEnumerable<object> Enumerate(object group, string collectionName)
        {
            var items = TiaReflectionHelper.GetEnumerableProperty(group, collectionName);
            if (items != null)
                foreach (var item in items)
                    yield return item;
            var groups = TiaReflectionHelper.GetGroups(group);
            if (groups == null) yield break;
            foreach (var childGroup in groups)
                foreach (var item in Enumerate(childGroup, collectionName))
                    yield return item;
        }
    }
}
