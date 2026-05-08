# OpenTiaToolkit MCP Client Configs

This folder contains ready-to-adapt MCP client configuration snippets for the OpenTiaToolkit MCP server.

There are two config families:

- Development checkout snippets point at a built executable inside this repository.
- Product-install snippets point at the stable installed executable:

```text
C:\Program Files\OpenTiaToolkit\OpenTiaToolkit.MCP.exe
```

For end users, an installer or first-run setup helper should register that stable executable with Siemens Openness once. Developers who rebuild the MCP executable must refresh the Siemens Openness allow-list because the entry is tied to the executable hash and path.

The MCP executable is produced by the `OpenTiaToolkit.MCP` project:

```text
<OpenTiaToolkitRoot>\src\OpenTiaToolkit.MCP\bin\Debug\net48\OpenTiaToolkit.MCP.exe
```

If you switch to a Release build, replace `Debug` with `Release` in the snippets.

## Files

### Development Checkout

- `codex.config.toml`: OpenAI Codex `config.toml` server block.
- `opencode.jsonc`: OpenCode project or user `opencode.json` MCP server block.
- `claude-code-add.ps1`: Claude Code CLI registration command for PowerShell.
- `claude-code.mcp.json`: Claude Code project `.mcp.json` snippet.
- `antigravity.mcp_config.json`: Google Antigravity raw MCP config snippet.
- `vscode.mcp.json`: VS Code / GitHub Copilot workspace `.vscode/mcp.json` snippet.
- `vscode-user-settings.json`: VS Code user settings snippet.

### Product Install

- `codex.installed.config.toml`: OpenAI Codex `config.toml` server block for the installed product.
- `opencode.installed.jsonc`: OpenCode project or user `opencode.json` MCP server block for the installed product.
- `claude-desktop.installed.json`: Claude Desktop config snippet.
- `claude-code-installed-add.ps1`: Claude Code CLI registration command for PowerShell.
- `claude-code.installed.mcp.json`: Claude Code project `.mcp.json` snippet.
- `antigravity.installed.mcp_config.json`: Google Antigravity raw MCP config snippet.
- `vscode.installed.mcp.json`: VS Code / GitHub Copilot workspace `.vscode/mcp.json` snippet.
- `vscode-user-settings.installed.json`: VS Code user settings snippet.

## Codex Plugin

The repo also includes a local Codex plugin package at [`plugins/opentia-toolkit`](../../plugins/opentia-toolkit). It bundles:

- the `opentia` MCP server configuration for the installed product path
- the `hmi-unified-agent-editing` skill and references
- a helper script for manually registering the installed MCP executable during development or manual installs

The marketplace entry lives at [`../../.agents/plugins/marketplace.json`](../../.agents/plugins/marketplace.json).

## Claude Code Plugin

The repo includes a Claude Code plugin package at [`plugins/opentia-toolkit-claude-code`](../../plugins/opentia-toolkit-claude-code). It bundles:

- the installed-product `opentia` MCP server configuration in `.mcp.json`
- the `hmi-unified-agent-editing` skill and references under `skills/`
- plugin metadata in `.claude-plugin/plugin.json`

The local Claude Code marketplace entry lives at [`../../.claude-plugin/marketplace.json`](../../.claude-plugin/marketplace.json). From the repo root, add and install it in Claude Code with:

```text
/plugin marketplace add .
/plugin install opentia-toolkit@opentia-toolkit-local
```

For development, you can also launch Claude Code with:

```powershell
claude --plugin-dir .\plugins\opentia-toolkit-claude-code
```

Claude Code does not load a plugin-root `CLAUDE.md` as project context. Plugin-specific operational guidance should live in skills, agents, hooks, or MCP configuration.

## Claude Desktop MCPB Package

The repo includes Claude Desktop MCPB source at [`plugins/opentia-toolkit-claude-desktop`](../../plugins/opentia-toolkit-claude-desktop). MCPB packages are installable Claude Desktop extensions that bundle a local MCP server and `manifest.json`.

Because build outputs are not committed, generate the `.mcpb` package from a local build:

```powershell
.\scripts\Pack-ClaudeDesktopMcpb.ps1
```

Install the MCPB CLI first if needed:

```powershell
npm install -g @anthropic-ai/mcpb
```

The pack script stages the MCP executable under `artifacts\claude-desktop-mcpb\opentia-toolkit`, validates the manifest with `mcpb` or `npx -y @anthropic-ai/mcpb`, and writes `artifacts\claude-desktop-mcpb\opentia-toolkit.mcpb`.

## OpenCode Plugin Bundle

The repo also includes a copyable OpenCode bundle at [`plugins/opentia-toolkit-opencode`](../../plugins/opentia-toolkit-opencode). It bundles:

- the installed-product `opentia` MCP server configuration in `opencode.json`
- an `AGENTS.md` rule file with the required TIA Portal connection sequence
- a local `.opencode/plugins/opentia-toolkit.js` plugin that sets the default OpenTiaToolkit shell log level
- the `hmi-unified-agent-editing` skill and references under `.opencode/skills`

OpenCode uses `opencode.json` for project or user config, `.opencode/plugins/` for local plugins, and `.opencode/skills/<name>/SKILL.md` for reusable skills. Use `opencode.jsonc` from this folder for development checkouts, or `opencode.installed.jsonc` / the bundle for installed-product paths.

## Required TIA Portal Sequence

Agents should call the tools in this order for a fresh MCP session:

```text
connect-portal
open-project
open-unified-hmi
```

Calling `open-project` before `connect-portal` can fail because the MCP server has not attached to TIA Portal yet.

## Siemens Openness Allow-List

Siemens Openness allow-list entries are hash-based and path-based. For shipped product builds, registration should happen once for the installed executable path. After building a new CLI or MCP executable during development, refresh the registry entry for the exact `.exe` you will launch:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Register-OpennessAllowList.ps1 -ExecutablePath .\src\OpenTiaToolkit.MCP\bin\Debug\net48\OpenTiaToolkit.MCP.exe
```

Run the same script for the CLI executable if your workflow also uses the CLI:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Register-OpennessAllowList.ps1 -ExecutablePath .\src\OpenTiaToolkit.CLI\bin\Debug\net48\OpenTiaToolkit.CLI.exe
```

## Notes For Windows Paths

- JSON configs need escaped backslashes, for example `E:\\Path\\To\\OpenTiaToolkit\\...`.
- TOML configs can use single-quoted Windows paths, for example `'E:\Path\To\OpenTiaToolkit\...'`.
- OpenCode local MCP configs use a `command` array and `environment` object inside an `mcp.<server-name>` entry.
- Keep `OPEN_TIA_MCP_LOG_LEVEL` at `Error` unless you are debugging startup or tool execution.
- Do not enable parallel MCP tool calls for this server. TIA Portal sessions are stateful and should be driven serially.
