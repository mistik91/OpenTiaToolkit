# OpenTiaToolkit MCP Client Configs

This folder contains ready-to-adapt MCP client configuration snippets for the OpenTiaToolkit MCP server.

The MCP executable is produced by the `OpenTiaToolkit.MCP` project:

```text
<OpenTiaToolkitRoot>\src\OpenTiaToolkit.MCP\bin\Debug\net48\OpenTiaToolkit.MCP.exe
```

For the development checkout used during validation, that path was:

```text
E:\Documents\Codex\OpenTiaToolkit\src\OpenTiaToolkit.MCP\bin\Debug\net48\OpenTiaToolkit.MCP.exe
```

If you switch to a Release build, replace `Debug` with `Release` in the snippets.

## Files

- `codex.config.toml`: OpenAI Codex `config.toml` server block.
- `claude-code-add.ps1`: Claude Code CLI registration command for PowerShell.
- `claude-code.mcp.json`: Claude Code project `.mcp.json` snippet.
- `antigravity.mcp_config.json`: Google Antigravity raw MCP config snippet.
- `vscode.mcp.json`: VS Code / GitHub Copilot workspace `.vscode/mcp.json` snippet.
- `vscode-user-settings.json`: VS Code user settings snippet.

## Required TIA Portal Sequence

Agents should call the tools in this order for a fresh MCP session:

```text
connect-portal
open-project
open-unified-hmi
```

Calling `open-project` before `connect-portal` can fail because the MCP server has not attached to TIA Portal yet.

## Siemens Openness Allow-List

Siemens Openness allow-list entries are hash-based. After building a new CLI or MCP executable, refresh the registry entry for the exact `.exe` you will launch:

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
- Keep `OPEN_TIA_MCP_LOG_LEVEL` at `Error` unless you are debugging startup or tool execution.
- Do not enable parallel MCP tool calls for this server. TIA Portal sessions are stateful and should be driven serially.
