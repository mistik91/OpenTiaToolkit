using System;
using System.Collections.Generic;
using System.Linq;
using OpenTiaToolkit.Core.Models;

namespace OpenTiaToolkit.Core.Services;

internal static class PlcBehaviorCompareAnalyzer
{
    internal static bool HasNoNewCompareDifferences(PlcSoftwareCompareResult baseline, PlcSoftwareCompareResult current)
    {
        var allowed = new HashSet<string>(baseline.Entries.Where(item => item.HasDifference).Select(Identity), StringComparer.Ordinal);
        return current.Entries.Where(item => item.HasDifference).Select(Identity).All(allowed.Contains);
    }

    private static string Identity(PlcSoftwareCompareEntry item) => string.Join("\u001f", item.Path, item.State, item.LeftName ?? string.Empty, item.RightName ?? string.Empty, item.Detail ?? string.Empty);
}
