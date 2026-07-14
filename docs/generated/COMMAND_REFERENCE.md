# Generated command reference

Generated from the runtime `CommandSchema` catalog. Do not edit by hand; run `scripts/Generate-CommandReference.ps1`.

Commands: **181**

## Devices

### get-device-info

Gets detailed information about a device

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `device-name` | String | yes |  | The name of the device |

### get-device-item-info

Gets detailed information about a device item

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `device-name` | String | yes |  | The name of the parent device |
| `item-name` | String | yes |  | The name of the device item |

## Hardware

### apply-hardware-patch

Dry-runs or applies transactional generic TIA hardware operations

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `operations` | Object | no |  | Operations array or { operations: [...] } object |
| `operations-path` | Path | no |  | JSON file containing operations; CLI-friendly alternative to operations |
| `dry-run` | Boolean | no | True | Validate operations in a rolled-back transaction without applying |
| `confirm-destructive` | Boolean | no | False | Permit delete and disconnect operations |
| `transaction-description` | String | no |  | Undo unit description for TIA Portal |
| `plc-name` | String | no |  | Optional PLC device name used with safety-password-env |
| `safety-password-env` | String | no | OPENTIA_SAFETY_PASSWORD | Environment variable containing the optional safety password; raw passwords are never accepted |
| `confirm` | Boolean | no | False | Explicitly authorizes this state-changing command after review of its target and impact. |

### audit-hardware-tag-mappings

Audits PLC I/Q tag addresses against discovered hardware IO ranges and optional catalog evidence

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `plc-name` | String | yes |  | PLC device name whose tag tables should be scanned |
| `tag-table-filter` | String | no |  | Optional tag table name/text filter |
| `catalog-search-text` | String | no |  | Optional hardware catalog search text such as Promag |
| `max-unmatched-tags` | Integer | no | 100 | Maximum unmatched tag entries to include; use -1 for all |
| `output-path` | Path | no |  | Optional CSV file or folder path for unmatched PLC tag rows |

### can-plug-hardware-item

Checks whether a hardware module or submodule can be plugged at a target position

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `target` | Object | no |  | Parent hardware target as a structured hardware target |
| `device-name` | String | no |  | Parent hardware target device name; CLI-friendly alternative to target |
| `item-path` | StringArray | no |  | Parent hardware target nested item path; use with device-name |
| `expected-type-identifier` | String | no |  | Optional expected target type identifier guard |
| `type-identifier` | String | yes |  | TIA hardware type identifier |
| `item-name` | String | yes |  | Name for the new hardware item |
| `position-number` | Integer | yes |  | Target slot or subslot position |

### find-hardware-catalog-items

Searches installed TIA Portal hardware catalog items by text

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `search-text` | String | no |  | Catalog search text; blank enumerates the available catalog |
| `culture` | String | no |  | Optional catalog language such as en-US |

### find-hardware-module-types

Finds module type identifiers using the project-level TIA module information provider

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `partial-type-identifier` | String | yes |  | Partial type identifier search value |

### get-gsd-parameter-data

Reads raw parameter data bytes from a GSD or GSDML hardware item

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `target` | Object | no |  | GSD device item target as a structured hardware target |
| `device-name` | String | no |  | GSD device item target device name; CLI-friendly alternative to target |
| `item-path` | StringArray | no |  | GSD device item target nested item path; use with device-name |
| `expected-type-identifier` | String | no |  | Optional expected target type identifier guard |
| `dataset-number` | Integer | yes |  | Parameter dataset number |
| `byte-offset` | Integer | no | 0 | Starting byte offset |
| `length` | Integer | yes |  | Number of bytes to read |

### get-hardware-drive-parameters

Reads bounded Startdrive parameter metadata, values, units, ranges, and enumerations through the official V21 API.

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `device-name` | String | no |  | Optional drive device name filter |
| `drive-object-index` | Integer | no |  | Optional zero-based drive object index |
| `filter` | String | no |  | Optional parameter name, number, or text filter |
| `max-results` | Integer | no | 500 | Maximum returned parameters (1-5000) |

### get-hardware-drive-telegrams

Lists Startdrive telegrams and their IO addresses exposed by hardware drive items

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `device-name` | String | no |  | Optional drive device name filter |
| `address-attributes` | StringArray | no |  | Optional named telegram address attributes to read |
| `output-path` | Path | no |  | Optional CSV file or folder path for drive telegram rows |

### get-hardware-feature-attributes

Reads engineering attributes from a hardware feature service such as NetworkInterface

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `target` | Object | no |  | Hardware target as a structured hardware target |
| `device-name` | String | no |  | Hardware target device name; CLI-friendly alternative to target |
| `item-path` | StringArray | no |  | Hardware target nested item path; use with device-name |
| `expected-type-identifier` | String | no |  | Optional expected target type identifier guard |
| `feature-service` | String | yes |  | Hardware feature service name, such as NetworkInterface |
| `attributes` | StringArray | no |  | Optional named feature attributes to read |

### get-hardware-io-addresses

Lists process-image address objects exposed by hardware device items

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `device-name` | String | no |  | Optional device name filter |
| `attributes` | StringArray | no |  | Optional named address attributes to read |
| `output-path` | Path | no |  | Optional CSV file or folder path for hardware IO address rows |

### get-hardware-item-capabilities

Gets available hardware feature services and attributes for one hardware target

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `target` | Object | no |  | Hardware target as a structured hardware target |
| `device-name` | String | no |  | Hardware target device name; CLI-friendly alternative to target |
| `item-path` | StringArray | no |  | Hardware target nested item path; use with device-name |
| `expected-type-identifier` | String | no |  | Optional expected target type identifier guard |
| `attributes` | StringArray | no |  | Optional named attributes to read |

### get-hardware-network-item-attributes

Reads engineering attributes from a NetworkInterface child such as a node or IO connector

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `target` | Object | no |  | Network interface hardware target as a structured hardware target |
| `device-name` | String | no |  | Network interface hardware target device name; CLI-friendly alternative to target |
| `item-path` | StringArray | no |  | Network interface hardware target nested item path; use with device-name |
| `expected-type-identifier` | String | no |  | Optional expected target type identifier guard |
| `network-collection` | String | yes |  | NetworkInterface collection name: Nodes, IoControllers, or IoConnectors |
| `network-item-index` | Integer | no | 0 | Zero-based item index in the network collection |
| `attributes` | StringArray | no |  | Optional named network item attributes to read |

### get-hardware-networks

Lists hardware network interfaces, nodes, IO controllers and IO connectors

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `device-name` | String | no |  | Optional device name filter |
| `output-path` | Path | no |  | Optional JSON file or folder path for hardware network interfaces |

### get-hardware-tree

Gets nested hardware devices and device items with reusable hardware target paths

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `device-name` | String | no |  | Optional device name filter |
| `output-path` | Path | no |  | Optional JSON file or folder path for the hardware tree |

## Insights

### get-multiuser-status

Reports project/local-session multiuser state and ownership through the official V21 API.

No parameters.

### get-plc-alarm-text-list-details

Reads PLC alarm system/user text-list metadata through the official V21 API.

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `max-results` | Integer | no | 500 | Maximum returned rows (1-5000) |

### get-technology-object-details

Reads PLC technology object metadata and parameters through the official V21 API.

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `max-results` | Integer | no | 500 | Maximum returned rows (1-5000) |

### get-unified-runtime-settings

Reads scalar Unified HMI runtime settings through the official V21 API.

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `max-results` | Integer | no | 500 | Maximum returned rows (1-5000) |

### get-watch-table-details

Reads PLC watch/force tables and their entry metadata through the official V21 API.

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `max-results` | Integer | no | 500 | Maximum returned rows (1-5000) |

### inspect-project-security

Inspects project security metadata and certificate/password/syslog service availability through V21 Openness.

No parameters.

### list-unified-alarms

Lists Unified HMI alarm classes, analog/discrete alarms, and OPC UA alarm types through the official V21 API.

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `max-results` | Integer | no | 500 | Maximum returned rows (1-5000) |
| `page-size` | Integer | no |  | Optional maximum number of rows returned in this page (1-1000). |
| `continuation-token` | String | no |  | Opaque continuation token returned by a previous page. |

### list-unified-text-lists

Lists Unified HMI user, system, and graphic text lists through the official V21 API.

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `max-results` | Integer | no | 500 | Maximum returned rows (1-5000) |
| `page-size` | Integer | no |  | Optional maximum number of rows returned in this page (1-1000). |
| `continuation-token` | String | no |  | Opaque continuation token returned by a previous page. |

### validate-safety-configuration

Validates official SafetyAdministration availability and offline login state for the selected PLC.

No parameters.

## Libraries

### check-library-updates

