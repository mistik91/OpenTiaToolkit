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
