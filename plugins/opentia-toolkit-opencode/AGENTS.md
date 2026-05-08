# OpenTiaToolkit Agent Notes

OpenTiaToolkit exposes Siemens TIA Portal automation through the `opentia` MCP server.

For a fresh MCP session, call tools in this order:

1. `connect-portal`
2. `open-project`
3. `open-unified-hmi`

Do not call `open-project` before `connect-portal`; the MCP server may not have attached to TIA Portal yet.

For Unified HMI screen edits, load and follow the `hmi-unified-agent-editing` skill. Prefer focused JSON screen snapshots, targeted patch operations, a dry run, then an apply with backup enabled unless the user explicitly asks otherwise.

Keep OpenTiaToolkit MCP calls serial. TIA Portal sessions are stateful, and parallel tool calls can race the same engineering session.