Reports project instances that are outdated against the project library or an open global library

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `library-scope` | String | no | project | Library scope: project or global |
| `global-library` | String | no |  | Open global library name or path when library-scope=global |
| `include-up-to-date` | Boolean | no | False | Include up-to-date instances in the report |
| `output-path` | Path | no |  | Optional JSON file or folder path for update messages |

### close-global-library

Closes an open user global library by name or path

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `global-library` | String | yes |  | Open global library name or path |

### copy-from-master-copy

Copies a library master copy into a PLC block group with dry-run and confirmation gates

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `library-scope` | String | no | project | Library scope: project or global |
| `global-library` | String | no |  | Open global library name or path when library-scope=global |
| `master-copy-path` | String | yes |  | Master copy path inside MasterCopyFolder |
| `plc-name` | String | yes |  | Target PLC device name |
| `target-group-path` | String | no |  | Optional target PLC block group path |
| `dry-run` | Boolean | no | True | Validate and return the planned copy without applying |
| `confirm` | Boolean | no | False | Explicitly confirm a non-dry-run copy |

### get-library-type-versions

Lists versions for a library type

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `library-scope` | String | no | project | Library scope: project or global |
| `global-library` | String | no |  | Open global library name or path when library-scope=global |
| `type-path` | String | yes |  | Library type path inside TypeFolder |
| `output-path` | Path | no |  | Optional JSON file or folder path for version rows |

### list-global-libraries

Lists known and open TIA Portal global libraries

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `output-path` | Path | no |  | Optional JSON file or folder path for library rows |
| `page-size` | Integer | no |  | Optional maximum number of rows returned in this page (1-1000). |
| `continuation-token` | String | no |  | Opaque continuation token returned by a previous page. |

### list-library-types

Lists library types in the project library or an open global library

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `library-scope` | String | no | project | Library scope: project or global |
| `global-library` | String | no |  | Open global library name or path when library-scope=global |
| `folder-path` | String | no |  | Optional folder path inside TypeFolder |
| `recursive` | Boolean | no | True | List nested folders recursively |
| `output-path` | Path | no |  | Optional JSON file or folder path for library type rows |
| `page-size` | Integer | no |  | Optional maximum number of rows returned in this page (1-1000). |
| `continuation-token` | String | no |  | Opaque continuation token returned by a previous page. |

### list-master-copies

Lists master copies in the project library or an open global library

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `library-scope` | String | no | project | Library scope: project or global |
| `global-library` | String | no |  | Open global library name or path when library-scope=global |
| `folder-path` | String | no |  | Optional folder path inside MasterCopyFolder |
| `recursive` | Boolean | no | True | List nested folders recursively |
| `output-path` | Path | no |  | Optional JSON file or folder path for master copy rows |
| `page-size` | Integer | no |  | Optional maximum number of rows returned in this page (1-1000). |
| `continuation-token` | String | no |  | Opaque continuation token returned by a previous page. |

### open-global-library

Opens a TIA Portal global library by file path

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `path` | Path | yes |  | Global library file path |
| `read-only` | Boolean | no | True | Open the library read-only |
| `upgrade` | Boolean | no | False | Open with upgrade |
| `confirm` | Boolean | no | False | Explicitly authorizes this state-changing command after review of its target and impact. |

## Multiuser

### add-project-to-multiuser-server

Adds a closed single-user TIA project to a configured Project Server.

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `server-alias` | String | yes |  | Connection alias |
| `project-path` | Path | yes |  | Closed .ap project file |
| `confirm` | Boolean | no | False | Explicitly authorizes this state-changing command after review of its target and impact. |

### commit-multiuser-session

Saves and checks in the selected local session, returning the created server revision.

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `comment` | String | yes |  | Server revision comment |
| `require-up-to-date` | Boolean | no | True | Block commit when the session is stale |
| `require-no-conflicts` | Boolean | no | True | Block commit when conflicted markings exist |
| `confirm` | Boolean | no | False | Explicitly authorizes this state-changing command after review of its target and impact. |

### create-multiuser-session

Creates an agent-owned Multiuser or Exclusive local session.

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `server-alias` | String | yes |  | Connection alias |
| `project-name` | String | yes |  | Server project name |
| `session-name` | String | yes |  | Unique local session name |
| `session-directory` | Path | yes |  | Directory for the local session |
| `mode` | String | no | Multiuser | Multiuser or Exclusive |
| `umac-user` | String | no |  | Optional UMAC user name |
| `umac-password-env` | String | no | OPENTIA_UMAC_PASSWORD | Environment variable containing the UMAC password (default: OPENTIA_UMAC_PASSWORD) |
| `umac-user-type` | String | no | project | UMAC user type: project or global |
| `confirm` | Boolean | no | False | Explicitly authorizes this state-changing command after review of its target and impact. |

### ensure-multiuser-server

Creates or updates a TIA Project Server connection.

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `server-alias` | String | yes |  | Connection alias |
| `protocol` | String | no | Https | Http or Https |
| `host` | String | yes |  | Project Server host |
| `port` | Integer | yes |  | Project Server port |
| `confirm` | Boolean | no | False | Explicitly authorizes this state-changing command after review of its target and impact. |

### get-multiuser-session-status

Reports up-to-date state, markings, and conflicts for the selected local session.

No parameters.

### list-multiuser-projects

Lists projects available on a configured TIA Project Server.

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `server-alias` | String | yes |  | Connection alias |
| `page-size` | Integer | no |  | Optional maximum number of rows returned in this page (1-1000). |
| `continuation-token` | String | no |  | Opaque continuation token returned by a previous page. |

### list-multiuser-servers

Lists configured TIA Project Server connections.

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `page-size` | Integer | no |  | Optional maximum number of rows returned in this page (1-1000). |
| `continuation-token` | String | no |  | Opaque continuation token returned by a previous page. |

### list-multiuser-sessions

Lists local sessions registered for a server project.

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `server-alias` | String | yes |  | Connection alias |
| `project-name` | String | yes |  | Server project name |
| `page-size` | Integer | no |  | Optional maximum number of rows returned in this page (1-1000). |
| `continuation-token` | String | no |  | Opaque continuation token returned by a previous page. |

### save-multiuser-session

Saves pending edits in the selected local session without checking in.

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `confirm` | Boolean | no | False | Explicitly authorizes this state-changing command after review of its target and impact. |

## PLC

### add-delete-plc-type-transaction-operation

Adds an exact PLC type deletion with XML snapshot and rollback proof to a persistent PLC code transaction

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `transaction-id` | String | yes |  | Draft PLC code transaction id |
| `type-name` | String | yes |  | Exact PLC type name |
| `group-path` | String | no |  | Exact PLC type group path |
| `depends-on` | String | no |  | Optional comma-separated operation sequences that must apply first |
| `confirm` | Boolean | yes |  | Confirm adding the reviewed deletion operation |

### add-plc-code-transaction-operation

Adds a create-only or baseline-bound SIMATIC SD block change to a draft PLC code transaction

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `transaction-id` | String | yes |  | Draft transaction id |
| `import-directory` | Path | yes |  | Directory containing the SIMATIC SD files |
| `file-name` | String | yes |  | Block/file name without extension |
| `group-path` | String | no |  | Exact target block group |
| `import-option` | String | no | Override | Siemens document import option |
| `target-mode` | String | no | create-only | create-only (default) or baseline-bound |
| `baseline-directory` | Path | no |  | Directory containing the untouched exported S7DCL/S7RES pair; required for baseline-bound |
| `baseline-file-name` | String | no |  | Untouched baseline file name without extension; defaults to file-name |
| `allowed-changes` | String | no |  | Comma-separated authorized sections: Attributes, Interface, Implementation, Resources |
| `depends-on` | String | no |  | Optional comma-separated operation sequences that must apply first |
| `confirm` | Boolean | yes |  | Confirm adding this local manifest operation |

### add-scl-source-transaction-operation

Adds validated SCL source import and exact generated-block replacement to a persistent PLC code transaction

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `transaction-id` | String | yes |  | Draft PLC code transaction id |
| `source-path` | Path | yes |  | UTF-8 SCL source file |
| `group-path` | String | no |  | Exact target block group for every declared block |
| `type-group-path` | String | no |  | Exact target PLC type group for every declared TYPE |
| `depends-on` | String | no |  | Optional comma-separated operation sequences that must apply first |
| `confirm` | Boolean | yes |  | Confirm adding the reviewed source operation |

### add-simaticml-transaction-operation

Adds a baseline-bound SimaticML block or PLC type patch to a persistent PLC code transaction

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `transaction-id` | String | yes |  | Draft PLC code transaction id |
| `input-path` | Path | yes |  | Patched SimaticML block or PLC type XML |
| `baseline-path` | Path | yes |  | Untouched SimaticML export of the exact target |
| `group-path` | String | no |  | Exact target block or PLC type group |
| `allow-flgnet-topology` | Boolean | no | False | Allow an explicitly reviewed FlgNet topology change |
| `depends-on` | String | no |  | Optional comma-separated operation sequences that must apply first |
| `confirm` | Boolean | yes |  | Confirm adding the reviewed XML patch operation |

