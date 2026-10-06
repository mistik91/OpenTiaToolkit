using OpenTiaToolkit.Core.Models;
using System;
using System.Linq;

namespace OpenTiaToolkit.Core.Services.Hardware
{
    internal static class HardwareReferenceRules
    {
        public static HardwareObjectRef Clone(HardwareObjectRef source) => new HardwareObjectRef
        {
            DeviceName = source.DeviceName,
            ItemPath = source.ItemPath?.ToArray() ?? Array.Empty<string>(),
            ExpectedTypeIdentifier = source.ExpectedTypeIdentifier
        };

        public static HardwareObjectRef Child(HardwareObjectRef parent, string name, string? typeIdentifier) => new HardwareObjectRef
        {
            DeviceName = parent.DeviceName,
            ItemPath = (parent.ItemPath ?? Array.Empty<string>()).Concat(new[] { name }).ToArray(),
            ExpectedTypeIdentifier = typeIdentifier
        };

        public static bool PathsEqual(HardwareObjectRef? left, HardwareObjectRef? right)
        {
            if (left == null || right == null) return left == right;
            return string.Equals(left.DeviceName, right.DeviceName, StringComparison.OrdinalIgnoreCase)
                && (left.ItemPath ?? Array.Empty<string>()).SequenceEqual(
                    right.ItemPath ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        }

        public static string FormatPath(HardwareObjectRef reference) => string.Join("/",
            new[] { reference.DeviceName }.Concat(reference.ItemPath ?? Array.Empty<string>())
                .Where(part => !string.IsNullOrWhiteSpace(part)));

        public static string? ValidatePlugPlacement(
            string itemName,
            HardwareObjectRef requestedReference,
            HardwareObjectRef actualReference,
            bool allowDisplacedPlacement)
        {
            if (PathsEqual(requestedReference, actualReference)) return null;
            if (!allowDisplacedPlacement)
            {
                throw new InvalidOperationException(
                    $"Plugged hardware item '{itemName}' was placed at '{FormatPath(actualReference)}' instead of requested '{FormatPath(requestedReference)}'. " +
                    "Set allowDisplacedPlacement=true to accept the actual placement.");
            }

            return $"TIA placed the plugged item at '{FormatPath(actualReference)}' instead of requested '{FormatPath(requestedReference)}'.";
        }
    }
}
