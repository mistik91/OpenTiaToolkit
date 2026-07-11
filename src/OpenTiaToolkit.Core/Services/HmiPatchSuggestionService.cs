using System;
using System.Collections.Generic;
using System.Linq;
using OpenTiaToolkit.Core.Models;

namespace OpenTiaToolkit.Core.Services;

internal static class HmiPatchSuggestionService
{
    public static List<string> BuildItemFilterSuggestions(string itemName) => new List<string>
    {
        $"Add '{itemName}' to item-names.",
        "Re-read the screen with matching item-names before retrying with base-hash."
    };

    public static List<string> BuildPropertyFilterSuggestions(string propertyName)
    {
        var suggestions = new List<string>
        {
            $"Add '{propertyName}' to properties.",
            "Re-read the screen with matching properties before retrying with base-hash."
        };
        if (HmiScreenJsonAgentSupport.CompositePropertyNames.Contains(propertyName, StringComparer.OrdinalIgnoreCase))
            suggestions.Add("Composite edits must filter by the root property name: EventHandlers, PropertyEventHandlers, Dynamizations, or Interface.");
        return suggestions;
    }

    public static List<string> SuggestItemNames(HmiJsonScreen screen, string itemName) => screen.Items
        .Select(item => new { item.Name, Score = Similarity(item.Name, itemName) })
        .OrderBy(item => item.Score)
        .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
        .Take(3)
        .Select(item => item.Name)
        .ToList();

    private static int Similarity(string candidate, string requested)
    {
        if (candidate.IndexOf(requested, StringComparison.OrdinalIgnoreCase) >= 0 ||
            requested.IndexOf(candidate, StringComparison.OrdinalIgnoreCase) >= 0) return 0;
        string left = candidate.ToUpperInvariant();
        string right = requested.ToUpperInvariant();
        var distances = new int[left.Length + 1, right.Length + 1];
        for (int i = 0; i <= left.Length; i++) distances[i, 0] = i;
        for (int j = 0; j <= right.Length; j++) distances[0, j] = j;
        for (int i = 1; i <= left.Length; i++)
            for (int j = 1; j <= right.Length; j++)
            {
                int cost = left[i - 1] == right[j - 1] ? 0 : 1;
                distances[i, j] = Math.Min(
                    Math.Min(distances[i - 1, j] + 1, distances[i, j - 1] + 1),
                    distances[i - 1, j - 1] + cost);
            }
        return distances[left.Length, right.Length];
    }
}
