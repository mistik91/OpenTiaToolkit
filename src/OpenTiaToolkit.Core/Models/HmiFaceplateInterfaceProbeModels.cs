using System.Collections.Generic;

namespace OpenTiaToolkit.Core.Models
{
    public sealed class HmiFaceplateInterfaceProbeResult
    {
        public string ScreenPath { get; set; } = string.Empty;

        public string ItemName { get; set; } = string.Empty;

        public string ItemType { get; set; } = string.Empty;

        public string? ContainedType { get; set; }

        public string? TargetProperty { get; set; }

        public bool WriteRequested { get; set; }

        public string? WriteValue { get; set; }

        public bool RestoreRequested { get; set; }

        public bool WriteSucceeded { get; set; }

        public bool RestoreSucceeded { get; set; }

        public string? WriteError { get; set; }

        public string? RestoreError { get; set; }

        public List<HmiFaceplateInterfaceProbeEntry> Entries { get; set; } = new List<HmiFaceplateInterfaceProbeEntry>();
    }

    public sealed class HmiFaceplateInterfaceProbeEntry
    {
        public string PropertyName { get; set; } = string.Empty;

        public string? Value { get; set; }

        public string EntryType { get; set; } = string.Empty;

        public string? ValuePropertyType { get; set; }

        public string? ValueDeclaringType { get; set; }

        public bool ValueCanRead { get; set; }

        public bool ValueCanWrite { get; set; }

        public bool IsTarget { get; set; }

        public string? ValueAfterWrite { get; set; }

        public string? ValueAfterRestore { get; set; }
    }
}
