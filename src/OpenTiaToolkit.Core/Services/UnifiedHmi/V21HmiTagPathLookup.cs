using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.HmiTags;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTiaToolkit.Core.Services;

internal static class V21HmiTagPathLookup
{
    internal static void PrimeRoots(HmiSoftware software, IEnumerable<string> normalizedPaths, IDictionary<string, HmiTag?> paths)
    {
        var remaining = new HashSet<string>(normalizedPaths.Where(path => !string.IsNullOrEmpty(path))
            .Select(path => path.Split('.')[0]), StringComparer.Ordinal);
        // Use exact lookups for a few roots; scan once for larger batches.
        if (remaining.Count < 8) return;
        foreach (var tag in software.Tags)
        {
            var name = tag.Name;
            if (remaining.Remove(name)) paths[name] = tag;
            if (remaining.Count == 0) break;
        }
        // Unmatched names still use native Find, preserving its case/error behavior.
    }

    internal static HmiTag? Find(HmiSoftware software, string normalizedPath, IDictionary<string, HmiTag?>? paths)
    {
        if (string.IsNullOrEmpty(normalizedPath)) return null;
        var segments = normalizedPath.Split('.');
        var path = segments[0];
        HmiTag? tag = null;
        if (paths == null || !paths.TryGetValue(path, out tag))
        {
            tag = software.Tags.Find(path);
            if (paths != null) paths[path] = tag;
        }

        for (var index = 1; tag != null && index < segments.Length; index++)
        {
            path += "." + segments[index];
            if (paths != null && paths.TryGetValue(path, out var cached)) tag = cached;
            else
            {
                tag = tag.Members.Find(segments[index]);
                if (paths != null) paths[path] = tag;
            }
        }

        return tag;
    }
}