### apply-block-document-import

Applies an exact-target SIMATIC SD plan and hash-verifies rollback after verification or compile failure

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `apply-token` | String | yes |  | One-use token returned by plan-block-document-import |
| `confirm` | Boolean | yes |  | Confirm the reviewed import apply |

### apply-plc-code-transaction

Atomically preflights, applies, compiles, verifies, or fully rolls back a planned PLC code transaction

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `transaction-id` | String | yes |  | Planned transaction id |
| `apply-token` | String | yes |  | One-use token returned by transaction planning |
| `confirm` | Boolean | yes |  | Confirm the complete reviewed transaction |

### approve-plc-code-impact

Revalidates and explicitly acknowledges a fingerprint-bound ReviewRequired or Unknown PLC code impact before issuing an apply token

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `transaction-id` | String | yes |  | ImpactReviewRequired PLC code transaction id |
| `impact-fingerprint` | String | yes |  | Exact fingerprint returned by transaction planning |
| `accept-unknown` | Boolean | no | False | Explicitly accept unresolved impact evidence after review |
| `confirm` | Boolean | yes |  | Confirm the reviewed impact acknowledgement |

### begin-plc-code-transaction

Begins a persistent PLC code transaction bound to the current TIA process, project, and PLC

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `plc-name` | String | yes |  | Exact PLC device name |
| `description` | String | no |  | Human-readable engineering intent |
| `compile-policy` | String | no | NoNewErrors | NoNewErrors (default) or CleanCompile |
| `maximum-new-warnings` | Integer | no |  | Optional maximum warning increase accepted by final compile |
| `capability-id` | String | no |  | One live capability discovery id to bind and revalidate at plan/apply |
| `capability-ids` | String | no |  | Comma-separated capability ids for an atomic multi-target transaction |

### compare-software

Compares PLC software to the online target

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `plc-name` | String | yes |  | The name of the PLC device |
| `target` | String | no | online | Compare target. Currently supported: online |
| `output-path` | Path | no |  | Optional JSON file or folder path for compare results |

### compile-software

Compiles the PLC software

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `plc-name` | String | yes |  | The name of the PLC device |
| `password` | String | no |  | Optional password for protected blocks |

### create-block

Creates a new block in PLC software (currently validated for ProDiag FB creation)

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `plc-name` | String | yes |  | The name of the PLC device |
| `block-name` | String | yes |  | The name of the block to create |
| `block-type` | String | yes |  | The type of block (currently validated: FB) |
| `language` | String | no |  | The programming language (currently validated: ProDiag) |
| `confirm` | Boolean | no | False | Explicitly authorizes this state-changing command after review of its target and impact. |

### delete-blocks

Deletes blocks matching a regex pattern from PLC software

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `plc-name` | String | yes |  | The name of the PLC device |
| `filter` | String | yes |  | Regex pattern for block names to delete |
| `confirm` | Boolean | no |  | Confirm deletion without prompting (default: false) |

### delete-external-source

Deletes one PLC external source after explicit confirmation

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `plc-name` | String | yes |  | The name of the PLC device |
| `source-name` | String | yes |  | Exact external source name |
| `confirm` | Boolean | yes |  | Confirm external source deletion |

### describe-plc-code-formats

Describes format-specific PLC authoring support, mutation routes, verification requirements, and limitations

No parameters.

### discard-plc-code-transaction

Deletes a terminal or never-applied PLC code transaction and its retained local artifacts

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `transaction-id` | String | yes |  | Transaction id |
| `confirm` | Boolean | yes |  | Confirm local transaction artifact deletion |

### discover-plc-code-capabilities

Discovers and fingerprints the live TIA, PLC, target, safety, VCI, and import/export evidence before PLC code generation

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `plc-name` | String | yes |  | Exact PLC device name |
| `object-kind` | String | no | Block | Block or Type |
| `object-name` | String | yes |  | Exact existing or intended object name |
| `group-path` | String | no |  | Exact PLC block or type group |
| `operation` | String | no | Modify | Modify or Create |
| `block-type` | String | no |  | FB, FC, OB, DB, GlobalDB, InstanceDB, or TYPE when known |
| `requested-language` | String | no |  | Requested code language such as LAD, FBD, or SCL |
| `requested-format` | String | no |  | Intended artifact format such as SCL, S7DCL, SimaticML, or VCI |
| `prefer-source-control` | Boolean | no | False | Rank an observed exact VCI mapping first |

### discover-plc-simulator-capabilities

Discovers the installed PLCSIM Advanced Runtime API and selects a powered local simulator by exact controller or instance identity

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `plc-name` | String | yes |  | Exact TIA PLC device name |
| `instance-name` | String | no |  | Optional exact PLCSIM Advanced instance name |
| `refresh-tags` | Boolean | no | False | Refresh and count the downloaded runtime tag list |

### download-software

Downloads PLC software to PLCSIM Advanced with dry-run and confirmation gates

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `plc-name` | String | yes |  | The name of the PLC device |
| `to-simulator` | Boolean | yes | False | Restrict download to PLCSIM Advanced |
| `dry-run` | Boolean | no | True | Validate and return the planned download without applying |
| `only-changes` | Boolean | no | False | Download only software changes instead of the full software load |
| `confirm` | Boolean | no | False | Explicitly confirm a non-dry-run download |

### export-block

Exports a single PLC block to XML

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `plc-name` | String | yes |  | The name of the PLC device |
| `block-name` | String | yes |  | The name of the block to export |
| `export-path` | String | yes |  | The full file path where the XML will be saved |

### export-block-as-docs

Exports a single block as SIMATIC SD documents (.s7dcl + .s7res)

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `plc-name` | String | yes |  | The name of the PLC device |
| `block-name` | String | yes |  | The name of the block to export |
| `export-path` | String | yes |  | The directory path where files will be saved |

### export-block-as-source

Exports a PLC block as an external source file when supported

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `plc-name` | String | yes |  | The name of the PLC device |
| `block-name` | String | yes |  | Name of the PLC block to export |
| `output-path` | Path | yes |  | Output source file or folder path |

### export-blocks-as-docs

Exports multiple blocks as SIMATIC SD documents (.s7dcl + .s7res)

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `plc-name` | String | yes |  | The name of the PLC device |
| `export-path` | String | yes |  | The directory path where files will be saved |
| `filter-regex` | String | no |  | Optional regex filter for block names (e.g., "Motor_.*") |

### export-plc

Exports all PLC blocks to XML files

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `plc-name` | String | yes |  | The name of the PLC device |
| `export-path` | Path | yes |  | The directory path for XML export |

### export-tags

Exports PLC tags from all tag tables

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `plc-name` | String | yes |  | The name of the PLC device |
| `culture` | String | no |  | Project language culture used for tag comments (default: en-US) |
| `output-path` | Path | no |  | Optional CSV file or folder path for exported PLC tags |

### export-type

Exports a single type (UDT) to XML

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `plc-name` | String | yes |  | The name of the PLC device |
| `type-name` | String | yes |  | The name of the type to export |
| `export-path` | String | yes |  | The full file path where the XML will be saved |

### export-types

Exports multiple types (UDT) to XML

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `plc-name` | String | yes |  | The name of the PLC device |
| `export-path` | String | yes |  | The directory path where XML files will be saved |
| `filter-regex` | String | no |  | Optional regex filter for type names (e.g., "Motor_.*") |

### find-replace-db-members

Finds and replaces GlobalDB member names with nested struct support

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `plc-name` | String | yes |  | The name of the PLC device |
| `rename-map` | Object | yes |  | Dictionary mapping old member names to new names (JSON string or Dictionary) |
| `case-insensitive` | Boolean | no |  | Enable case-insensitive matching (default: false) |
| `dry-run` | Boolean | no |  | Preview changes without applying (default: true) |
| `target-blocks` | StringArray | no |  | Optional list of specific GlobalDB names (null = all) |
| `output-path` | Path | no |  | Optional CSV file or folder path for find/replace result rows |
| `confirm` | Boolean | no | False | Explicitly authorizes this state-changing command after review of its target and impact. |

### find-replace-tags

Finds and replaces PLC tag names across all tag tables

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `plc-name` | String | yes |  | The name of the PLC device |
| `rename-map` | Object | yes |  | Dictionary mapping old tag names to new names (JSON string or Dictionary) |
| `case-insensitive` | Boolean | no |  | Enable case-insensitive matching (default: false) |
| `dry-run` | Boolean | no |  | Preview changes without applying (default: true) |
| `substring-match` | Boolean | no |  | Enable substring matching instead of exact matching (default: false) |
| `output-path` | Path | no |  | Optional CSV file or folder path for find/replace result rows |
| `confirm` | Boolean | no | False | Explicitly authorizes this state-changing command after review of its target and impact. |

