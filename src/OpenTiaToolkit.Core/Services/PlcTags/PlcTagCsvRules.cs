using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTiaToolkit.Core.Services.PlcTags
{
    internal static class PlcTagCsvRules
    {
        public static void AddTagIndex(
            Dictionary<string, List<PlcTagCsvTagState>> index,
            string key,
            PlcTagCsvTagState tag)
        {
            if (!index.TryGetValue(key, out var matches))
            {
                matches = new List<PlcTagCsvTagState>();
                index[key] = matches;
            }

            matches.Add(tag);
        }

        public static string AllocateTagName(string requestedName, HashSet<string> reservedTagNames)
        {
            if (reservedTagNames.Add(requestedName))
            {
                return requestedName;
            }

            var suffix = 1;
            string candidate;
            do
            {
                candidate = $"{requestedName}({suffix++})";
            }
            while (!reservedTagNames.Add(candidate));

            return candidate;
        }

        public static bool HasExactNameConflict(
            string tagName,
            PlcTagCsvTagState allowedTag,
            Dictionary<string, List<PlcTagCsvTagState>> tagsByFullName) =>
            tagsByFullName.TryGetValue(tagName, out var matches)
            && matches.Any(match => !ReferenceEquals(match.TagObject, allowedTag.TagObject));

        public static void AddAmbiguousMatchError(
            TagUpdateRow csvRow,
            string matchDescription,
            List<PlcTagCsvTagState> matches,
            TagUpdateResult result)
        {
            result.Errors.Add(new TagUpdateError
            {
                Item = csvRow.TagName,
                Error = $"Multiple existing tags match {matchDescription}. Update is ambiguous.",
                Details = FormatTagLocations(matches),
                LineNumber = csvRow.LineNumber
            });
            result.Summary.TagsSkipped++;
        }

        public static void AddExactNameConflictError(
            TagUpdateRow csvRow,
            List<PlcTagCsvTagState> matches,
            TagUpdateResult result)
        {
            result.Errors.Add(new TagUpdateError
            {
                Item = csvRow.TagName,
                Error = $"A tag named '{csvRow.TagName}' already exists. Creation or rename would create a duplicate.",
                Details = FormatTagLocations(matches),
                LineNumber = csvRow.LineNumber
            });
            result.Summary.TagsSkipped++;
        }

        private static string FormatTagLocations(IEnumerable<PlcTagCsvTagState> matches) =>
            string.Join("; ", matches.Select(match => $"[{match.TagTableName}] {match.FullName}"));
    }

    internal sealed class PlcTagCsvTagState
    {
        public object TagObject { get; set; } = null!;
        public string TagTableName { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string NormalizedAddress { get; set; } = string.Empty;
        public string BaseName { get; set; } = string.Empty;
        public int PageNumber { get; set; }
    }
}
