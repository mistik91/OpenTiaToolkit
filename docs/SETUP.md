# Setup

This guide covers a normal OpenTiaToolkit setup on Windows with TIA Portal V21.

## Prerequisites

- Windows
- TIA Portal V21 with Openness enabled
- .NET SDK
- Git
- Inno Setup 6, only when building the installer

Install Inno Setup for the current Windows user with:

```powershell
winget install --id JRSoftware.InnoSetup --exact --scope user
```

The default Siemens Openness reference path is:

```text
C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48
```

Most users who installed TIA Portal in the default location do not need to change anything.

## Installed Product Setup

For normal use, download the latest Windows installer from the [GitHub releases page](https://github.com/mistik91/OpenTiaToolkit/releases).

For local development or release packaging, build the installer from the repo root:

```powershell
.\scripts\Build-Installer.ps1
```

That command produces an unsigned installer when no signing certificate is configured. Unsigned builds are permitted only for an explicitly labeled preview and include `UNSIGNED-NOTICE.txt`. Stable release builds require a certificate in the Windows certificate store:

```powershell
$env:OPEN_TIA_SIGNING_CERT_THUMBPRINT = "<certificate-thumbprint>"
.\scripts\Build-Installer.ps1 -RequireSigning
```

To validate the payload without compiling an installer, stage the product layout:

```powershell
.\scripts\Build-Installer.ps1 -StageOnly
```

The installer places OpenTiaToolkit under:

```text
C:\Program Files\OpenTiaToolkit
```

It installs isolated `mcp\`, `cli\`, and `worker-v21\` payloads, plus plugins, skills, schemas, compatibility metadata, and docs. Setup sets `OPEN_TIA_MCP_EXE`, `OPEN_TIA_WORKER_V21_EXE`, and the legacy `OPEN_TIA_WORKER_EXE`, and can register `OpenTiaToolkit.Worker.V21.exe` with Siemens Openness. Version-specific variables take precedence. CLI and MCP are vendor-free and do not require Openness allow-list entries; see [API compatibility](API_COMPATIBILITY.md).

After installation, ask your agent to install or configure the local plugin from the installed folder. See [INSTALL_AGENT_PLUGINS.md](INSTALL_AGENT_PLUGINS.md).

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

Siemens Openness allow-list entries are tied to executable path and hash. Register the exact rebuilt V21 worker you plan to launch.

Run PowerShell as Administrator:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Register-OpennessAllowList.ps1 -ExecutablePath .\src\OpenTiaToolkit.Worker.V21\bin\Debug\net48\OpenTiaToolkit.Worker.V21.exe
```

Repeat this after rebuilding or signing the worker. Do not register the vendor-free CLI or MCP host.

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
C:\Program Files\OpenTiaToolkit\mcp\OpenTiaToolkit.MCP.exe
```

For installed-product setups, the recommended path is to ask your agent to install the local plugin or configure the MCP server from:

```text
C:\Program Files\OpenTiaToolkit\plugins
```

See [INSTALL_AGENT_PLUGINS.md](INSTALL_AGENT_PLUGINS.md) for suggested prompts and manual fallbacks.

The Codex plugin package lives in `plugins/opentia-toolkit`. It runs `scripts/run-opentia-mcp.ps1`, which uses `OPEN_TIA_MCP_EXE` when set or falls back to this repo's Debug/Release MCP build output. It includes the top-level `opentia-toolkit-workflow` router plus specialized TIA interaction, Multiuser collaboration, HMI editing, hardware, PLC, library, trend logging, VCI, and repo development skills.

The Claude Code plugin package lives in `plugins/opentia-toolkit-claude-code` and uses the same launcher pattern. It includes the same workflow skills. The repo root also has a local Claude Code marketplace at `.claude-plugin/marketplace.json`.

The Claude Desktop MCPB source package lives in `plugins/opentia-toolkit-claude-desktop`. Claude Desktop MCPB packages expose the MCP server but do not currently bundle skill folders, so keep equivalent project/prompt guidance available when using Claude Desktop. Build a `.mcpb` extension from a local MCP build with:

```powershell
.\scripts\Pack-ClaudeDesktopMcpb.ps1
```

The packer writes `opentia-toolkit.mcpb` and `MCPB-SHA256SUMS.txt` under `artifacts\claude-desktop-mcpb`.

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

If TIA rejects access, refresh the Openness allow-list entry for the exact rebuilt `OpenTiaToolkit.Worker.V21.exe`.

If an MCP client cannot start the server, run the MCP executable directly from PowerShell first and verify the path in the client config. After rebuilding or changing which Debug/Release executable is used, restart the MCP client so it launches the newly registered binary.
