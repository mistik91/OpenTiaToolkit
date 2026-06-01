# OpenTiaToolkit OpenCode Plugin Bundle

This bundle packages OpenTiaToolkit guidance for OpenCode:

- `opencode.json`: installed-product MCP configuration for the `opentia` server
- `AGENTS.md`: project instructions for safe OpenTiaToolkit/TIA Portal sessions
- `.opencode/plugins/opentia-toolkit.js`: a lightweight OpenCode plugin that sets the default OpenTiaToolkit shell log level
- `.opencode/skills/opentia-toolkit-workflow`: top-level router and shared safety workflow
- `.opencode/skills/basic-tia-interaction`: TIA Portal instance/project discovery and selection guidance
- `.opencode/skills/hmi-unified-agent-editing`: HMI Unified JSON export, patch, import, probe, and audit workflow guidance
- `.opencode/skills/hardware-configuration-workflow`: generic hardware discovery, guarded patch, dry-run, and apply guidance
- `.opencode/skills/plc-block-and-tag-workflow`: PLC blocks, types, tags, CSV updates, rename, and compile guidance
- `.opencode/skills/hmi-trend-logging-workflow`: Unified HMI trend logging, logging tag, and data log lifecycle guidance
- `.opencode/skills/vci-sync-workflow`: TIA Version Control Interface workspace, mapping, status, and sync guidance
- `.opencode/skills/opentia-dev-workflow`: repo development, test, plugin, and skill packaging guidance

The configured MCP server also exposes generic hardware operations for catalog discovery, module/submodule composition, PROFINET wiring, and guarded GSD/GSDML parameter changes.

The bundle assumes the product installer has placed the MCP executable at:

```text
C:\Program Files\OpenTiaToolkit\OpenTiaToolkit.MCP.exe
```

## Install

Copy the contents of this directory into an OpenCode project root, then restart OpenCode from that project.

For a global install, merge `opencode.json` into `~/.config/opencode/opencode.json`, copy `AGENTS.md` to `~/.config/opencode/AGENTS.md` if you want the rules globally, and copy `.opencode/plugins` plus `.opencode/skills` into `~/.config/opencode/`.

For development checkouts, use `docs/mcp-client-configs/opencode.jsonc` instead of this installed-product `opencode.json`.

The supplied `opencode.json` sets a 600-second MCP timeout because broad TIA Portal Openness reads, such as Unified HMI tag enumeration, can exceed one minute on real projects.

## Expected First Use

1. Install OpenTiaToolkit into the stable product path.
2. Let the installer or setup helper register `OpenTiaToolkit.MCP.exe` with Siemens Openness.
3. Copy this bundle into the OpenCode project or global config directory.
4. Restart OpenCode so the MCP server, plugin, rules, and skills are discovered.
