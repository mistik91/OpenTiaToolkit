# OpenTiaToolkit CLI Usage Guide

Complete guide for using the OpenTiaToolkit CLI application.

## Table of Contents

- [Basic Usage](#basic-usage)
- [Command Reference](#command-reference)
- [Output Formats](#output-formats)
- [Examples](#examples)
- [Troubleshooting](#troubleshooting)

## Basic Usage

### Invocation

```bash
OpenTiaToolkit.CLI.exe <command> [options]
```

### Getting Help

```bash
# List all commands
OpenTiaToolkit.CLI.exe help

# Get help for a specific command
OpenTiaToolkit.CLI.exe help open-project
```

### Command Name Format

All commands use **kebab-case** (lowercase with hyphens):

- ✅ `open-project`
- ✅ `list-devices`
- ✅ `close-project`
- ❌ `OpenProject`
- ❌ `List_Devices`

## Command Reference

### Portal Management

#### connect-portal

Connects to a running TIA Portal instance or starts a new one.

```bash
OpenTiaToolkit.CLI.exe connect-portal
```

**Output**:
```
Connected to TIA Portal Siemens.Engineering.V19
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
version   : V19
```

### Project Operations

#### open-project

Opens a TIA Portal project file (.ap19 for local projects, .als19 for multiuser sessions).

```bash
OpenTiaToolkit.CLI.exe open-project --project-path="C:\Projects\MyProject.ap19"
```

**Parameters**:
- `project-path` (required): Full path to the project file

**Output**:
```
Project opened: MyProject

Name:     MyProject
Path:     C:\Projects\MyProject.ap19
Version:  V19
Created:  2025-01-15 10:30:00
```

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
MyProject                               C:\Projects\MyProject.ap19                  V19        2025-01-15 10:30
MySession (Session)                       C:\Projects\MyProject.als19                 V19        2025-01-15 10:35
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
Path:     C:\Projects\MyProject.ap19
Version:  V19
Created:  2025-01-15 10:30:00
```

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

### PLC Operations

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
```

**Parameters**:
- `plc-name` (required): Name of the PLC device
- `depth` (optional): Maximum depth to traverse (null for unlimited)

**Output**:
```
PLC Software Tree for PLC_1:

PLC_1 (PlcSoftware)
├── Blocks
│   ├── OB1 (Organization Block)
│   ├── FC1 (Function)
│   └── FB1 (Function Block)
├── Types
│   └── MotorType (User Defined Type)
└── Source Files
    └── Main.xml
```

#### compile-software

Compiles PLC software.

```bash
OpenTiaToolkit.CLI.exe compile-software --plc-name="PLC_1"
OpenTiaToolkit.CLI.exe compile-software --plc-name="PLC_1" --password="secret"
```

**Parameters**:
- `plc-name` (required): Name of the PLC device
- `password` (optional): Password for protected blocks

**Output**:
```
Compilation Result for PLC_1:

Success:       True
Error Count:   0
Warning Count: 2
Message:       Compilation completed successfully
```

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
```

**Parameters**:
- `plc-name` (required): Name of the PLC device
- `filter` (optional): Regex filter for block names

**Output**:
```
Found 3 blocks in PLC_1:

Name          Type                  Language    Number
----------------------------------------------------------
OB1           Organization Block    FBD         1
FC_Motor      Function              STL         10
FB_MotorCtrl  Function Block        FBD         1
```

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
```

**Parameters**:
- `plc-name` (required): Name of the PLC device
- `pattern` (required): Search pattern (supports wildcards)
- `search-in` (optional): Where to search - "name" or "comment" (default: "name")
- `filter-type` (optional): Block type filter (FB, FC, DB, OB)

**Output**:
```
Found 2 blocks matching pattern "Motor":

Name          Type                  Language    Number    Comment
----------------------------------------------------------------------------
FC_Motor      Function              STL         10        Motor control
FB_MotorCtrl  Function Block        FBD         1        Motor controller
```

### Find and Replace Operations (Phase 5)

#### create-tag

Creates a new PLC tag in the specified tag table with support for various data types and optional logical address assignment.

```bash
# Create tag with logical address in default tag table
OpenTiaToolkit.CLI.exe create-tag --plc-name="PLC_1" --tag-name="Motor_Status" --data-type="Bool" --logical-address="%M10.0"

# Create tag with specific tag table
OpenTiaToolkit.CLI.exe create-tag --plc-name="PLC_1" --tag-name="Temperature" --data-type="Int" --logical-address="%MW12" --tag-table="MyTags"

# Create tag without address (auto-assigned by TIA Portal)
OpenTiaToolkit.CLI.exe create-tag --plc-name="PLC_1" --tag-name="Counter" --data-type="DInt"

# Create Real (floating point) tag
OpenTiaToolkit.CLI.exe create-tag --plc-name="PLC_1" --tag-name="Setpoint" --data-type="Real" --logical-address="%MD20"

# Create String tag
OpenTiaToolkit.CLI.exe create-tag --plc-name="PLC_1" --tag-name="Message" --data-type="String" --logical-address="%MB100"
```

**Parameters**:
- `plc-name` (required): Name of the PLC device
- `tag-name` (required): Name of the tag to create
- `data-type` (required): Data type (e.g., 'Bool', 'Int', 'DInt', 'Real', 'String', 'Word', 'DWord', 'Byte', 'S5Time', 'Time')
- `logical-address` (optional): Logical address (e.g., '%M10.0', '%MW12', '%DB1.DBD0', '%Q0.0', '%I0.0')
- `tag-table` (optional): Tag table name (default: first/default table)

**Supported Data Types**:
- Boolean: `Bool`
- Integers: `SInt`, `Int`, `DInt`, `USInt`, `UInt`, `UDInt`
- Floating-point: `Real`, `LReal`
- Strings/Bytes: `String`, `Char`, `Byte`, `Word`, `DWord`, `LWord`
- Timers/Counters: `S5Time`, `Time`, `LTime`, `Date`, `Date_and_Time`, `TimeOfDay`
- Hardware: `Timer`, `Counter`

**Logical Address Format**:
- Memory bits: `%M10.0`, `%MX10.0`
- Memory words: `%MW12`, `%MD20`
- Inputs: `%I0.0`, `%IW0`, `%ID0`
- Outputs: `%Q0.0`, `%QW0`, `%QD0`
- DB addresses: `%DB1.DBD0`, `%DB1.DBW0`, `%DB1.DBB0`

**Output**:
```
Tag 'Motor_Status' created successfully in 'PLC tags' (Type: Bool, Address: %M10.0)

Tag Name:      Motor_Status
Tag Table:     PLC tags
Data Type:     Bool
Logical Address: %M10.0
```

**Error Handling**:
- Duplicate tag names are detected and reported
- Invalid tag table names return descriptive errors
- Invalid data types are caught with clear messages
- TIA Portal API limitations are documented

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
```

**Parameters**:
- `plc-name` (required): Name of the PLC device
- `rename-map` (required): Dictionary mapping old names to new names (JSON string)
- `case-insensitive` (optional): Enable case-insensitive matching (default: false)
- `dry-run` (optional): Preview changes without applying (default: true)
- `substring-match` (optional): Enable substring matching instead of exact matching (default: false)

**Substring Matching**:
When `substring-match=true`, the command will find and replace substrings within tag names rather than requiring exact matches. This is useful for bulk renaming operations.

Examples:
- Mapping `{"Speed":"RPM"}` with `--substring-match=true`:
  - `Motor_Speed_Setpoint` → `Motor_RPM_Setpoint`
  - `Valve_Speed_Actual` → `Valve_RPM_Actual`
  - `Speed_Setpoint` → `RPM_Setpoint`
  - `Pump_Speed_RPM` → `Pump_RPM_RPM` ⚠️ (note: double replacement!)

- Mapping `{"_":"-"}` with `--substring-match=true`:
  - `Motor_Speed_Setpoint` → `Motor-Speed-Setpoint`
  - `Valve_Open_Status` → `Valve-Open-Status`

**⚠️ Warning**: Substring matching can cause unintended replacements:
- Common substrings like `"Speed"` will match in many tags
- Existing instances of the replacement text will be replaced again
- Always use `--dry-run=true` first to see all affected tags
- Use more specific substrings when possible (e.g., `"_Speed"` instead of `"Speed"`)

**Note**: Substring matching applies only the first matching rule from the rename map to each tag.

**Rename Map Format**:
The `rename-map` parameter accepts multiple input formats:
- JSON string: `'{"OldName":"NewName"}'`
- Escaped JSON: `'"{\"OldName\":\"NewName\"}"'`

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

> **NOTE**: This command is currently disabled. The command file and service method remain available for potential future re-enabling.

Finds and replaces GlobalDB member names with nested struct support.

```bash
# Preview DB member changes (dry-run is default)
OpenTiaToolkit.CLI.exe find-replace-db-members --plc-name="PLC_1" --rename-map='{"OldMember":"NewMember"}' --dry-run=true

# Rename DB members in all GlobalDBs
OpenTiaToolkit.CLI.exe find-replace-db-members --plc-name="PLC_1" --rename-map='{"Speed_RPM":"SpeedRpm"}' --dry-run=false

# Rename specific DB members only
OpenTiaToolkit.CLI.exe find-replace-db-members --plc-name="PLC_1" --rename-map='{"OldMember":"NewMember"}' --target-blocks="ProcessData" "Configuration"

# Rename with case-insensitive matching
OpenTiaToolkit.CLI.exe find-replace-db-members --plc-name="PLC_1" --rename-map='{"speed_rpm":"SpeedRpm"}' --case-insensitive=true --dry-run=false
```

**Parameters**:
- `plc-name` (required): Name of the PLC device
- `rename-map` (required): Dictionary mapping old member names to new names (JSON string)
- `case-insensitive` (optional): Enable case-insensitive matching (default: false)
- `dry-run` (optional): Preview changes without applying (default: true)
- `target-blocks` (optional): List of specific GlobalDB names (default: all)

**Output**:
```
DB members processed: 500, Matches: 30, Changes: previewed: 25, Skipped: 5, Warnings: 0, Errors: 0

Changes:
- ProcessData.Motor.Speed_RPM -> SpeedRpm
- Configuration.Valve.Open_Status -> OpenStatus
```

**Key Features**:
- Nested structure support (handles UDT instances, structs, arrays)
- Target specific blocks using `--target-blocks`
- Recursive traversal of complex data types

#### find-replace-udt-members

> **NOTE**: This command is currently disabled. The command file and service method remain available for potential future re-enabling.

Finds and replaces UDT member names with nested struct support.

```bash
# Preview UDT member changes (dry-run is default)
OpenTiaToolkit.CLI.exe find-replace-udt-members --plc-name="PLC_1" --rename-map='{"Speed_RPM":"SpeedRpm"}' --dry-run=true

# Rename UDT members in all UDTs
OpenTiaToolkit.CLI.exe find-replace-udt-members --plc-name="PLC_1" --rename-map='{"Speed_RPM":"SpeedRpm"}' --dry-run=false

# Rename specific UDT members only
OpenTiaToolkit.CLI.exe find-replace-udt-members --plc-name="PLC_1" --rename-map='{"OldMember":"NewMember"}' --target-types="MotorType" "ValveType"
```

**Parameters**:
- `plc-name` (required): Name of the PLC device
- `rename-map` (required): Dictionary mapping old member names to new names (JSON string)
- `case-insensitive` (optional): Enable case-insensitive matching (default: false)
- `dry-run` (optional): Preview changes without applying (default: true)
- `target-types` (optional): List of specific UDT names (default: all)

**Output**:
```
UDT members processed: 200, Matches: 15, Changes: previewed: 12, Skipped: 3, Warnings: 0, Errors: 0

Changes:
- MotorType.Speed_RPM -> SpeedRpm
- ValveType.Open_Status -> OpenStatus
```

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
12345         V19        False
67890         V19        True
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
Project saved to: C:\Projects\MyCopy\MyProject.ap19
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
├── PLC_1 (Device)
│   └── CPU (DeviceItem)
├── HMI_1 (Device)
│   └── HMI_Application (DeviceItem)
└── IO_System (Device)
    └── Interface (DeviceItem)
```

## Output Formats

### Success Messages

```
✓ Command completed successfully
```

### Error Messages

```
✗ Error: Failed to open project
Details: Project file not found
```

### Table Formatting

Lists and collections are formatted as tables for readability:

```
Found 2 project(s):

Name                 Path                  Version    Created
-------------------------------------------------------------------
Project1             C:\Path1.ap19        V19        2025-01-01
Project2             C:\Path2.ap19        V19        2025-01-02
```

## Examples

### Example 1: Connect and Explore

```bash
# Connect to TIA Portal
OpenTiaToolkit.CLI.exe connect-portal

# Check status
OpenTiaToolkit.CLI.exe portal-status

# List open projects
OpenTiaToolkit.CLI.exe list-projects
```

### Example 2: Project Workflow

```bash
# Open a project
OpenTiaToolkit.CLI.exe open-project --project-path="C:\Projects\Line1.ap19"

# Get project info
OpenTiaToolkit.CLI.exe get-project-info

# List all devices
OpenTiaToolkit.CLI.exe list-devices

# Close when done
OpenTiaToolkit.CLI.exe close-project

# Disconnect
OpenTiaToolkit.CLI.exe disconnect-portal
```

### Example 3: Batch Processing

```powershell
# PowerShell script to process multiple projects
$projects = @(
    "C:\Projects\Line1.ap19",
    "C:\Projects\Line2.ap19",
    "C:\Projects\Line3.ap19"
)

foreach ($project in $projects) {
    Write-Host "Processing: $project"
    OpenTiaToolkit.CLI.exe open-project --project-path="$project"
    OpenTiaToolkit.CLI.exe list-devices
    OpenTiaToolkit.CLI.exe close-project
}
```

### Example 4: PLC Operations Workflow

```bash
# Connect and open project
OpenTiaToolkit.CLI.exe connect-portal
OpenTiaToolkit.CLI.exe open-project --project-path="C:\Projects\Line1.ap19"

# Open PLC software
OpenTiaToolkit.CLI.exe open-plc --plc-name="PLC_1"

# Get software structure
OpenTiaToolkit.CLI.exe get-software-tree --plc-name="PLC_1" --depth=2

# List blocks
OpenTiaToolkit.CLI.exe get-blocks --plc-name="PLC_1"

# Compile the software
OpenTiaToolkit.CLI.exe compile-software --plc-name="PLC_1"

# Export all blocks to XML
OpenTiaToolkit.CLI.exe export-plc --plc-name="PLC_1" --export-path="C:\Exports\PLC_1"
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
OpenTiaToolkit.CLI.exe open-project --project-path="C:\Projects\Line1.ap19"
OpenTiaToolkit.CLI.exe get-project-tree

# Get limited depth for overview
OpenTiaToolkit.CLI.exe get-project-tree --depth=1

# Drill down into specific device
OpenTiaToolkit.CLI.exe get-device-info --device-name="PLC_1"
OpenTiaToolkit.CLI.exe get-device-item-info --device-name="PLC_1" --item-name="CPU"

# Save project after analysis
OpenTiaToolkit.CLI.exe save-project
```

### Example 8: HMI Device Discovery

```bash
# Connect and open project
OpenTiaToolkit.CLI.exe connect-portal
OpenTiaToolkit.CLI.exe open-project --project-path="C:\Projects\Line1.ap19"

# List all HMI devices
OpenTiaToolkit.CLI.exe list-hmis

# Get detailed HMI device info
OpenTiaToolkit.CLI.exe get-device-info --device-name="HMI_1"
```

### Example 9: Block Search and Export Workflow

```bash
# Connect and open project
OpenTiaToolkit.CLI.exe connect-portal
OpenTiaToolkit.CLI.exe open-project --project-path="C:\Projects\Line1.ap19"

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

### Example 10: Safe Block Deletion Workflow

```bash
# Connect and open project
OpenTiaToolkit.CLI.exe connect-portal
OpenTiaToolkit.CLI.exe open-project --project-path="C:\Projects\Line1.ap19"

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

### Example 11: Complete PLC Backup Workflow

```powershell
# PowerShell script to backup all PLCs in a project
$project = "C:\Projects\Line1.ap19"
$exportBase = "C:\Backups\$(Get-Date -Format 'yyyyMMdd')"

OpenTiaToolkit.CLI.exe connect-portal
OpenTiaToolkit.CLI.exe open-project --project-path="$project"

# Get all devices
# (Parse list-devices output to find PLCs)

# For each PLC, export blocks
$plcs = @("PLC_1", "PLC_2", "PLC_3")

foreach ($plc in $plcs) {
    $exportPath = "$exportBase\$plc"
    New-Item -ItemType Directory -Force -Path $exportPath
    OpenTiaToolkit.CLI.exe export-plc --plc-name="$plc" --export-path="$exportPath"
}

OpenTiaToolkit.CLI.exe close-project
OpenTiaToolkit.CLI.exe disconnect-portal
```

### Example 12: Tag Renaming Workflow (Find and Replace)

```bash
# Connect and open project
OpenTiaToolkit.CLI.exe connect-portal
OpenTiaToolkit.CLI.exe open-project --project-path="C:\Projects\Line1.ap19"

# Preview tag name changes (recommended first step)
OpenTiaToolkit.CLI.exe find-replace-tags --plc-name="PLC_1" --rename-map='{"Motor_Speed":"MotorSpeed","Valve_Open":"ValveOpen"}' --dry-run=true

# Review the preview output, then apply the changes
OpenTiaToolkit.CLI.exe find-replace-tags --plc-name="PLC_1" --rename-map='{"Motor_Speed":"MotorSpeed","Valve_Open":"ValveOpen"}' --dry-run=false

# Save the project after renaming
OpenTiaToolkit.CLI.exe save-project
```

### Example 13: DB Member Renaming with Nested Structures

```bash
# Connect and open project
OpenTiaToolkit.CLI.exe connect-portal
OpenTiaToolkit.CLI.exe open-project --project-path="C:\Projects\Line1.ap19"

# Preview DB member changes in specific blocks
OpenTiaToolkit.CLI.exe find-replace-db-members --plc-name="PLC_1" --rename-map='{"Speed_RPM":"SpeedRpm","Enable_Signal":"Enable"}' --target-blocks="MotorData" "ValveData" --dry-run=true

# Apply changes to all GlobalDBs
OpenTiaToolkit.CLI.exe find-replace-db-members --plc-name="PLC_1" --rename-map='{"Speed_RPM":"SpeedRpm"}' --dry-run=false

# Rename UDT members (propagates to all DB instances)
OpenTiaToolkit.CLI.exe find-replace-udt-members --plc-name="PLC_1" --rename-map='{"Status_Flag":"Status"}' --target-types="MotorType" --dry-run=false
```

### Example 14: Large-Scale Refactoring with Case-Insensitive Matching

```bash
# Connect and open project
OpenTiaToolkit.CLI.exe connect-portal
OpenTiaToolkit.CLI.exe open-project --project-path="C:\Projects\Line1.ap19"

# Standardize naming conventions (case-insensitive to catch variations)
OpenTiaToolkit.CLI.exe find-replace-tags --plc-name="PLC_1" --rename-map='{"Motor_Speed":"MotorSpeed","Valve_Open":"ValveOpen"}' --case-insensitive=true --dry-run=true

# Review and apply
OpenTiaToolkit.CLI.exe find-replace-tags --plc-name="PLC_1" --rename-map='{"Motor_Speed":"MotorSpeed","Valve_Open":"ValveOpen"}' --case-insensitive=true --dry-run=false

# Save and compile to verify
OpenTiaToolkit.CLI.exe save-project
OpenTiaToolkit.CLI.exe compile-software --plc-name="PLC_1"
```

### Example 15: PLC Tag Creation Workflow

```bash
# Connect and open project
OpenTiaToolkit.CLI.exe connect-portal
OpenTiaToolkit.CLI.exe open-project --project-path="C:\Projects\Line1.ap19"

# Open PLC software
OpenTiaToolkit.CLI.exe open-plc --plc-name="PLC_1"

# Create individual tags with specific addresses
OpenTiaToolkit.CLI.exe create-tag --plc-name="PLC_1" --tag-name="Motor_Run" --data-type="Bool" --logical-address="%M10.0"
OpenTiaToolkit.CLI.exe create-tag --plc-name="PLC_1" --tag-name="Motor_Speed" --data-type="Int" --logical-address="%MW12"
OpenTiaToolkit.CLI.exe create-tag --plc-name="PLC_1" --tag-name="Temperature" --data-type="Real" --logical-address="%MD20"

# Create tags in a specific tag table
OpenTiaToolkit.CLI.exe create-tag --plc-name="PLC_1" --tag-name="Alarm_Text" --data-type="String" --tag-table="HMI_Tags"

# Create tags with auto-assigned addresses
OpenTiaToolkit.CLI.exe create-tag --plc-name="PLC_1" --tag-name="Status_ID" --data-type="DInt"
OpenTiaToolkit.CLI.exe create-tag --plc-name="PLC_1" --tag-name="Error_Code" --data-type="Int"

# Save the project
OpenTiaToolkit.CLI.exe save-project
```

### Example 16: Substring Replacement Workflow

```bash
# Connect and open project
OpenTiaToolkit.CLI.exe connect-portal
OpenTiaToolkit.CLI.exe open-project --project-path="C:\Projects\Line1.ap19"

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

### Example 16b: Safer Substring Replacement with Specific Patterns

```bash
# Connect and open project
OpenTiaToolkit.CLI.exe connect-portal
OpenTiaToolkit.CLI.exe open-project --project-path="C:\Projects\Line1.ap19"

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

### Example 17: Batch Tag Creation from CSV

```powershell
# PowerShell script to create tags from CSV file
$csvFile = "C:\Tags\new_tags.csv"
# CSV format: TagName,DataType,LogicalAddress,TagTable

Import-Csv $csvFile | ForEach-Object {
    $params = @{
        "plc-name" = "PLC_1"
        "tag-name" = $_.TagName
        "data-type" = $_.DataType
    }

    if ($_.LogicalAddress) {
        $params["logical-address"] = $_.LogicalAddress
    }

    if ($_.TagTable) {
        $params["tag-table"] = $_.TagTable
    }

    $paramList = foreach ($key in $params.Keys) {
        "--$key=$($params[$key])"
    }

    OpenTiaToolkit.CLI.exe create-tag @paramList
}
```

## Argument Parsing Details

### Supported Formats

```bash
# Format 1: --name=value
OpenTiaToolkit.CLI.exe open-project --project-path="C:\Project.ap19"

# Format 2: --name value (without quotes)
OpenTiaToolkit.CLI.exe open-project --project-path C:\Project.ap19

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
OpenTiaToolkit.CLI.exe open-project --project-path="C:\My Projects\Project.ap19"

# Also works
OpenTiaToolkit.CLI.exe open-project --project-path="C:\My Projects\Project.ap19"
```

## Troubleshooting

### Command Not Found

```
✗ Unknown command: 'opn-project'. Available commands: close-project, connect-portal, ...
```

**Solution**: Check for typos in command name. Use kebab-case.

### Missing Required Argument

```
✗ Error: project-path is required
```

**Solution**: Provide the required parameter:
```bash
OpenTiaToolkit.CLI.exe open-project --project-path="C:\Project.ap19"
```

### TIA Portal Not Running

```
✗ Error: Failed to connect to TIA Portal
Details: No TIA Portal instance found
```

**Solution**: Start TIA Portal manually first, or the tool will attempt to start a new instance.

### Project Already Open

```
✗ Error: Failed to open project: C:\Project.ap19
Details: Project is already open
```

**Solution**: Close the project first, or if already open, use `get-project-info` to work with it.

### No TIA Openness API

```
Fatal error: Cannot load Siemens.Engineering assembly
```

**Solution**:
- Ensure TIA Portal V19+ is installed
- The Siemens resolver package handles assembly loading
- Set `TiaPortalLocation` environment variable if needed

## Tips and Tricks

### Create Aliases

Create batch files or aliases for common workflows:

```batch
@REM dev-open.bat
OpenTiaToolkit.CLI.exe connect-portal
OpenTiaToolkit.CLI.exe open-project --project-path="C:\Dev\Current.ap19"
OpenTiaToolkit.CLI.exe list-devices
```

### Pipe Output

```bash
# Export device list to file
OpenTiaToolkit.CLI.exe list-devices > devices.txt

# Count devices
OpenTiaToolkit.CLI.exe list-devices | find /c "Device"
```

### Error Handling

```bash
# Check exit code
OpenTiaToolkit.CLI.exe list-devices
echo %ERRORLEVEL%

# 0 = success, 1 = failure
```

## Best Practices

1. **Always check connection** before project operations:
   ```bash
   OpenTiaToolkit.CLI.exe connect-portal
   OpenTiaToolkit.CLI.exe portal-status
   ```

2. **Close projects when done** to avoid locking files:
   ```bash
   OpenTiaToolkit.CLI.exe close-project
   ```

3. **Use `--save`** when making changes you want to keep:
   ```bash
   OpenTiaToolkit.CLI.exe close-project --save
   ```

4. **Test with small projects** first before production use

## Advanced Usage

### Integration with Scripts

PowerShell example:

```powershell
# Open, inspect, close
$project = "C:\Projects\MyProject.ap19"
OpenTiaToolkit.CLI.exe connect-portal | Out-Null
OpenTiaToolkit.CLI.exe open-project --project-path="$project" | Out-Null

$devices = OpenTiaToolkit.CLI.exe list-devices
if ($LASTEXITCODE -eq 0) {
    Write-Host "Project has devices!"
}

OpenTiaToolkit.CLI.exe close-project | Out-Null
OpenTiaToolkit.CLI.exe disconnect-portal | Out-Null
```

### Error Handling in Scripts

```powershell
$ErrorActionPreference = "Stop"

try {
    OpenTiaToolkit.CLI.exe open-project --project-path="C:\Project.ap19"
}
catch {
    Write-Host "Failed to open project"
    exit 1
}
```

For more examples, see the [README.md](../README.md) or [Development Guide](DEV_GUIDE.md).
