# Install Agent Plugins

The Windows installer places OpenTiaToolkit in:

```text
C:\Program Files\OpenTiaToolkit
```

It also sets `OPEN_TIA_MCP_EXE` to:

```text
C:\Program Files\OpenTiaToolkit\mcp\OpenTiaToolkit.MCP.exe
```

and registers that executable with Siemens Openness during setup when the registration task is selected.

## Ask Your Agent

After installation, the easiest setup path is to ask your agent to install or configure OpenTiaToolkit from the installed folder.

Useful prompts:

```text
Install the local OpenTiaToolkit plugin from C:\Program Files\OpenTiaToolkit\plugins\opentia-toolkit and configure its MCP server.
```

```text
Configure OpenTiaToolkit as a local MCP server. Use C:\Program Files\OpenTiaToolkit\mcp\OpenTiaToolkit.MCP.exe as the command and use the guidance in C:\Program Files\OpenTiaToolkit\docs\mcp-client-configs.
```

```text
Install the OpenTiaToolkit Claude Code plugin from C:\Program Files\OpenTiaToolkit\plugins\opentia-toolkit-claude-code.
```

```text
Configure OpenCode with the OpenTiaToolkit bundle in C:\Program Files\OpenTiaToolkit\plugins\opentia-toolkit-opencode.
```

The installed plugin packages include workflow skills for TIA Portal discovery, PLC work, Unified HMI editing, hardware configuration, trend logging, libraries, VCI synchronization, and OpenTiaToolkit repo development.

## Installed Layout

```text
C:\Program Files\OpenTiaToolkit
  OpenTiaToolkit.MCP.exe
  OpenTiaToolkit.CLI.exe
  install-manifest.json
  docs\
    INSTALL_AGENT_PLUGINS.md
    mcp-client-configs\
  plugins\
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
