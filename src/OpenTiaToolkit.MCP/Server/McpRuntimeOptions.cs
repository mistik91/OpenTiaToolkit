using System;
using System.Collections.Generic;

namespace OpenTiaToolkit.MCP.Server
{
    public sealed class McpRuntimeOptions
    {
        public TimeSpan? ToolTimeout { get; set; }

        public TimeSpan SessionResetGracePeriod { get; set; } = TimeSpan.FromSeconds(5);

        public IReadOnlyList<string> AllowedFileRoots { get; set; } = Array.Empty<string>();

        public long MaxInputFileBytes { get; set; } = 16L * 1024L * 1024L;

        public bool AllowFileOverwrite { get; set; }
    }
}
