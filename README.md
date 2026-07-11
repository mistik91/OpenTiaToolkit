<p align="center">
  <img src="docs/assets/opentia-toolkit-logo.png" alt="OpenTiaToolkit logo" width="720">
</p>

# OpenTiaToolkit

OpenTiaToolkit is a Windows automation toolkit for Siemens TIA Portal Openness. It provides one shared command surface through a human-friendly CLI and an MCP server for agent/harness workflows.

The project targets **TIA Portal V21 and forward**. Current development and validation are performed against V21.

## What It Does

- Connects to running TIA Portal instances or opens a new one.
- Opens, inspects, saves, and closes TIA Portal projects.
- Lists project languages and exports/imports Siemens project text workbooks for translation workflows.
- Lists devices, PLCs, HMI devices, project trees, and PLC software trees.
- Runs PLC workflows such as compile, online compare, guarded PLCSIM Advanced download, online status checks, block browse, import/export, VCI workspace synchronization, search, delete, tag creation, CSV tag updates, and rename operations.
- Works with project and global libraries: open/list/close global libraries, list master copies and library types, copy approved master copies into PLC block groups, and report library update status before mutation.
- Supports generic hardware catalog discovery, module/submodule composition, PROFINET IO identity/wiring, process-image address assignment, Startdrive telegram configuration, and transactional GSD/GSDML parameter changes.
- Supports Unified HMI browse, screen/item mutation, tag/trend setup, JSON export/diff/import, focused screen patches, faceplate interface probes, and safety preflights.
- Exposes the same capabilities through both CLI and MCP, so scripts and agents use the same implementation.
- Ships agent-ready workflow guidance for common TIA/HMI interaction patterns.

For the detailed command surface, see [CLI usage](docs/CLI_USAGE.md).

## Integrations

OpenTiaToolkit includes ready-to-use plugin or bundle packages for:

| Harness | Package |
|---------|---------|
| Codex | [plugins/opentia-toolkit](plugins/opentia-toolkit) |
| Claude Code | [plugins/opentia-toolkit-claude-code](plugins/opentia-toolkit-claude-code) |
| Claude Desktop | [plugins/opentia-toolkit-claude-desktop](plugins/opentia-toolkit-claude-desktop) |
| OpenCode | [plugins/opentia-toolkit-opencode](plugins/opentia-toolkit-opencode) |

It also ships MCP config snippets for users who do not want a plugin package or are using a plain MCP client:

| MCP Client | Configs |
|------------|---------|
| Codex | `codex.config.toml`, `codex.installed.config.toml` |
| Claude Desktop | `claude-desktop.installed.json` |
| Claude Code | `claude-code.mcp.json`, `claude-code.installed.mcp.json`, `claude-code-add.ps1` |
| OpenCode | `opencode.jsonc`, `opencode.installed.jsonc` |
| Google Antigravity | `antigravity.mcp_config.json`, `antigravity.installed.mcp_config.json` |
| VS Code / GitHub Copilot | `vscode.mcp.json`, `vscode.installed.mcp.json`, user settings snippets |

See [MCP client configs](docs/mcp-client-configs/README.md) for the full matrix and connection sequence.

## Quick Start

### Prerequisites

- Windows
- TIA Portal V21 with Openness enabled
- .NET SDK
- Git

The default Siemens Openness reference path is:

```text
C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48
```

Most TIA Portal V21 installations use this path. Non-standard installs can override it with `TiaPortalPublicApiPath`; see [setup](docs/SETUP.md).

### Install

