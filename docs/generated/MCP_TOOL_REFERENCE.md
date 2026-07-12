# Generated MCP tool reference

Generated from the same runtime command catalog used by the CLI. Every command below is exposed as an MCP tool with the listed kebab-case name and parameters.

| Tool | Domain | Parameters | Description |
|---|---|---|---|
| `add-device` | Portal | `order-number`, `version`, `name`, `confirm` | Adds a device with its primary item to the current project |
| `add-project-to-multiuser-server` | Multiuser | `server-alias`, `project-path`, `confirm` | Adds a closed single-user TIA project to a configured Project Server. |
| `apply-block-document-import` | PLC | `apply-token`, `confirm` | Applies a reviewed SIMATIC SD import plan, verifies and compiles it, and rolls back failures |
| `apply-blueprint` | V21 | `apply-token`, `confirm` | Applies a previously reviewed blueprint plan using its state-bound one-use token. |
| `apply-hardware-patch` | Hardware | `operations`, `operations-path`, `dry-run`, `confirm-destructive`, `transaction-description`, `plc-name`, `safety-password-env`, `confirm` | Dry-runs or applies transactional generic TIA hardware operations |
| `apply-hmi-screen-json-edit` | Unified HMI | `hmi-name`, `screen-path`, `edited-screen`, `base-hash`, `dry-run`, `allow-stale-base`, `fail-on-warning`, `require-clean-preflight`, `backup`, `backup-path`, `item-names`, `properties`, `working-path`, `confirm` | Dry-runs or applies one Unified HMI screen JSON edit with safety checks |
| `apply-hmi-screen-json-patch` | Unified HMI | `hmi-name`, `screen-path`, `operations`, `operations-path`, `base-hash`, `dry-run`, `allow-stale-base`, `fail-on-warning`, `require-clean-preflight`, `backup`, `backup-path`, `item-names`, `properties`, `working-path`, `confirm` | Dry-runs or applies compact Unified HMI screen patch operations with safety checks |
| `archive-project` | Portal | `archive-path`, `mode`, `confirm` | Archives the current project to a .zap21 archive |
| `audit-hardware-tag-mappings` | Hardware | `plc-name`, `tag-table-filter`, `catalog-search-text`, `max-unmatched-tags`, `output-path` | Audits PLC I/Q tag addresses against discovered hardware IO ranges and optional catalog evidence |
| `audit-hmi-label-proximity` | Unified HMI | `input-path`, `screen-path`, `target-items`, `target-types`, `label-texts`, `label-types`, `max-candidates`, `output-path` | Creates an auditable nearest-label mapping report from an exported Unified HMI JSON file |
| `can-plug-hardware-item` | Hardware | `target`, `device-name`, `item-path`, `expected-type-identifier`, `type-identifier`, `item-name`, `position-number` | Checks whether a hardware module or submodule can be plugged at a target position |
| `capabilities` | V21 |  | Reports the worker capability profile and exact Siemens assembly hashes. |
| `check-library-updates` | Libraries | `library-scope`, `global-library`, `include-up-to-date`, `output-path` | Reports project instances that are outdated against the project library or an open global library |
| `close-global-library` | Libraries | `global-library` | Closes an open user global library by name or path |
| `close-project` | Portal | `save`, `confirm` | Closes the currently open project |
| `close-tia-portal` | Portal | `confirm` | Closes the current TIA Portal instance |
| `commit-multiuser-session` | Multiuser | `comment`, `require-up-to-date`, `require-no-conflicts`, `confirm` | Saves and checks in the selected local session, returning the created server revision. |
| `compare-software` | PLC | `plc-name`, `target`, `output-path` | Compares PLC software to the online target |
| `compile-software` | PLC | `plc-name`, `password` | Compiles the PLC software |
| `connect-instance` | Portal | `instance-id` | Connects to a specific TIA Portal instance by process ID |
| `connect-portal` | Portal |  | Connects to a running TIA Portal instance or starts a new one |
| `connect-vci-block` | VCI | `workspace-name`, `plc-name`, `block-name`, `relative-directory`, `file-name`, `file-format`, `block-type`, `group-path`, `confirm` | Connects one PLC block to existing VCI workspace files |
| `copy-from-master-copy` | Libraries | `library-scope`, `global-library`, `master-copy-path`, `plc-name`, `target-group-path`, `dry-run`, `confirm` | Copies a library master copy into a PLC block group with dry-run and confirmation gates |
| `copy-hmi-screen-from-template` | Unified HMI | `hmi-name`, `template-screen-path`, `target-screen-path`, `find-text`, `replace-text`, `confirm` | Copies a Unified HMI screen from a template screen with optional find/replace |
| `create-block` | PLC | `plc-name`, `block-name`, `block-type`, `language`, `confirm` | Creates a new block in PLC software (currently validated for ProDiag FB creation) |
| `create-hmi-item` | Unified HMI | `hmi-name`, `screen-path`, `item-type`, `item-name`, `left`, `top`, `width`, `height`, `confirm` | Creates a Unified HMI item on a screen |
| `create-hmi-screen` | Unified HMI | `hmi-name`, `screen-path`, `width`, `height`, `confirm` | Creates a Unified HMI screen if it does not already exist |
| `create-hmi-trend` | Unified HMI | `hmi-name`, `tag-path`, `datalog-name`, `cyclic`, `cycle`, `storage-folder`, `log-max-size`, `duration-days`, `segment-max-size`, `confirm` | Creates or updates Unified HMI logging infrastructure for a tag |
| `create-hmi-trends` | Unified HMI | `hmi-name`, `configs`, `dry-run`, `confirm` | Plans or applies multiple Unified HMI trend logging configurations |
| `create-multiuser-session` | Multiuser | `server-alias`, `project-name`, `session-name`, `session-directory`, `mode`, `umac-user`, `umac-password-env`, `umac-user-type`, `confirm` | Creates an agent-owned Multiuser or Exclusive local session. |
| `create-project` | Portal | `project-path`, `confirm` | Creates a new TIA Portal project and selects it as the current project |
| `create-vci-workspace` | VCI | `workspace-name`, `root-path`, `workspace-language`, `confirm` | Creates a Version Control Interface workspace in the open project |
| `delete-blocks` | PLC | `plc-name`, `filter`, `confirm` | Deletes blocks matching a regex pattern from PLC software |
| `delete-external-source` | PLC | `plc-name`, `source-name`, `confirm` | Deletes one PLC external source after explicit confirmation |
| `delete-hmi-data-log` | Unified HMI | `hmi-name`, `datalog-name`, `confirm` | Deletes one Unified HMI data log archive when no logging tags still reference it |
| `delete-hmi-data-logs` | Unified HMI | `hmi-name`, `configs`, `dry-run`, `confirm` | Plans or deletes multiple unreferenced Unified HMI data log archives |
| `delete-vci-workspace` | VCI | `workspace-name`, `confirm` | Deletes a Version Control Interface workspace definition |
| `describe-api` | V21 |  | Self-describes the installed V21 public API surface and prioritized feature availability. |
| `diff-api-profile` | V21 | `baseline-path` | Compares the installed V21 public API self-description with a committed migration baseline. |
| `diff-hmi-json` | Unified HMI | `base-path`, `modified-path`, `patch-path`, `screens`, `item-names`, `properties` | Creates a minimal Unified HMI JSON patch from two exports |
| `disconnect-portal` | Portal | `confirm` | Disconnects from the TIA Portal instance |
| `doctor` | V21 |  | Checks the isolated worker runtime, Siemens V21 API, paths, and process architecture. |
| `download-software` | PLC | `plc-name`, `to-simulator`, `dry-run`, `only-changes`, `confirm` | Downloads PLC software to PLCSIM Advanced with dry-run and confirmation gates |
| `ensure-multiuser-server` | Multiuser | `server-alias`, `protocol`, `host`, `port`, `confirm` | Creates or updates a TIA Project Server connection. |
| `export-block` | PLC | `plc-name`, `block-name`, `export-path` | Exports a single PLC block to XML |
| `export-block-as-docs` | PLC | `plc-name`, `block-name`, `export-path` | Exports a single block as SIMATIC SD documents (.s7dcl + .s7res) |
| `export-block-as-source` | PLC | `plc-name`, `block-name`, `output-path` | Exports a PLC block as an external source file when supported |
| `export-blocks-as-docs` | PLC | `plc-name`, `export-path`, `filter-regex` | Exports multiple blocks as SIMATIC SD documents (.s7dcl + .s7res) |
| `export-hmi-json` | Unified HMI | `hmi-name`, `output-path`, `screens`, `item-names`, `properties` | Exports the supported Unified HMI surface to JSON |
| `export-plc` | PLC | `plc-name`, `export-path` | Exports all PLC blocks to XML files |
| `export-tags` | PLC | `plc-name`, `culture`, `output-path` | Exports PLC tags from all tag tables |
| `export-texts` | Project Texts | `output-path`, `source-culture`, `target-cultures`, `categories`, `objects` | Exports project texts to a Siemens project text workbook |
| `export-type` | PLC | `plc-name`, `type-name`, `export-path` | Exports a single type (UDT) to XML |
| `export-types` | PLC | `plc-name`, `export-path`, `filter-regex` | Exports multiple types (UDT) to XML |
| `export-vci-block` | VCI | `workspace-name`, `plc-name`, `block-name`, `relative-directory`, `file-name`, `file-format`, `block-type`, `group-path` | Exports one PLC block into a VCI workspace and creates its mapping |
| `find-hardware-catalog-items` | Hardware | `search-text`, `culture` | Searches installed TIA Portal hardware catalog items by text |
| `find-hardware-module-types` | Hardware | `partial-type-identifier` | Finds module type identifiers using the project-level TIA module information provider |
| `find-replace-db-members` | PLC | `plc-name`, `rename-map`, `case-insensitive`, `dry-run`, `target-blocks`, `output-path`, `confirm` | Finds and replaces GlobalDB member names with nested struct support |
| `find-replace-tags` | PLC | `plc-name`, `rename-map`, `case-insensitive`, `dry-run`, `substring-match`, `output-path`, `confirm` | Finds and replaces PLC tag names across all tag tables |
| `find-replace-udt-members` | PLC | `plc-name`, `rename-map`, `case-insensitive`, `dry-run`, `target-types`, `output-path`, `confirm` | Finds and replaces UDT member names with nested struct support |
| `generate-blocks-from-source` | PLC | `plc-name`, `source-name`, `dry-run`, `confirm` | Generates PLC blocks from an external source when supported |
| `get-block-info` | PLC | `plc-name`, `block-name` | Gets detailed information about a specific PLC block |
| `get-blocks` | PLC | `plc-name`, `filter`, `output-path`, `page-size`, `continuation-token` | Lists blocks from PLC software |
| `get-cross-references` | PLC | `plc-name`, `name`, `output-path` | Gets PLC cross references for a tag, block, or named PLC object |
| `get-device-info` | Devices | `device-name` | Gets detailed information about a device |
| `get-device-item-info` | Devices | `device-name`, `item-name` | Gets detailed information about a device item |
| `get-gsd-parameter-data` | Hardware | `target`, `device-name`, `item-path`, `expected-type-identifier`, `dataset-number`, `byte-offset`, `length` | Reads raw parameter data bytes from a GSD or GSDML hardware item |
| `get-hardware-drive-parameters` | Hardware | `device-name`, `drive-object-index`, `filter`, `max-results` | Reads bounded Startdrive parameter metadata, values, units, ranges, and enumerations through the official V21 API. |
| `get-hardware-drive-telegrams` | Hardware | `device-name`, `address-attributes`, `output-path` | Lists Startdrive telegrams and their IO addresses exposed by hardware drive items |
| `get-hardware-feature-attributes` | Hardware | `target`, `device-name`, `item-path`, `expected-type-identifier`, `feature-service`, `attributes` | Reads engineering attributes from a hardware feature service such as NetworkInterface |
| `get-hardware-io-addresses` | Hardware | `device-name`, `attributes`, `output-path` | Lists process-image address objects exposed by hardware device items |
| `get-hardware-item-capabilities` | Hardware | `target`, `device-name`, `item-path`, `expected-type-identifier`, `attributes` | Gets available hardware feature services and attributes for one hardware target |
| `get-hardware-network-item-attributes` | Hardware | `target`, `device-name`, `item-path`, `expected-type-identifier`, `network-collection`, `network-item-index`, `attributes` | Reads engineering attributes from a NetworkInterface child such as a node or IO connector |
| `get-hardware-networks` | Hardware | `device-name`, `output-path` | Lists hardware network interfaces, nodes, IO controllers and IO connectors |
| `get-hardware-tree` | Hardware | `device-name`, `output-path` | Gets nested hardware devices and device items with reusable hardware target paths |
| `get-hmi-item-properties` | Unified HMI | `hmi-name`, `screen-path`, `item-name`, `properties` | Gets readable properties for a Unified HMI item |
| `get-hmi-screen-info` | Unified HMI | `hmi-name`, `screen-path` | Gets detailed information about a Unified HMI screen |
| `get-hmi-screen-json` | Unified HMI | `hmi-name`, `screen-path`, `item-names`, `properties`, `working-path`, `include-screen` | Gets one Unified HMI screen as canonical JSON for agent editing |
| `get-hmi-screen-summary` | Unified HMI | `hmi-name`, `screen-path`, `include-items`, `include-faceplates`, `working-path` | Summarizes item counts and faceplate contained types for one Unified HMI screen |
| `get-library-type-versions` | Libraries | `library-scope`, `global-library`, `type-path`, `output-path` | Lists versions for a library type |
| `get-multiuser-session-status` | Multiuser |  | Reports up-to-date state, markings, and conflicts for the selected local session. |
| `get-multiuser-status` | Insights |  | Reports project/local-session multiuser state and ownership through the official V21 API. |
| `get-online-status` | PLC | `plc-name`, `go-online` | Gets PLC online status, optionally going online first |
| `get-plc-alarm-text-list-details` | Insights | `max-results` | Reads PLC alarm system/user text-list metadata through the official V21 API. |
| `get-project-info` | Portal |  | Gets information about the currently open project |
| `get-project-tree` | Portal | `depth` | Gets the hierarchical project structure |
| `get-software-tree` | PLC | `plc-name`, `depth`, `output-path` | Gets the hierarchical PLC software structure |
| `get-technology-object-details` | Insights | `max-results` | Reads PLC technology object metadata and parameters through the official V21 API. |
| `get-unified-runtime-settings` | Insights | `max-results` | Reads scalar Unified HMI runtime settings through the official V21 API. |
| `get-vci-block-formats` | VCI | `plc-name`, `block-name`, `block-type`, `group-path` | Gets VCI-supported workspace file formats for one PLC block |
| `get-vci-block-status` | VCI | `workspace-name`, `plc-name`, `block-name`, `block-type`, `group-path` | Gets VCI comparison status for one mapped PLC block |
| `get-vci-workspace` | VCI | `workspace-name` | Gets details for a Version Control Interface workspace |
| `get-watch-table-details` | Insights | `max-results` | Reads PLC watch/force tables and their entry metadata through the official V21 API. |
| `hmi-status` | Unified HMI | `hmi-name`, `screen-path`, `open-if-needed` | Gets Unified HMI session status and optionally probes a screen |
| `import-block` | PLC | `plc-name`, `import-path`, `group-path`, `confirm` | Imports a single block from XML file |
| `import-block-from-docs` | PLC | `plc-name`, `import-directory`, `file-name`, `group-path`, `import-option`, `confirm` | Imports a single block from SIMATIC SD documents (.s7dcl/.s7res) |
| `import-blocks` | PLC | `plc-name`, `import-directory`, `filter-regex`, `group-path`, `confirm` | Imports multiple blocks from XML files in a directory |
| `import-blocks-from-docs` | PLC | `plc-name`, `import-directory`, `filter-regex`, `group-path`, `import-option`, `confirm` | Imports multiple blocks from SIMATIC SD documents (.s7dcl/.s7res) |
| `import-hmi-json` | Unified HMI | `hmi-name`, `input-path`, `dry-run`, `apply`, `confirm`, `fail-on-warning`, `require-clean-preflight`, `backup`, `backup-path`, `screens`, `item-names`, `properties` | Preflights or imports a Unified HMI JSON export or patch |
| `import-source` | PLC | `plc-name`, `source-path`, `dry-run`, `confirm` | Imports a PLC external source file when supported |
| `import-texts` | Project Texts | `input-path`, `update-source-language`, `dry-run`, `confirm` | Imports project texts from a Siemens project text workbook |
| `import-type` | PLC | `plc-name`, `import-path`, `group-path`, `confirm` | Imports a single type (UDT) from XML file |
| `import-types` | PLC | `plc-name`, `import-directory`, `filter-regex`, `group-path`, `confirm` | Imports multiple types (UDT) from XML files in a directory |
| `inspect-hmi-screen-json-capabilities` | Unified HMI | `hmi-name`, `screen-path`, `item-names`, `properties`, `include-item-types`, `include-writable-properties`, `include-snapshot`, `working-path` | Inspects one Unified HMI screen and returns agent-oriented patch capabilities and guidance |
| `inspect-project-security` | Insights |  | Inspects project security metadata and certificate/password/syslog service availability through V21 Openness. |
| `list-devices` | Portal | `page-size`, `continuation-token` | Lists all devices in the currently open project |
| `list-external-sources` | PLC | `plc-name`, `output-path`, `page-size`, `continuation-token` | PLC external sources |
| `list-global-libraries` | Libraries | `output-path`, `page-size`, `continuation-token` | Lists known and open TIA Portal global libraries |
| `list-hmi-item-types` | Unified HMI | `filter`, `page-size`, `continuation-token` | Lists the validated Unified HMI item types supported by this toolkit |
| `list-hmis` | Unified HMI | `page-size`, `continuation-token` | Lists all HMI devices found in the project |
| `list-hmi-screens` | Unified HMI | `hmi-name`, `group-path`, `recursive`, `page-size`, `continuation-token` | Lists screens in a Unified HMI |
| `list-hmi-tags` | Unified HMI | `hmi-name`, `filter`, `root-path`, `allow-full-scan`, `max-results`, `max-depth`, `include-details`, `include-logging-counts`, `output-path`, `page-size`, `continuation-token` | Lists tags in a Unified HMI |
| `list-instances` | Portal | `page-size`, `continuation-token` | Lists all running TIA Portal instances |
| `list-library-types` | Libraries | `library-scope`, `global-library`, `folder-path`, `recursive`, `output-path`, `page-size`, `continuation-token` | Lists library types in the project library or an open global library |
| `list-master-copies` | Libraries | `library-scope`, `global-library`, `folder-path`, `recursive`, `output-path`, `page-size`, `continuation-token` | Lists master copies in the project library or an open global library |
| `list-multiuser-projects` | Multiuser | `server-alias`, `page-size`, `continuation-token` | Lists projects available on a configured TIA Project Server. |
| `list-multiuser-servers` | Multiuser | `page-size`, `continuation-token` | Lists configured TIA Project Server connections. |
| `list-multiuser-sessions` | Multiuser | `server-alias`, `project-name`, `page-size`, `continuation-token` | Lists local sessions registered for a server project. |
| `list-plc-alarm-text-lists` | PLC | `plc-name`, `output-path`, `page-size`, `continuation-token` | PLC alarm text lists |
| `list-plcs` | PLC | `page-size`, `continuation-token` | Lists all PLC devices found in the project |
| `list-project-languages` | Project Texts | `page-size`, `continuation-token` | Lists project languages and active language settings |
| `list-projects` | Portal | `page-size`, `continuation-token` | Lists all open TIA Portal projects and sessions |
| `list-technology-objects` | PLC | `plc-name`, `output-path`, `page-size`, `continuation-token` | PLC technology objects |
| `list-unified-alarms` | Insights | `max-results`, `page-size`, `continuation-token` | Lists Unified HMI alarm classes, analog/discrete alarms, and OPC UA alarm types through the official V21 API. |
| `list-unified-text-lists` | Insights | `max-results`, `page-size`, `continuation-token` | Lists Unified HMI user, system, and graphic text lists through the official V21 API. |
| `list-vci-mappings` | VCI | `workspace-name`, `output-path`, `page-size`, `continuation-token` | Lists mapped objects in a Version Control Interface workspace |
| `list-vci-workspaces` | VCI | `page-size`, `continuation-token` | Lists Version Control Interface workspaces in the open project |
| `list-watch-tables` | PLC | `plc-name`, `output-path`, `page-size`, `continuation-token` | PLC watch and force tables |
| `modify-tags` | PLC | `plc-name`, `operations`, `input-path`, `dry-run`, `confirm`, `safety-password-env`, `output-path` | Plans or applies explicit PLC tag create, update, delete, and replace operations |
| `open-global-library` | Libraries | `path`, `read-only`, `upgrade`, `confirm` | Opens a TIA Portal global library by file path |
| `open-plc` | PLC | `plc-name` | Opens a PLC device software by name |
| `open-project` | Portal | `project-path`, `umac-user`, `umac-password-env`, `umac-user-type` | Opens a TIA Portal project file (.ap21/.als21 primary; legacy project formats also supported) |
| `open-tia-portal` | Portal | `with-ui` | Opens a new TIA Portal instance (with UI by default) |
| `open-unified-hmi` | Unified HMI | `hmi-name` | Opens a Unified HMI by device or device item name |
| `plan-block-document-import` | PLC | `plc-name`, `import-directory`, `file-name`, `group-path`, `import-option` | Validates a SIMATIC SD import and creates a state-bound, rollback-capable one-use apply plan |
| `plan-blueprint` | V21 | `blueprint-path` | Builds a state-bound engineering plan and short-lived one-use apply token. |
| `portal-status` | Portal |  | Gets the current TIA Portal connection status |
| `probe-hmi-faceplate-interface` | Unified HMI | `hmi-name`, `screen-path`, `item-name`, `item-name-path`, `property`, `write`, `value`, `restore`, `confirm` | Inspects Unified HMI faceplate interface metadata and optionally probes one reversible value write |
| `remove-hmi-logging-tag` | Unified HMI | `hmi-name`, `tag-path`, `datalog-name`, `logging-tag-name`, `confirm` | Removes one Unified HMI logging tag after verifying its data log association |
| `remove-hmi-logging-tags` | Unified HMI | `hmi-name`, `configs`, `dry-run`, `confirm` | Plans or removes multiple Unified HMI logging tags after data log verification |
| `retrieve-project` | Portal | `archive-path`, `target-directory`, `upgrade`, `umac-user`, `umac-password-env`, `umac-user-type`, `confirm` | Retrieves an archived project and selects it as the current project |
| `save-as-project` | Portal | `destination-path`, `confirm` | Saves the project to a new location |
| `save-multiuser-session` | Multiuser | `confirm` | Saves pending edits in the selected local session without checking in. |
| `save-project` | Portal | `confirm` | Saves the current project |
| `search-blocks` | PLC | `plc-name`, `pattern`, `search-in`, `filter-type`, `output-path`, `page-size`, `continuation-token` | Searches PLC blocks by name/comment with optional type filter |
| `set-hmi-item-property` | Unified HMI | `hmi-name`, `screen-path`, `item-name`, `property`, `value`, `confirm` | Sets a property or attribute on a Unified HMI item |
| `start-cpu` | PLC | `plc-name`, `confirm` | Starts the PLC CPU when a supported online runtime operation is available |
| `stop-cpu` | PLC | `plc-name`, `confirm` | Stops the PLC CPU when a supported online runtime operation is available |
| `sync-vci-block` | VCI | `workspace-name`, `plc-name`, `block-name`, `direction`, `create-mapping`, `relative-directory`, `file-name`, `file-format`, `block-type`, `group-path`, `confirm` | Synchronizes one mapped PLC block with a VCI workspace |
| `sync-vci-blocks` | VCI | `workspace-name`, `plc-name`, `direction`, `filter-regex`, `block-types`, `create-mappings`, `relative-directory`, `file-format`, `include-skipped`, `output-path`, `confirm` | Synchronizes selected PLC blocks with a VCI workspace |
| `sync-vci-plc` | VCI | `workspace-name`, `plc-name`, `direction`, `scope`, `create-mappings`, `relative-directory`, `file-format`, `include-skipped`, `output-path`, `confirm` | Synchronizes supported objects of a PLC with a VCI workspace |
| `validate-blueprint` | V21 | `blueprint-path` | Validates a declarative engineering blueprint against the V21 worker command contract. |
| `validate-plc-documents` | PLC | `import-directory`, `file-name` | Validates SIMATIC SD (.s7dcl/.s7res) files offline before PLC import |
| `validate-safety-configuration` | Insights |  | Validates official SafetyAdministration availability and offline login state for the selected PLC. |
| `version` | V21 |  | Reports toolkit, IPC, worker, runtime, and target TIA versions. |

## Infrastructure tools

| Tool | Description |
|---|---|
| `operation-status` | Reports the active serialized operation and latest progress. |
| `cancel-operation` | Requests cooperative cancellation of the active operation. |
| `reset-session` | Waits for quiescence, then disposes the TIA attachment; fails closed when a vendor call remains active. |

MCP path parameters are restricted by `OPEN_TIA_MCP_ALLOWED_ROOTS`, input size by `OPEN_TIA_MCP_MAX_INPUT_FILE_BYTES`, and explicit output overwrite by `OPEN_TIA_MCP_ALLOW_FILE_OVERWRITE`.