### find-replace-udt-members

Finds and replaces UDT member names with nested struct support

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `plc-name` | String | yes |  | The name of the PLC device |
| `rename-map` | Object | yes |  | Dictionary mapping old member names to new names (JSON string or Dictionary) |
| `case-insensitive` | Boolean | no |  | Enable case-insensitive matching (default: false) |
| `dry-run` | Boolean | no |  | Preview changes without applying (default: true) |
| `target-types` | StringArray | no |  | Optional list of specific UDT names (null = all) |
| `output-path` | Path | no |  | Optional CSV file or folder path for find/replace result rows |
| `confirm` | Boolean | no | False | Explicitly authorizes this state-changing command after review of its target and impact. |

### generate-blocks-from-source

Generates PLC blocks from an external source when supported

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `plc-name` | String | yes |  | The name of the PLC device |
| `source-name` | String | yes |  | Name of the imported source file |
| `dry-run` | Boolean | no |  | Preview without mutating the PLC project (default: true) |
| `confirm` | Boolean | no |  | Required for non-dry-run generation |

### get-block-info

Gets detailed information about a specific PLC block

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `plc-name` | String | yes |  | The name of the PLC device |
| `block-name` | String | yes |  | The name of the block |

### get-blocks

Lists blocks from PLC software

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `plc-name` | String | yes |  | The name of the PLC device |
| `filter` | String | no |  | Optional regex filter for block names |
| `output-path` | Path | no |  | Optional CSV file or folder path for PLC block rows |
| `page-size` | Integer | no |  | Optional maximum number of rows returned in this page (1-1000). |
| `continuation-token` | String | no |  | Opaque continuation token returned by a previous page. |

### get-cross-references

Gets PLC cross references for a tag, block, or named PLC object

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `plc-name` | String | yes |  | The name of the PLC device |
| `name` | String | yes |  | Tag, block, or PLC object name to look up |
| `output-path` | Path | no |  | Optional CSV file or folder path for cross-reference rows |

### get-online-status

Gets PLC online status, optionally going online first

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `plc-name` | String | yes |  | The name of the PLC device |
| `go-online` | Boolean | no | True | Attempt to go online before reading status |

### get-plc-code-evidence

Gets one persistent PLC code qualification evidence manifest

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `evidence-id` | String | yes |  | Evidence id |

### get-plc-code-impact

Gets the semantic delta, resolved dependencies, live PLC consumers, policy status, and fingerprint for a planned PLC code transaction

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `transaction-id` | String | yes |  | Planned or impact-review PLC code transaction id |

### get-plc-code-transaction

Gets the persistent manifest, journal, hashes, compile evidence, and recovery state for one PLC code transaction

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `transaction-id` | String | yes |  | Transaction id |

### get-software-tree

Gets the hierarchical PLC software structure

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `plc-name` | String | yes |  | The name of the PLC device |
| `depth` | Integer | no |  | Maximum depth to traverse (null for unlimited) |
| `output-path` | Path | no |  | Optional JSON file or folder path for the PLC software tree |

### import-block

Imports a single block from XML file

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `plc-name` | String | yes |  | The name of the PLC device |
| `import-path` | String | yes |  | Full path to the XML file to import |
| `group-path` | String | no |  | Optional path within PLC program (e.g., 'Program Blocks/Motors') |
| `confirm` | Boolean | no | False | Explicitly authorizes this state-changing command after review of its target and impact. |

### import-block-from-docs

Imports a single block from SIMATIC SD documents (.s7dcl/.s7res)

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `plc-name` | String | yes |  | The name of the PLC device |
| `import-directory` | String | yes |  | Directory containing .s7dcl and .s7res files |
| `file-name` | String | yes |  | Block name without extension (e.g., 'MotorControl') |
| `group-path` | String | no |  | Optional path within PLC program |
| `import-option` | String | no |  | Import behavior: Override, None, SkipInactiveCultures, ActivateInactiveCultures (default: Override) |
| `confirm` | Boolean | no | False | Explicitly authorizes this state-changing command after review of its target and impact. |

### import-blocks

Imports multiple blocks from XML files in a directory

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `plc-name` | String | yes |  | The name of the PLC device |
| `import-directory` | String | yes |  | Directory containing XML files to import |
| `filter-regex` | String | no |  | Optional regex filter for file names |
| `group-path` | String | no |  | Optional path within PLC program |
| `confirm` | Boolean | no | False | Explicitly authorizes this state-changing command after review of its target and impact. |

### import-blocks-from-docs

Imports multiple blocks from SIMATIC SD documents (.s7dcl/.s7res)

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `plc-name` | String | yes |  | The name of the PLC device |
| `import-directory` | String | yes |  | Directory containing .s7dcl and .s7res files |
| `filter-regex` | String | no |  | Optional regex filter for block names |
| `group-path` | String | no |  | Optional path within PLC program |
| `import-option` | String | no |  | Import behavior: Override, None, SkipInactiveCultures, ActivateInactiveCultures (default: Override) |
| `confirm` | Boolean | no | False | Explicitly authorizes this state-changing command after review of its target and impact. |

### import-source

Imports a PLC external source file when supported

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `plc-name` | String | yes |  | The name of the PLC device |
| `source-path` | Path | yes |  | Path to a .scl or .awl source file |
| `dry-run` | Boolean | no |  | Preview without mutating the PLC project (default: true) |
| `confirm` | Boolean | no |  | Required for non-dry-run imports |

### import-type

Imports a single type (UDT) from XML file

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `plc-name` | String | yes |  | The name of the PLC device |
| `import-path` | String | yes |  | Full path to the XML file to import |
| `group-path` | String | no |  | Optional path within type groups (e.g., 'Types/Subgroups') |
| `confirm` | Boolean | no | False | Explicitly authorizes this state-changing command after review of its target and impact. |

### import-types

Imports multiple types (UDT) from XML files in a directory

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `plc-name` | String | yes |  | The name of the PLC device |
| `import-directory` | String | yes |  | Directory containing XML files to import |
| `filter-regex` | String | no |  | Optional regex filter for file names |
| `group-path` | String | no |  | Optional path within type groups |
| `confirm` | Boolean | no | False | Explicitly authorizes this state-changing command after review of its target and impact. |

### list-external-sources

PLC external sources

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `plc-name` | String | yes |  | The name of the PLC device |
| `output-path` | Path | no |  | Optional CSV file or folder path for rows |
| `page-size` | Integer | no |  | Optional maximum number of rows returned in this page (1-1000). |
| `continuation-token` | String | no |  | Opaque continuation token returned by a previous page. |

### list-plc-alarm-text-lists

PLC alarm text lists

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `plc-name` | String | yes |  | The name of the PLC device |
| `output-path` | Path | no |  | Optional CSV file or folder path for rows |
| `page-size` | Integer | no |  | Optional maximum number of rows returned in this page (1-1000). |
| `continuation-token` | String | no |  | Opaque continuation token returned by a previous page. |

### list-plc-code-evidence

Lists versioned PLC code evidence, including failed and cleanup-quarantined probes

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `status` | String | no |  | Optional Running, Verified, Failed, or Quarantined filter |
| `code-format` | String | no |  | Optional exact code-format filter |
| `page-size` | Integer | no |  | Optional maximum number of rows returned in this page (1-1000). |
| `continuation-token` | String | no |  | Opaque continuation token returned by a previous page. |

### list-plc-code-transactions

Lists persistent PLC code transactions, including interrupted and recovery-required changes

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `page-size` | Integer | no |  | Optional maximum number of rows returned in this page (1-1000). |
| `continuation-token` | String | no |  | Opaque continuation token returned by a previous page. |

### list-plcs

Lists all PLC devices found in the project

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `page-size` | Integer | no |  | Optional maximum number of rows returned in this page (1-1000). |
| `continuation-token` | String | no |  | Opaque continuation token returned by a previous page. |

### list-technology-objects

PLC technology objects

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `plc-name` | String | yes |  | The name of the PLC device |
| `output-path` | Path | no |  | Optional CSV file or folder path for rows |
| `page-size` | Integer | no |  | Optional maximum number of rows returned in this page (1-1000). |
| `continuation-token` | String | no |  | Opaque continuation token returned by a previous page. |

### list-watch-tables

PLC watch and force tables

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `plc-name` | String | yes |  | The name of the PLC device |
| `output-path` | Path | no |  | Optional CSV file or folder path for rows |
| `page-size` | Integer | no |  | Optional maximum number of rows returned in this page (1-1000). |
| `continuation-token` | String | no |  | Opaque continuation token returned by a previous page. |

### modify-tags

