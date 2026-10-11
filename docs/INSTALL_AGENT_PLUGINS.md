# Install Agent Plugins

The Windows installer places OpenTiaToolkit in:

```text
C:\Program Files\OpenTiaToolkit
```

It also sets `OPEN_TIA_MCP_EXE` to:

```text
C:\Program Files\OpenTiaToolkit\mcp\OpenTiaToolkit.MCP.exe
```

The setup registration task registers the isolated V21 worker—not the vendor-free MCP host—with Siemens Openness.

## Portable Agent Plugins 1.0

`plugins\opentia-toolkit-agent-plugin` is an Agent Plugins 1.0 package with a root manifest, 18 workflow skills and portable stdio MCP configuration. Use a client's supported local-plugin installation flow when it accepts this format. Separate bundles and MCP snippets are provided below for other clients.

Independent installed preview.8 acceptance exercised the portable package in Codex, including actual desktop doctor execution and 189-tool backend discovery. Other client packages/configuration examples are available but were not independently qualified in this campaign. Some desktop chats expose only a subset of backend tools; missing wrappers are not proof that the server omitted them. Enable one OpenTia plugin/server configuration per client and verify the installed worker path with an actual `doctor` call. See [qualification and client limits](PREVIEW8_ACCEPTANCE.md).

The plugin does not contain TIA Portal or replace OpenTiaToolkit installation. Install the Windows product and register its V21 worker first. The launcher then discovers the adjacent installed MCP payload even for a custom installation directory; `OPEN_TIA_MCP_EXE` remains an explicit override.

## Ask Your Agent

After installation, the easiest setup path is to ask your agent to install or configure OpenTiaToolkit from the installed folder.

Useful prompts:

```text
Install the portable OpenTiaToolkit Agent Plugin from C:\Program Files\OpenTiaToolkit\plugins\opentia-toolkit-agent-plugin and configure its MCP server.
```

In VS Code, enable `chat.plugins.enabled` and register the installed directory in user settings:

```json
{
  "chat.plugins.enabled": true,
  "chat.pluginLocations": {
    "C:\\Program Files\\OpenTiaToolkit\\plugins\\opentia-toolkit-agent-plugin": true
  }
}
```

Other conforming clients should use their local-directory Agent Plugins installation flow.

```text
Configure OpenTiaToolkit as a local MCP server. Use C:\Program Files\OpenTiaToolkit\mcp\OpenTiaToolkit.MCP.exe as the command and use the guidance in C:\Program Files\OpenTiaToolkit\docs\mcp-client-configs.
```

```text
Install the OpenTiaToolkit Claude Code plugin from C:\Program Files\OpenTiaToolkit\plugins\opentia-toolkit-claude-code.
```

```text
Configure OpenCode with the OpenTiaToolkit bundle in C:\Program Files\OpenTiaToolkit\plugins\opentia-toolkit-opencode.
```

The installed plugin packages include workflow skills for TIA Portal discovery, Project Server and Multiuser collaboration, state-bound engineering blueprints, PLC work, Unified HMI editing, Unified discrete-alarm engineering, hardware configuration, trend logging, libraries, VCI synchronization, and OpenTiaToolkit repo development.

## Installed Layout

```text
C:\Program Files\OpenTiaToolkit
  install-manifest.json
  cli\
    OpenTiaToolkit.CLI.exe
  mcp\
    OpenTiaToolkit.MCP.exe
  worker-v21\
    OpenTiaToolkit.Worker.V21.exe
  docs\
    INSTALL_AGENT_PLUGINS.md
    mcp-client-configs\
  plugins\
    opentia-toolkit-agent-plugin\
      plugin.json
      mcp.json
      skills\
    opentia-toolkit\
    opentia-toolkit-claude-code\
    opentia-toolkit-claude-desktop\
    opentia-toolkit-opencode\
  scripts\
    Register-OpennessAllowList.ps1
  skills\
```

## Manual Fallback

If your client does not support local plugin installation, use the installed-product snippets in:

```text
C:\Program Files\OpenTiaToolkit\docs\mcp-client-configs
```

Those snippets point directly at the installed MCP executable:

```text
C:\Program Files\OpenTiaToolkit\mcp\OpenTiaToolkit.MCP.exe
```

Keep `OPEN_TIA_MCP_LOG_LEVEL` at `Error` unless you are debugging startup or tool execution.

## Acceptance and startup troubleshooting

The legacy Codex and Claude Code configurations use the process/user/machine `OPEN_TIA_MCP_EXE` setting, then the default Program Files installation. This avoids relying on the client expanding plugin-root tokens or starting in the plugin directory. For a custom installation or source build, set that path explicitly through the client or the existing installer setting. The portable package retains its plugin-root launcher and adjacent-layout discovery.

Verify actual tool discovery and execution after installing a plugin. A successful launcher `-ResolveOnly` check proves the path selection, but does not prove the client handshake. The repository's `scripts/Test-McpStdio.ps1` can exercise that handshake with `-ExecutablePath <installed-mcp-exe> -LauncherScriptPath <plugin-launcher> -DiscoverWorkerFromLayout`; this checks discovery without inherited worker overrides.

If a bundled server fails but direct MCP works, retain the failure details and client version; do not call the plugin accepted based on the fallback. Compare the cached plugin launcher with the installed package and inspect worker overrides. Enable one OpenTia server at a time for the comparison to avoid duplicate tools. Restore the previous settings afterward unless the user chooses the working configuration.

Back up client settings before editing. Prefer the client's supported MCP configuration command or a TOML-aware editor, and validate the resulting configuration before restarting. Do not append a duplicate MCP table after a command has already created it. For Codex, `codex mcp get <server-name> --json` checks that a direct entry parses; actual discovery remains a separate acceptance check.
