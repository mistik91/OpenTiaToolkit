# OpenTiaToolkit Agent Notes

OpenTiaToolkit exposes Siemens TIA Portal automation through the `opentia` MCP server.

For a fresh or ambiguous OpenTiaToolkit request, load and follow the `opentia-toolkit-workflow` skill. For live TIA Portal work, load and follow `basic-tia-interaction` next. In particular, discover running instances first, verify the open project after connecting, and do not report that no project is open from a connect response alone.

When no TIA instance or project is already selected, the usual tool order is:

1. `connect-portal`
2. `list-projects` or `get-project-info`
3. `open-project` only if discovery proves no project is open or the user requested a different project
4. `open-unified-hmi`

When the user says an instance/project is already open, start with `list-instances`; if one instance exists, use `connect-instance`, then verify the project with `list-projects` or `get-project-info`. If multiple instances exist, probe each instance and choose the one matching the specified project, or ask if no project was specified.

Do not call `open-project` before `connect-portal`; the MCP server may not have attached to TIA Portal yet.

For Unified HMI screen edits, load and follow the `basic-tia-interaction` skill first when the TIA context is not proven, then the `hmi-unified-agent-editing` skill. Prefer focused JSON screen snapshots, targeted patch operations, a dry run, then an apply with backup enabled unless the user explicitly asks otherwise. Filtered patch backups are scoped to the selected screen/items/properties; after a successful apply, use `newBaseHash` for the next batch.

For hardware configuration, establish the TIA project with `basic-tia-interaction`, then use `hardware-configuration-workflow`: discover catalog identifiers and hardware target paths, and use `apply-hardware-patch` with `dry-run=true` before applying. Keep vendor/device-specific type identifiers and GSD parameter byte meanings outside the generic toolkit layer.

For PLC block, type, tag, CSV update, rename, or compile work, use `plc-block-and-tag-workflow`. For project/global library, master copy, library type, UDT version, or update-check work, use `library-workflow`. For Unified HMI trend logging, logging tags, or data log lifecycle work, use `hmi-trend-logging-workflow`. For TIA Version Control Interface work, use `vci-sync-workflow`. For repo development, tests, plugin packaging, or skill packaging, use `opentia-dev-workflow`.

Keep OpenTiaToolkit MCP calls serial. The MCP server rejects overlapping TIA operations with active-operation details; after a timeout or disconnected response, wait for the in-flight operation to finish and verify the filtered target before retrying.