Plans or applies explicit PLC tag create, update, delete, and replace operations

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `plc-name` | String | yes |  | The name of the PLC device |
| `operations` | ObjectArray | no |  | Inline operation array with action, optional match, and set objects |
| `input-path` | Path | no |  | Optional .json or .csv operation file. Use either operations or input-path |
| `dry-run` | Boolean | no | True | Validate and preview operations without applying them |
| `confirm` | Boolean | no | False | Confirm delete and replace operations when dry-run is false |
| `safety-password-env` | String | no | OPENTIA_SAFETY_PASSWORD | Environment variable containing the optional safety password; raw passwords are never accepted |
| `output-path` | Path | no |  | Optional JSON file or folder path for operation results |

### open-plc

Opens a PLC device software by name

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `plc-name` | String | yes |  | The name of the PLC device |

### plan-block-document-import

Binds an exact PLC block identity and creates a state-bound, rollback-capable SIMATIC SD apply plan

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `plc-name` | String | yes |  | PLC device name |
| `import-directory` | Path | yes |  | Directory containing the SIMATIC SD files |
| `file-name` | String | yes |  | Block/file name without extension |
| `group-path` | String | no |  | Optional target block group |
| `import-option` | String | no | Override | Override, None, SkipInactiveCultures, or ActivateInactiveCultures |

### plan-plc-code-transaction

Snapshots every existing target and produces one dependency-ordered, atomic, state-bound PLC code transaction plan

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `transaction-id` | String | yes |  | Draft transaction id |

### query-plc-behavior-evidence

Finds behavior evidence matching the exact code signature, TIA/PLC environment, vector hash, and simulator fingerprint

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `plc-name` | String | yes |  | Exact PLC device name |
| `capability-ids` | String | yes |  | Comma-separated current Create capability id for every declared probe object |
| `package-path` | Path | yes |  | Versioned behavior probe package manifest listing all disposable code, contract DB, and scheduler artifacts |
| `vector-path` | Path | yes |  | Versioned behavior vector JSON |
| `instance-name` | String | no |  | Optional exact PLCSIM Advanced instance name |

### query-plc-code-evidence

Finds verified evidence matching the current exact TIA/PLC/route compatibility fingerprint and normalized code construct signature

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `capability-ids` | String | yes |  | Comma-separated current capability ids for every declared object |
| `code-format` | String | no | auto | auto, scl, or s7dcl |
| `input-path` | Path | yes |  | SCL or S7DCL artifact to qualify |

### recover-plc-code-behavior-probe

Recovers an interrupted behavior probe, removes its disposable objects offline and from PLCSIM, and never promotes its behavior evidence

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `evidence-id` | String | yes |  | Behavior evidence id in Running or Quarantined state |
| `only-changes` | Boolean | no | True | Use a software-changes cleanup download |
| `confirm` | Boolean | yes |  | Confirm mandatory offline and simulator cleanup |

### recover-plc-code-transaction

Conservatively restores every baseline in an interrupted or unverified PLC code transaction

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `transaction-id` | String | yes |  | Interrupted or recovery-required transaction id |
| `confirm` | Boolean | yes |  | Confirm restoration from the retained recovery package |

### run-plc-code-behavior-probe

Runs a disposable code probe through compile, simulator-only download, deterministic stimulation/observation, rollback, cleanup download, and online comparison

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `plc-name` | String | yes |  | Exact PLC device name |
| `capability-ids` | String | yes |  | Comma-separated current Create capability id for every declared probe object |
| `package-path` | Path | yes |  | Versioned behavior probe package manifest listing all disposable code, contract DB, and scheduler artifacts |
| `vector-path` | Path | yes |  | Versioned behavior vector JSON |
| `instance-name` | String | no |  | Optional exact PLCSIM Advanced instance name |
| `compile-policy` | String | no | NoNewErrors | NoNewErrors (default) or CleanCompile |
| `maximum-new-warnings` | Integer | no |  | Optional maximum warning increase |
| `accept-unknown-impact` | Boolean | no | False | Accept a reviewed Unknown impact fingerprint |
| `only-changes` | Boolean | no | True | Use software-changes downloads for probe and cleanup |
| `force` | Boolean | no | False | Run despite exact existing behavioral evidence |
| `confirm` | Boolean | yes |  | Confirm temporary project mutation, simulator download/stimulation, and mandatory cleanup |

### run-plc-code-probe

Runs an exact all-create SCL or S7DCL probe, verifies it, removes every probe artifact, and records versioned evidence

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `plc-name` | String | yes |  | Exact PLC device name |
| `capability-ids` | String | yes |  | Comma-separated Create capability id for every declared probe object |
| `code-format` | String | no | auto | auto, scl, or s7dcl |
| `input-path` | Path | yes |  | Disposable SCL or S7DCL probe artifact |
| `group-path` | String | no |  | Exact target block group |
| `type-group-path` | String | no |  | Exact target PLC type group for SCL TYPE declarations |
| `compile-policy` | String | no | NoNewErrors | NoNewErrors (default) or CleanCompile |
| `maximum-new-warnings` | Integer | no |  | Optional maximum warning increase accepted by the probe |
| `accept-unknown-impact` | Boolean | no | False | Explicitly accept reviewed unresolved impact in the disposable target |
| `force` | Boolean | no | False | Run again even when exact verified evidence already exists |
| `confirm` | Boolean | yes |  | Confirm temporary live mutation and mandatory cleanup |

### search-blocks

Searches PLC blocks by name/comment with optional type filter

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `plc-name` | String | yes |  | The name of the PLC device |
| `pattern` | String | yes |  | Search text pattern |
| `search-in` | String | no |  | Where to search: 'name' or 'comment' (default: 'name') |
| `filter-type` | String | no |  | Optional block type filter (e.g., 'FB', 'FC', 'DB', 'OB') |
| `output-path` | Path | no |  | Optional CSV file or folder path for matching PLC block rows |
| `page-size` | Integer | no |  | Optional maximum number of rows returned in this page (1-1000). |
| `continuation-token` | String | no |  | Opaque continuation token returned by a previous page. |

### start-cpu

Starts the PLC CPU when a supported online runtime operation is available

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `plc-name` | String | yes |  | The name of the PLC device |
| `confirm` | Boolean | no | False | Explicitly confirm CPU runtime state change |

### stop-cpu

Stops the PLC CPU when a supported online runtime operation is available

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `plc-name` | String | yes |  | The name of the PLC device |
| `confirm` | Boolean | no | False | Explicitly confirm CPU runtime state change |

### validate-plc-behavior-package

Validates a versioned multi-artifact disposable behavior package and its required contract DB and scheduler OB

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `package-path` | Path | yes |  | Behavior probe package JSON file |

### validate-plc-behavior-vector

Validates a versioned deterministic PLCSIM behavior vector without changing TIA or simulator state

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `vector-path` | Path | yes |  | Behavior vector JSON file |

### validate-plc-code-artifact

Validates SCL, S7DCL, or SimaticML with format-specific structural and semantic guardrails

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `code-format` | String | no | auto | auto, scl, s7dcl, or simaticml |
| `input-path` | Path | yes |  | PLC code artifact path |
| `baseline-path` | Path | no |  | Untouched exported SimaticML baseline for patch validation |
| `allow-flgnet-topology` | Boolean | no | False | Allow reviewed FlgNet topology changes to proceed as a live probe |

### validate-plc-documents

Validates SIMATIC SD structure, resources, and instruction forms offline before PLC import

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `import-directory` | Path | yes |  | Directory containing the SIMATIC SD files |
| `file-name` | String | yes |  | Block/file name without extension |

## Portal

### add-device

Adds a device with its primary item to the current project

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `order-number` | String | yes |  | TIA hardware type identifier or order number |
| `version` | String | no |  | Optional catalog version appended to the order number when needed |
| `name` | String | yes |  | Name for the device and primary item |
| `confirm` | Boolean | no | False | Explicitly authorizes this state-changing command after review of its target and impact. |

### archive-project

Archives the current project to a .zap21 archive

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `archive-path` | Path | yes |  | Target .zap21 archive path |
| `mode` | String | no | Compressed | Archive mode: Compressed, None, DiscardRestorableData, or DiscardRestorableDataAndCompressed |
| `confirm` | Boolean | no | False | Explicitly authorizes this state-changing command after review of its target and impact. |

### close-project

Closes the currently open project

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `save` | Boolean | no | False | Whether to save changes before closing (default: false) |
| `confirm` | Boolean | no | False | Explicitly authorizes this state-changing command after review of its target and impact. |

### close-tia-portal

Closes the current TIA Portal instance

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `confirm` | Boolean | no | False | Explicitly authorizes this state-changing command after review of its target and impact. |

### connect-instance

Connects to a specific TIA Portal instance by process ID

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `instance-id` | Integer | yes |  | The process ID of the TIA Portal instance to connect to |

### connect-portal

