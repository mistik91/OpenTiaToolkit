using System.Collections.Generic;

namespace OpenTiaToolkit.Core.Models
{
    /// <summary>
    /// Detailed information about a Unified HMI screen.
    /// </summary>
    public sealed class HmiScreenInfo
    {
        public string Name { get; set; } = string.Empty;

        public string Path { get; set; } = string.Empty;

        public string? GroupPath { get; set; }

        public int Width { get; set; }

        public int Height { get; set; }

        public bool Enabled { get; set; }

        public int ItemCount { get; set; }

        public IReadOnlyList<HmiScreenItemInfo> Items { get; set; } = new List<HmiScreenItemInfo>();
    }

    /// <summary>
    /// Summary information about a Unified HMI screen item.
    /// </summary>
    public sealed class HmiScreenItemInfo
    {
        public string ScreenPath { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Type { get; set; } = string.Empty;

        public int? Left { get; set; }

        public int? Top { get; set; }

        public int? Width { get; set; }

        public int? Height { get; set; }
    }

    /// <summary>
    /// Information about a supported HMI item type.
    /// </summary>
    public sealed class HmiItemTypeInfo
    {
        public string Type { get; set; } = string.Empty;

        public string Category { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public int DefaultWidth { get; set; }

        public int DefaultHeight { get; set; }

        public IReadOnlyList<string> Aliases { get; set; } = new List<string>();
    }

    /// <summary>
    /// Readable HMI item property or attribute value.
    /// </summary>
    public sealed class HmiItemPropertyInfo
    {
        public string Name { get; set; } = string.Empty;

        public string? Value { get; set; }

        public string ValueType { get; set; } = string.Empty;

        public bool IsWritable { get; set; }

        public string Source { get; set; } = string.Empty;
    }
}
