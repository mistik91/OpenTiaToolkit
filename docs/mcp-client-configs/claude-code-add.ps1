# Registers OpenTiaToolkit as a user-scoped Claude Code MCP server.
# Replace E:\Path\To\OpenTiaToolkit with your local checkout path.

claude mcp add `
    --transport stdio `
    --scope user `
    --env OPEN_TIA_MCP_LOG_LEVEL=Error `
    opentia `
    -- "E:\Path\To\OpenTiaToolkit\src\OpenTiaToolkit.MCP\bin\Debug\net48\OpenTiaToolkit.MCP.exe"
