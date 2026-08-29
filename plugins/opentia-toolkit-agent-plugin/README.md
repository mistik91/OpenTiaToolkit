# OpenTiaToolkit Agent Plugin

This portable Agent Plugins 1.0 package combines 18 OpenTiaToolkit workflow skills with the `opentia` stdio MCP server configuration for compatible agent clients.

The package is an integration layer, not the Windows product payload. Install OpenTiaToolkit and register its isolated V21 worker with Siemens Openness before using MCP tools. The MCP launcher uses an explicit `OPEN_TIA_MCP_EXE`, discovers an adjacent or default installed-product payload, or falls back to Debug/Release output in a source checkout.

## Included

- `plugin.json`: Agent Plugins 1.0 portable manifest.
- `mcp.json`: portable stdio MCP server configuration.
- `scripts/run-opentia-mcp.ps1`: installed-product and development launcher.
- `skills/`: the shared OpenTiaToolkit router and specialized workflow skills.

The legacy Codex, Claude Code, Claude Desktop, and OpenCode packages remain as separate sibling directories.
