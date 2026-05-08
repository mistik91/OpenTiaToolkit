# Registers an installed OpenTiaToolkit MCP executable as a user-scoped Claude Code MCP server.

claude mcp add `
    --transport stdio `
    --scope user `
    --env OPEN_TIA_MCP_LOG_LEVEL=Error `
    opentia `
    -- "C:\Program Files\OpenTiaToolkit\OpenTiaToolkit.MCP.exe"
