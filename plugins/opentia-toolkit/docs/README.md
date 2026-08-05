# OpenTiaToolkit Codex Plugin

This plugin packages the OpenTiaToolkit MCP server configuration together with TIA Portal workflow skills. The MCP server also provides generic catalog, topology, PROFINET, and GSD/GSDML parameter operations for device-specific hardware workflows.

The MCP entry runs `scripts/run-opentia-mcp.ps1`. The launcher uses `OPEN_TIA_MCP_EXE` when set; otherwise it looks for the repo build output at:

```text
src\OpenTiaToolkit.MCP\bin\Debug\net48\OpenTiaToolkit.MCP.exe
src\OpenTiaToolkit.MCP\bin\Release\net48\OpenTiaToolkit.MCP.exe
```

Register the exact MCP executable you launch with Siemens Openness. Developers who rebuild the MCP executable must refresh the Siemens Openness allow-list because the entry is tied to the executable hash and path.

## Included

- `.mcp.json`: Codex-compatible MCP server entry for `opentia`.
- `scripts/run-opentia-mcp.ps1`: MCP launcher for repo builds or `OPEN_TIA_MCP_EXE`.
- `skills/opentia-toolkit-workflow`: top-level router and shared safety workflow.
- `skills/engineering-blueprint-workflow`: state-bound PLC/HMI/hardware blueprint validation, plan, apply, and verification guidance.
- `skills/basic-tia-interaction`: TIA Portal instance/project discovery and selection guidance.
- `skills/multiuser-collaboration-workflow`: Project Server identity, local session, conflict, save, and check-in guidance.
- `skills/hmi-unified-agent-editing`: HMI Unified JSON export, patch, import, probe, and audit workflow guidance.
- `skills/hmi-discrete-alarm-workflow`: canonical Unified discrete-alarm reads, transactional create/update dry-runs and applies, and guarded deletion guidance.
- `skills/hardware-configuration-workflow`: generic hardware discovery, guarded patch, dry-run, and apply guidance.
- `skills/plc-block-and-tag-workflow`: PLC blocks, types, tags, CSV updates, rename, and compile guidance.
- `skills/library-workflow`: project/global library, master copy, library type, UDT version, and update-check guidance.
- `skills/hmi-trend-logging-workflow`: Unified HMI trend logging, logging tag, and data log lifecycle guidance.
- `skills/vci-sync-workflow`: TIA Version Control Interface workspace, mapping, status, and sync guidance.
- `skills/opentia-dev-workflow`: repo development, test, plugin, and skill packaging guidance.

## Expected First Use

1. Build `OpenTiaToolkit.MCP` or set `OPEN_TIA_MCP_EXE` to a built MCP executable.
2. Build and register the exact `OpenTiaToolkit.Worker.V21.exe`; the MCP host itself is vendor-free.
3. Enable this plugin in Codex.
4. Restart Codex so the `opentia` MCP server and skills are loaded.
