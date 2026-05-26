# OpenTiaToolkit Claude Code Plugin

This plugin packages the OpenTiaToolkit MCP server configuration together with TIA Portal and HMI Unified agent-editing skills for Claude Code. The MCP server also provides generic catalog, topology, PROFINET, and GSD/GSDML parameter operations for device-specific hardware workflows.

The plugin assumes the product installer has placed the MCP executable at:

```text
C:\Program Files\OpenTiaToolkit\OpenTiaToolkit.MCP.exe
```

For end users, Siemens Openness registration should be handled once by the installer or first-run setup. Developers who rebuild the MCP executable must refresh the Siemens Openness allow-list because the entry is tied to the executable hash and path.

## Included

- `.mcp.json`: Claude Code plugin MCP server entry for `opentia`.
- `skills/basic-tia-interaction`: TIA Portal instance/project discovery and selection guidance.
- `skills/hmi-unified-agent-editing`: HMI Unified JSON export, patch, import, probe, and audit workflow guidance.

Claude Code does not load a plugin-root `CLAUDE.md` as project context. Operational guidance that should travel with the package belongs in skills, agents, hooks, or MCP configuration.

## Expected First Use

1. Install OpenTiaToolkit into the stable product path.
2. Let the installer or setup helper register `OpenTiaToolkit.MCP.exe` with Siemens Openness.
3. Test locally with:

```powershell
claude --plugin-dir .\plugins\opentia-toolkit-claude-code
```

4. For marketplace-style installation, add the repo root as a local marketplace and install the plugin:

```text
/plugin marketplace add .
/plugin install opentia-toolkit@opentia-toolkit-local
```