Connects to a running TIA Portal instance or starts a new one

No parameters.

### create-project

Creates a new TIA Portal project and selects it as the current project

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `project-path` | Path | yes |  | Project folder path or .ap21 file path |
| `confirm` | Boolean | no | False | Explicitly authorizes this state-changing command after review of its target and impact. |

### disconnect-portal

Disconnects from the TIA Portal instance

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `confirm` | Boolean | no | False | Explicitly authorizes this state-changing command after review of its target and impact. |

### get-project-info

Gets information about the currently open project

No parameters.

### get-project-tree

Gets the hierarchical project structure

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `depth` | Integer | no |  | Maximum depth to traverse (null for unlimited) |

### list-devices

Lists all devices in the currently open project

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `page-size` | Integer | no |  | Optional maximum number of rows returned in this page (1-1000). |
| `continuation-token` | String | no |  | Opaque continuation token returned by a previous page. |

### list-instances

Lists all running TIA Portal instances

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `page-size` | Integer | no |  | Optional maximum number of rows returned in this page (1-1000). |
| `continuation-token` | String | no |  | Opaque continuation token returned by a previous page. |

### list-projects

Lists all open TIA Portal projects and sessions

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `page-size` | Integer | no |  | Optional maximum number of rows returned in this page (1-1000). |
| `continuation-token` | String | no |  | Opaque continuation token returned by a previous page. |

### open-project

Opens a TIA Portal project file (.ap21/.als21 primary; legacy project formats also supported)

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `project-path` | Path | yes |  | Full path to the project file |
| `umac-user` | String | no |  | Optional UMAC user name |
| `umac-password-env` | String | no | OPENTIA_UMAC_PASSWORD | Environment variable containing the UMAC password (default: OPENTIA_UMAC_PASSWORD) |
| `umac-user-type` | String | no | project | UMAC user type: project or global |

### open-tia-portal

Opens a new TIA Portal instance (with UI by default)

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `with-ui` | Boolean | no | True | Start TIA Portal with user interface |

### portal-status

Gets the current TIA Portal connection status

No parameters.

### retrieve-project

Retrieves an archived project and selects it as the current project

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `archive-path` | Path | yes |  | Source .zap archive path |
| `target-directory` | Path | yes |  | Directory where the project should be retrieved |
| `upgrade` | Boolean | no | True | Upgrade the retrieved project to the current TIA version |
| `umac-user` | String | no |  | Optional UMAC user name |
| `umac-password-env` | String | no | OPENTIA_UMAC_PASSWORD | Environment variable containing the UMAC password (default: OPENTIA_UMAC_PASSWORD) |
| `umac-user-type` | String | no | project | UMAC user type: project or global |
| `confirm` | Boolean | no | False | Explicitly authorizes this state-changing command after review of its target and impact. |

### save-as-project

Saves the project to a new location

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `destination-path` | Path | yes |  | The destination path for the project |
| `confirm` | Boolean | no | False | Explicitly authorizes this state-changing command after review of its target and impact. |

### save-project

Saves the current project

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `confirm` | Boolean | no | False | Explicitly authorizes this state-changing command after review of its target and impact. |

## Project Texts

### export-texts

Exports project texts to a Siemens project text workbook

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `output-path` | Path | yes |  | Target .xlsx file path or folder |
| `source-culture` | String | yes |  | Source language culture name, such as en-US |
| `target-cultures` | StringArray | yes |  | Target language culture names |
| `categories` | StringArray | no |  | Optional project text category identifiers |
| `objects` | StringArray | yes |  | Top-level device or device-item names to export |

### import-texts

Imports project texts from a Siemens project text workbook

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `input-path` | Path | yes |  | Source .xlsx project text workbook |
| `update-source-language` | Boolean | no | False | Allow import to update source-language text |
| `dry-run` | Boolean | no | True | Validate inputs without invoking Siemens text import |
| `confirm` | Boolean | no | False | Explicitly authorizes this state-changing command after review of its target and impact. |

### list-project-languages

Lists project languages and active language settings

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `page-size` | Integer | no |  | Optional maximum number of rows returned in this page (1-1000). |
| `continuation-token` | String | no |  | Opaque continuation token returned by a previous page. |

## Unified HMI

### apply-hmi-screen-json-edit

Dry-runs or applies one Unified HMI screen JSON edit with safety checks

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `hmi-name` | String | yes |  | The Unified HMI name |
| `screen-path` | String | yes |  | The slash-delimited screen path |
| `edited-screen` | Object | yes |  | The edited HmiJsonScreen object |
| `base-hash` | String | no |  | Optional expected hash from get-hmi-screen-json |
| `dry-run` | Boolean | no | True | Validate changes without applying them |
| `allow-stale-base` | Boolean | no | False | Allow applying when base-hash no longer matches |
| `fail-on-warning` | Boolean | no | True | Treat warnings as a safety block |
| `require-clean-preflight` | Boolean | no | True | Run a blocking clean preflight before apply |
| `backup` | Boolean | no | True | Create a backup export before applying changes |
| `backup-path` | Path | no |  | Optional backup folder or .json file path |
| `item-names` | StringArray | no |  | Optional item names to include |
| `properties` | StringArray | no |  | Optional property/composite names to include |
| `working-path` | Path | no |  | Optional artifact root path |
| `confirm` | Boolean | no | False | Explicitly authorizes this state-changing command after review of its target and impact. |

### apply-hmi-screen-json-patch

Dry-runs or applies compact Unified HMI screen patch operations with safety checks

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `hmi-name` | String | yes |  | The Unified HMI name |
| `screen-path` | String | yes |  | The slash-delimited screen path |
| `operations` | Object | no |  | Patch operations array or { operations: [...] } object |
| `operations-path` | Path | no |  | Optional JSON file containing patch operations array or { operations: [...] } object |
| `base-hash` | String | no |  | Optional expected hash from get-hmi-screen-json |
| `dry-run` | Boolean | no | True | Validate changes without applying them |
| `allow-stale-base` | Boolean | no | False | Allow applying when base-hash no longer matches |
| `fail-on-warning` | Boolean | no | True | Treat warnings as a safety block |
| `require-clean-preflight` | Boolean | no | True | Run a blocking clean preflight before apply |
| `backup` | Boolean | no | True | Create a backup export before applying changes |
| `backup-path` | Path | no |  | Optional backup folder or .json file path |
| `item-names` | StringArray | no |  | Optional item names to include; keep identical to the snapshot when using base-hash |
| `properties` | StringArray | no |  | Optional property/composite names to include; keep identical to the snapshot when using base-hash |
| `working-path` | Path | no |  | Optional artifact root path |
| `confirm` | Boolean | no | False | Explicitly authorizes this state-changing command after review of its target and impact. |

### audit-hmi-label-proximity

Creates an auditable nearest-label mapping report from an exported Unified HMI JSON file

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `input-path` | Path | yes |  | Unified HMI JSON export file |
| `screen-path` | String | no |  | Optional screen path to audit |
| `target-items` | StringArray | no |  | Optional target item names |
| `target-types` | StringArray | no |  | Optional target item types; defaults to HmiFaceplateContainer |
| `label-texts` | StringArray | no |  | Optional exact label text values to include |
| `label-types` | StringArray | no |  | Optional label item types; defaults to HmiText,HmiLabel |
| `max-candidates` | Integer | no | 5 | Number of candidates to include per target |
| `output-path` | Path | no |  | Optional JSON report output path |

### copy-hmi-screen-from-template

Copies a Unified HMI screen from a template screen with optional find/replace

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `hmi-name` | String | yes |  | The Unified HMI name |
| `template-screen-path` | String | yes |  | The template screen path |
| `target-screen-path` | String | yes |  | The new target screen path |
| `find-text` | String | no |  | Optional text to replace while cloning |
| `replace-text` | String | no |  | Optional replacement text |
| `confirm` | Boolean | no | False | Explicitly authorizes this state-changing command after review of its target and impact. |

### create-hmi-item

Creates a Unified HMI item on a screen

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `hmi-name` | String | yes |  | The Unified HMI name |
| `screen-path` | String | yes |  | The slash-delimited screen path |
| `item-type` | String | yes |  | The item type or alias |
| `item-name` | String | yes |  | The new item name |
| `left` | Integer | no |  | Optional left position |
| `top` | Integer | no |  | Optional top position |
| `width` | Integer | no |  | Optional item width |
| `height` | Integer | no |  | Optional item height |
| `confirm` | Boolean | no | False | Explicitly authorizes this state-changing command after review of its target and impact. |

### create-hmi-screen

Creates a Unified HMI screen if it does not already exist

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `hmi-name` | String | yes |  | The Unified HMI name |
| `screen-path` | String | yes |  | The slash-delimited screen path |
| `width` | Integer | no |  | Optional screen width |
| `height` | Integer | no |  | Optional screen height |
| `confirm` | Boolean | no | False | Explicitly authorizes this state-changing command after review of its target and impact. |