For a normal installed-product setup, download the latest Windows installer from the [GitHub releases page](https://github.com/mistik91/OpenTiaToolkit/releases):

```text
OpenTiaToolkit-0.1.0-Setup.exe
```

For local development or release packaging, build the Inno Setup installer:

```powershell
.\scripts\Build-Installer.ps1
```

The installer places the product under:

```text
C:\Program Files\OpenTiaToolkit
```

It also sets `OPEN_TIA_MCP_EXE` and can register the installed MCP executable with Siemens Openness. After installation, ask your agent to install or configure the local plugin from `C:\Program Files\OpenTiaToolkit\plugins`; see [agent plugin setup](docs/INSTALL_AGENT_PLUGINS.md).

### Build

```powershell
git clone https://github.com/mistik91/OpenTiaToolkit.git
cd OpenTiaToolkit
dotnet build OpenTiaToolkit.sln
```

### Register Openness Allow-List Entries

Siemens Openness allow-list entries are tied to executable path and hash. After building, register the exact CLI or MCP executable you will run:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Register-OpennessAllowList.ps1 -ExecutablePath .\src\OpenTiaToolkit.CLI\bin\Debug\net48\OpenTiaToolkit.CLI.exe
powershell -ExecutionPolicy Bypass -File .\scripts\Register-OpennessAllowList.ps1 -ExecutablePath .\src\OpenTiaToolkit.MCP\bin\Debug\net48\OpenTiaToolkit.MCP.exe
```

Repeat this after rebuilding those executables.

### Run The CLI

Interactive session:

```powershell
.\src\OpenTiaToolkit.CLI\bin\Debug\net48\OpenTiaToolkit.CLI.exe
```

Single command:

```powershell
.\src\OpenTiaToolkit.CLI\bin\Debug\net48\OpenTiaToolkit.CLI.exe help
```

Script file:

```powershell
.\src\OpenTiaToolkit.CLI\bin\Debug\net48\OpenTiaToolkit.CLI.exe --script .\inspect-project.commands.txt
```

Example session:

```text
connect-portal
open-project --project-path="C:\Projects\MyProject.ap21"
list-devices
portal-status
```

## MCP Server

The MCP server is built at:

```text
src\OpenTiaToolkit.MCP\bin\Debug\net48\OpenTiaToolkit.MCP.exe
```

A typical installed-product config uses:

```json
{
  "mcpServers": {
    "opentia": {
      "command": "C:\\Program Files\\OpenTiaToolkit\\mcp\\OpenTiaToolkit.MCP.exe",
      "args": [],
      "env": {
        "OPEN_TIA_MCP_LOG_LEVEL": "Error",
        "OPEN_TIA_MCP_TOOL_TIMEOUT_SECONDS": "600"
      }
    }
  }
}
```

The MCP server also exposes infrastructure tools for long-running TIA operations: `operation-status`, `cancel-operation`, and `reset-session`. Use `operation-status` after a timeout or busy response, and `reset-session` only when the current TIA attachment needs to be discarded. Reset requests cancellation and waits for quiescence before disposing Siemens objects; if the configured grace period expires, reset fails closed and the MCP process must be restarted. PLC batch tag commands report per-operation or per-item progress and honor cooperative cancellation between items and before TIA write calls.

For development checkouts and client-specific snippets, use [docs/mcp-client-configs](docs/mcp-client-configs/README.md).

## Documentation

| Topic | Link |
|-------|------|
| Setup and Openness registration | [docs/SETUP.md](docs/SETUP.md) |
| Installed agent plugin setup | [docs/INSTALL_AGENT_PLUGINS.md](docs/INSTALL_AGENT_PLUGINS.md) |
| Full CLI command reference | [docs/CLI_USAGE.md](docs/CLI_USAGE.md) |
| CLI automation harness | [docs/CLI_TESTING.md](docs/CLI_TESTING.md) |
| MCP client/plugin setup | [docs/mcp-client-configs/README.md](docs/mcp-client-configs/README.md) |
| TIA Portal version support | [docs/TIA_PORTAL_VERSION_SUPPORT.md](docs/TIA_PORTAL_VERSION_SUPPORT.md) |
| Development notes | [docs/DEV_GUIDE.md](docs/DEV_GUIDE.md) |
| Generic hardware automation | [docs/HARDWARE_AUTOMATION.md](docs/HARDWARE_AUTOMATION.md) |
| Examples | [examples/README.md](examples/README.md) |
| Third-party notices | [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) |

## Examples

The [examples](examples/README.md) folder contains:

- read-only CLI project inspection
- PLC export through the CLI
- MCP project/device discovery
- VCI workspace creation and bidirectional S7DCL synchronization through MCP
- safe Unified HMI screen patch flow

Use disposable project copies for mutation workflows until you are comfortable with the behavior.

## Validation

Fast tests:

```powershell
dotnet test .\tests\OpenTiaToolkit.Tests\OpenTiaToolkit.Tests.csproj --filter "TestCategory!=Integration"
```

Live TIA integration tests:

```powershell
dotnet test .\tests\OpenTiaToolkit.Tests\OpenTiaToolkit.Tests.csproj --filter "TestCategory=Integration"
```

Integration tests require TIA Portal V21, allow-list registration, and local test project paths. See [setup](docs/SETUP.md#run-tests).

## Status

OpenTiaToolkit is being prepared for open beta and public release. The CLI, MCP server, plugin packages, and core TIA/HMI workflows are actively validated against real TIA Portal V21 projects. Treat mutation workflows as engineering tools: start on disposable project copies, review dry-run output, and keep project backups.

## License

OpenTiaToolkit is licensed under the [MIT License](LICENSE).
