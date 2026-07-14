# OpenTiaToolkit CLI Usage Guide

Complete guide for using the OpenTiaToolkit CLI application.

## Table of Contents

- [Basic Usage](#basic-usage)
- [Command Reference](#command-reference)
- [Output Formats](#output-formats)
- [Agent and MCP Packaging](#agent-and-mcp-packaging)
- [Examples](#examples)
- [Troubleshooting](#troubleshooting)

## Basic Usage

### Invocation

```bash
OpenTiaToolkit.CLI.exe <command> [options] [--format table|json]
OpenTiaToolkit.CLI.exe --script <file>|-
```

### Session State

Single-command invocations are stateless. Each `OpenTiaToolkit.CLI.exe <command>` call starts a new process, so portal attachment, open project, selected PLC, and selected HMI state do not carry over from a previous call.

Run multi-step workflows in one interactive CLI session, use `--script <file>` for shell/CI automation, or use the MCP server for stateful agent automation. Command examples in the reference show command syntax; dependent commands must be entered in the same session or script process.

### Script Mode

Script mode executes REPL-shaped commands from a file in one process, so state persists for the duration of the script without the interactive banner or `tia>` prompt. Use `--script -` to read commands from stdin.

```powershell
OpenTiaToolkit.CLI.exe --script .\inspect-project.commands.txt
Get-Content .\inspect-project.commands.txt | OpenTiaToolkit.CLI.exe --script -
```

Script files support blank lines and full-line comments beginning with `#`. Execution stops at the first parse or command failure and the process returns that failure exit code.

### Getting Help

```bash
# List all commands
OpenTiaToolkit.CLI.exe help

# Get help for a specific command
OpenTiaToolkit.CLI.exe help open-project
```

### Command Name Format

All commands use **kebab-case** (lowercase with hyphens):

- Good: `open-project`
- Good: `list-devices`
- Good: `close-project`
- Not valid: `OpenProject`
- Not valid: `List_Devices`

### Large Output Artifacts

Large command results are kept out of inline MCP/CLI output when they exceed the conservative 32 KB response threshold. The command returns a compact artifact manifest with fields such as `outputPath`, `format`, `dataKind`, `bytesWritten`, `itemCount`, and `reason`; read the file at `outputPath` for the full payload.

Some commands choose a domain-specific artifact format. For example, `list-hmi-tags` writes large tag tables as CSV, while `get-hmi-screen-json` keeps canonical screen snapshots as JSON because patch/edit workflows depend on that shape. MCP also has a generic JSON artifact fallback for oversized structured results from commands that do not have custom artifact handling.

## Command Reference

### Portal Management

#### connect-portal

Connects to a running TIA Portal instance or starts a new one.

```bash
OpenTiaToolkit.CLI.exe connect-portal
```

**Output**:
```
Connected to TIA Portal Siemens.Engineering.V21
```

#### disconnect-portal

Disconnects from the TIA Portal instance.

```bash
OpenTiaToolkit.CLI.exe disconnect-portal
```

**Output**:
```
Disconnected from TIA Portal
```

#### portal-status

Gets the current TIA Portal connection status.

```bash
OpenTiaToolkit.CLI.exe portal-status
```

**Output**:
```
connected : True
version   : V21
```

### Project Operations

#### open-project

Opens a TIA Portal project file (.ap21 for local projects, .als21 for multiuser sessions; legacy formats are also accepted where supported).

```bash
OpenTiaToolkit.CLI.exe open-project --project-path="C:\Projects\MyProject.ap21"

# Protected projects: keep the password out of shell history
$env:OPENTIA_UMAC_PASSWORD = "<your-project-password>"
OpenTiaToolkit.CLI.exe open-project --project-path="C:\Projects\Protected.ap21" --umac-user="Engineer"
```

**Parameters**:
- `project-path` (required): Full path to the project file
- `umac-user` (optional): UMAC user name for protected projects
- `umac-password-env` (optional, default: `OPENTIA_UMAC_PASSWORD`): Environment variable containing the UMAC password
- `umac-user-type` (optional, default: `project`): UMAC user type (`project` or `global`)

**Output**:
```
Project opened: MyProject

Name:     MyProject
Path:     C:\Projects\MyProject.ap21
Version:  V21
Created:  2025-01-15 10:30:00
```

#### create-project

Creates a new project and selects it as the current project.

```bash
OpenTiaToolkit.CLI.exe create-project --project-path="C:\Projects\NewProject.ap21"
```

**Parameters**:
- `project-path` (required): Project folder path or `.ap21` file path. The parent folder is used as the TIA target directory and the file/folder name becomes the project name.

#### add-device

Adds a hardware device with its primary item to the current project.

```bash
OpenTiaToolkit.CLI.exe add-device --order-number="OrderNumber:6ES7 214-1AG40-0XB0" --version="V4.6" --name="PLC_1"
```

**Parameters**:
- `order-number` (required): TIA hardware type identifier or order number
- `version` (optional): Catalog version appended to the order number when needed
- `name` (required): Name for the device and primary item

#### archive-project

Archives the current project to a `.zap21` file.

```bash
OpenTiaToolkit.CLI.exe save-project
OpenTiaToolkit.CLI.exe archive-project --archive-path="C:\Backups\MyProject.zap21"
```

**Parameters**:
- `archive-path` (required): Target `.zap21` archive path
- `mode` (optional, default: `Compressed`): `Compressed`, `None`, `DiscardRestorableData`, or `DiscardRestorableDataAndCompressed`

TIA Portal requires the project to have no unsaved changes before archiving. Run `save-project` first when the current session has edits.

#### retrieve-project

Retrieves an archived project and selects it as the current project.

```bash
OpenTiaToolkit.CLI.exe retrieve-project --archive-path="C:\Backups\MyProject.zap21" --target-directory="C:\Projects"

# Protected archive/project
$env:OPENTIA_UMAC_PASSWORD = "<your-project-password>"
OpenTiaToolkit.CLI.exe retrieve-project --archive-path="C:\Backups\Protected.zap21" --target-directory="C:\Projects" --umac-user="Engineer"
```

**Parameters**:
- `archive-path` (required): Source `.zap` archive path
- `target-directory` (required): Directory where the project should be retrieved
- `upgrade` (optional, default: true): Upgrade the retrieved project to the current TIA version
- `umac-user` (optional): UMAC user name for protected projects
- `umac-password-env` (optional, default: `OPENTIA_UMAC_PASSWORD`): Environment variable containing the UMAC password
- `umac-user-type` (optional, default: `project`): UMAC user type (`project` or `global`)

#### close-project

Closes the currently open project.

```bash
# Close without saving
OpenTiaToolkit.CLI.exe close-project

# Close with saving
OpenTiaToolkit.CLI.exe close-project --save
```

**Parameters**:
- `save` (optional, default: false): Save changes before closing

**Output**:
```
Project closed (changes not saved)
```

#### list-projects

Lists all open TIA Portal projects and sessions.

```bash
OpenTiaToolkit.CLI.exe list-projects
```

**Output**:
```
Found 2 project(s):

Name                                    Path                                      Version    Created
------------------------------------------------------------------------------------------------------------
MyProject                               C:\Projects\MyProject.ap21                  V21        2025-01-15 10:30
MySession (Session)                       C:\Projects\MyProject.als21                 V21        2025-01-15 10:35
```

#### get-project-info

Gets information about the currently open project.

```bash
OpenTiaToolkit.CLI.exe get-project-info
```

**Output**:
```
Project Information:

Name:     MyProject
Path:     C:\Projects\MyProject.ap21
Version:  V21
Created:  2025-01-15 10:30:00
```

### Project Texts and Languages

#### list-project-languages

Lists project languages, active languages, and the editing/reference language settings.

```bash
OpenTiaToolkit.CLI.exe list-project-languages
```

#### export-texts

Exports project texts to Siemens' native project text workbook format (`.xlsx`) for translation workflows.

```bash
OpenTiaToolkit.CLI.exe export-texts --output-path="C:\Exports\project_texts.xlsx" --source-culture="en-US" --target-cultures="de-DE,it-IT" --objects="HMI_1"

# Limit the export to selected text categories when needed
OpenTiaToolkit.CLI.exe export-texts --output-path="C:\Exports" --source-culture="en-US" --target-cultures="de-DE" --categories="alarms,screens" --objects="PLC_1,HMI_1"
```

**Parameters**:
- `output-path` (required): Target `.xlsx` file path or folder. Folder paths write `project_texts.xlsx`.
- `source-culture` (required): Source language culture name, such as `en-US`.
- `target-cultures` (required): Comma-separated target language culture names.
- `objects` (required): Top-level device or device-item names to export.
- `categories` (optional): Project text category identifiers.

#### import-texts

Imports a Siemens project text workbook. The command defaults to dry-run validation; pass `--dry-run=false` to apply changes.

```bash
OpenTiaToolkit.CLI.exe import-texts --input-path="C:\Exports\project_texts.xlsx"
OpenTiaToolkit.CLI.exe import-texts --input-path="C:\Exports\project_texts.xlsx" --dry-run=false --update-source-language=false
```

**Parameters**:
- `input-path` (required): Source `.xlsx` project text workbook.
- `dry-run` (optional, default: true): Validate the project and workbook path without invoking Siemens text import.
- `update-source-language` (optional, default: false): Allow import to update source-language text.

### Device Operations

#### list-devices

Lists all devices in the currently open project.

```bash
OpenTiaToolkit.CLI.exe list-devices
```

**Output**:
```
Found 3 device(s):

Name                            Type                            Identification                   IP Address
----------------------------------------------------------------------------------------
PLC_1                           S7-1500                         CPU 1516-3 PN/DP                 192.168.0.1
HMI_1                           TP1200                          Comfort Panel                     192.168.0.2
IO_System                        ET 200SP                         Interface                           Not configured
```

#### get-device-info

Gets detailed information about a specific device.

```bash
OpenTiaToolkit.CLI.exe get-device-info --device-name="PLC_1"
```

**Parameters**:
- `device-name` (required): Name of the device

**Output**:
```
Device Information:

Name:            PLC_1
Type:            S7-1500
Identification:  CPU 1516-3 PN/DP
IP Address:      192.168.0.1
```

#### get-device-item-info

Gets details about a device item.

```bash
OpenTiaToolkit.CLI.exe get-device-item-info --device-name="PLC_1" --item-name="CPU"
```

**Parameters**:
- `device-name` (required): Name of the device
- `item-name` (required): Name of the device item

**Output**:
```
Device Item Information:

Device:  PLC_1
Item:    CPU
Type:    CPU 1516-3 PN/DP
Status:  Configured
```

#### list-hmis

Lists all HMI devices found in the project with automatic HMI discovery.

```bash
OpenTiaToolkit.CLI.exe list-hmis
```

**Output**:
```
Found 1 HMI device(s):

Name                            Type                            Parent Device                   Confirmed
------------------------------------------------------------------------------------------------------------
HMI_1                           TP1200                          Controller_1                    Yes
```

### Generic Hardware Automation

These commands expose vendor-neutral TIA Portal hardware primitives for GSD/GSDML devices, modules, PROFINET interfaces, and parameter records. Keep manufacturer-specific catalog identifiers and parameter encodings in a device workflow or skill; see [HARDWARE_AUTOMATION.md](HARDWARE_AUTOMATION.md) for the full safety model.

#### find-hardware-catalog-items

Searches the installed TIA Portal hardware catalog for usable type identifiers.

```bash
OpenTiaToolkit.CLI.exe find-hardware-catalog-items --search-text="AL1403"
```

#### get-hardware-tree

Returns hierarchical devices and items with target paths reusable by the remaining hardware commands.

```bash
OpenTiaToolkit.CLI.exe get-hardware-tree --device-name="IO_Master_01"
OpenTiaToolkit.CLI.exe get-hardware-tree --output-path="C:\Exports\hardware_tree.json"
```

When `--output-path` is provided, the nested hardware tree is written as JSON. Without `--output-path`, large tree results that exceed the 32 KB inline response threshold are automatically written to a temporary JSON artifact.

#### get-hardware-io-addresses

Lists process-image IO address objects assigned directly to hardware items, including GSD/GSDML modules.

```bash
OpenTiaToolkit.CLI.exe get-hardware-io-addresses --device-name="AL1403_231" --attributes="StartAddress,IoType,Length"
OpenTiaToolkit.CLI.exe get-hardware-io-addresses --attributes="StartAddress,IoType,Length" --output-path="C:\Exports\hardware_io_addresses.csv"
```

When `--output-path` is provided, IO address rows are written as CSV. Without `--output-path`, large IO address results that exceed the 32 KB inline response threshold are automatically written to a temporary CSV artifact.

#### get-hardware-drive-telegrams

Lists native Startdrive telegrams and their process-image IO addresses for SINAMICS drives.
Some Startdrive devices do not expose optional drive-object metadata; telegram and address fields are returned independently.

```bash
OpenTiaToolkit.CLI.exe get-hardware-drive-telegrams --device-name="SINAMICS G_1" --address-attributes="StartAddress,IoType,Length"
OpenTiaToolkit.CLI.exe get-hardware-drive-telegrams --address-attributes="StartAddress,IoType,Length" --output-path="C:\Exports\drive_telegrams.csv"
```

When `--output-path` is provided, drive telegram and IO address rows are written as CSV. Without `--output-path`, large drive telegram results that exceed the 32 KB inline response threshold are automatically written to a temporary CSV artifact.

#### audit-hardware-tag-mappings

Compares PLC `%I/%Q` tag addresses with discovered hardware IO ranges and optional catalog evidence.
Use it when tag tables suggest a field device, but `list-devices` or `get-hardware-tree` do not show one.

```bash
OpenTiaToolkit.CLI.exe audit-hardware-tag-mappings --plc-name="PLC_1" --tag-table-filter="FLOW_METERS" --catalog-search-text="Promag"
OpenTiaToolkit.CLI.exe audit-hardware-tag-mappings --plc-name="PLC_1" --max-unmatched-tags=-1 --output-path="C:\Exports\unmatched_hardware_tags.csv"
```

When `--output-path` is provided, the audit writes the unmatched PLC tag rows as CSV and returns the audit summary in the artifact manifest. For complete large audit objects over MCP, the generic JSON artifact fallback preserves the full structured result.

#### get-hardware-item-capabilities

Reports feature services and optional attribute values for a specific item.

```bash
OpenTiaToolkit.CLI.exe get-hardware-item-capabilities --device-name="IO_Master_01" --item-path="DAP/PN-IO" --attributes="Name"
```

#### get-hardware-feature-attributes

Reads attributes owned by a feature service such as `NetworkInterface`.

```bash
OpenTiaToolkit.CLI.exe get-hardware-feature-attributes --device-name="IO_Master_01" --item-path="DAP/PN-IO" --feature-service="NetworkInterface" --attributes="InterfaceOperatingMode"
```

#### get-hardware-network-item-attributes

Reads attributes owned by an item of a network service collection. PROFINET IP address fields are exposed on the node contained by `NetworkInterface`.

```bash
OpenTiaToolkit.CLI.exe get-hardware-network-item-attributes --device-name="IO_Master_01" --item-path="DAP/PN-IO" --network-collection="Nodes" --network-item-index=0 --attributes="Address,SubnetMask,IpProtocolSelection"
```

#### find-hardware-module-types

Resolves complete module identifiers using the project-level Siemens `ModuleInformationProvider`.

```bash
OpenTiaToolkit.CLI.exe find-hardware-module-types --partial-type-identifier="GSD:GSDML-V2.35-IFM-AL1X0X-20230127-075900.XML/M"
```

#### can-plug-hardware-item

Checks a proposed module or submodule location without changing the project.

```bash
OpenTiaToolkit.CLI.exe can-plug-hardware-item --device-name="IO_Master_01" --item-path="8 Ports_1" --type-identifier="GSD:..." --item-name="Port_1" --position-number=2
```

#### get-hardware-networks

Lists discovered network interfaces, nodes, IO controllers, IO connectors, and current connections. Node entries include readable PROFINET attributes such as `Address`, `SubnetMask`, `PnDeviceName`, and `PnDeviceNameAutoGeneration`; IO connector entries include attributes such as `PnDeviceNumber` when TIA exposes them. The result also includes project IO-system `connectedIoDevices` with the same node and connector attribute shape, useful for PROFINET IO devices that are connected to an IO system but are not surfaced as top-level project devices.

```bash
OpenTiaToolkit.CLI.exe get-hardware-networks --device-name="IO_Master_01"
OpenTiaToolkit.CLI.exe get-hardware-networks --output-path="C:\Exports\hardware_networks.json"
```

When `--output-path` is provided, network interface details are written as JSON. Without `--output-path`, large network results that exceed the 32 KB inline response threshold are automatically written to a temporary JSON artifact.

#### get-gsd-parameter-data

Reads raw parameter data exposed by a GSD/GSDML item.

```bash
OpenTiaToolkit.CLI.exe get-gsd-parameter-data --device-name="IO_Master_01" --item-path="8 Ports_1/Port_1" --dataset-number=47360 --byte-offset=0 --length=16
```

#### apply-hardware-patch

Performs a transactional sequence of generic hardware operations. It defaults to `--dry-run=true`, rolling back all mutations after validating them. Use expected-value guards before applying writes.

```bash
OpenTiaToolkit.CLI.exe apply-hardware-patch --operations-path="C:\Engineering\hardware-patch.json" --dry-run=true
OpenTiaToolkit.CLI.exe apply-hardware-patch --operations-path="C:\Engineering\hardware-patch.json" --dry-run=false
$env:OPENTIA_SAFETY_PASSWORD = Read-Host -AsSecureString | ConvertFrom-SecureString -AsPlainText
OpenTiaToolkit.CLI.exe apply-hardware-patch --operations-path="C:\Engineering\drive-patch.json" --dry-run=true --plc-name="PLC_1" --safety-password-env="OPENTIA_SAFETY_PASSWORD"
```

Supported patch operations include device instantiation, module/submodule plugging, GSD parameter read/write, PROFINET identity configuration (`configure-profinet-device` for IP address, station name, and device number), process-image relocation (`set-io-address-start`), Startdrive telegram number/address/size/insert/erase changes, subnet and IO-system connection, and explicitly confirmed disconnect/delete/telegram-erase operations. `targetRef` may be combined with a relative `target.itemPath` to address children of newly created items in the same transaction. `plug-item` verifies the actual path TIA assigned to the created item and fails by default if TIA placed it somewhere other than the requested parent path; set `allowDisplacedPlacement=true` only when you want to accept and report that actual placement. For GSD/GSDML subslots that TIA displaces, prefer `allowDisplacedPlacement=true` plus a `resultId` on `plug-item`, then use that `resultId` for follow-up IO address or parameter operations instead of adding device-specific commands. For new PROFINET drives, connect the drive interface to the subnet and IO system before assigning Startdrive telegram addresses. PROFINET station name (`pnDeviceName`) is separate from TIA's display `Name`, which can be written with guarded `set-attributes`. Use `--plc-name` and a `--safety-password-env` reference when hardware mutations touch fail-safe drive/project data, including dry-runs. Never pass the password value itself as an argument.

### Unified HMI Operations

#### open-unified-hmi

Opens a Unified HMI by device or device item name.

```bash
OpenTiaToolkit.CLI.exe open-unified-hmi --hmi-name="HMI_1"
```

#### list-hmi-screens

Lists Unified HMI screens.

```bash
OpenTiaToolkit.CLI.exe list-hmi-screens --hmi-name="HMI_1"
OpenTiaToolkit.CLI.exe list-hmi-screens --hmi-name="HMI_1" --group-path="Screens/Templates" --recursive=true
```

#### get-hmi-screen-info

Gets screen metadata and item summaries.

```bash
OpenTiaToolkit.CLI.exe get-hmi-screen-info --hmi-name="HMI_1" --screen-path="Screens/Main"
```

#### create-hmi-screen

Creates a Unified HMI screen if it does not already exist.

```bash
OpenTiaToolkit.CLI.exe create-hmi-screen --hmi-name="HMI_1" --screen-path="Codex/Main" --width=1280 --height=720
```

#### copy-hmi-screen-from-template

Copies a template screen to a new path, optionally replacing text values while cloning.

```bash
OpenTiaToolkit.CLI.exe copy-hmi-screen-from-template --hmi-name="HMI_1" --template-screen-path="Templates/Motor" --target-screen-path="Generated/Motor_01" --find-text="Template" --replace-text="Motor 01"
```

#### list-hmi-item-types

Lists validated Unified HMI item types and aliases supported by the toolkit.

```bash
OpenTiaToolkit.CLI.exe list-hmi-item-types
OpenTiaToolkit.CLI.exe list-hmi-item-types --filter="button"
```

#### create-hmi-item

Creates a screen item if it does not already exist.

```bash
OpenTiaToolkit.CLI.exe create-hmi-item --hmi-name="HMI_1" --screen-path="Codex/Main" --item-type="button" --item-name="StartButton" --left=40 --top=60 --width=160 --height=44
```

#### get-hmi-item-properties

Reads item properties or attributes. Use `properties` to request a focused set.

```bash
OpenTiaToolkit.CLI.exe get-hmi-item-properties --hmi-name="HMI_1" --screen-path="Codex/Main" --item-name="StartButton"
OpenTiaToolkit.CLI.exe get-hmi-item-properties --hmi-name="HMI_1" --screen-path="Codex/Main" --item-name="StartButton" --properties="Text,Visible,Left,Top"
```

#### set-hmi-item-property

Writes one item property or attribute.

```bash
OpenTiaToolkit.CLI.exe set-hmi-item-property --hmi-name="HMI_1" --screen-path="Codex/Main" --item-name="StartButton" --property="Text" --value="Start"
```

#### list-hmi-tags

Lists Unified HMI tags, optionally filtered by text. To avoid expensive full-project Unified HMI tag enumeration, unscoped scans are blocked by default.

```bash
OpenTiaToolkit.CLI.exe list-hmi-tags --hmi-name="HMI_1" --root-path="Analog_FT235_GEN"
OpenTiaToolkit.CLI.exe list-hmi-tags --hmi-name="HMI_1" --root-path="Analog_FT235_GEN.PV" --include-logging-counts=true
OpenTiaToolkit.CLI.exe list-hmi-tags --hmi-name="HMI_1" --allow-full-scan=true --filter="Motor" --max-results=100 --max-depth=4 --include-details=false
OpenTiaToolkit.CLI.exe list-hmi-tags --hmi-name="HMI_1" --root-path="Analog_FT235_GEN" --output-path="C:\Exports\hmi_tags.csv"
```

Use `--root-path` when the tag subtree is known. It resolves that tag directly instead of scanning the complete Unified HMI tag collection before filtering. `--root-path="/"` and `--root-path="\"` are treated as unscoped scans and still require `--allow-full-scan=true`.

For large Unified HMI projects, prefer a concrete leaf or subtree `--root-path`. Only use `--allow-full-scan=true` when discovery is unavoidable, and keep it bounded with `--max-results` and `--max-depth`. `--include-logging-counts` defaults to `false` because reading logging tag counts can be expensive on large tag trees.

When `--output-path` is provided, `list-hmi-tags` writes a CSV file and returns a compact artifact manifest. Without `--output-path`, tag results that would exceed the 32 KB inline response threshold are automatically written to a temporary CSV artifact instead of being streamed inline.

#### create-hmi-trend

Creates or updates data logging infrastructure for a Unified HMI tag.

```bash
OpenTiaToolkit.CLI.exe create-hmi-trend --hmi-name="HMI_1" --tag-path="HMI_Tags/MotorSpeed" --datalog-name="MotorSpeedLog" --cyclic=true --cycle="T2s"
```

`--storage-folder` is retained for compatibility, but Unified HMI exposes the data log storage folder as read-only through Openness. The command reports the folder configured by TIA and does not attempt to change it.

#### remove-hmi-logging-tag

Removes one logging tag after checking that it references the expected data log. Deletion always requires explicit confirmation.

```bash
OpenTiaToolkit.CLI.exe remove-hmi-logging-tag --hmi-name="HMI_1" --tag-path="HMI_Tags/MotorSpeed" --datalog-name="MotorSpeedLog" --confirm=true
```

For logging tags not created with the toolkit-derived name, provide `--logging-tag-name="ExistingLoggingTag"`.

#### delete-hmi-data-log

Deletes a named data log archive only after its logging tag references have been removed. Deletion always requires explicit confirmation; active references are returned as a blocking result.

```bash
OpenTiaToolkit.CLI.exe delete-hmi-data-log --hmi-name="HMI_1" --datalog-name="MotorSpeedLog" --confirm=true
```

#### create-hmi-trends

Plans or applies several logging configurations in one operation. The default is `--dry-run=true`; use `--dry-run=false` after reviewing planned entries.

```bash
OpenTiaToolkit.CLI.exe create-hmi-trends --hmi-name="HMI_1" --configs="[{\"tagPath\":\"Motor.PV\",\"dataLogName\":\"MotorLog\",\"cyclic\":true,\"cycle\":\"T5s\"},{\"tagPath\":\"Motor.SP\",\"dataLogName\":\"MotorLog\"}]" --dry-run=false
```

Multiple `tagPath` entries can intentionally share the same `dataLogName`. Use that pattern to create one combined archive for related values, such as `Motor.PV`, `Motor.SP`, and `Motor.CURR`, instead of creating one archive per tag. Bulk creation resolves each requested tag path directly; it does not require `list-hmi-tags` discovery first.

#### remove-hmi-logging-tags

Plans or removes multiple verified logging tag associations. Applied deletion requires both `--dry-run=false` and `--confirm=true`.

```bash
OpenTiaToolkit.CLI.exe remove-hmi-logging-tags --hmi-name="HMI_1" --configs="[{\"tagPath\":\"Motor.PV\",\"dataLogName\":\"MotorLog\"},{\"tagPath\":\"Motor.SP\",\"dataLogName\":\"MotorLog\"}]" --dry-run=false --confirm=true
```

#### delete-hmi-data-logs

Plans or deletes multiple unreferenced archives in one request. Archives with active references are reported as blocked without preventing unrelated eligible archives from being processed.

```bash
OpenTiaToolkit.CLI.exe delete-hmi-data-logs --hmi-name="HMI_1" --configs="[{\"dataLogName\":\"MotorLog\"},{\"dataLogName\":\"OldMotorLog\"}]" --dry-run=false --confirm=true
```

### Unified HMI JSON Import/Export

The JSON flow is the repeatable import/export path for Unified HMI screens. It supports screens, items, scalar properties, faceplate interface bindings, faceplate interface entry dynamizations, event handlers, property event handlers, dynamizations, and value-converter mapping tables.

#### export-hmi-json

Exports the supported Unified HMI JSON surface.

```bash
OpenTiaToolkit.CLI.exe export-hmi-json --hmi-name="HMI_1" --output-path="C:\Exports\Hmi"
OpenTiaToolkit.CLI.exe export-hmi-json --hmi-name="HMI_1" --output-path="C:\Exports\Main.json" --screens="Codex/Main" --properties="Text,Visible,EventHandlers,PropertyEventHandlers,Dynamizations"
```

#### diff-hmi-json

Creates a minimal patch from a base export and an edited export.

```bash
OpenTiaToolkit.CLI.exe diff-hmi-json --base-path="C:\Exports\Hmi\Base" --modified-path="C:\Exports\Hmi\Edited" --patch-path="C:\Exports\Hmi\Patch"
```

#### import-hmi-json

Preflights or imports a Unified HMI JSON export or patch.

```bash
OpenTiaToolkit.CLI.exe import-hmi-json --hmi-name="HMI_1" --input-path="C:\Exports\Hmi\Patch" --dry-run=true --fail-on-warning=true
OpenTiaToolkit.CLI.exe import-hmi-json --hmi-name="HMI_1" --input-path="C:\Exports\Hmi\Patch" --dry-run=false --apply=true --confirm=true --require-clean-preflight=true --backup=true --backup-path="C:\Exports\Hmi\Backup"
```

**Parameters**:
- `hmi-name` (required for export/import): Unified HMI name
- `output-path` (export): Target folder or `.json` file
- `base-path`, `modified-path`, `patch-path` (diff): Export or patch folders/files
- `input-path` (import): Source export or patch folder/file
- `dry-run` (import, default `true`): Validate without applying
- `apply` and `confirm` (import, default `false`): Both are required with `dry-run=false` after reviewing a dry-run result
- `fail-on-warning` (import, default `true`): Treat warnings as blocking
- `require-clean-preflight` (import, default `true`): Re-run a clean preflight before applying
- `backup` (import, default `true` on apply) and `backup-path`: Export a backup before mutation; when screen/item/property filters are supplied, the backup uses the same scope.
- `screens`, `item-names`, `properties`: Optional filters for export, diff, and import

Applied HMI JSON imports require the target to be offline in TIA Portal before the Siemens Openness transaction is opened. If TIA reports online mode, the import is returned as a failed command with the structured result preserved and `blockedReason: "tia-online-mode"`; agents should take the HMI/project offline manually, then replay the same explicitly confirmed apply command. The toolkit does not call `GoOffline()` automatically.

#### get-hmi-screen-json

Agent-oriented helper that returns one screen as a canonical `HmiJsonScreen` object with a `baseHash`.

```bash
OpenTiaToolkit.CLI.exe get-hmi-screen-json --hmi-name="HMI_1" --screen-path="Codex/Main" --properties="Text,Visible,EventHandlers,PropertyEventHandlers,Dynamizations"
OpenTiaToolkit.CLI.exe get-hmi-screen-json --hmi-name="HMI_1" --screen-path="Codex/Main" --include-screen=false
```

Large snapshots are automatically returned as a compact artifact manifest with `outputPath`, `baseHash`, filters, and recommended patch arguments. The full canonical screen JSON remains on disk at the reported export path. Use `--include-screen=false` when you want the file manifest even for smaller snapshots.

**Agent loop**:
- Read the screen with `get-hmi-screen-json`.
- For broad edits, update the returned `data.screen` object and send it with `data.baseHash` to `apply-hmi-screen-json-edit`.
- For small targeted edits, send compact operations with the same `data.baseHash` to `apply-hmi-screen-json-patch`.

#### apply-hmi-screen-json-edit

Dry-runs or applies one edited screen JSON object through the same export, diff, preflight, transactional import, backup, and verify pipeline used by the lower-level JSON commands. `dry-run` defaults to `true`; `backup` defaults to `true` when applying. For filtered screen edits, backup uses the same screen/item/property filters rather than exporting the whole HMI.

```bash
OpenTiaToolkit.CLI.exe apply-hmi-screen-json-edit --hmi-name="HMI_1" --screen-path="Codex/Main" --edited-screen="{...}" --base-hash="..." --dry-run=true
OpenTiaToolkit.CLI.exe apply-hmi-screen-json-edit --hmi-name="HMI_1" --screen-path="Codex/Main" --edited-screen="{...}" --base-hash="..." --dry-run=false
```

**Parameters**:
- `edited-screen` (required): Edited `HmiJsonScreen` object. MCP clients should pass a JSON object; CLI users can pass a JSON string.
- `base-hash`: Optional hash returned by `get-hmi-screen-json`; mismatches block the edit by default.
- `allow-stale-base`: Allows edits when the current screen hash differs from `base-hash`.
- `dry-run`: Validate and return patch/preflight details without applying.
- `backup` and `backup-path`: Export a filtered backup before mutation; omit filters only when a full-HMI backup is intended.
- `working-path`: Optional root for generated base, edited, patch, backup, and verify artifacts.

#### apply-hmi-screen-json-patch

Dry-runs or applies compact agent operations by first transforming the current single-screen JSON snapshot, then running the same safe edit pipeline as `apply-hmi-screen-json-edit`. `dry-run` defaults to `true`; `backup` defaults to `true` when applying. Applied imports run inside a Siemens undo transaction: successful imports commit, while safety-blocked or failed property updates roll back. If the HMI/project is online, apply is blocked with `blockedReason: "tia-online-mode"` and `guidance.recommendedNextAction: "take-hmi-offline-and-retry"`; take the target offline in TIA Portal before replaying the same apply payload.

```bash
OpenTiaToolkit.CLI.exe apply-hmi-screen-json-patch --hmi-name="HMI_1" --screen-path="Codex/Main" --operations="{\"operations\":[{\"op\":\"set-property\",\"item\":\"Button_1\",\"property\":\"Text\",\"value\":\"Start\"}]}" --base-hash="..." --dry-run=true
OpenTiaToolkit.CLI.exe apply-hmi-screen-json-patch --hmi-name="HMI_1" --screen-path="Codex/Main" --operations-path="C:\Exports\Hmi\patch-ops.json" --base-hash="..." --item-names="Valve_1" --properties="Interface" --dry-run=true
OpenTiaToolkit.CLI.exe apply-hmi-screen-json-patch --hmi-name="HMI_1" --screen-path="Codex/Main" --operations="{\"operations\":[{\"op\":\"set-layout\",\"item\":\"Button_1\",\"left\":120,\"top\":80,\"width\":180,\"height\":44}]}" --base-hash="..." --dry-run=false
OpenTiaToolkit.CLI.exe apply-hmi-screen-json-patch --hmi-name="HMI_1" --screen-path="Codex/Main" --operations="{\"operations\":[{\"op\":\"create-item\",\"item\":\"StatusText\",\"itemType\":\"text\",\"left\":120,\"top\":140,\"width\":240,\"height\":40,\"properties\":{\"Text\":\"Ready\"}}]}" --base-hash="..." --dry-run=false
OpenTiaToolkit.CLI.exe apply-hmi-screen-json-patch --hmi-name="HMI_1" --screen-path="Codex/Main" --operations="{\"operations\":[{\"op\":\"duplicate-item\",\"source\":\"Button_1\",\"item\":\"Button_2\",\"left\":120,\"top\":200,\"properties\":{\"Text\":\"Start backup\"}},{\"op\":\"delete-item\",\"item\":\"OldButton\"}]}" --base-hash="..." --dry-run=true
OpenTiaToolkit.CLI.exe apply-hmi-screen-json-patch --hmi-name="HMI_1" --screen-path="Codex/Main" --operations="{\"operations\":[{\"op\":\"set-event-handler\",\"item\":\"Button_1\",\"eventType\":\"Click\",\"scriptCode\":\"return 42;\"},{\"op\":\"set-dynamization\",\"item\":\"Button_1\",\"property\":\"Visible\",\"tag\":\"HmiTag_Bool\"}]}" --base-hash="..." --dry-run=true
```

**Supported operations**:
- `set-property`: Set one screen or item property. Omit `item` to target the screen.
- `set-properties`: Set several properties from a `properties` object.
- `move-item`: Set `left` and/or `top` for an item.
- `resize-item`: Set `width` and/or `height` for an item.
- `set-layout`: Set any combination of `left`, `top`, `width`, and `height`.
- `rename-item`: Rename an item using `newName` or `targetName`; `source` is accepted as an alias for the current item name.
- `create-item`: Create an item using `itemType`, geometry fields, and optional `properties`. Rectangular items use `left`/`top`/`width`/`height`; `HmiCircle` / `circle` uses `CenterX`, `CenterY`, and `Radius` inside `properties`.
- `delete-item`: Delete an item from the screen.
- `duplicate-item`: Clone `source`/`sourceItem` into `item`/`targetItem`/`targetName`, with optional geometry and property overrides.
- `set-event-handler` / `remove-event-handler`: Create, replace, or remove event handlers. Use `eventHandler` for full JSON or `eventType` plus `scriptCode` for compact script handlers.
- `set-property-event-handler` / `remove-property-event-handler`: Create, replace, or remove property event handlers. Use `propertyEventHandler` for full JSON or `property`, `eventType`, and `scriptCode`.
- `set-dynamization` / `remove-dynamization`: Create, replace, or remove property dynamizations. Use `dynamization` for full JSON or compact `property`, `dynamizationType`, and `tag`.
- `set-interface`: Set one faceplate container interface binding. Use `item`, `property`/`propertyName`, and `value`; filter with the root property `Interface`. Interface entries can also contain nested `Dynamizations`, including `TagParameter` bindings. Compact `set-interface` can set a nested interface dynamization with `dynamizationType` and `dynamicTagName`; when omitted, the nested dynamization property defaults to the interface entry name exported by TIA.

**Parameters**:
- `operations` (required): JSON array or `{ "operations": [...] }`. MCP clients should pass this as an object/array instead of a CLI string.
- `operations-path`: Optional JSON file containing the operations array or `{ "operations": [...] }`; use this for large or quote-heavy patches.
- `base-hash`: Optional hash returned by `get-hmi-screen-json`; mismatches block the patch by default.
- `item-names` and `properties`: When using `base-hash`, keep these identical to the snapshot filters so the hash represents the same JSON shape. `get-hmi-screen-json` returns `snapshotFilters` and `recommendedPatchArgs` to make this round-trip explicit for agents. For `duplicate-item`, include both source and target item names. For composite operations, include the composite root property: `EventHandlers`, `PropertyEventHandlers`, `Dynamizations`, or `Interface`.
- Circle geometry: For `HmiCircle` / `circle`, use `CenterX`, `CenterY`, and `Radius` in the operation `properties` object and include those names in the filter. Do not use `Left`, `Top`, `Width`, or `Height` for circles in JSON patch/edit payloads.
- `allow-stale-base`: Allows patches when the current screen hash differs from `base-hash`.
- `dry-run`: Validate and return operation, diff, and preflight details without applying.
- `backup` and `backup-path`: Export a backup before mutation using the same targeted screen/item/property filters.
- `working-path`: Optional root for generated base, edited, patch, backup, and verify artifacts.

Patch/edit results include `guidance`, which tells agents whether to apply, re-read, fix filters, inspect preflight, or verify. Successful apply results include `newBaseHash`, computed from the verified filtered screen export, for the next patch batch. The nested import result reports `transactionUsed` and `rolledBack`. If a client times out or loses a response, do not issue another mutation immediately: reconnect or run `hmi-status` after the in-flight request completes, then re-read the same filtered target before deciding what to retry.

`get-hmi-screen-json` requires canonical grouped screen paths. If a leaf name uniquely matches a nested screen, a failed lookup now suggests the canonical path; `list-hmi-screens --recursive=true` remains the discovery command for ambiguous names.

Large `set-interface` batches remain sensitive to TIA Openness latency because each changed interface binding is applied through its underlying engineering write. Targeted backups, verified hash chaining, and transaction safety reduce overhead and recovery risk, but callers should still keep batch sizes practical for the project being edited.

Faceplate interface entries may export as richer objects when TIA exposes metadata:

```json
{
  "propertyName": "First_Step_NR",
  "valueType": "String",
  "value": "Step_1",
  "dynamizations": [
    {
      "property": "First_Step_NR",
      "type": "TagParameter",
      "binding": {
        "dynamicTagName": "Step_'Step_1'"
      }
    }
  ]
}
```

#### get-hmi-screen-summary

Returns compact screen counts without exporting a large payload. Use this before broad HMI edits to understand item counts and faceplate contained types. Set `include-faceplates=true` to return faceplate-only discovery details: contained type, `GEN`, normalized `NAME` display text, and `FORMAT`, plus configured and placeholder counts.

```bash
OpenTiaToolkit.CLI.exe get-hmi-screen-summary --hmi-name="HMI_1" --screen-path="Codex/Main"
OpenTiaToolkit.CLI.exe get-hmi-screen-summary --hmi-name="HMI_1" --screen-path="Codex/Main" --include-items=true
OpenTiaToolkit.CLI.exe get-hmi-screen-summary --hmi-name="HMI_1" --screen-path="Codex/Main" --include-faceplates=true
```

`include-items` returns all screen items, whereas `include-faceplates` returns only faceplate containers with key interface bindings. Missing interface properties are returned as `null`.

#### hmi-status

Reports TIA/HMI session health, current project, selected HMI, running TIA instances, and optionally probes a screen. Use it after Openness failures or before long HMI workflows.

```bash
OpenTiaToolkit.CLI.exe hmi-status --hmi-name="HMI_1" --screen-path="Codex/Main" --open-if-needed=true
```

#### probe-hmi-faceplate-interface

Inspects a faceplate container's `Interface` composition and optionally performs one direct write probe with restore. This is diagnostic tooling for separating Siemens Openness setter behavior from the JSON patch/import pipeline.

```bash
OpenTiaToolkit.CLI.exe probe-hmi-faceplate-interface --hmi-name="HMI_1" --screen-path="Codex/Main" --item-name="Valve_1" --property="GEN"
OpenTiaToolkit.CLI.exe probe-hmi-faceplate-interface --hmi-name="HMI_1" --screen-path="Codex/Main" --item-name-path="C:\Exports\Hmi\item-name.txt" --property="GEN" --write=true --value="Valve_1_GEN" --restore=true
```

#### audit-hmi-label-proximity

Creates an offline JSON report from an exported HMI JSON file showing nearest-label matches before a bulk edit. The report includes target item, matched label item/text, distance, deltas, candidate counts, skipped candidates, and duplicate-label flags.

```bash
OpenTiaToolkit.CLI.exe audit-hmi-label-proximity --input-path="C:\Exports\Hmi\Main.json" --screen-path="Codex/Main" --label-texts="V116" --output-path="C:\Exports\Hmi\Main_label_audit.json"
```

#### inspect-hmi-screen-json-capabilities

Returns compact patch capabilities, supported item types, item names, exported properties, optional writable property metadata, patchable composite roots, and operation hints. The full screen snapshot is omitted by default to keep agent context small.

```bash
OpenTiaToolkit.CLI.exe inspect-hmi-screen-json-capabilities --hmi-name="HMI_1" --screen-path="Codex/Main" --item-names="Button_1" --properties="Text,Left,Top,Width,Height,EventHandlers,Dynamizations"
```

**Parameters**:
- `hmi-name` and `screen-path` are required.
- `item-names` and `properties` scope the snapshot and returned `recommendedPatchArgs`.
- `include-item-types`: Include supported item types and aliases. Defaults to `true`.
- `include-writable-properties`: Include writable property metadata for exported items. Defaults to `true`.
- `include-snapshot`: Include the full screen snapshot in the result. Defaults to `false`; use `get-hmi-screen-json` when the snapshot is the main output you need.
- `working-path`: Optional root for generated snapshot artifacts.

### Library Operations

These commands work with the currently connected TIA Portal and open project. `library-scope` defaults to `project`; pass `library-scope=global` with `global-library` after opening a user global library.

#### Global library lifecycle

```bash
OpenTiaToolkit.CLI.exe list-global-libraries
OpenTiaToolkit.CLI.exe open-global-library --path="C:\Libraries\Company.al21" --read-only=true
OpenTiaToolkit.CLI.exe close-global-library --global-library="Company"
```

#### Master copies

Lists approved master copies from the project library or an open global library. Results can be written to JSON with `--output-path`.

```bash
OpenTiaToolkit.CLI.exe list-master-copies --library-scope=project
OpenTiaToolkit.CLI.exe list-master-copies --library-scope=global --global-library="Company" --folder-path="Motion" --output-path="C:\Exports\master_copies.json"
```

Copies a master copy into a PLC block group. The command defaults to `dry-run=true`; applying requires `--dry-run=false --confirm=true`.

```bash
OpenTiaToolkit.CLI.exe copy-from-master-copy --master-copy-path="Motion/MotorFB" --plc-name="PLC_1" --target-group-path="Motors"
OpenTiaToolkit.CLI.exe copy-from-master-copy --library-scope=global --global-library="Company" --master-copy-path="Motion/MotorFB" --plc-name="PLC_1" --target-group-path="Motors" --dry-run=false --confirm=true
```

#### Library types and update checks

```bash
OpenTiaToolkit.CLI.exe list-library-types --library-scope=project --output-path="C:\Exports\library_types.json"
OpenTiaToolkit.CLI.exe get-library-type-versions --library-scope=global --global-library="Company" --type-path="Motion/MotorType"
OpenTiaToolkit.CLI.exe check-library-updates --library-scope=global --global-library="Company" --include-up-to-date=false --output-path="C:\Exports\library_updates.json"
```

### PLC Operations

#### list-plcs

Lists all PLC devices found in the currently open project.

```bash
OpenTiaToolkit.CLI.exe list-plcs
```

**Parameters**:
- None

**Output**:
```
Found 2 PLC device(s):

Name       Device Name    Type
---------------------------------
PLC_1      PLC_1          S7-1500
PLC_2      PLC_2          S7-1500
```

#### open-plc

Opens PLC device software.

```bash
OpenTiaToolkit.CLI.exe open-plc --plc-name="PLC_1"
```

**Parameters**:
- `plc-name` (required): Name of the PLC device

**Output**:
```
PLC software opened: PLC_1
```

#### get-software-tree

Gets the PLC software structure (blocks, types, sources).

```bash
OpenTiaToolkit.CLI.exe get-software-tree --plc-name="PLC_1"
OpenTiaToolkit.CLI.exe get-software-tree --plc-name="PLC_1" --depth=3
OpenTiaToolkit.CLI.exe get-software-tree --plc-name="PLC_1" --output-path="C:\Exports\plc_software_tree.json"
```

**Parameters**:
- `plc-name` (required): Name of the PLC device
- `depth` (optional): Maximum depth to traverse (null for unlimited)
- `output-path` (optional): JSON file or folder path for the PLC software tree

When `--output-path` is provided, the PLC software tree is written as JSON. Without `--output-path`, large tree results that exceed the 32 KB inline response threshold are automatically written to a temporary JSON artifact.

**Output**:
```
PLC Software Tree for PLC_1:

PLC_1 (PlcSoftware)
+-- Blocks
|   +-- OB1 (Organization Block)
|   +-- FC1 (Function)
|   +-- FB1 (Function Block)
+-- Types
|   +-- MotorType (User Defined Type)
+-- Source Files
    +-- Main.xml
```

#### compile-software

Compiles PLC software.

```bash
OpenTiaToolkit.CLI.exe compile-software --plc-name="PLC_1"
OpenTiaToolkit.CLI.exe compile-software --plc-name="PLC_1" --password="<block-password>"
```

**Parameters**:
- `plc-name` (required): Name of the PLC device
- `password` (optional): Password for protected blocks

For the current V21 implementation, the password argument is accepted for command compatibility. See [TIA Portal version support](TIA_PORTAL_VERSION_SUPPORT.md#plc-compilation) for the current validation notes.

**Output**:
```
Compilation Result for PLC_1:

Success:       True
Error Count:   0
Warning Count: 2
Message:       Compilation completed successfully
```

#### compare-software

Compares PLC software to the online target and returns a structured difference tree.

```bash
OpenTiaToolkit.CLI.exe compare-software --plc-name="PLC_1"
OpenTiaToolkit.CLI.exe compare-software --plc-name="PLC_1" --output-path="C:\Exports\plc_compare.json"
```

**Parameters**:
- `plc-name` (required): Name of the PLC device
- `target` (optional, default: `online`): Compare target. Currently supported: `online`
- `output-path` (optional): JSON file or folder path for compare results

When `--output-path` is provided, the compare tree is written as JSON. Without `--output-path`, large compare results that exceed the 32 KB inline response threshold are automatically written to a temporary JSON artifact.

#### download-software

Plans or downloads PLC software to PLCSIM Advanced. Real-hardware download is deliberately blocked.

```bash
OpenTiaToolkit.CLI.exe download-software --plc-name="PLC_1" --to-simulator=true
OpenTiaToolkit.CLI.exe download-software --plc-name="PLC_1" --to-simulator=true --dry-run=false --confirm=true
OpenTiaToolkit.CLI.exe download-software --plc-name="PLC_1" --to-simulator=true --dry-run=false --confirm=true --only-changes=true
```

**Parameters**:
- `plc-name` (required): Name of the PLC device
- `to-simulator` (required): Must be `true`; selects the guarded PLCSIM Advanced path
- `dry-run` (optional, default: `true`): Validate and return the planned download without applying
- `only-changes` (optional, default: `false`): Use TIA `SoftwareOnlyChanges` instead of a full software load
- `confirm` (optional, default: `false`): Required when `dry-run=false`

Non-dry-run downloads require TIA Portal to already have a configured PLCSIM Advanced download connection for the selected PLC.
TIA Openness may still block downloads for fail-safe projects; when Siemens returns that fail-safe data cannot be loaded through Openness, use TIA Portal's UI workflow for that safety download step.

#### get-online-status

Gets PLC online status and optionally asks TIA Portal to go online first.

```bash
OpenTiaToolkit.CLI.exe get-online-status --plc-name="PLC_1"
OpenTiaToolkit.CLI.exe get-online-status --plc-name="PLC_1" --go-online=false
```

**Parameters**:
- `plc-name` (required): Name of the PLC device
- `go-online` (optional, default: `true`): Attempt to go online before reading status

#### start-cpu / stop-cpu

Attempts to start or stop the online CPU when the selected TIA online provider exposes a supported runtime operation.

```bash
OpenTiaToolkit.CLI.exe start-cpu --plc-name="PLC_1" --confirm=true
OpenTiaToolkit.CLI.exe stop-cpu --plc-name="PLC_1" --confirm=true
```

**Parameters**:
- `plc-name` (required): Name of the PLC device
- `confirm` (optional, default: `false`): Required for runtime state changes

Some TIA Portal online providers expose connection state but not CPU RUN/STOP operations through Openness. In that case these commands return a structured not-supported result listing the available online invocations.

#### export-plc

Exports all PLC blocks to XML files.

```bash
OpenTiaToolkit.CLI.exe export-plc --plc-name="PLC_1" --export-path="C:\Exports"
```

**Parameters**:
- `plc-name` (required): Name of the PLC device
- `export-path` (required): Directory to export XML files

**Output**:
```
Exported PLC blocks from PLC_1 to: C:\Exports

Blocks exported:
- OB1.xml
- FC1.xml
- FB1.xml
- MotorType.xml
```

#### get-blocks

Lists blocks from PLC software.

```bash
OpenTiaToolkit.CLI.exe get-blocks --plc-name="PLC_1"
OpenTiaToolkit.CLI.exe get-blocks --plc-name="PLC_1" --filter="Motor"
OpenTiaToolkit.CLI.exe get-blocks --plc-name="PLC_1" --output-path="C:\Exports\plc_blocks.csv"
```

**Parameters**:
- `plc-name` (required): Name of the PLC device
- `filter` (optional): Regex filter for block names
- `output-path` (optional): CSV file or folder path for PLC block rows

When `--output-path` is provided, PLC block rows are written as CSV. Without `--output-path`, large block lists that exceed the 32 KB inline response threshold are automatically written to a temporary CSV artifact.

**Output**:
```
Found 3 blocks in PLC_1:

Name          Type                  Language    Number
----------------------------------------------------------
OB1           Organization Block    FBD         1
FC_Motor      Function              STL         10
FB_MotorCtrl  Function Block        FBD         1
```

#### create-block

Creates a new PLC block. The current validated creation path is Function Block (`FB`) with `ProDiag`/`S7_PDIAG` language in TIA Portal V21.

```bash
OpenTiaToolkit.CLI.exe create-block --plc-name="PLC_1" --block-name="FB_Diagnostics" --block-type="FB" --language="ProDiag"
```

**Parameters**:
- `plc-name` (required): Name of the PLC device
- `block-name` (required): Name of the block to create
- `block-type` (required): Block type to create. Currently validated value: `FB`
- `language` (optional): Programming language. Currently validated values: `ProDiag`, `S7_PDIAG`

**Output**:
```
Block 'FB_Diagnostics' created successfully
```

**Safety Notes**:
- This command mutates the open project. Create a backup or use a disposable validation project before applying it to production work.
- Generic FC/DB/OB/UDT creation is not currently supported by this command.

#### delete-blocks

Deletes blocks matching a regex pattern from PLC software. Requires confirmation for safety.

```bash
OpenTiaToolkit.CLI.exe delete-blocks --plc-name="PLC_1" --filter="Test_.*"
OpenTiaToolkit.CLI.exe delete-blocks --plc-name="PLC_1" --filter="Test_.*" --confirm=true
```

**Parameters**:
- `plc-name` (required): Name of the PLC device
- `filter` (required): Regex pattern to match block names
- `confirm` (optional): Skip confirmation prompt (default: false)

**Output**:
```
Preview: Found 3 blocks matching filter "Test_.*":
  - FC_Test1 (Function)
  - FB_Test2 (Function Block)
  - DB_TestData (Data Block)

Proceed with deletion? (Use --confirm=true to skip this prompt)
Deleted 3 blocks, skipped 0, failed 0
```

**Safety Features**:
- Preview of blocks to be deleted before confirmation
- Skips read-only/protected blocks with warning
- Requires `--confirm=true` flag to proceed without prompt
- Returns count of deleted, skipped, and failed blocks

#### get-block-info

Gets detailed information about a specific PLC block.

```bash
OpenTiaToolkit.CLI.exe get-block-info --plc-name="PLC_1" --block-name="Main"
```

**Parameters**:
- `plc-name` (required): Name of the PLC device
- `block-name` (required): Name of the block

**Output**:
```
Block Information:

Name:            Main
Type:            Organization Block
Number:          1
Language:        FBD
Read-Only:       No
Comment:         Main Cycle Block
Author:          (if available)
Memory Size:     (if available)
Programming Language: FBD
```

#### export-block

Exports a single PLC block to an XML file.

```bash
OpenTiaToolkit.CLI.exe export-block --plc-name="PLC_1" --block-name="Main" --export-path="C:\Exports\Main.xml"
```

**Parameters**:
- `plc-name` (required): Name of the PLC device
- `block-name` (required): Name of the block to export
- `export-path` (required): Full file path for the exported XML file

**Output**:
```
Exported block 'Main' to: C:\Exports\Main.xml
```

**Note**: The `export-path` parameter is a full file path (not a directory). The file will be created at the specified location.

#### search-blocks

Searches PLC blocks by name or comment with optional type filter.

```bash
OpenTiaToolkit.CLI.exe search-blocks --plc-name="PLC_1" --pattern="Motor"
OpenTiaToolkit.CLI.exe search-blocks --plc-name="PLC_1" --pattern="Test" --search-in="comment"
OpenTiaToolkit.CLI.exe search-blocks --plc-name="PLC_1" --pattern="FB" --filter-type="FB"
OpenTiaToolkit.CLI.exe search-blocks --plc-name="PLC_1" --pattern="Motor" --output-path="C:\Exports\motor_blocks.csv"
```

**Parameters**:
- `plc-name` (required): Name of the PLC device
- `pattern` (required): Search pattern (supports wildcards)
- `search-in` (optional): Where to search - "name" or "comment" (default: "name")
- `filter-type` (optional): Block type filter (FB, FC, DB, OB)
- `output-path` (optional): CSV file or folder path for matching PLC block rows

When `--output-path` is provided, matching block rows are written as CSV. Without `--output-path`, large search results that exceed the 32 KB inline response threshold are automatically written to a temporary CSV artifact.

**Output**:
```
Found 2 blocks matching pattern "Motor":

Name          Type                  Language    Number    Comment
----------------------------------------------------------------------------
FC_Motor      Function              STL         10        Motor control
FB_MotorCtrl  Function Block        FBD         1        Motor controller
```

### SIMATIC SD Export

#### export-block-as-docs

Exports a single PLC block as SIMATIC SD documents (.s7dcl + .s7res) in the supported TIA Portal V21 workflow.

```bash
OpenTiaToolkit.CLI.exe export-block-as-docs --plc-name="PLC_1" --block-name="Motor" --export-path="C:\Exports"
```

**Parameters**:
- `plc-name` (required): Name of the PLC device
- `block-name` (required): Name of the block to export
- `export-path` (required): Directory path for exported files

**Output Files**:
- `{block-name}.s7dcl` - Multi-language document file
- `{block-name}.s7res` - Resource file

**Version Requirements**:
- Requires a supported TIA Portal version

**Output**:
```
Successfully exported block as SIMATIC SD documents: Motor

Format: SIMATIC SD (.s7dcl + .s7res)
```

#### export-blocks-as-docs

Exports multiple PLC blocks as SIMATIC SD documents with optional regex filtering in the supported TIA Portal V21 workflow.

```bash
# Export all blocks
OpenTiaToolkit.CLI.exe export-blocks-as-docs --plc-name="PLC_1" --export-path="C:\Exports"

# Export with regex filter
OpenTiaToolkit.CLI.exe export-blocks-as-docs --plc-name="PLC_1" --filter-regex="Motor_.*" --export-path="C:\Exports"
```

**Parameters**:
- `plc-name` (required): Name of the PLC device
- `export-path` (required): Directory path for exported files
- `filter-regex` (optional): Regex filter for block names (e.g., "Motor_.*")

**Output**:
```
Exported 5/6 blocks as SIMATIC SD documents

Summary:
  Total:     6
  Succeeded: 5
  Failed:    1

Exported:
  - Motor_1
  - Motor_2
  - Motor_3
  - Motor_4
  - Motor_5

Failed:
  - Motor_Test (inconsistent)

Format: SIMATIC SD (.s7dcl + .s7res)
```

### Version Control Interface (VCI)

VCI maintains a workspace and persistent mappings between PLC objects and version-controlled files. Unlike one-off import/export, synchronization compares mapped content and updates only in the selected direction.

```bash
# Create a new workspace repository
OpenTiaToolkit.CLI.exe create-vci-workspace --workspace-name="PLC_Repo" --root-path="C:\Repo\PLC_1" --workspace-language="en-US"

# Inspect workspaces and supported block formats
OpenTiaToolkit.CLI.exe list-vci-workspaces
OpenTiaToolkit.CLI.exe list-vci-mappings --workspace-name="PLC_Repo" --output-path="C:\Exports\vci_mappings.csv"
OpenTiaToolkit.CLI.exe get-vci-block-formats --plc-name="PLC_1" --block-name="FC_Control" --block-type="FC"

# Create a surgical S7DCL mapping and synchronize project to workspace
OpenTiaToolkit.CLI.exe export-vci-block --workspace-name="PLC_Repo" --plc-name="PLC_1" --block-name="FC_Control" --block-type="FC" --relative-directory="." --file-format="SimaticSD"
OpenTiaToolkit.CLI.exe sync-vci-block --workspace-name="PLC_Repo" --plc-name="PLC_1" --block-name="FC_Control" --block-type="FC" --direction="project-to-workspace"

# Bulk operation limited by regex, optionally creating mappings
OpenTiaToolkit.CLI.exe sync-vci-blocks --workspace-name="PLC_Repo" --plc-name="PLC_1" --direction="project-to-workspace" --filter-regex="^FC_Control$" --create-mappings=true --relative-directory="." --file-format="SimaticSD"
OpenTiaToolkit.CLI.exe sync-vci-blocks --workspace-name="PLC_Repo" --plc-name="PLC_1" --direction="project-to-workspace" --include-skipped=true --output-path="C:\Exports\vci_block_sync.json"

# Traverse the PLC without automatically mapping new objects
OpenTiaToolkit.CLI.exe sync-vci-plc --workspace-name="PLC_Repo" --plc-name="PLC_1" --direction="project-to-workspace" --scope="all-supported"

# Include individual skipped unmapped items when an audit needs full detail
OpenTiaToolkit.CLI.exe sync-vci-plc --workspace-name="PLC_Repo" --plc-name="PLC_1" --direction="project-to-workspace" --scope="all-supported" --include-skipped=true
OpenTiaToolkit.CLI.exe sync-vci-plc --workspace-name="PLC_Repo" --plc-name="PLC_1" --direction="project-to-workspace" --scope="all-supported" --include-skipped=true --output-path="C:\Exports\vci_plc_sync.json"
```

**Commands**:
- `list-vci-workspaces`, `get-vci-workspace`, `create-vci-workspace`, `delete-vci-workspace`
- `list-vci-mappings`, `get-vci-block-formats`, `get-vci-block-status`
- `export-vci-block`, `connect-vci-block`, `sync-vci-block`
- `sync-vci-blocks`, `sync-vci-plc`

**Synchronization options**:
- `direction`: `project-to-workspace` or `workspace-to-project`
- `scope` for `sync-vci-plc`: `blocks`, `blocks-and-types`, or `all-supported`
- `all-supported` covers blocks, PLC types, tag tables, and technological instance DBs exposed by the V21 API.
- `create-mappings=true` is supported for project-to-workspace synchronization. Workspace-to-project synchronization requires an existing mapping created with `connect-vci-block`.
- `include-skipped=true` adds per-object entries for skipped unmapped items. It defaults to `false`; counts and failures are always returned.
- `output-path` writes large review results directly: `list-vci-mappings` writes CSV, while `sync-vci-blocks` and `sync-vci-plc` write JSON. Without `output-path`, results above the 32 KB inline response threshold automatically spill to a temporary artifact.

**Repository notes**:
- For a new repository, provide `workspace-language` when creating the workspace.
- To reconnect a repository that already contains `.vci` metadata, omit `workspace-language`; Siemens reports an error if the repository language is configured again.
- The flat workspace root (`--relative-directory="."`) is validated for S7DCL mappings on V21. Siemens may reject nested mapping directories depending on workspace configuration.
- A mapping with compare state `Equal` is already synchronized; the toolkit treats it as a successful no-op because Siemens rejects invoking synchronization on equal content.

### Type Export (UDT)

#### export-type

Exports a single User Defined Type (UDT) to XML.

```bash
OpenTiaToolkit.CLI.exe export-type --plc-name="PLC_1" --type-name="MotorData" --export-path="C:\Exports\MotorData.xml"
```

**Parameters**:
- `plc-name` (required): Name of the PLC device
- `type-name` (required): Name of the type to export
- `export-path` (required): Full file path for the exported XML file

**Validation**:
- Checks `IsConsistent` property before export
- Returns error if type is inconsistent

**Output**:
```
Successfully exported type: MotorData

Format: XML
```

#### export-types

Exports multiple User Defined Types (UDT) to XML with optional regex filtering.

```bash
# Export all types
OpenTiaToolkit.CLI.exe export-types --plc-name="PLC_1" --export-path="C:\Exports"

# Export with regex filter
OpenTiaToolkit.CLI.exe export-types --plc-name="PLC_1" --filter-regex="Motor_.*" --export-path="C:\Exports"
```

**Parameters**:
- `plc-name` (required): Name of the PLC device
- `export-path` (required): Directory path for exported XML files
- `filter-regex` (optional): Regex filter for type names (e.g., "Motor_.*")

**Output**:
```
Exported 3/4 types to XML

Summary:
  Total:     4
  Succeeded: 3
  Failed:    1

Exported:
  - Motor_1.xml
  - Motor_2.xml
  - Motor_3.xml

Failed:
  - Motor_Test (inconsistent)
```

### Import Operations (Phase 4.6)

#### import-block

Imports a single PLC block from an XML file.

```bash
# Import to root of PLC program
OpenTiaToolkit.CLI.exe import-block --plc-name="PLC_1" --import-path="C:\Imports\FB_Motor.xml"

# Import to specific group path
OpenTiaToolkit.CLI.exe import-block --plc-name="PLC_1" --import-path="C:\Imports\FB_Motor.xml" --group-path="Program Blocks/Motors"
```

**Parameters**:
- `plc-name` (required): Name of the PLC device
- `import-path` (required): Full path to the XML file to import
- `group-path` (optional): Path within PLC program (e.g., "Program Blocks/Motors"). Empty for root.

**Output**:
```
Successfully imported block from: C:\Imports\FB_Motor.xml
```

#### import-blocks

Imports multiple PLC blocks from XML files in a directory with optional regex filtering.

```bash
# Import all blocks from directory
OpenTiaToolkit.CLI.exe import-blocks --plc-name="PLC_1" --import-directory="C:\Imports\Blocks"

# Import with regex filter
OpenTiaToolkit.CLI.exe import-blocks --plc-name="PLC_1" --import-directory="C:\Imports\Blocks" --filter-regex="FB_.*"

# Import to specific group path
OpenTiaToolkit.CLI.exe import-blocks --plc-name="PLC_1" --import-directory="C:\Imports\Blocks" --group-path="Program Blocks/Motors"
```

**Parameters**:
- `plc-name` (required): Name of the PLC device
- `import-directory` (required): Directory containing XML files to import
- `filter-regex` (optional): Regex filter for file names (e.g., "FB_.*")
- `group-path` (optional): Path within PLC program (e.g., "Program Blocks/Motors"). Empty for root.

**Output**:
```
Successfully imported 5/6 blocks

Summary:
  Total:     6
  Succeeded: 5
  Failed:    1

Imported:
  - FB_Motor_1.xml
  - FB_Motor_2.xml
  - FB_Motor_3.xml
  - FB_Valve.xml
  - FC_Control.xml

Failed:
  - FB_Test.xml (invalid format)
```

#### import-type

Imports a single User Defined Type (UDT) from an XML file.

```bash
# Import to root of type groups
OpenTiaToolkit.CLI.exe import-type --plc-name="PLC_1" --import-path="C:\Imports\MotorData.xml"

# Import to specific type group path
OpenTiaToolkit.CLI.exe import-type --plc-name="PLC_1" --import-path="C:\Imports\MotorData.xml" --group-path="Types/Motor"
```

**Parameters**:
- `plc-name` (required): Name of the PLC device
- `import-path` (required): Full path to the XML file to import
- `group-path` (optional): Path within type groups (e.g., "Types/Subgroups"). Empty for root.

**Output**:
```
Successfully imported type from: C:\Imports\MotorData.xml
```

#### import-types

Imports multiple User Defined Types (UDT) from XML files in a directory with optional regex filtering.

```bash
# Import all types from directory
OpenTiaToolkit.CLI.exe import-types --plc-name="PLC_1" --import-directory="C:\Imports\Types"

# Import with regex filter
OpenTiaToolkit.CLI.exe import-types --plc-name="PLC_1" --import-directory="C:\Imports\Types" --filter-regex="Motor_.*"

# Import to specific type group path
OpenTiaToolkit.CLI.exe import-types --plc-name="PLC_1" --import-directory="C:\Imports\Types" --group-path="Types/Motor"
```

**Parameters**:
- `plc-name` (required): Name of the PLC device
- `import-directory` (required): Directory containing XML files to import
- `filter-regex` (optional): Regex filter for file names (e.g., "Motor_.*")
- `group-path` (optional): Path within type groups (e.g., "Types/Subgroups"). Empty for root.

**Output**:
```
Successfully imported 3/4 types

Summary:
  Total:     4
  Succeeded: 3
  Failed:    1

Imported:
  - Motor_1.xml
  - Motor_2.xml
  - Motor_3.xml

Failed:
  - Motor_Test.xml (dependency missing)
```

#### Guarded SIMATIC SD authoring and import

Use the guarded workflow for agent-authored or modified `.s7dcl` files:

```bash
OpenTiaToolkit.CLI.exe validate-plc-code-artifact --code-format=s7dcl --input-path="C:\Imports\Docs\Motor.s7dcl"
OpenTiaToolkit.CLI.exe validate-plc-documents --import-directory="C:\Imports\Docs" --file-name="Motor"
OpenTiaToolkit.CLI.exe discover-plc-code-capabilities --plc-name="PLC_1" --object-name="Motor" --block-type=FB --operation=Modify --requested-language=LAD
OpenTiaToolkit.CLI.exe begin-plc-code-transaction --plc-name="PLC_1" --description="Update Motor" --capability-id="<capability-id>"
OpenTiaToolkit.CLI.exe add-plc-code-transaction-operation --transaction-id="<id>" --import-directory="C:\Imports\Docs" --file-name="Motor" --target-mode=create-only --import-option="Override" --confirm=true
OpenTiaToolkit.CLI.exe add-plc-code-transaction-operation --transaction-id="<id>" --import-directory="C:\Changes" --file-name="Motor" --target-mode=baseline-bound --baseline-directory="C:\Exports\Untouched" --allowed-changes=Implementation --import-option="Override" --confirm=true
OpenTiaToolkit.CLI.exe plan-plc-code-transaction --transaction-id="<id>"
# When planning returns ImpactReviewRequired rather than an apply token:
OpenTiaToolkit.CLI.exe get-plc-code-impact --transaction-id="<id>"
OpenTiaToolkit.CLI.exe approve-plc-code-impact --transaction-id="<id>" --impact-fingerprint="<reviewed-fingerprint>" --confirm=true
OpenTiaToolkit.CLI.exe apply-plc-code-transaction --transaction-id="<id>" --apply-token="<token-from-plan>" --confirm=true
```

`validate-plc-documents` runs without TIA and checks UTF-8 decoding, block/file-name agreement, block/network/rung boundaries, conditional S7RES presence, duplicate/missing `MLC_*` resources, branch junctions, instruction arguments, typed templates, timer/counter/edge instance rules, and safety-language boundaries. Each instruction is classified as `verified`, `known-unsupported`, or `unknown-requires-live-probe`. Known-unsupported forms fail validation; unknown project-specific calls remain valid but require guarded Siemens import and compile verification. UTF-8 without BOM is reported as a warning.

The persistent transaction validates the input and binds one exact target identity: PLC, normalized block-group path, declared block type, name, and block number when present. Ambiguous names and wrong-type collisions fail closed. It exports that exact block and computes input, baseline, and impact hashes. A `Safe` plan returns a restart-safe thirty-minute one-use apply token; other plans follow the impact-review branch below. Use `group-path` whenever the target is not in the root Program blocks group.

`apply-block-document-import` rejects changed input or exact-target state, consumes the token once, imports into the planned group, verifies group/type/name/number, and compiles. If import/read-back/compile fails, it restores the exact prior document or deletes only the exact newly created target, re-exports the restoration, requires the restored hash to equal the baseline hash, and recompiles. An unproven rollback is reported as a hard failure requiring inspection. It does not save the project.

### Persistent multi-block PLC code transactions

Use a PLC code transaction for every agent-authored change, including one block. The lifecycle is explicit and persistent:

```powershell
OpenTiaToolkit.CLI.exe discover-plc-code-capabilities --plc-name="PLC_1" --object-name="FB_Conveyor" --block-type=FB --operation=Create --requested-language=LAD
OpenTiaToolkit.CLI.exe discover-plc-code-capabilities --plc-name="PLC_1" --object-name="FC_ConveyorCall" --block-type=FC --operation=Create --requested-language=LAD
OpenTiaToolkit.CLI.exe begin-plc-code-transaction --plc-name="PLC_1" --description="Add conveyor logic" --compile-policy=NoNewErrors --capability-ids="<fb-capability-id>,<fc-capability-id>"
OpenTiaToolkit.CLI.exe add-plc-code-transaction-operation --transaction-id="<id>" --import-directory="C:\changes" --file-name="FB_Conveyor" --group-path="Conveyor" --confirm=true
OpenTiaToolkit.CLI.exe add-plc-code-transaction-operation --transaction-id="<id>" --import-directory="C:\changes" --file-name="FC_ConveyorCall" --depends-on=1 --confirm=true
# Native SCL alternative:
OpenTiaToolkit.CLI.exe add-scl-source-transaction-operation --transaction-id="<id>" --source-path="C:\changes\Conveyor.scl" --type-group-path="ConveyorTypes" --confirm=true
# Existing-block or PLC-type SimaticML fallback alternative:
OpenTiaToolkit.CLI.exe add-simaticml-transaction-operation --transaction-id="<id>" --input-path="C:\changes\Motor.patch.xml" --baseline-path="C:\changes\Motor.export.xml" --group-path="Conveyor" --confirm=true
# Guarded PLC-type deletion:
OpenTiaToolkit.CLI.exe add-delete-plc-type-transaction-operation --transaction-id="<id>" --type-name="T_Obsolete" --group-path="ConveyorTypes" --confirm=true
OpenTiaToolkit.CLI.exe plan-plc-code-transaction --transaction-id="<id>"
# Safe plans contain an apply token. ReviewRequired/Unknown plans require this review branch:
OpenTiaToolkit.CLI.exe get-plc-code-impact --transaction-id="<id>"
OpenTiaToolkit.CLI.exe approve-plc-code-impact --transaction-id="<id>" --impact-fingerprint="<reviewed-fingerprint>" --accept-unknown=false --confirm=true
OpenTiaToolkit.CLI.exe apply-plc-code-transaction --transaction-id="<id>" --apply-token="<token>" --confirm=true
OpenTiaToolkit.CLI.exe get-plc-code-transaction --transaction-id="<id>"
OpenTiaToolkit.CLI.exe discard-plc-code-transaction --transaction-id="<id>" --confirm=true
```

`discover-plc-code-capabilities` reads the attached TIA version and installed build/update evidence, project, PLC family/type, exact target identity/language/protection, safety context, VCI mappings/status/formats, and live document/source/XML export evidence. It ranks VCI, native documents, native SCL, and SimaticML with explicit eligibility reasons. Safety targets, read-only targets, and ambiguous name-only identities fail closed. VCI selection routes to guarded directional synchronization rather than a direct-import transaction.

`begin-plc-code-transaction` requires and binds one capability id per exact target plus the current TIA process, project, and PLC. Every fingerprint is re-observed at begin, plan, and apply; changed target existence, language, protection, safety, VCI evidence, route evidence, or environment is stale and requires rediscovery. `NoNewErrors` accepts an already-imperfect baseline only when the transaction introduces no additional compile errors; `CleanCompile` requires a clean final compile. `maximum-new-warnings` can additionally cap warning growth.

`plan-plc-code-transaction` validates every input again, resolves every exact block and PLC-type target, rejects duplicate targets, snapshots existing targets before any mutation, records missing baselines for generated targets, and performs a baseline compile. S7DCL operations exposed through the CLI are create-only by default. Existing targets require `target-mode=baseline-bound`, an untouched exported pair, and explicit `allowed-changes` selected from `Attributes`, `Interface`, `Implementation`, and `Resources`; identity drift, unauthorized section changes, a stale baseline, or a no-op candidate fail before mutation. The contract's baseline hash, candidate hash, changed sections, and fingerprint are persisted and revalidated at apply. Planning derives dependencies from authored calls, merges optional `depends-on` operation sequences, rejects cyclic dependencies, and records a deterministic topological execution order. External calls are resolved against the bound PLC block tree; missing or ambiguous dependencies become explicit `Unknown` impact evidence rather than an assumption.

Planning also computes a deterministic impact report over every target. It compares normalized external-interface and persistent-layout signatures, looks up live PLC cross-references for interface/layout/identity/deletion changes, excludes Siemens rows that merely describe the target's own interface while reporting their count, records exact external PLC consumers, and fingerprints dependency and consumer state. New targets and proven body-only replacements are `Safe` and receive a one-use apply token immediately. Interface or layout changes and consumer-free deletions are `ReviewRequired`; unavailable cross-reference or signature evidence is `Unknown`; deletion with active external PLC consumers is `Blocked`. `ImpactReviewRequired` has no apply token. Review it with `get-plc-code-impact`, then use `approve-plc-code-impact` with the exact fingerprint. `Unknown` additionally requires `accept-unknown=true`; `Blocked` cannot be approved. DB/type findings explicitly warn that HMI, recipe, OPC UA, communication, and absolute-address consumers are outside the PLC cross-reference proof.

`approve-plc-code-impact` and `apply-plc-code-transaction` both re-observe the complete impact fingerprint. A changed dependency, caller, consumer set, candidate signature, or baseline makes the transaction stale. `apply-plc-code-transaction` also checks every input and exact target hash as one atomic preflight. It then applies operations in the reviewed dependency order, verifies exact identities, compiles once, and exports canonical TIA documents. Any failure restores all existing targets and removes all newly created targets in reverse execution order. Success is reported only after impact, compile-policy, and canonical-export verification.

SCL operations validate every declared block and PLC `TYPE`, track every generated target, and support a separate `type-group-path`. SimaticML operations require an untouched export of the exact existing block or PLC type, preserve IDs/namespaces/topology by default, re-export current TIA state during planning, and exclude only volatile export metadata from baseline comparison. Exact PLC-type deletion uses the same XML snapshot, compile, canonical read-back, and rollback proof. VCI workspace-to-project sync is separate from transactions and requires `confirm=true`, validated workspace files, existing mappings, post-sync `Equal` status, and compile verification.

Manifests, integrity hashes, and append-only `journal.jsonl` records survive CLI/MCP or worker restarts under `%TEMP%\OpenTiaToolkit\plc-code-transactions`. `list-plc-code-transactions` discovers interrupted work. Use `recover-plc-code-transaction --confirm=true` for `Applying`, `Verifying`, `DisposingProbe`, `RollingBack`, or `RecoveryRequired` states. Recovery conservatively restores every baseline, including operations whose last journal write may have been interrupted. An unproven recovery also writes `recovery.json`; such a transaction cannot be discarded, and its backup package is never age-cleaned automatically.

Transactions do not save the TIA project and do not create Git or VCI commits. Saving and source-control synchronization remain separate explicit decisions after transaction verification.

### Disposable PLC code qualification probes

Use the automated runner only with uniquely named missing targets and `Create` capability manifests. Supply one capability id per SCL declaration or the single S7DCL block:

```powershell
OpenTiaToolkit.CLI.exe validate-plc-code-artifact --code-format=s7dcl --input-path="C:\probes\Codex_LAD_Probe.s7dcl"
OpenTiaToolkit.CLI.exe discover-plc-code-capabilities --plc-name="PLC_1" --object-name="Codex_LAD_Probe" --block-type=FC --operation=Create --requested-language=LAD
OpenTiaToolkit.CLI.exe run-plc-code-probe --plc-name="PLC_1" --capability-ids="<probe-capability-id>" --code-format=s7dcl --input-path="C:\probes\Codex_LAD_Probe.s7dcl" --confirm=true
OpenTiaToolkit.CLI.exe get-plc-code-evidence --evidence-id="<evidence-id>"
OpenTiaToolkit.CLI.exe list-plc-code-evidence --status=Verified

# Query the intended artifact with freshly discovered intended-target capabilities.
OpenTiaToolkit.CLI.exe query-plc-code-evidence --capability-ids="<intended-capability-ids>" --code-format=s7dcl --input-path="C:\changes\ProductionShape.s7dcl"
```

`run-plc-code-probe` composes the normal transaction engine. It validates every create capability, proves every target is missing, snapshots the baseline compile, applies once, verifies exact generated identities, compiles, and exports canonical hashes. Even after success it rolls the transaction back: exact new blocks and PLC types are deleted in reverse order, an SCL external source is removed, absence is re-read, and the baseline compile condition is proven. The local transaction package is discarded only after that cleanup proof.

The persistent evidence registry is stored under `%LOCALAPPDATA%\OpenTiaToolkit\plc-code-evidence\v1`. Only `Verified` records are eligible for reuse. `Failed` means qualification failed but cleanup was proven. `Quarantined` means cleanup was not proven and must be treated as an incident; it is never promoted. `Running` may identify an interrupted probe and is also never promoted.

Evidence matching is deliberately narrow. The qualification signature normalizes disposable object names and S7DCL block numbers while retaining the code shape, declared object kinds, constructs, dependencies, resource content, and validator catalog version. The compatibility fingerprint binds TIA version/build, CPU family/type/order number, selected route and format, safety context, and declared object shapes. Evidence with external project or library dependencies is additionally bound to the project fingerprint. An evidence miss does not change the artifact's offline classification. Use `force=true` only when an exact verified probe must be repeated. `accept-unknown-impact=true` is a separate explicit acknowledgement for a reviewed `Unknown` disposable impact; it does not relax cleanup or evidence matching.

For manual native-source diagnostics that cannot use the automated runner, `delete-external-source --plc-name="PLC_1" --source-name="Probe" --confirm=true` removes the source definition after every generated block/type has been reviewed and deleted; this path does not create promotable evidence. The older `plan-block-document-import` token is process-local and should be used only when plan/apply share one persistent host session; separate CLI invocations should use the persistent transaction.

### Disposable PLCSIM behavioral verification

Behavioral verification extends qualification with a versioned multi-artifact package, a deterministic reset/stimulate/assert/cleanup vector, and an exact PLCSIM Advanced Runtime API fingerprint. It is intentionally limited to uniquely named missing targets. A typical package contains native SCL logic and scheduler OB sources plus an S7DCL contract DB whose tags are the only runtime values the vector may write.

```powershell
OpenTiaToolkit.CLI.exe validate-plc-behavior-package --package-path="C:\probes\Behavior_Package.json"
OpenTiaToolkit.CLI.exe validate-plc-behavior-vector --vector-path="C:\probes\Behavior_Vector.json"
OpenTiaToolkit.CLI.exe discover-plc-simulator-capabilities --plc-name="PLC_1" --instance-name="PLCaa" --refresh-tags=true

# Discover a fresh Create capability for every declared object, then pass all ids together.
OpenTiaToolkit.CLI.exe query-plc-behavior-evidence --plc-name="PLC_1" --capability-ids="<fc-id>,<db-id>,<ob-id>" --package-path="C:\probes\Behavior_Package.json" --vector-path="C:\probes\Behavior_Vector.json" --instance-name="PLCaa"
OpenTiaToolkit.CLI.exe run-plc-code-behavior-probe --plc-name="PLC_1" --capability-ids="<fc-id>,<db-id>,<ob-id>" --package-path="C:\probes\Behavior_Package.json" --vector-path="C:\probes\Behavior_Vector.json" --instance-name="PLCaa" --only-changes=true --confirm=true
```

The runner records the pre-existing offline/online difference set, returns TIA offline, applies and canonically verifies one persistent transaction, downloads only to the selected local simulator, proves the comparison did not gain differences, executes typed bounded assertions, restores every written tag, rolls back all generated objects, recompiles, downloads the clean program, restores the original difference set, and verifies that disposable runtime tags are absent. Only `Verified` evidence with the same code signature, capability compatibility, vector hash, and simulator fingerprint can be reused.

`Quarantined` means either offline or simulator cleanup is unproven. Do not start another probe over it. Reconnect the exact project/PLC and run `recover-plc-code-behavior-probe --evidence-id="<id>" --only-changes=true --confirm=true`. Recovery can turn the record into non-reusable `Failed` evidence with `cleanupProven=true`; it never promotes behavior.

#### import-block-from-docs

Imports a single PLC block from SIMATIC SD documents (.s7dcl + .s7res) in the supported TIA Portal V21 workflow.

```bash
# Import to root of PLC program
OpenTiaToolkit.CLI.exe import-block-from-docs --plc-name="PLC_1" --import-directory="C:\Imports\Docs" --file-name="Motor"

# Import with specific import option
OpenTiaToolkit.CLI.exe import-block-from-docs --plc-name="PLC_1" --import-directory="C:\Imports\Docs" --file-name="Motor" --import-option="Override"
```

**Parameters**:
- `plc-name` (required): Name of the PLC device
- `import-directory` (required): Directory containing .s7dcl and .s7res files
- `file-name` (required): Block name without extension
- `group-path` (optional): Path within PLC program (e.g., "Program Blocks/Motors"). Empty for root.
- `import-option` (optional): Import behavior - "Override", "None", "SkipInactiveCultures", or "ActivateInactiveCultures" (default: "Override")

**Output**:
```
Successfully imported block from documents: Motor

Format: SIMATIC SD (.s7dcl + .s7res)
```

**Import Options**:
- `Override`: Overwrites existing blocks (default)
- `None`: Does not overwrite existing blocks
- `SkipInactiveCultures`: Skips inactive language versions
- `ActivateInactiveCultures`: Activates all language versions

**Note**: This low-level command requires a supported TIA Portal version. S7RES is required when the S7DCL references multilingual `MLC_*` resources; data documents without those references can be S7DCL-only. Prefer the guarded workflow above for authored changes.

#### import-blocks-from-docs

Imports multiple PLC blocks from SIMATIC SD documents with optional regex filtering in the supported TIA Portal V21 workflow. This is a low-level bulk operation; validate authored files individually before using it.

```bash
# Import all blocks from directory
OpenTiaToolkit.CLI.exe import-blocks-from-docs --plc-name="PLC_1" --import-directory="C:\Imports\Docs"

# Import with regex filter
OpenTiaToolkit.CLI.exe import-blocks-from-docs --plc-name="PLC_1" --import-directory="C:\Imports\Docs" --filter-regex="Motor_.*"

# Import with specific import option
OpenTiaToolkit.CLI.exe import-blocks-from-docs --plc-name="PLC_1" --import-directory="C:\Imports\Docs" --import-option="Override"
```

**Parameters**:
- `plc-name` (required): Name of the PLC device
- `import-directory` (required): Directory containing .s7dcl and .s7res files
- `filter-regex` (optional): Regex filter for block names (e.g., "Motor_.*")
- `group-path` (optional): Path within PLC program (e.g., "Program Blocks/Motors"). Empty for root.
- `import-option` (optional): Import behavior - "Override", "None", "SkipInactiveCultures", or "ActivateInactiveCultures" (default: "Override")

**Output**:
```
Successfully imported 4/5 blocks from documents

Summary:
  Total:     5
  Succeeded: 4
  Failed:    1

Imported:
  - Motor_1
  - Motor_2
  - Motor_3
  - Valve_1

Failed:
  - Motor_Test (version mismatch)
```

**Note**: This command requires a supported TIA Portal version. Each block requires paired .s7dcl and .s7res files.

### Find and Replace Operations (Phase 5)

#### modify-tags

Plans or applies an explicit list of PLC tag operations. Supported actions are `create`, `update`, `delete`, and `replace`.

```bash
# Inline JSON preview
OpenTiaToolkit.CLI.exe modify-tags --plc-name="PLC_1" --operations='[
  {
    "action": "update",
    "match": { "tagTable": "INPUT", "name": "OldSignal" },
    "set": { "name": "NewSignal", "logicalAddress": "%I10.0", "comment": "Updated signal" }
  },
  {
    "action": "create",
    "set": { "tagTable": "INPUT", "name": "Ready", "dataType": "Bool", "logicalAddress": "%I10.1" }
  }
]' --dry-run=true

# Apply a JSON or CSV operation file
OpenTiaToolkit.CLI.exe modify-tags --plc-name="PLC_1" --input-path="C:\Tags\operations.json" --dry-run=false

# Delete and replace operations require confirmation when applying
OpenTiaToolkit.CLI.exe modify-tags --plc-name="PLC_1" --input-path="C:\Tags\operations.csv" --dry-run=false --confirm=true
```

JSON operations use:

```json
{
  "action": "create|update|delete|replace",
  "match": {
    "tagTable": "INPUT",
    "name": "ExistingTag",
    "logicalAddress": "%I0.0"
  },
  "set": {
    "tagTable": "INPUT",
    "name": "ResultTag",
    "dataType": "Bool",
    "logicalAddress": "%I0.1",
    "comment": "Result comment"
  }
}
```

- Match fields are exact, case-insensitive, and combined with AND.
- `update`, `delete`, and `replace` must resolve to exactly one existing tag.
- `create` requires `set.tagTable`, `set.name`, and `set.dataType`.
- `update` changes only fields present in `set`. Use JSON `null` for `set.comment` to clear the comment.
- `replace` deletes and recreates the matched tag. Unspecified replacement fields inherit their values from the original tag.
- `update` cannot move a tag between tables; use `replace`.
- Name collisions fail instead of being auto-suffixed.
- German `%E`/`%A` addresses are normalized to `%I`/`%Q`.
- Operations are processed sequentially, including during dry-run planning.

CSV columns are:

```csv
Action,MatchTagTable,MatchName,MatchLogicalAddress,TagTable,Name,DataType,LogicalAddress,Comment,ClearComment
update,INPUT,OldSignal,,INPUT,NewSignal,,%E10.0,Updated signal,false
create,,,,INPUT,Ready,Bool,%E10.1,Ready input,false
replace,INPUT,Obsolete,,,,,,Replacement comment,false
delete,INPUT,Unused,,,,,,,false
```

Use exactly one of `operations` or `input-path`. `dry-run` defaults to `true`. Applying a batch containing `delete` or `replace` requires `confirm=true`. `safety-password-env` and JSON `output-path` are optional; raw passwords are rejected.

When invoked through MCP, `modify-tags` reports operation-level progress to `operation-status` and honors `cancel-operation` between operations and before TIA write calls.

#### find-replace-tags

Finds and replaces PLC tag names across all tag tables with optional case-insensitive matching, substring matching, and dry-run preview.

```bash
# Preview tag name changes (dry-run is default)
OpenTiaToolkit.CLI.exe find-replace-tags --plc-name="PLC_1" --rename-map='{"Motor_Speed":"MotorSpeed","Valve_Open":"ValveOpen"}' --dry-run=true

# Actually apply tag changes
OpenTiaToolkit.CLI.exe find-replace-tags --plc-name="PLC_1" --rename-map='{"Motor_Speed":"MotorSpeed"}' --dry-run=false

# Rename with case-insensitive matching
OpenTiaToolkit.CLI.exe find-replace-tags --plc-name="PLC_1" --rename-map='{"motor_speed":"MotorSpeed"}' --case-insensitive=true --dry-run=false

# Substring matching - replaces "Speed" with "RPM" in all matching tags
OpenTiaToolkit.CLI.exe find-replace-tags --plc-name="PLC_1" --rename-map='{"Speed":"RPM"}' --substring-match=true --dry-run=true

# Substring matching with case-insensitivity
OpenTiaToolkit.CLI.exe find-replace-tags --plc-name="PLC_1" --rename-map='{"speed":"RPM"}' --substring-match=true --case-insensitive=true --dry-run=false

# Write preview rows to CSV
OpenTiaToolkit.CLI.exe find-replace-tags --plc-name="PLC_1" --rename-map='{"Motor_Speed":"MotorSpeed"}' --dry-run=true --output-path="C:\Reports\tag-renames.csv"
```

**Parameters**:
- `plc-name` (required): Name of the PLC device
- `rename-map` (required): Dictionary mapping old names to new names (JSON string or CSV file path)
- `case-insensitive` (optional): Enable case-insensitive matching (default: false)
- `dry-run` (optional): Preview changes without applying (default: true)
- `substring-match` (optional): Enable substring matching instead of exact matching (default: false)
- `output-path` (optional): CSV file or folder path for result rows. If omitted, large inline results automatically spill to a temporary CSV artifact.

When invoked through MCP, `find-replace-tags`, `find-replace-db-members`, and `find-replace-udt-members` report batch progress to `operation-status` and honor `cancel-operation` between processed tags, DB members, UDT members, and before TIA write calls.

**Substring Matching**:
When `substring-match=true`, the command will find and replace substrings within tag names rather than requiring exact matches. This is useful for bulk renaming operations.

Examples:
- Mapping `{"Speed":"RPM"}` with `--substring-match=true`:
  - `Motor_Speed_Setpoint` -> `Motor_RPM_Setpoint`
  - `Valve_Speed_Actual` -> `Valve_RPM_Actual`
  - `Speed_Setpoint` -> `RPM_Setpoint`
  - `Pump_Speed_RPM` -> `Pump_RPM_RPM` (warning: double replacement!)

- Mapping `{"_":"-"}` with `--substring-match=true`:
  - `Motor_Speed_Setpoint` -> `Motor-Speed-Setpoint`
  - `Valve_Open_Status` -> `Valve-Open-Status`

**Warning**: Substring matching can cause unintended replacements:
- Common substrings like `"Speed"` will match in many tags
- Existing instances of the replacement text will be replaced again
- Always use `--dry-run=true` first to see all affected tags
- Use more specific substrings when possible (e.g., `"_Speed"` instead of `"Speed"`)

**Note**: Substring matching applies only the first matching rule from the rename map to each tag.

**Rename Map Format**:
The `rename-map` parameter accepts multiple input formats:
- JSON string: `'{"OldName":"NewName"}'`
- Escaped JSON: `'"{\"OldName\":\"NewName\"}"'`
- CSV file path: `"C:\Tags\rename_tags.csv"` (file must end with `.csv`)

**CSV File Format**:
CSV files should have two columns:
```csv
OldName,NewName
Motor_Speed,MotorSpeed
Valve_Open,ValveOpen
Sensor_Temp1,SensorTemperature1
```
- Header row is optional (auto-detected if contains keywords like "old", "new", "current", "actual")
- First column: Current/actual tag name (old name)
- Second column: New/modified tag name
- Empty lines are skipped
- Whitespace in columns is trimmed

**Output**:
```
Tags processed: 150, Matches: 25, Changes: previewed: 20, Skipped: 5, Warnings: 2, Errors: 0

Summary:
Total Processed: 150
Matches Found: 25
Changes Applied: 20 (previewed)
Skipped: 5
Warnings: 2
Errors: 0

Changes:
- TagTable.PLC tags: Motor_Speed -> MotorSpeed
- TagTable.PLC tags: Valve_Open -> ValveOpen

Warnings:
- COLLISION: Name 'MotorSpeed' already exists in tag tables at TagTable.PLC tags
```

For CSV artifacts, rows use `Kind,ItemType,Path,Before,After,Name,Reason,Code,Message,Details`. `Kind` is one of `Change`, `Skipped`, `Warning`, or `Error`.

**Substring Matching Output Example**:
```
Tags processed: 150, Matches: 45, Changes: previewed: 42, Skipped: 3, Warnings: 0, Errors: 0

Changes:
- TagTable.PLC tags: Motor_Speed_Setpoint -> Motor_RPM_Setpoint
- TagTable.PLC tags: Valve_Speed_Actual -> Valve_RPM_Actual
- TagTable.PLC tags: Speed_Setpoint -> RPM_Setpoint
- TagTable.PLC tags: Pump_Speed_RPM -> Pump_RPM_RPM
```

**Best Practices**:
1. Always use `dry-run=true` first to preview changes
2. Check warnings and errors before applying
3. Backup your project before using `dry-run=false`
4. Review the "Skipped" items for potential issues
5. **For substring matching:**
   - Test with specific patterns first - `{"Speed":"RPM"}` affects ALL tags containing "Speed"
   - Use more specific substrings to avoid unintended replacements
   - Consider using `--case-insensitive` carefully - it catches more variations
   - Preview the results carefully - substring matching can affect many tags
   - Order matters: only the first matching rule in the rename map is applied

#### find-replace-db-members

Finds and replaces GlobalDB member names with nested struct support.

> **Safety note**: This command is registered in both CLI and MCP and can mutate GlobalDB members when `dry-run=false`. Run with `dry-run=true`, review skipped/warning rows, and back up the project before applying changes.

```bash
# Preview DB member changes (dry-run is default)
OpenTiaToolkit.CLI.exe find-replace-db-members --plc-name="PLC_1" --rename-map='{"OldMember":"NewMember"}' --dry-run=true

# Rename DB members in all GlobalDBs
OpenTiaToolkit.CLI.exe find-replace-db-members --plc-name="PLC_1" --rename-map='{"Speed_RPM":"SpeedRpm"}' --dry-run=false

# Rename specific DB members only
OpenTiaToolkit.CLI.exe find-replace-db-members --plc-name="PLC_1" --rename-map='{"OldMember":"NewMember"}' --target-blocks="ProcessData" "Configuration"

# Rename with case-insensitive matching
OpenTiaToolkit.CLI.exe find-replace-db-members --plc-name="PLC_1" --rename-map='{"speed_rpm":"SpeedRpm"}' --case-insensitive=true --dry-run=false

# Write preview rows to CSV
OpenTiaToolkit.CLI.exe find-replace-db-members --plc-name="PLC_1" --rename-map='{"OldMember":"NewMember"}' --dry-run=true --output-path="C:\Reports\db-member-renames.csv"
```

**Parameters**:
- `plc-name` (required): Name of the PLC device
- `rename-map` (required): Dictionary mapping old member names to new names (JSON string)
- `case-insensitive` (optional): Enable case-insensitive matching (default: false)
- `dry-run` (optional): Preview changes without applying (default: true)
- `target-blocks` (optional): List of specific GlobalDB names (default: all)
- `output-path` (optional): CSV file or folder path for result rows. If omitted, large inline results automatically spill to a temporary CSV artifact.

**Output**:
```
DB members processed: 500, Matches: 30, Changes: previewed: 25, Skipped: 5, Warnings: 0, Errors: 0

Changes:
- ProcessData.Motor.Speed_RPM -> SpeedRpm
- Configuration.Valve.Open_Status -> OpenStatus
```

For CSV artifacts, rows use `Kind,ItemType,Path,Before,After,Name,Reason,Code,Message,Details`. `Kind` is one of `Change`, `Skipped`, `Warning`, or `Error`.

**Key Features**:
- Nested structure support (handles UDT instances, structs, arrays)
- Target specific blocks using `--target-blocks`
- Recursive traversal of complex data types

#### find-replace-udt-members

Finds and replaces UDT member names with nested struct support.

> **Safety note**: This command is registered in both CLI and MCP and can mutate UDT definitions when `dry-run=false`. Run with `dry-run=true`, review skipped/warning rows, and back up the project before applying changes.

```bash
# Preview UDT member changes (dry-run is default)
OpenTiaToolkit.CLI.exe find-replace-udt-members --plc-name="PLC_1" --rename-map='{"Speed_RPM":"SpeedRpm"}' --dry-run=true

# Rename UDT members in all UDTs
OpenTiaToolkit.CLI.exe find-replace-udt-members --plc-name="PLC_1" --rename-map='{"Speed_RPM":"SpeedRpm"}' --dry-run=false

# Rename specific UDT members only
OpenTiaToolkit.CLI.exe find-replace-udt-members --plc-name="PLC_1" --rename-map='{"OldMember":"NewMember"}' --target-types="MotorType" "ValveType"

# Write preview rows to CSV
OpenTiaToolkit.CLI.exe find-replace-udt-members --plc-name="PLC_1" --rename-map='{"Speed_RPM":"SpeedRpm"}' --dry-run=true --output-path="C:\Reports\udt-member-renames.csv"
```

**Parameters**:
- `plc-name` (required): Name of the PLC device
- `rename-map` (required): Dictionary mapping old member names to new names (JSON string)
- `case-insensitive` (optional): Enable case-insensitive matching (default: false)
- `dry-run` (optional): Preview changes without applying (default: true)
- `target-types` (optional): List of specific UDT names (default: all)
- `output-path` (optional): CSV file or folder path for result rows. If omitted, large inline results automatically spill to a temporary CSV artifact.

**Output**:
```
UDT members processed: 200, Matches: 15, Changes: previewed: 12, Skipped: 3, Warnings: 0, Errors: 0

Changes:
- MotorType.Speed_RPM -> SpeedRpm
- ValveType.Open_Status -> OpenStatus
```

For CSV artifacts, rows use `Kind,ItemType,Path,Before,After,Name,Reason,Code,Message,Details`. `Kind` is one of `Change`, `Skipped`, `Warning`, or `Error`.

**Key Features**:
- Nested structure support for UDT definitions
- Changes propagate to all DB instances using the UDT
- Target specific types using `--target-types`

### Portal Instance Management (Phase 2)

#### open-tia-portal

Launches a new TIA Portal instance.

```bash
OpenTiaToolkit.CLI.exe open-tia-portal
OpenTiaToolkit.CLI.exe open-tia-portal --with-ui=true
```

**Parameters**:
- `with-ui` (optional): Start TIA Portal with user interface (default: false)

**Output**:
```
TIA Portal instance opened (ID: 12345)
```

#### close-tia-portal

Closes the current TIA Portal instance.

```bash
OpenTiaToolkit.CLI.exe close-tia-portal
```

**Output**:
```
TIA Portal instance closed
```

#### list-instances

Lists all running TIA Portal instances.

```bash
OpenTiaToolkit.CLI.exe list-instances
```

**Output**:
```
Found 2 TIA Portal instance(s):

Process ID    Version    Is Attached
-----------------------------------------
12345         V21        False
67890         V21        True
```

#### connect-instance

Connects to a specific TIA Portal instance by process ID.

```bash
OpenTiaToolkit.CLI.exe connect-instance --instance-id=12345
```

**Parameters**:
- `instance-id` (required): Process ID of the TIA Portal instance

**Output**:
```
Connected to TIA Portal instance 12345
```

### Project Save Operations (Phase 2)

#### save-project

Saves the current project.

```bash
OpenTiaToolkit.CLI.exe save-project
```

**Output**:
```
Project saved successfully: MyProject
```

#### save-as-project

Saves the project to a new location.

```bash
OpenTiaToolkit.CLI.exe save-as-project --destination-path="C:\Projects\MyCopy"
```

**Parameters**:
- `destination-path` (required): Directory to save the project

**Output**:
```
Project saved to: C:\Projects\MyCopy\MyProject.ap21
```

#### get-project-tree

Gets the hierarchical project structure.

```bash
OpenTiaToolkit.CLI.exe get-project-tree
OpenTiaToolkit.CLI.exe get-project-tree --depth=2
```

**Parameters**:
- `depth` (optional): Maximum depth to traverse (null for unlimited)

**Output**:
```
Project Tree:

MyProject (Project)
+-- PLC_1 (Device)
|   +-- CPU (DeviceItem)
+-- HMI_1 (Device)
|   +-- HMI_Application (DeviceItem)
+-- IO_System (Device)
    +-- Interface (DeviceItem)
```

## Output Formats

### JSON Output

Use the global `--format json` option to emit a machine-readable result envelope:

```bash
OpenTiaToolkit.CLI.exe list-devices --format json
OpenTiaToolkit.CLI.exe --format=json list-devices
```

The JSON shape is stable across commands:

```json
{
  "success": true,
  "message": "Found 2 device(s)",
  "error": null,
  "data": {}
}
```

### Success Messages

```
Command completed successfully
```

### Error Messages

```
Error: Failed to open project
Details: Project file not found
```

### Table Formatting

Lists and collections are formatted as tables for readability:

```
Found 2 project(s):

Name                 Path                  Version    Created
-------------------------------------------------------------------
Project1             C:\Path1.ap21        V21        2025-01-01
Project2             C:\Path2.ap21        V21        2025-01-02
```

## Agent and MCP Packaging

**Codex skill packaging**:
- Agent workflow skills are versioned in `skills/`, including the top-level router, PLC code engineering router, S7DCL LAD/data authoring, native SCL source authoring, SimaticML fallback, session selection, HMI, hardware, library, trend logging, VCI, multiuser, and repo-development workflows.
- PLC code skills include a shared verified/probe/unsupported coverage matrix, format-specific catalogs, and V21-tested reusable assets for branches, timers, analog scaling, edge memory, nested DB layouts, stateful SCL FBs, and TYPE-plus-block generation.
- Run `powershell -ExecutionPolicy Bypass -File .\scripts\Package-HmiUnifiedAgentSkill.ps1` to validate and zip the default HMI skill under `artifacts\skills`.
- Pass `-SkillSourcePath .\skills\basic-tia-interaction -PackageName basic-tia-interaction.zip` to package the TIA session-selection skill.
- Add `-Install` to refresh `$CODEX_HOME\skills\<skill-name>`.

**Claude Desktop MCPB packaging**:
- The MCPB source package is versioned in `plugins/opentia-toolkit-claude-desktop`.
- Run `powershell -ExecutionPolicy Bypass -File .\scripts\Pack-ClaudeDesktopMcpb.ps1` to stage the MCP build output and create `artifacts\claude-desktop-mcpb\opentia-toolkit.mcpb` with `mcpb` or `npx -y @anthropic-ai/mcpb`.
- Add `-RegisterOpenness` from an elevated PowerShell session to register the exact staged isolated V21 worker path and hash with Siemens Openness. The bundled MCP host is vendor-free.

## Examples

The examples below are session command sequences. Start the CLI with no command first, then enter stateful commands in that same session. Some examples keep the executable prefix to show normal CLI syntax; do not run dependent steps as separate single-command processes.

```powershell
OpenTiaToolkit.CLI.exe
```

### Example 1: Connect and Explore

```bash
# Connect to TIA Portal
connect-portal

# Check status
portal-status

# List open projects
list-projects
```

### Example 2: Project Workflow

```bash
# Connect to TIA Portal
connect-portal

# Open a project
open-project --project-path="C:\Projects\Line1.ap21"

# Get project info
get-project-info

# List all devices
list-devices

# Close when done
close-project

# Disconnect
disconnect-portal
```

### Example 3: Batch Processing

Put repeatable batch work in a command file and run it with `--script`.

```powershell
OpenTiaToolkit.CLI.exe --script .\inspect-lines.commands.txt
```

`inspect-lines.commands.txt`:

```text
connect-portal
open-project --project-path="C:\Projects\Line1.ap21"
list-devices --format json
close-project
open-project --project-path="C:\Projects\Line2.ap21"
list-devices --format json
close-project
disconnect-portal
```

### Example 4: PLC Operations Workflow

```bash
# Connect and open project
connect-portal
open-project --project-path="C:\Projects\Line1.ap21"

# Open PLC software
open-plc --plc-name="PLC_1"

# Get software structure
get-software-tree --plc-name="PLC_1" --depth=2

# List blocks
get-blocks --plc-name="PLC_1"

# Compile the software
compile-software --plc-name="PLC_1"

# Export all blocks to XML
export-plc --plc-name="PLC_1" --export-path="C:\Exports\PLC_1"
```

### Example 5: Multi-Instance Management

```bash
# List all running TIA Portal instances
OpenTiaToolkit.CLI.exe list-instances

# Open a new TIA Portal instance (headless)
OpenTiaToolkit.CLI.exe open-tia-portal

# Open another with UI for debugging
OpenTiaToolkit.CLI.exe open-tia-portal --with-ui=true

# Connect to a specific instance
OpenTiaToolkit.CLI.exe connect-instance --instance-id=12345

# Work with projects in parallel instances
```

### Example 6: Project Tree Analysis

```bash
# Open project and get full tree
OpenTiaToolkit.CLI.exe open-project --project-path="C:\Projects\Line1.ap21"
OpenTiaToolkit.CLI.exe get-project-tree

# Get limited depth for overview
OpenTiaToolkit.CLI.exe get-project-tree --depth=1

# Drill down into specific device
OpenTiaToolkit.CLI.exe get-device-info --device-name="PLC_1"
OpenTiaToolkit.CLI.exe get-device-item-info --device-name="PLC_1" --item-name="CPU"

# Save project after analysis
OpenTiaToolkit.CLI.exe save-project
```

### Example 7: HMI Device Discovery

```bash
# Connect and open project
OpenTiaToolkit.CLI.exe connect-portal
OpenTiaToolkit.CLI.exe open-project --project-path="C:\Projects\Line1.ap21"

# List all HMI devices
OpenTiaToolkit.CLI.exe list-hmis

# Get detailed HMI device info
OpenTiaToolkit.CLI.exe get-device-info --device-name="HMI_1"
```

### Example 8: Block Search and Export Workflow

```bash
# Connect and open project
OpenTiaToolkit.CLI.exe connect-portal
OpenTiaToolkit.CLI.exe open-project --project-path="C:\Projects\Line1.ap21"

# Open PLC software
OpenTiaToolkit.CLI.exe open-plc --plc-name="PLC_1"

# Search for blocks by name
OpenTiaToolkit.CLI.exe search-blocks --plc-name="PLC_1" --pattern="Motor"

# Search by comment
OpenTiaToolkit.CLI.exe search-blocks --plc-name="PLC_1" --pattern="Test" --search-in="comment"

# Search by block type
OpenTiaToolkit.CLI.exe search-blocks --plc-name="PLC_1" --pattern="FB" --filter-type="FB"

# Export specific block
OpenTiaToolkit.CLI.exe export-block --plc-name="PLC_1" --block-name="FB_Motor" --export-path="C:\Exports\FB_Motor.xml"

# Get detailed block information
OpenTiaToolkit.CLI.exe get-block-info --plc-name="PLC_1" --block-name="Main"
```

### Example 9: SIMATIC SD Export

```bash
# Connect and open project with a supported TIA Portal version
OpenTiaToolkit.CLI.exe connect-portal
OpenTiaToolkit.CLI.exe open-project --project-path="C:\Projects\Line1.ap21"

# Open PLC software
OpenTiaToolkit.CLI.exe open-plc --plc-name="PLC_1"

# Export single block as SIMATIC SD documents
OpenTiaToolkit.CLI.exe export-block-as-docs --plc-name="PLC_1" --block-name="FB_Motor" --export-path="C:\Exports"
# Output: Creates FB_Motor.s7dcl and FB_Motor.s7res files

# Export multiple blocks with regex filter
OpenTiaToolkit.CLI.exe export-blocks-as-docs --plc-name="PLC_1" --filter-regex="Motor_.*" --export-path="C:\Exports"
# Output: Exports all blocks matching "Motor_.*" pattern

# Export all blocks as SIMATIC SD documents
OpenTiaToolkit.CLI.exe export-blocks-as-docs --plc-name="PLC_1" --export-path="C:\Exports\AllBlocks"
```

**Note**: SIMATIC SD export requires a supported TIA Portal version. The .s7dcl + .s7res format is the official multi-language document format used by TIA Portal.

### Example 10: Type Export (UDT)

```bash
# Connect and open project
OpenTiaToolkit.CLI.exe connect-portal
OpenTiaToolkit.CLI.exe open-project --project-path="C:\Projects\Line1.ap21"

# Open PLC software
OpenTiaToolkit.CLI.exe open-plc --plc-name="PLC_1"

# Export single type (UDT) to XML
OpenTiaToolkit.CLI.exe export-type --plc-name="PLC_1" --type-name="MotorData" --export-path="C:\Exports\MotorData.xml"

# Export multiple types with regex filter
OpenTiaToolkit.CLI.exe export-types --plc-name="PLC_1" --filter-regex="Motor_.*" --export-path="C:\Exports"
# Output: Exports all types matching "Motor_.*" pattern as XML files

# Export all types
OpenTiaToolkit.CLI.exe export-types --plc-name="PLC_1" --export-path="C:\Exports\AllTypes"
```

**Note**: Type export checks the `IsConsistent` property. Inconsistent types will be skipped with an error message.

### Example 11: Safe Block Deletion Workflow

```bash
# Connect and open project
OpenTiaToolkit.CLI.exe connect-portal
OpenTiaToolkit.CLI.exe open-project --project-path="C:\Projects\Line1.ap21"

# Open PLC software
OpenTiaToolkit.CLI.exe open-plc --plc-name="PLC_1"

# Preview blocks to be deleted (without --confirm flag)
OpenTiaToolkit.CLI.exe delete-blocks --plc-name="PLC_1" --filter="Test_.*"
# Output will show preview and prompt for confirmation

# Delete blocks with explicit confirmation
OpenTiaToolkit.CLI.exe delete-blocks --plc-name="PLC_1" --filter="Test_.*" --confirm=true
# Output: Deleted 3 blocks, skipped 0, failed 0

# Verify deletion
OpenTiaToolkit.CLI.exe get-blocks --plc-name="PLC_1"
```

### Example 12: Complete PLC Backup Workflow

Run the backup commands in one stateful session so the project remains open between exports.

```text
connect-portal
open-project --project-path="C:\Projects\Line1.ap21"
export-plc --plc-name="PLC_1" --export-path="C:\Backups\20250115\PLC_1"
export-plc --plc-name="PLC_2" --export-path="C:\Backups\20250115\PLC_2"
export-plc --plc-name="PLC_3" --export-path="C:\Backups\20250115\PLC_3"
close-project
disconnect-portal
```

### Example 13: Tag Renaming Workflow (Find and Replace)

```bash
# Connect and open project
OpenTiaToolkit.CLI.exe connect-portal
OpenTiaToolkit.CLI.exe open-project --project-path="C:\Projects\Line1.ap21"

# Preview tag name changes (recommended first step)
OpenTiaToolkit.CLI.exe find-replace-tags --plc-name="PLC_1" --rename-map='{"Motor_Speed":"MotorSpeed","Valve_Open":"ValveOpen"}' --dry-run=true

# Review the preview output, then apply the changes
OpenTiaToolkit.CLI.exe find-replace-tags --plc-name="PLC_1" --rename-map='{"Motor_Speed":"MotorSpeed","Valve_Open":"ValveOpen"}' --dry-run=false

# Save the project after renaming
OpenTiaToolkit.CLI.exe save-project
```

### Example 14: DB Member Renaming with Nested Structures

```bash
# Connect and open project
OpenTiaToolkit.CLI.exe connect-portal
OpenTiaToolkit.CLI.exe open-project --project-path="C:\Projects\Line1.ap21"

# Preview DB member changes in specific blocks
OpenTiaToolkit.CLI.exe find-replace-db-members --plc-name="PLC_1" --rename-map='{"Speed_RPM":"SpeedRpm","Enable_Signal":"Enable"}' --target-blocks="MotorData" "ValveData" --dry-run=true

# Apply changes to all GlobalDBs
OpenTiaToolkit.CLI.exe find-replace-db-members --plc-name="PLC_1" --rename-map='{"Speed_RPM":"SpeedRpm"}' --dry-run=false

# Rename UDT members (propagates to all DB instances)
OpenTiaToolkit.CLI.exe find-replace-udt-members --plc-name="PLC_1" --rename-map='{"Status_Flag":"Status"}' --target-types="MotorType" --dry-run=false
```

### Example 15: Large-Scale Refactoring with Case-Insensitive Matching

```bash
# Connect and open project
OpenTiaToolkit.CLI.exe connect-portal
OpenTiaToolkit.CLI.exe open-project --project-path="C:\Projects\Line1.ap21"

# Standardize naming conventions (case-insensitive to catch variations)
OpenTiaToolkit.CLI.exe find-replace-tags --plc-name="PLC_1" --rename-map='{"Motor_Speed":"MotorSpeed","Valve_Open":"ValveOpen"}' --case-insensitive=true --dry-run=true

# Review and apply
OpenTiaToolkit.CLI.exe find-replace-tags --plc-name="PLC_1" --rename-map='{"Motor_Speed":"MotorSpeed","Valve_Open":"ValveOpen"}' --case-insensitive=true --dry-run=false

# Save and compile to verify
OpenTiaToolkit.CLI.exe save-project
OpenTiaToolkit.CLI.exe compile-software --plc-name="PLC_1"
```

### Example 16: PLC Tag Modification Workflow

```bash
# Connect and open project
OpenTiaToolkit.CLI.exe connect-portal
OpenTiaToolkit.CLI.exe open-project --project-path="C:\Projects\Line1.ap21"

# Open PLC software
OpenTiaToolkit.CLI.exe open-plc --plc-name="PLC_1"

# Preview an explicit batch
OpenTiaToolkit.CLI.exe modify-tags --plc-name="PLC_1" --input-path="C:\Tags\operations.json" --dry-run=true

# Apply after reviewing the before/after snapshots
OpenTiaToolkit.CLI.exe modify-tags --plc-name="PLC_1" --input-path="C:\Tags\operations.json" --dry-run=false --confirm=true

# Save the project
OpenTiaToolkit.CLI.exe save-project
```

### Example 17: Substring Replacement Workflow

```bash
# Connect and open project
OpenTiaToolkit.CLI.exe connect-portal
OpenTiaToolkit.CLI.exe open-project --project-path="C:\Projects\Line1.ap21"

# Open PLC software
OpenTiaToolkit.CLI.exe open-plc --plc-name="PLC_1"

# Preview substring replacement - replace "Speed" with "RPM"
OpenTiaToolkit.CLI.exe find-replace-tags --plc-name="PLC_1" --rename-map='{"Speed":"RPM"}' --substring-match=true --dry-run=true

# Preview underscore to hyphen conversion
OpenTiaToolkit.CLI.exe find-replace-tags --plc-name="PLC_1" --rename-map='{"_":"-"}' --substring-match=true --dry-run=true

# Apply substring replacement
OpenTiaToolkit.CLI.exe find-replace-tags --plc-name="PLC_1" --rename-map='{"Speed":"RPM"}' --substring-match=true --dry-run=false

# Save and compile to verify
OpenTiaToolkit.CLI.exe save-project
OpenTiaToolkit.CLI.exe compile-software --plc-name="PLC_1"
```

### Example 18: Safer Substring Replacement with Specific Patterns

```bash
# Connect and open project
OpenTiaToolkit.CLI.exe connect-portal
OpenTiaToolkit.CLI.exe open-project --project-path="C:\Projects\Line1.ap21"

# Open PLC software
OpenTiaToolkit.CLI.exe open-plc --plc-name="PLC_1"

# Use more specific patterns to avoid unintended replacements
# Only replace "_Speed" (with underscore) to avoid matching "SpeedRef" or similar
OpenTiaToolkit.CLI.exe find-replace-tags --plc-name="PLC_1" --rename-map='{"_Speed":"_RPM"}' --substring-match=true --dry-run=true

# Preview specific prefix replacements - only affect tags starting with pattern
OpenTiaToolkit.CLI.exe find-replace-tags --plc-name="PLC_1" --rename-map='{"Motor_":"MTR_","Valve_":"VLV_"}' --substring-match=true --dry-run=true

# Apply safer patterns
OpenTiaToolkit.CLI.exe find-replace-tags --plc-name="PLC_1" --rename-map='{"_Speed":"_RPM"}' --substring-match=true --dry-run=false

# Save project
OpenTiaToolkit.CLI.exe save-project
```

### Example 19: Batch Tag Creation from CSV

Generate REPL-shaped commands from CSV, then run them in the same stateful CLI session after connecting and opening the project.

```powershell
$csvFile = "C:\Tags\new_tags.csv"
$commandFile = "C:\Tags\create_tags.commands.txt"
# CSV format: TagName,DataType,LogicalAddress,TagTable

$commands = @(
    'connect-portal',
    'open-project --project-path="C:\Projects\Line1.ap21"',
    'open-plc --plc-name="PLC_1"'
)

$operationFile = "C:\Tags\tag-operations.json"
$operations = Import-Csv $csvFile | ForEach-Object {
    @{
        action = "create"
        set = @{
            tagTable = $_.TagTable
            name = $_.TagName
            dataType = $_.DataType
            logicalAddress = $_.LogicalAddress
        }
    }
}
$operations | ConvertTo-Json -Depth 5 | Set-Content $operationFile
$commands += "modify-tags --plc-name=PLC_1 --input-path=`"$operationFile`" --dry-run=false"

$commands += @('save-project', 'close-project', 'disconnect-portal')
$commands | Set-Content -Path $commandFile
```

### Example 20: Batch Tag Rename from CSV File

Create `C:\Tags\rename_tags.csv`:

```csv
OldName,NewName
Motor_Speed,MotorSpeed
Valve_Open,ValveOpen
Sensor_Temp1,SensorTemperature1
```

Then run the rename commands in one stateful session:

```text
connect-portal
open-project --project-path="C:\Projects\Line1.ap21"
find-replace-tags --plc-name="PLC_1" --rename-map="C:\Tags\rename_tags.csv" --dry-run=true
find-replace-tags --plc-name="PLC_1" --rename-map="C:\Tags\rename_tags.csv" --dry-run=false
save-project
```

**CSV Format Notes**:
- Header row with "OldName,NewName" is recommended (auto-detected)
- Files without headers also work: `Motor_Speed,MotorSpeed` on first line
- Empty lines are skipped
- Whitespace around values is trimmed automatically
- Last occurrence wins for duplicate old names (case-insensitive)

**Example CSV file creation**:
```powershell
# Create CSV file using PowerShell
$csvContent = @"
OldName,NewName
Motor_Speed,MotorSpeed
Valve_Open,ValveOpen
Sensor_Temp1,SensorTemperature1
"@
Set-Content -Path "C:\Tags\rename_tags.csv" -Value $csvContent
```

## Argument Parsing Details

### Supported Formats

```bash
# Format 1: --name=value
OpenTiaToolkit.CLI.exe open-project --project-path="C:\Project.ap21"

# Format 2: --name value (without quotes)
OpenTiaToolkit.CLI.exe open-project --project-path C:\Project.ap21

# Format 3: Short flags (single dash)
OpenTiaToolkit.CLI.exe close-project -s
```

### Boolean Flags

```bash
# Explicit false
OpenTiaToolkit.CLI.exe close-project --save=false

# Explicit true
OpenTiaToolkit.CLI.exe close-project --save=true

# Flag without value (defaults to true)
OpenTiaToolkit.CLI.exe close-project --save
```

### Path Arguments

Windows paths with spaces should be quoted:

```bash
# Correct
OpenTiaToolkit.CLI.exe open-project --project-path="C:\My Projects\Project.ap21"

# Also works
OpenTiaToolkit.CLI.exe open-project --project-path="C:\My Projects\Project.ap21"
```

## Troubleshooting

### Command Not Found

```
Unknown command: 'opn-project'. Available commands: close-project, connect-portal, ...
```

**Solution**: Check for typos in command name. Use kebab-case.

### Missing Required Argument

```
Error: project-path is required
```

**Solution**: Provide the required parameter:
```bash
OpenTiaToolkit.CLI.exe open-project --project-path="C:\Project.ap21"
```

### TIA Portal Not Running

```
Error: Failed to connect to TIA Portal
Details: No TIA Portal instance found
```

**Solution**: Start TIA Portal manually first, or the tool will attempt to start a new instance.

### Project Already Open

```
Error: Failed to open project: C:\Project.ap21
Details: Project is already open
```

**Solution**: Close the project first, or if already open, use `get-project-info` to work with it.

### No TIA Openness API

```
Fatal error: Cannot load Siemens.Engineering assembly
```

**Solution**:
- Ensure TIA Portal V21 is installed
- The Siemens resolver package handles assembly loading
- Set `OPEN_TIA_PORTAL_ROOT` to the TIA Portal installation root for a non-standard runtime installation (`TiaPortalLocation` remains a compatibility alias)

## Tips and Tricks

### Create Aliases

Create aliases for one-off commands or to start an interactive session. Do not put stateful multi-step workflows in a batch file as repeated `OpenTiaToolkit.CLI.exe <command>` calls, because each call starts a new process.

```batch
@REM tia-session.bat
OpenTiaToolkit.CLI.exe
```

### Pipe Output

```bash
# Export stateless command output to a file
OpenTiaToolkit.CLI.exe list-instances --format json > instances.json

# Count running TIA Portal instances in table output
OpenTiaToolkit.CLI.exe list-instances | find /c "Instance"
```

### Error Handling

```bash
# Check exit code
OpenTiaToolkit.CLI.exe list-instances
echo %ERRORLEVEL%

# 0 = success, 1 = failure
```

## Best Practices

1. **Keep dependent commands in one session**:
   ```bash
   connect-portal
   open-project --project-path="C:\Projects\Line1.ap21"
   list-devices
   ```

2. **Always check connection** before project operations:
   ```bash
   connect-portal
   portal-status
   ```

3. **Close projects when done** to avoid locking files:
   ```bash
   close-project
   ```

4. **Use `--save`** when making changes you want to keep:
   ```bash
   close-project --save
   ```

5. **Test with small projects** first before production use

## Advanced Usage

### Integration with Scripts

For stateful shell automation, put dependent commands in a script file and run them with `--script`. Repeated single-command invocations are useful for stateless operations such as help, list-instances, and command syntax checks, but they do not preserve portal/project state.

REPL-shaped command sequence:

```text
connect-portal
open-project --project-path="C:\Projects\MyProject.ap21"
list-devices --format json
close-project
disconnect-portal
```

Run it:

```powershell
OpenTiaToolkit.CLI.exe --script .\inspect-project.commands.txt
```

### Error Handling in Scripts

Script mode stops on the first failed command and returns a non-zero exit code:

```powershell
OpenTiaToolkit.CLI.exe --script .\inspect-project.commands.txt

if ($LASTEXITCODE -ne 0) {
    Write-Host "Script failed"
    exit 1
}
```

For more examples, see the [README.md](../README.md) or [Development Guide](DEV_GUIDE.md).

For automated CLI regression coverage against a real V21 project, see the [CLI Testing Guide](CLI_TESTING.md).
