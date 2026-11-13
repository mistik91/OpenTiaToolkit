using OpenTiaToolkit.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTiaToolkit.Core.Services;

internal static class HmiTrendBatchRules
{
    // Match the batch cache's archive identity and the setters' effective values.
    // Per-tag acquisition settings and read-only storage folders are independent.
    internal static IReadOnlyList<string> ConflictingDataLogs(IReadOnlyList<HmiTrendCreateRequest> configs) =>
        configs.Where(config => !string.IsNullOrWhiteSpace(config.DataLogName))
            .GroupBy(config => config.DataLogName, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Select(config => (
                Math.Max(1, config.LogMaxSize),
                Math.Max(1, config.DurationDays),
                Math.Max(1, config.SegmentMaxSize))).Distinct().Skip(1).Any())
            .Select(group => group.Key)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
