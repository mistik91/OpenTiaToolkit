# Setup

This guide covers a normal OpenTiaToolkit setup on Windows with TIA Portal V21.

## Prerequisites

- Windows
- TIA Portal V21 with Openness enabled and the legitimate Siemens engineering licenses/components for your workflow
- .NET Framework 4.8 for the installed CLI, MCP host, and worker
- Membership in the Siemens TIA Openness Windows group (sign out/in after changing it)
- For source builds: the .NET SDK pinned by `global.json`, .NET Framework 4.8 targeting tools, and Git
- Inno Setup 6, only when building the installer

Installed-product use needs no SDK or source build. A healthy `doctor` verifies access/API readiness, not engineering licenses; validate the intended licensed workflow on a disposable project. See [preview.8 qualification](PREVIEW8_ACCEPTANCE.md) for tested environments and limits.

The default Siemens Openness reference path is:

```text
C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48
```

Most users who installed TIA Portal in the default location do not need to change anything.

## Installed Product Setup

For normal use, choose the intended preview version explicitly and download its Windows installer from the [GitHub releases page](https://github.com/mistik91/OpenTiaToolkit/releases).

Download the intended installer together with `INSTALLER-SHA256SUMS.txt` and `UNSIGNED-NOTICE.txt` from the [preview.8 release](https://github.com/mistik91/OpenTiaToolkit/releases/tag/v1.0.0-preview.8); verify its hash using the [README quick start](../README.md#install-a-preview). Run normal Setup with its exact-worker registration task selected; approve Windows elevation when requested.

The installer places OpenTiaToolkit under:

```text
C:\Program Files\OpenTiaToolkit
```

It installs isolated `mcp\`, `cli\`, and `worker-v21\` payloads, plus plugins, skills, schemas, compatibility metadata, and docs. Setup sets `OPEN_TIA_MCP_EXE`, `OPEN_TIA_WORKER_V21_EXE`, and the legacy `OPEN_TIA_WORKER_EXE`, and can register `OpenTiaToolkit.Worker.V21.exe` with Siemens Openness. Version-specific variables take precedence. CLI and MCP are vendor-free and do not require Openness allow-list entries; see [API compatibility](API_COMPATIBILITY.md).

After installation, ask your agent to install or configure the local plugin from the installed folder. See [INSTALL_AGENT_PLUGINS.md](INSTALL_AGENT_PLUGINS.md).

Installer environment changes do not update an already-running shell or agent client; open a new terminal and restart the client when needed. Preview.7 and later also discover the installed sibling worker directly without these variables. Explicit overrides still take precedence: inspect process/user/machine `OPEN_TIA_*` values if startup selects an old development worker. Run `doctor` and confirm its reported worker path. Binary baseline warnings require review and affected-workflow testing; see [API compatibility](API_COMPATIBILITY.md).

## First run

Open a new terminal and check the installed identity before connecting:

```powershell
& "C:\Program Files\OpenTiaToolkit\cli\OpenTiaToolkit.CLI.exe" version
& "C:\Program Files\OpenTiaToolkit\cli\OpenTiaToolkit.CLI.exe" doctor
& "C:\Program Files\OpenTiaToolkit\cli\OpenTiaToolkit.CLI.exe"
```

Use one interactive/script session for a connected workflow. Inspect running instances and the exact project before mutations; see the [README example](../README.md#install-a-preview). Begin engineering on a disposable project copy with independent backups. `compile-software` is PLC-only in preview.8; HMI and hardware compiler checks use normal TIA UI.

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

The portable Agent Plugins 1.0 package lives in `plugins/opentia-toolkit-agent-plugin`. Compatible clients discover its skills from `skills/` and its stdio MCP definition from `mcp.json`. Its launcher uses `OPEN_TIA_MCP_EXE` when explicitly set, discovers an adjacent installed-product payload for default or custom installer locations, or falls back to this repo's Debug/Release MCP build output. It includes the top-level `opentia-toolkit-workflow` router plus specialized TIA interaction, Multiuser collaboration, HMI editing, Unified discrete-alarm engineering, hardware, PLC, library, trend logging, VCI, and repo development skills.

The portable package does not replace product installation. TIA Portal V21, the MCP and isolated worker binaries, and Siemens Openness registration must already be installed before its MCP tools can run. Clients can still load and inspect the packaged skills when the local product is absent.

The legacy Codex plugin remains in `plugins/opentia-toolkit` as a separate package so its `.codex-plugin` and `.mcp.json` files do not become non-portable files inside the Agent Plugins 1.0 package.

The Claude Code plugin package lives in `plugins/opentia-toolkit-claude-code` and uses the same launcher pattern. It includes the same workflow skills. The repo root also has a local Claude Code marketplace at `.claude-plugin/marketplace.json`.

The Claude Desktop MCPB source package lives in `plugins/opentia-toolkit-claude-desktop`. Claude Desktop MCPB packages expose the MCP server but do not currently bundle skill folders, so keep equivalent project/prompt guidance available when using Claude Desktop. Build a `.mcpb` extension from a local MCP build with:

```powershell
.\scripts\Pack-ClaudeDesktopMcpb.ps1
```

The packer writes `opentia-toolkit.mcpb` and `MCPB-SHA256SUMS.txt` under `artifacts\claude-desktop-mcpb`.

The OpenCode bundle lives in `plugins/opentia-toolkit-opencode`. Copy its contents into an OpenCode project root to get the MCP launcher config, `.opencode` plugin, OpenTiaToolkit workflow skills, and `AGENTS.md` rules. For a development checkout, you can also use `docs/mcp-client-configs/opencode.jsonc` and replace the placeholder path with your local build output.

Generic hardware configuration, including GSD/GSDML instantiation, PROFINET identity/wiring, process-image address assignment, Startdrive telegram edits, and guarded parameter-record changes, is available through both CLI and MCP. See [HARDWARE_AUTOMATION.md](HARDWARE_AUTOMATION.md) before building device-specific workflows.

## Clone And Build (contributors)

```powershell
git clone https://github.com/mistik91/OpenTiaToolkit.git
cd OpenTiaToolkit
dotnet build OpenTiaToolkit.sln
```

For a non-standard TIA Portal installation, pass `TiaPortalPublicApiPath`:

```powershell
dotnet build OpenTiaToolkit.sln /p:TiaPortalPublicApiPath="D:\Siemens\Portal V21\PublicAPI\V21\net48"
```

## Register A Rebuilt Worker (contributors)

Siemens Openness allow-list entries are tied to executable path and hash. Register the exact rebuilt V21 worker you plan to launch.

Run PowerShell as Administrator:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Register-OpennessAllowList.ps1 -ExecutablePath .\src\OpenTiaToolkit.Worker.V21\bin\Debug\net48\OpenTiaToolkit.Worker.V21.exe
```

Repeat this after rebuilding or signing the worker. Do not register the vendor-free CLI or MCP host.

## Run The Development CLI (contributors)

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

## Build The Installer (contributors)

Install Inno Setup for the current Windows user with:

```powershell
winget install --id JRSoftware.InnoSetup --exact --scope user
```

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

## Archive and file-access policy

MCP path parameters default to Documents, Desktop and `%TEMP%\OpenTiaToolkit`; existing input files are capped at 16 MiB. `OPEN_TIA_MCP_ALLOWED_ROOTS` accepts semicolon-separated roots and `OPEN_TIA_MCP_MAX_INPUT_FILE_BYTES` accepts a positive byte limit. Use an explicit, narrowly scoped client configuration if a reviewed workflow needs another root or limit; do not disable file protections or silently expand them.

For a large test archive, the owner can retrieve it through normal TIA UI into a new disposable folder under a permitted root, close it, then open that copy through a newly owned toolkit session. Verify its exact path and ownership before edits. Keep the original/archive unchanged. Device-creation previews require a saved standalone snapshot without unsaved changes and never save or close the selected project automatically.

## Upgrade, recovery and uninstall

Use one enabled OpenTia integration per client. An upgrade can stop the installed MCP/worker while the client retains a closed transport; restart the client normally and verify an actual doctor call and selected worker path. Missing desktop wrappers can coexist with complete backend discovery and are not guaranteed to be repaired by another restart.

If an operation loses its session, inspect its returned result, reset explicitly and reselect/reopen the intended instance/project before further writes. Obtain fresh state; do not blindly replay a timed-out mutation. Closing a project/portal through the API can leave a GUI root alive. Close remaining windows normally and avoid process-name-wide termination.

Uninstall can retain exact-worker Siemens entries and client-managed configuration/cache. Complete immediate product-root removal was not observed in one first-use lifecycle test. Stop using the installed integration before uninstalling; review remaining product files and client entries individually without deleting shared Siemens registrations or development installations. See [the qualification record](PREVIEW8_ACCEPTANCE.md) for the exact observations.

The accepted installer includes a pre-acceptance documentation snapshot. Current repository guidance and release `VALIDATION_PREVIEW8.md` supplement it; documentation follow-ups do not silently replace accepted installer or plugin bytes.
