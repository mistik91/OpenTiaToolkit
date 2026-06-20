# Setup

This guide covers a normal beta setup for OpenTiaToolkit on Windows with TIA Portal V21.

## Prerequisites

- Windows
- TIA Portal V21 with Openness enabled
- .NET SDK
- Git

The default Siemens Openness reference path is:

```text
C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48
```

Most users who installed TIA Portal in the default location do not need to change anything.

## Clone And Build

```powershell
git clone https://github.com/mistik91/OpenTiaToolkit.git
cd OpenTiaToolkit
dotnet build OpenTiaToolkit.sln
```

For a non-standard TIA Portal installation, pass `TiaPortalPublicApiPath`:

```powershell
dotnet build OpenTiaToolkit.sln /p:TiaPortalPublicApiPath="D:\Siemens\Portal V21\PublicAPI\V21\net48"
```

## Register Siemens Openness Allow-List Entries

Siemens Openness allow-list entries are tied to executable path and hash. Register each rebuilt executable you plan to launch.

Run PowerShell as Administrator:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Register-OpennessAllowList.ps1 -ExecutablePath .\src\OpenTiaToolkit.CLI\bin\Debug\net48\OpenTiaToolkit.CLI.exe
powershell -ExecutionPolicy Bypass -File .\scripts\Register-OpennessAllowList.ps1 -ExecutablePath .\src\OpenTiaToolkit.MCP\bin\Debug\net48\OpenTiaToolkit.MCP.exe
```

Repeat this after rebuilding those executables.

## Run The CLI

Interactive mode:

```powershell
.\src\OpenTiaToolkit.CLI\bin\Debug\net48\OpenTiaToolkit.CLI.exe
```

Single command mode:

```powershell
.\src\OpenTiaToolkit.CLI\bin\Debug\net48\OpenTiaToolkit.CLI.exe help
```

Script mode:

```powershell
.\src\OpenTiaToolkit.CLI\bin\Debug\net48\OpenTiaToolkit.CLI.exe --script .\inspect-project.commands.txt
```

## Configure MCP Clients

Ready-to-adapt snippets live in `docs/mcp-client-configs`.

Use the development checkout snippets when running from this repository. Use the installed-product snippets only when an installer or your own deployment has placed the MCP executable at:

```text
C:\Program Files\OpenTiaToolkit\OpenTiaToolkit.MCP.exe
```

The Codex plugin package lives in `plugins/opentia-toolkit`. It runs `scripts/run-opentia-mcp.ps1`, which uses `OPEN_TIA_MCP_EXE` when set or falls back to this repo's Debug/Release MCP build output. It includes the top-level `opentia-toolkit-workflow` router plus specialized TIA interaction, HMI editing, hardware, PLC, library, trend logging, VCI, and repo development skills.

The Claude Code plugin package lives in `plugins/opentia-toolkit-claude-code` and uses the same launcher pattern. It includes the same workflow skills. The repo root also has a local Claude Code marketplace at `.claude-plugin/marketplace.json`.

The Claude Desktop MCPB source package lives in `plugins/opentia-toolkit-claude-desktop`. Claude Desktop MCPB packages expose the MCP server but do not currently bundle skill folders, so keep equivalent project/prompt guidance available when using Claude Desktop. Build a `.mcpb` extension from a local MCP build with:

```powershell
.\scripts\Pack-ClaudeDesktopMcpb.ps1
```

The OpenCode bundle lives in `plugins/opentia-toolkit-opencode`. Copy its contents into an OpenCode project root to get the MCP launcher config, `.opencode` plugin, OpenTiaToolkit workflow skills, and `AGENTS.md` rules. For a development checkout, you can also use `docs/mcp-client-configs/opencode.jsonc` and replace the placeholder path with your local build output.

Generic hardware configuration, including GSD/GSDML instantiation, PROFINET identity/wiring, process-image address assignment, Startdrive telegram edits, and guarded parameter-record changes, is available through both CLI and MCP. See [HARDWARE_AUTOMATION.md](HARDWARE_AUTOMATION.md) before building device-specific workflows.

## Run Tests

Fast tests do not require a live TIA Portal session:

```powershell
dotnet test .\tests\OpenTiaToolkit.Tests\OpenTiaToolkit.Tests.csproj --filter "TestCategory!=Integration"
```

Live integration tests require TIA Portal V21, Openness allow-list registration, and a test project:

```powershell
$env:OPEN_TIA_TEST_PROJECT_PATH = "D:\Siemens\V21\MCP_TEST_V21\MCP_TEST_V21.ap21"
$env:OPEN_TIA_TEST_EXPORT_PATH = "D:\Siemens\V21\MCP_TEST_V21\Exports"
$env:OPEN_TIA_TEST_OUTPUT_PATH = "D:\Siemens\V21\MCP_TEST_OUTPUT"
dotnet test .\tests\OpenTiaToolkit.Tests\OpenTiaToolkit.Tests.csproj --filter "TestCategory=Integration"
```

Unified HMI validation can use a separate project:

```powershell
$env:OPEN_TIA_TEST_HMI_PROJECT_PATH = "D:\Siemens\V21\HmiTest\HmiTest.ap21"
$env:OPEN_TIA_TEST_UNIFIED_HMI_NAME = "HMI_1"
```

## Troubleshooting

If build cannot find Siemens assemblies, confirm the PublicAPI path exists or pass `/p:TiaPortalPublicApiPath=...`.

If TIA rejects CLI or MCP access, refresh the Openness allow-list entry for the exact rebuilt executable.

If an MCP client cannot start the server, run the MCP executable directly from PowerShell first and verify the path in the client config. After rebuilding or changing which Debug/Release executable is used, restart the MCP client so it launches the newly registered binary.