### create-hmi-trend

Creates or updates Unified HMI logging infrastructure for a tag

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `hmi-name` | String | yes |  | The Unified HMI name |
| `tag-path` | String | yes |  | The HMI tag path |
| `datalog-name` | String | yes |  | The target data log name |
| `cyclic` | Boolean | no | False | Whether cyclic logging should be used |
| `cycle` | String | no |  | Optional cycle value, such as T5s |
| `storage-folder` | String | no | /Trends | Compatibility option; Unified HMI storage folder is read-only and is not changed |
| `log-max-size` | Integer | no | 2000 | Optional data log max size |
| `duration-days` | Integer | no | 365 | Optional log duration in days |
| `segment-max-size` | Integer | no | 500 | Optional segment max size |
| `confirm` | Boolean | no | False | Explicitly authorizes this state-changing command after review of its target and impact. |

### create-hmi-trends

Plans or applies multiple Unified HMI trend logging configurations

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `hmi-name` | String | yes |  | The Unified HMI name |
| `configs` | ObjectArray | yes |  | Trend configs containing tagPath, dataLogName, cyclic, cycle, and optional sizing properties |
| `dry-run` | Boolean | no | True | Validate and report planned configurations without applying them |
| `confirm` | Boolean | no | False | Explicitly authorizes this state-changing command after review of its target and impact. |

### delete-hmi-data-log

Deletes one Unified HMI data log archive when no logging tags still reference it

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `hmi-name` | String | yes |  | The Unified HMI name |
| `datalog-name` | String | yes |  | The data log archive name |
| `confirm` | Boolean | yes |  | Confirm deletion |

### delete-hmi-data-logs

Plans or deletes multiple unreferenced Unified HMI data log archives

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `hmi-name` | String | yes |  | The Unified HMI name |
| `configs` | ObjectArray | yes |  | Data log configs containing dataLogName |
| `dry-run` | Boolean | no | True | Validate and report planned archive deletions without applying them |
| `confirm` | Boolean | no | False | Confirm deletion when dry-run is false |

### diff-hmi-json

Creates a minimal Unified HMI JSON patch from two exports

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `base-path` | Path | yes |  | Base export folder or .json file path |
| `modified-path` | Path | yes |  | Edited export folder or .json file path |
| `patch-path` | Path | yes |  | Target patch folder or .json file path |
| `screens` | String | no |  | Optional comma-separated screen paths |
| `item-names` | String | no |  | Optional comma-separated item names |
| `properties` | String | no |  | Optional comma-separated property paths |

### export-hmi-json

Exports the supported Unified HMI surface to JSON

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `hmi-name` | String | yes |  | The Unified HMI name |
| `output-path` | Path | yes |  | Target folder or .json file path |
| `screens` | String | no |  | Optional comma-separated screen paths |
| `item-names` | String | no |  | Optional comma-separated item names |
| `properties` | String | no |  | Optional comma-separated property paths |

### get-hmi-item-properties

Gets readable properties for a Unified HMI item

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `hmi-name` | String | yes |  | The Unified HMI name |
| `screen-path` | String | yes |  | The slash-delimited screen path |
| `item-name` | String | yes |  | The screen item name |
| `properties` | StringArray | no |  | Optional property names |

### get-hmi-screen-info

Gets detailed information about a Unified HMI screen

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `hmi-name` | String | yes |  | The Unified HMI name |
| `screen-path` | String | yes |  | The slash-delimited screen path |

### get-hmi-screen-json

Gets one Unified HMI screen as canonical JSON for agent editing

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `hmi-name` | String | yes |  | The Unified HMI name |
| `screen-path` | String | yes |  | The slash-delimited screen path |
| `item-names` | StringArray | no |  | Optional item names to include |
| `properties` | StringArray | no |  | Optional property/composite names to include |
| `working-path` | Path | no |  | Optional artifact root path |
| `include-screen` | Boolean | no | True | Include the full screen object inline when it stays below the spill threshold |

### get-hmi-screen-summary

Summarizes item counts and faceplate contained types for one Unified HMI screen

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `hmi-name` | String | yes |  | The Unified HMI name |
| `screen-path` | String | yes |  | The slash-delimited screen path |
| `include-items` | Boolean | no | False | Include compact per-item entries |
| `include-faceplates` | Boolean | no | False | Include compact faceplate interface entries and configuration counts |
| `working-path` | Path | no |  | Optional artifact root path |

### hmi-status

Gets Unified HMI session status and optionally probes a screen

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `hmi-name` | String | no |  | Optional Unified HMI name expected by the caller |
| `screen-path` | String | no |  | Optional screen path to probe |
| `open-if-needed` | Boolean | no | False | Open hmi-name before probing when no matching HMI is selected |

### import-hmi-json

Preflights or imports a Unified HMI JSON export or patch

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `hmi-name` | String | yes |  | The Unified HMI name |
| `input-path` | Path | yes |  | Source folder or .json file path |
| `dry-run` | Boolean | no | True | Validate changes without applying them |
| `apply` | Boolean | no | False | Explicitly request an import apply; requires dry-run=false and confirm=true |
| `confirm` | Boolean | no | False | Confirm the reviewed import apply |
| `fail-on-warning` | Boolean | no | True | Treat warnings as a safety block |
| `require-clean-preflight` | Boolean | no | True | Run a blocking clean preflight before apply |
| `backup` | Boolean | no | True | Create a backup export before applying changes |
| `backup-path` | Path | no |  | Optional backup folder or .json file path |
| `screens` | String | no |  | Optional comma-separated screen paths |
| `item-names` | String | no |  | Optional comma-separated item names |
| `properties` | String | no |  | Optional comma-separated property paths |

### inspect-hmi-screen-json-capabilities

Inspects one Unified HMI screen and returns agent-oriented patch capabilities and guidance

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `hmi-name` | String | yes |  | The Unified HMI name |
| `screen-path` | String | yes |  | The slash-delimited screen path |
| `item-names` | StringArray | no |  | Optional item names to include |
| `properties` | StringArray | no |  | Optional property/composite names to include |
| `include-item-types` | Boolean | no | True | Include supported item types and aliases |
| `include-writable-properties` | Boolean | no | True | Include writable property metadata for exported items |
| `include-snapshot` | Boolean | no | False | Include the full screen snapshot in the result |
| `working-path` | Path | no |  | Optional artifact root path |

### list-hmi-item-types

Lists the validated Unified HMI item types supported by this toolkit

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `filter` | String | no |  | Optional text filter |
| `page-size` | Integer | no |  | Optional maximum number of rows returned in this page (1-1000). |
| `continuation-token` | String | no |  | Opaque continuation token returned by a previous page. |

### list-hmis

Lists all HMI devices found in the project

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `page-size` | Integer | no |  | Optional maximum number of rows returned in this page (1-1000). |
| `continuation-token` | String | no |  | Opaque continuation token returned by a previous page. |

### list-hmi-screens

Lists screens in a Unified HMI

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `hmi-name` | String | yes |  | The Unified HMI name |
| `group-path` | String | no |  | Optional screen group path |
| `recursive` | Boolean | no | True | Traverse nested screen groups |
| `page-size` | Integer | no |  | Optional maximum number of rows returned in this page (1-1000). |
| `continuation-token` | String | no |  | Opaque continuation token returned by a previous page. |

### list-hmi-tags

Lists tags in a Unified HMI

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `hmi-name` | String | yes |  | The Unified HMI name |
| `filter` | String | no |  | Optional text filter |
| `root-path` | String | no |  | Optional tag path to enumerate only that subtree |
| `allow-full-scan` | Boolean | no | False | Allow scanning the full HMI tag tree when root-path is omitted |
| `max-results` | Integer | no | 200 | Maximum number of tags to return during enumeration |
| `max-depth` | Integer | no | 4 | Maximum member depth to traverse from the selected root |
| `include-details` | Boolean | no | True | Read detailed tag metadata such as display name and data type |
| `include-logging-counts` | Boolean | no | False | Read logging tag counts for returned tags |
| `output-path` | Path | no |  | Optional CSV file or folder path for large tag lists |
| `page-size` | Integer | no |  | Optional maximum number of rows returned in this page (1-1000). |
| `continuation-token` | String | no |  | Opaque continuation token returned by a previous page. |

### open-unified-hmi

Opens a Unified HMI by device or device item name

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `hmi-name` | String | yes |  | The Unified HMI device or device item name |

### probe-hmi-faceplate-interface

