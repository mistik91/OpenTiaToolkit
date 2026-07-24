using OpenTiaToolkit.Core.Models;
using Siemens.Engineering.HW;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTiaToolkit.Core.Services.Hardware
{
    internal sealed class HardwareResolvedTarget
    {
        public HardwareResolvedTarget(HardwareObject obj, HardwareObjectRef reference)
        {
            Object = obj;
            Reference = reference;
        }

        public HardwareObject Object { get; }
        public HardwareObjectRef Reference { get; }
    }

    internal sealed class HardwareTargetResolver
    {
        private readonly HardwareEngineeringContext _context;

        public HardwareTargetResolver(HardwareEngineeringContext context) => _context = context;

        public HardwareResolvedTarget Resolve(HardwareObjectRef reference)
        {
            if (reference == null || string.IsNullOrWhiteSpace(reference.DeviceName))
                throw new ArgumentException("Hardware target deviceName is required.");
            var device = ProjectDeviceEnumerator.FindDevice(_context.RequireProject(), reference.DeviceName)
                ?? throw new InvalidOperationException($"Device not found: {reference.DeviceName}");
            var current = Traverse(device, reference.ItemPath, false, reference.DeviceName);
            ValidateType(current, reference.ExpectedTypeIdentifier);
            return new HardwareResolvedTarget(current, HardwareReferenceRules.Clone(reference));
        }

        public HardwareResolvedTarget ResolveOperationTarget(
            HardwarePatchOperation operation,
            IDictionary<string, HardwareResolvedTarget> references)
        {
            if (!string.IsNullOrWhiteSpace(operation.TargetRef))
            {
                if (!references.TryGetValue(operation.TargetRef!, out var existing))
                    throw new InvalidOperationException($"Unknown targetRef: {operation.TargetRef}");
                return operation.Target?.ItemPath?.Length > 0 ? ResolveRelative(existing, operation.Target) : existing;
            }
            if (operation.Target == null)
                throw new InvalidOperationException($"Operation '{operation.Op}' requires target or targetRef.");
            return Resolve(operation.Target);
        }

        public HardwareResolvedTarget ResolveRelative(HardwareResolvedTarget parent, HardwareObjectRef relative)
        {
            var current = parent.Object;
            var reference = HardwareReferenceRules.Clone(parent.Reference);
            foreach (var name in relative.ItemPath ?? Array.Empty<string>())
            {
                current = SelectChild(current, name, true, relative.ItemPath);
                reference = HardwareReferenceRules.Child(reference, current.Name, current.TypeIdentifier);
            }
            ValidateType(current, relative.ExpectedTypeIdentifier);
            return new HardwareResolvedTarget(current, reference);
        }

        public HardwareObjectRef? FindActualReference(HardwareObject target, string deviceName)
        {
            var device = ProjectDeviceEnumerator.FindDevice(_context.RequireProject(), deviceName)
                ?? throw new InvalidOperationException($"Device not found: {deviceName}");
            var root = new HardwareObjectRef { DeviceName = device.Name, ExpectedTypeIdentifier = device.TypeIdentifier };
            if (HardwareObjectsEqual(device, target)) return root;
            foreach (var item in device.DeviceItems)
            {
                var found = FindActualReference(item, target, HardwareReferenceRules.Child(root, item.Name, item.TypeIdentifier));
                if (found != null) return found;
            }
            return null;
        }

        private static HardwareObject Traverse(HardwareObject current, string[]? path, bool relative, string deviceName)
        {
            foreach (var name in path ?? Array.Empty<string>()) current = SelectChild(current, name, relative, path, deviceName);
            return current;
        }

        private static HardwareObject SelectChild(HardwareObject current, string name, bool relative, string[]? path, string? deviceName = null)
        {
            var matches = HardwareTargetResolutionRules.FindNameMatches(current.DeviceItems.Cast<HardwareObject>(), candidate => candidate.Name, name);
            if (matches.Count == 0)
            {
                var formatted = string.Join("/", path ?? Array.Empty<string>());
                throw new InvalidOperationException(relative
                    ? $"Relative hardware item path was not found: {formatted}"
                    : $"Hardware item path was not found: {deviceName}/{formatted}");
            }
            if (matches.Count > 1)
                throw new InvalidOperationException(relative
                    ? $"Relative hardware item path is ambiguous at: {name}"
                    : $"Hardware item path is ambiguous at: {name}");
            return matches[0];
        }

        private static void ValidateType(HardwareObject target, string? expectedTypeIdentifier)
        {
            if (!HardwareTargetResolutionRules.TypeMatches(target.TypeIdentifier, expectedTypeIdentifier))
                throw new InvalidOperationException($"Hardware target type does not match expected type identifier: {expectedTypeIdentifier}");
        }

        private static HardwareObjectRef? FindActualReference(DeviceItem current, HardwareObject target, HardwareObjectRef reference)
        {
            if (HardwareObjectsEqual(current, target)) return HardwareReferenceRules.Clone(reference);
            foreach (var child in current.DeviceItems)
            {
                var found = FindActualReference(child, target, HardwareReferenceRules.Child(reference, child.Name, child.TypeIdentifier));
                if (found != null) return found;
            }
            return null;
        }

        private static bool HardwareObjectsEqual(HardwareObject left, HardwareObject right) =>
            ReferenceEquals(left, right) || left.Equals(right);
    }

    internal static class HardwareTargetResolutionRules
    {
        public static List<T> FindNameMatches<T>(IEnumerable<T> candidates, Func<T, string?> getName, string name) =>
            candidates.Where(candidate => string.Equals(getName(candidate), name, StringComparison.OrdinalIgnoreCase)).ToList();

        public static bool TypeMatches(string? actual, string? expected) => string.IsNullOrWhiteSpace(expected) ||
            string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }
}
