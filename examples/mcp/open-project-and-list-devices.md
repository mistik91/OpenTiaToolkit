# MCP Example: Open Project And List Devices

Use this sequence from an MCP-capable client after configuring the `opentia` server.

```text
connect-portal
open-project --project-path="D:\Siemens\V21\MCP_TEST_V21\MCP_TEST_V21.ap21"
list-devices
get-project-info
```

Replace the project path with your own `.ap21` file.

For a fresh MCP server session, call `connect-portal` before project or HMI commands.

