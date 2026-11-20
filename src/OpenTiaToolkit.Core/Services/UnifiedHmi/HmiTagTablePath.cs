using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.HmiTags;
using System;
using System.Linq;

namespace OpenTiaToolkit.Core.Services;

internal static class HmiTagTablePath
{
    internal static bool IsValid(string path) => !path.Split('/').Any(string.IsNullOrWhiteSpace);

    internal static HmiTagTableComposition Resolve(HmiSoftware software, string path, out string tableName)
    {
        if (!IsValid(path))
            throw new ArgumentException("Specify a table-path with no empty segments.", nameof(path));

        var segments = path.Split('/');
        var tables = software.TagTables;
        var groups = software.TagTableGroups;
        foreach (var groupName in segments.Take(segments.Length - 1))
        {
            var group = groups.Find(groupName)
                ?? throw new InvalidOperationException($"HMI tag table group '{groupName}' was not found.");
            tables = group.TagTables;
            groups = group.Groups;
        }

        tableName = segments[segments.Length - 1];
        return tables;
    }
}
