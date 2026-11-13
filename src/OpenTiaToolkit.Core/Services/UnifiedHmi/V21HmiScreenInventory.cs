using OpenTiaToolkit.Core.Models;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.UI.Base;
using Siemens.Engineering.HmiUnified.UI.ScreenGroup;
using Siemens.Engineering.HmiUnified.UI.Screens;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTiaToolkit.Core.Services
{
    /// <summary>Screen inventory projection and lightweight canonical path discovery.</summary>
    internal static class V21HmiScreenInventory
    {
        public static string NormalizeScreenPath(string? screenPath)
        {
            if (string.IsNullOrWhiteSpace(screenPath))
            {
                return string.Empty;
            }

            return string.Join(
                "/",
                screenPath!
                    .Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(segment => segment.Trim())
                    .Where(segment => !string.IsNullOrWhiteSpace(segment)));
        }

        public static IReadOnlyList<HmiScreenInfo> ListScreens(HmiSoftware software, string? groupPath, bool recursive)
        {
            var normalizedGroupPath = NormalizeGroupPath(groupPath);
            var results = new List<HmiScreenInfo>();

            if (string.IsNullOrEmpty(normalizedGroupPath))
            {
                foreach (var screen in software.Screens)
                {
                    results.Add(ToScreenInfo(screen, screen.Name, includeItems: false));
                }

                foreach (var group in software.ScreenGroups)
                {
                    CollectScreens(group, group.Name, recursive, results);
                }
            }
            else
            {
                var group = FindGroup(software, normalizedGroupPath.Split('/'));
                if (group == null)
                {
                    return Array.Empty<HmiScreenInfo>();
                }

                foreach (var screen in group.Screens)
                {
                    results.Add(ToScreenInfo(screen, CombinePath(normalizedGroupPath, screen.Name), includeItems: false));
                }

                if (recursive)
                {
                    foreach (var childGroup in group.Groups)
                    {
                        CollectScreens(childGroup, CombinePath(normalizedGroupPath, childGroup.Name), recursive, results);
                    }
                }
            }

            return results
                .OrderBy(screen => screen.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static IReadOnlyList<string> ListScreenPaths(HmiSoftware software)
        {
            var paths = software.Screens.Select(screen => screen.Name).ToList();
            foreach (var group in software.ScreenGroups)
                CollectScreenPaths(group, group.Name, paths);
            return paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static void CollectScreenPaths(HmiScreenGroup group, string groupPath, ICollection<string> paths)
        {
            foreach (var screen in group.Screens)
                paths.Add(CombinePath(groupPath, screen.Name));
            foreach (var child in group.Groups)
                CollectScreenPaths(child, CombinePath(groupPath, child.Name), paths);
        }

        public static HmiScreenInfo ToScreenInfo(HmiScreen screen, string path, bool includeItems)
        {
            var items = includeItems
                ? screen.ScreenItems.Cast<HmiScreenItemBase>().Select(item => ToItemInfo(item, path)).ToList()
                : new List<HmiScreenItemInfo>();

            var normalizedPath = NormalizeScreenPath(path);
            var slashIndex = normalizedPath.LastIndexOf('/');

            return new HmiScreenInfo
            {
                Name = screen.Name,
                Path = normalizedPath,
                GroupPath = slashIndex >= 0 ? normalizedPath.Substring(0, slashIndex) : null,
                Width = (int)screen.Width,
                Height = (int)screen.Height,
                Enabled = screen.Enabled,
                ItemCount = screen.ScreenItems.Count,
                Items = items
            };
        }

        public static HmiScreenItemInfo ToItemInfo(HmiScreenItemBase item, string screenPath)
        {
            return new HmiScreenItemInfo
            {
                ScreenPath = NormalizeScreenPath(screenPath),
                Name = item.Name,
                Type = item.GetType().Name,
                Left = V21HmiItemPropertyAccessor.TryReadIntAttribute(item, "Left"),
                Top = V21HmiItemPropertyAccessor.TryReadIntAttribute(item, "Top"),
                Width = V21HmiItemPropertyAccessor.TryReadIntAttribute(item, "Width"),
                Height = V21HmiItemPropertyAccessor.TryReadIntAttribute(item, "Height")
            };
        }

        private static void CollectScreens(HmiScreenGroup group, string groupPath, bool recursive, List<HmiScreenInfo> results)
        {
            foreach (var screen in group.Screens)
            {
                results.Add(new HmiScreenInfo
                {
                    Name = screen.Name,
                    Path = CombinePath(groupPath, screen.Name),
                    GroupPath = groupPath,
                    Width = (int)screen.Width,
                    Height = (int)screen.Height,
                    Enabled = screen.Enabled,
                    ItemCount = screen.ScreenItems.Count
                });
            }

            if (!recursive)
            {
                return;
            }

            foreach (var childGroup in group.Groups)
            {
                CollectScreens(childGroup, CombinePath(groupPath, childGroup.Name), recursive, results);
            }
        }

        internal static HmiScreenGroup? FindGroup(HmiSoftware software, IEnumerable<string> segments)
        {
            HmiScreenGroup? currentGroup = null;

            foreach (var segment in segments)
            {
                currentGroup = currentGroup == null
                    ? software.ScreenGroups.Find(segment)
                    : currentGroup.Groups.Find(segment);

                if (currentGroup == null)
                {
                    return null;
                }
            }

            return currentGroup;
        }

        private static string NormalizeGroupPath(string? path) => NormalizeScreenPath(path);
        private static string CombinePath(string prefix, string leaf) => string.IsNullOrWhiteSpace(prefix) ? leaf : prefix + "/" + leaf;
    }
}
