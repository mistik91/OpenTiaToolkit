# OpenTiaToolkit Codex Plugin

This plugin packages the OpenTiaToolkit MCP server configuration together with TIA Portal workflow skills. The MCP server also provides generic catalog, topology, PROFINET, and GSD/GSDML parameter operations for device-specific hardware workflows.

The plugin assumes the product installer has placed the MCP executable at:

```text
C:\Program Files\OpenTiaToolkit\OpenTiaToolkit.MCP.exe
```

For end users, Siemens Openness registration should be handled once by the installer or first-run setup. Developers who rebuild the MCP executable must refresh the Siemens Openness allow-list because the entry is tied to the executable hash and path.

## Included

- `.mcp.json`: Codex-compatible MCP server entry for `opentia`.
- `skills/opentia-toolkit-workflow`: top-level router and shared safety workflow.
- `skills/basic-tia-interaction`: TIA Portal instance/project discovery and selection guidance.
- `skills/hmi-unified-agent-editing`: HMI Unified JSON export, patch, import, probe, and audit workflow guidance.
- `skills/hardware-configuration-workflow`: generic hardware discovery, guarded patch, dry-run, and apply guidance.
- `skills/plc-block-and-tag-workflow`: PLC blocks, types, tags, CSV updates, rename, and compile guidance.
- `skills/hmi-trend-logging-workflow`: Unified HMI trend logging, logging tag, and data log lifecycle guidance.
- `skills/vci-sync-workflow`: TIA Version Control Interface workspace, mapping, status, and sync guidance.
- `skills/opentia-dev-workflow`: repo development, test, plugin, and skill packaging guidance.

## Expected First Use

1. Install OpenTiaToolkit into the stable product path.
2. Let the installer or setup helper register `OpenTiaToolkit.MCP.exe` with Siemens Openness.
3. Enable this plugin in Codex.
4. Restart Codex so the `opentia` MCP server and skills are loaded.
