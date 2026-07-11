using System;

namespace OpenTiaToolkit.MCP.Server
{
    public sealed class McpRuntimeOptions
    {
        public TimeSpan? ToolTimeout { get; set; }

        public TimeSpan SessionResetGracePeriod { get; set; } = TimeSpan.FromSeconds(5);
    }
}
