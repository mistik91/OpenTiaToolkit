using OpenTiaToolkit.Core.Models;
using System;
using System.Collections.Generic;

namespace OpenTiaToolkit.Core.Services.V21
{
    internal static class V21CompilationDiagnosticProjector
    {
        public static IReadOnlyList<CompilationDiagnostic> Project<T>(
            IEnumerable<T>? roots,
            Func<T, string> state,
            Func<T, string?> path,
            Func<T, string?> description,
            Func<T, IEnumerable<T>?> children)
        {
            var result = new List<CompilationDiagnostic>();
            Add(roots, result, state, path, description, children);
            return result;
        }

        private static void Add<T>(IEnumerable<T>? nodes, List<CompilationDiagnostic> result, Func<T, string> state, Func<T, string?> path, Func<T, string?> description, Func<T, IEnumerable<T>?> children)
        {
            if (nodes == null) return;
            foreach (var node in nodes)
            {
                result.Add(new CompilationDiagnostic(state(node), path(node), null, description(node), null));
                Add(children(node), result, state, path, description, children);
            }
        }
    }
}
