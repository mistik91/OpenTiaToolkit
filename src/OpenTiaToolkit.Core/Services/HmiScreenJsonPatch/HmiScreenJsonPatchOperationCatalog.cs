using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTiaToolkit.Core.Services
{
    internal static class HmiScreenJsonPatchOperationCatalog
    {
        private static readonly string[] Operations =
        {
            "set-property", "set-properties", "move-item", "resize-item", "set-layout",
            "rename-item", "create-item", "delete-item", "duplicate-item",
            "set-event-handler", "remove-event-handler", "set-property-event-handler",
            "remove-property-event-handler", "set-dynamization", "remove-dynamization", "set-interface"
        };

        public static IReadOnlyList<string> SupportedOperations => Operations;

        public static string Normalize(string? operation)
        {
            return string.IsNullOrWhiteSpace(operation)
                ? string.Empty
                : new string(operation!.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        }
    }
}
