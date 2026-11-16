using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.HmiTags;
using System.Collections.Generic;

namespace OpenTiaToolkit.Core.Services;

internal static class V21HmiTagPathLookup
{
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
