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

`plugins\opentia-toolkit-agent-plugin` is an Agent Plugins 1.0 package. Its root manifest, 18 workflow skills, and portable stdio MCP configuration can be loaded together by compatible clients including ChatGPT/Codex, Cursor, GitHub Copilot, Kiro, and VS Code.

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
