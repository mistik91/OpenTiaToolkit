# OpenTiaToolkit Claude Desktop MCPB Source

This folder is the source for a Claude Desktop MCPB extension package.

MCPB packages are zip archives containing a local MCP server and a `manifest.json`. Because OpenTiaToolkit build outputs are not committed to this repository, use the repository pack script to stage the built MCP executable and its dependencies before creating the `.mcpb` file.

Claude Desktop MCPB packages expose the `opentia` MCP server, but they do not currently bundle Codex/Claude Code/OpenCode-style skill folders. For agent guidance equivalent to the packaged skills, keep the relevant prompt or project instructions available to Claude Desktop, especially the `basic-tia-interaction` rule: discover running TIA instances, verify the open project after connecting, and select the matching instance automatically when the project was specified.

## Build

From the repository root:

```powershell
.\scripts\Pack-ClaudeDesktopMcpb.ps1
```

The script:

1. Builds `OpenTiaToolkit.MCP`.
2. Copies `plugins\opentia-toolkit-claude-desktop\manifest.json` into a staging folder.
3. Copies the MCP build output into `server\`.
4. Runs `mcpb validate` and `mcpb pack` when the MCPB CLI is available, or falls back to `npx -y @anthropic-ai/mcpb` when `npx` is available.

Install the MCPB CLI first if you want offline/repeatable packing without `npx`:

```powershell
npm install -g @anthropic-ai/mcpb
```

The generated package is written under:

```text
artifacts\claude-desktop-mcpb
```

## Install

In Claude Desktop, install the generated `.mcpb` file through Settings > Extensions > Advanced settings > Install Extension, or open the `.mcpb` file directly if your OS has associated it with Claude Desktop.

The bundled executable still must be registered with Siemens Openness for the exact staged path and hash. The pack script can do this with `-RegisterOpenness` when run from an elevated PowerShell session.
