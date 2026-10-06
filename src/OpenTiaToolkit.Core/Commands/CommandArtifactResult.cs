using System.Collections.Generic;

namespace OpenTiaToolkit.Core.Commands
{
    /// <summary>
    /// Compact manifest returned when a command writes a large payload to disk.
    /// </summary>
    public sealed class CommandArtifactResult
    {
        public string OutputPath { get; set; } = string.Empty;

        public string Format { get; set; } = string.Empty;

        public string DataKind { get; set; } = string.Empty;

        public long BytesWritten { get; set; }

        public int? ItemCount { get; set; }

        public bool InlineOutputSuppressed { get; set; }

        public string Reason { get; set; } = string.Empty;

        public IReadOnlyList<string> Columns { get; set; } = new List<string>();

        public object? Metadata { get; set; }
    }
}