Inspects Unified HMI faceplate interface metadata and optionally probes one reversible value write

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `hmi-name` | String | yes |  | The Unified HMI name |
| `screen-path` | String | yes |  | The slash-delimited screen path |
| `item-name` | String | no |  | The faceplate item name |
| `item-name-path` | Path | no |  | Optional text file containing the exact faceplate item name |
| `property` | String | no |  | Optional interface property name to target |
| `write` | Boolean | no | False | Attempt a direct write to the targeted interface property |
| `value` | String | no |  | The direct write value when write is true |
| `restore` | Boolean | no | True | Restore the original value after a write probe |
| `confirm` | Boolean | no | False | Explicitly authorizes this state-changing command after review of its target and impact. |

### remove-hmi-logging-tag

Removes one Unified HMI logging tag after verifying its data log association

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `hmi-name` | String | yes |  | The Unified HMI name |
| `tag-path` | String | yes |  | The HMI tag path |
| `datalog-name` | String | yes |  | The expected data log name |
| `logging-tag-name` | String | no |  | Optional explicit logging tag name; defaults to the toolkit-derived name |
| `confirm` | Boolean | yes |  | Confirm deletion |

### remove-hmi-logging-tags

Plans or removes multiple Unified HMI logging tags after data log verification

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `hmi-name` | String | yes |  | The Unified HMI name |
| `configs` | ObjectArray | yes |  | Removal configs containing tagPath, dataLogName, and optional loggingTagName |
| `dry-run` | Boolean | no | True | Validate and report planned removals without applying them |
| `confirm` | Boolean | no | False | Confirm deletion when dry-run is false |

### set-hmi-item-property

Sets a property or attribute on a Unified HMI item

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `hmi-name` | String | yes |  | The Unified HMI name |
| `screen-path` | String | yes |  | The slash-delimited screen path |
| `item-name` | String | yes |  | The screen item name |
| `property` | String | yes |  | The property or attribute name |
| `value` | String | yes |  | The new property value |
| `confirm` | Boolean | no | False | Explicitly authorizes this state-changing command after review of its target and impact. |

## V21

### apply-blueprint

Applies a previously reviewed blueprint plan using its state-bound one-use token.

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `apply-token` | String | yes |  | One-use token returned by plan-blueprint |
| `confirm` | Boolean | no | False | Explicitly authorizes this state-changing command after review of its target and impact. |

### capabilities

Reports the worker capability profile and exact Siemens assembly hashes.

No parameters.

### describe-api

Self-describes the installed V21 public API surface and prioritized feature availability.

No parameters.

### diff-api-profile

Compares the installed V21 public API self-description with a committed migration baseline.

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `baseline-path` | Path | no |  | Optional API profile baseline |

### doctor

Checks the isolated worker runtime, Siemens V21 API, paths, and process architecture.

No parameters.

### plan-blueprint

Builds a state-bound engineering plan and short-lived one-use apply token.

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `blueprint-path` | Path | yes |  | Blueprint JSON file |

### validate-blueprint

Validates a declarative engineering blueprint against the V21 worker command contract.

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `blueprint-path` | Path | yes |  | Blueprint JSON file |

### version

Reports toolkit, IPC, worker, runtime, and target TIA versions.

No parameters.

## VCI

### connect-vci-block

Connects one PLC block to existing VCI workspace files

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `workspace-name` | String | yes |  | Workspace name |
| `plc-name` | String | yes |  | PLC device name |
| `block-name` | String | yes |  | Exact block name |
| `relative-directory` | String | yes |  | Path relative to workspace root |
| `file-name` | String | yes |  | Existing file name without extension |
| `file-format` | String | yes |  | Format returned by get-vci-block-formats |
| `block-type` | String | no |  | Optional block type such as FC or DB |
| `group-path` | String | no |  | Optional block group path |
| `confirm` | Boolean | no | False | Explicitly authorizes this state-changing command after review of its target and impact. |

### create-vci-workspace

Creates a Version Control Interface workspace in the open project

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `workspace-name` | String | yes |  | Workspace name |
| `root-path` | Path | yes |  | Workspace filesystem root |
| `workspace-language` | String | no |  | Optional culture such as en-US |
| `confirm` | Boolean | no | False | Explicitly authorizes this state-changing command after review of its target and impact. |

### delete-vci-workspace

Deletes a Version Control Interface workspace definition

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `workspace-name` | String | yes |  | Workspace name |
| `confirm` | Boolean | yes |  | Confirm deletion |

### export-vci-block

Exports one PLC block into a VCI workspace and creates its mapping

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `workspace-name` | String | yes |  | Workspace name |
| `plc-name` | String | yes |  | PLC device name |
| `block-name` | String | yes |  | Exact block name |
| `relative-directory` | String | yes |  | Path relative to workspace root |
| `file-name` | String | no |  | File name without extension; defaults to block name |
| `file-format` | String | yes |  | Format returned by get-vci-block-formats |
| `block-type` | String | no |  | Optional block type such as FC or DB |
| `group-path` | String | no |  | Optional block group path |

### get-vci-block-formats

Gets VCI-supported workspace file formats for one PLC block

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `plc-name` | String | yes |  | PLC device name |
| `block-name` | String | yes |  | Exact block name |
| `block-type` | String | no |  | Optional block type such as FC or DB |
| `group-path` | String | no |  | Optional block group path |

### get-vci-block-status

Gets VCI comparison status for one mapped PLC block

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `workspace-name` | String | yes |  | Workspace name |
| `plc-name` | String | yes |  | PLC device name |
| `block-name` | String | yes |  | Exact block name |
| `block-type` | String | no |  | Optional block type |
| `group-path` | String | no |  | Optional block group path |
| `confirm` | Boolean | no | False | Confirm workspace-to-project mutation after review |

### get-vci-workspace

Gets details for a Version Control Interface workspace

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `workspace-name` | String | yes |  | Workspace name |

### list-vci-mappings

Lists mapped objects in a Version Control Interface workspace

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `workspace-name` | String | yes |  | Workspace name |
| `output-path` | Path | no |  | Optional CSV file or folder path for VCI mapping rows |
| `page-size` | Integer | no |  | Optional maximum number of rows returned in this page (1-1000). |
| `continuation-token` | String | no |  | Opaque continuation token returned by a previous page. |

### list-vci-workspaces

Lists Version Control Interface workspaces in the open project

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `page-size` | Integer | no |  | Optional maximum number of rows returned in this page (1-1000). |
| `continuation-token` | String | no |  | Opaque continuation token returned by a previous page. |

### sync-vci-block

Synchronizes one mapped PLC block with a VCI workspace

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `workspace-name` | String | yes |  | Workspace name |
| `plc-name` | String | yes |  | PLC device name |
| `block-name` | String | yes |  | Exact block name |
| `direction` | String | yes |  | project-to-workspace or workspace-to-project |
| `create-mapping` | Boolean | no | False | Export and map an unmapped item for project-to-workspace |
| `relative-directory` | String | no |  | Path relative to workspace root |
| `file-name` | String | no |  | Optional file name without extension |
| `file-format` | String | no |  | Optional supported file format |
| `block-type` | String | no |  | Optional block type |
| `group-path` | String | no |  | Optional block group path |
| `confirm` | Boolean | no | False | Explicitly authorizes this state-changing command after review of its target and impact. |

### sync-vci-blocks

Synchronizes selected PLC blocks with a VCI workspace

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `workspace-name` | String | yes |  | Workspace name |
| `plc-name` | String | yes |  | PLC device name |
| `direction` | String | yes |  | project-to-workspace or workspace-to-project |
| `filter-regex` | String | no |  | Optional block-name regular expression |
| `block-types` | StringArray | no |  | Optional set of types such as FC, DB |
| `create-mappings` | Boolean | no | False | Export and map unmapped items for project-to-workspace |
| `relative-directory` | String | no |  | Base path relative to workspace root |
| `file-format` | String | no |  | Optional supported file format |
| `include-skipped` | Boolean | no | False | Include per-item results for skipped unmapped objects |
| `output-path` | Path | no |  | Optional JSON file or folder path for the bulk sync result |
| `confirm` | Boolean | no | False | Confirm workspace-to-project mutation after review |

### sync-vci-plc

Synchronizes supported objects of a PLC with a VCI workspace

| Parameter | Type | Required | Default | Description |
|---|---|---:|---|---|
| `workspace-name` | String | yes |  | Workspace name |
| `plc-name` | String | yes |  | PLC device name |
| `direction` | String | yes |  | project-to-workspace or workspace-to-project |
| `scope` | String | no | blocks | blocks, blocks-and-types, or all-supported |
| `create-mappings` | Boolean | no | False | Export and map unmapped items for project-to-workspace |
| `relative-directory` | String | no |  | Base path relative to workspace root |
| `file-format` | String | no |  | Optional supported file format |
| `include-skipped` | Boolean | no | False | Include per-item results for skipped unmapped objects |
| `output-path` | Path | no |  | Optional JSON file or folder path for the PLC sync result |
| `confirm` | Boolean | no | False | Confirm workspace-to-project mutation after review |
