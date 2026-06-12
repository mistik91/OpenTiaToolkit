using System;
using System.Collections.Generic;

namespace OpenTiaToolkit.CLI.Output
{
    internal static class TreeTextFormatter
    {
        public const int DefaultMaxDepth = 20;

        public static void Format<TNode>(
            TNode node,
            List<string> lines,
            Func<TNode, string> getName,
            Func<TNode, string?> getType,
            Func<TNode, IReadOnlyCollection<TNode>?> getChildren,
            string prefix = "",
            int depth = 0,
            int maxDepth = DefaultMaxDepth)
        {
            if (depth > maxDepth)
            {
                lines.Add($"{prefix}[Max depth reached]");
                return;
            }

            var type = getType(node);
            var typeInfo = string.IsNullOrEmpty(type) || type == "Unknown"
                ? string.Empty
                : $" [{type}]";

            lines.Add($"{prefix}{getName(node)}{typeInfo}");

            var children = getChildren(node);
            if (children == null || children.Count == 0)
            {
                return;
            }

            var childPrefix = prefix + "  ";
            foreach (var child in children)
            {
                Format(child, lines, getName, getType, getChildren, childPrefix, depth + 1, maxDepth);
            }
        }
    }
}
