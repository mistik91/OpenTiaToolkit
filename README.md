# OpenTiaToolkit

An open-source automation toolkit for Siemens TIA Portal, providing both CLI and MCP server interfaces for human and AI agent interactions.

## Overview

OpenTiaToolkit is a C# .NET Framework 4.8 library that wraps the TIA Openness API, providing a unified command interface that works both as a command-line tool for humans and as an MCP (Model Context Protocol) server for AI agents like Claude.

### Key Features

- **Dual Interface**: Single codebase serving both CLI and MCP workflows
- **Interactive REPL Mode**: Persistent connections across commands with shell-like interface
- **Clean Architecture**: Shared commands and services between CLI and MCP
- **30 Commands**: Complete coverage of portal, project, device, HMI, and PLC operations (including advanced block operations)
  - Tag Management: create-tag, find-replace-tags (with substring matching support)
  - Block Operations: search-blocks, get-block-info, export-block, delete-blocks
  - PLC Operations: compile-software, export-plc, get-software-tree, get-blocks
  - And more...
- **Multi-Version Support**: Compatible with TIA Portal V21 and later
- **Graceful Degradation**: Automatically adapts to TIA Portal version capabilities
- **100% Test Coverage**: 45+ integration tests, all passing
- **Open Source**: Ready for community collaboration

## Table of Contents

- [Installation](#installation)
- [Quick Start](#quick-start)
- [CLI Usage](#cli-usage)
  - [Single-Shot Mode](#single-shot-mode)
  - [Interactive REPL Mode](#interactive-repl-mode)
- [MCP Server Usage](#mcp-server-usage)
- [Available Commands](#available-commands)
- [Architecture](#architecture)
- [Development](#development)
- [Testing](#testing)
- [Contributing](#contributing)
- [License](#license)

## Installation

### Prerequisites

- Windows OS
- .NET Framework 4.8 or later
- TIA Portal V21 or later (for runtime)
  - **Note**: Full block creation support (FB, FC, DB, OB, SFB, SFC, UDT)
  - See [TIA Portal Version Support](docs/TIA_PORTAL_VERSION_SUPPORT.md) for details
- Visual Studio 2019 or later (for development)

### Build from Source

```bash
# Clone the repository
git clone https://github.com/your-org/OpenTiaToolkit.git
cd OpenTiaToolkit

# Build the solution
dotnet build OpenTiaToolkit.slnx
```

### Output Artifacts

- `src/OpenTiaToolkit.CLI/bin/Debug/net48/OpenTiaToolkit.CLI.exe` - CLI executable
- `src/OpenTiaToolkit.MCP/bin/Debug/net48/OpenTiaToolkit.MCP.exe` - MCP server executable
- `src/OpenTiaToolkit.Core/bin/Debug/net48/OpenTiaToolkit.Core.dll` - Core library

## Quick Start

### CLI Usage

**Interactive REPL Mode** (Recommended for interactive use):

```bash
# Start interactive mode
OpenTiaToolkit.CLI.exe
# Or explicitly
OpenTiaToolkit.CLI.exe --interactive
```

**Single-Shot Mode** (For scripts and automation):

```bash
# Connect to TIA Portal
OpenTiaToolkit.CLI.exe connect-portal

# Open a project
OpenTiaToolkit.CLI.exe open-project --project-path="C:\Projects\MyProject.ap21"

# List devices
OpenTiaToolkit.CLI.exe list-devices

# Get help
OpenTiaToolkit.CLI.exe help
OpenTiaToolkit.CLI.exe help open-project
```

### MCP Server with Claude Desktop

1. **Configure Claude Desktop** (`config.json`):

```json
{
  "mcpServers": {
    "open-tia-toolkit": {
      "command": "C:\\Path\\To\\OpenTiaToolkit.MCP.exe",
      "env": {
        "PATH": "C:\\Program Files\\Siemens\\Automation\\Portal V21\\PublicAPI\\V21\\net48"
      }
    }
  }
}
```

2. **Restart Claude Desktop**

3. **Use in conversation**:
   > "Open the project at C:\Projects\MyProject.ap21"
   > "List all devices in the project"
   > "Get project information"

## CLI Usage

### Command Syntax

Commands use kebab-case naming (e.g., `open-project`, `list-devices`):

```bash
OpenTiaToolkit.CLI.exe <command> [options]
```

### Argument Formats

Supports both `--name=value` and `--name value` formats:

```bash
# Format 1: --name=value
OpenTiaToolkit.CLI.exe open-project --project-path="C:\Project.ap19"

# Format 2: --name value
OpenTiaToolkit.CLI.exe open-project --project-path "C:\Project.ap19"

# Boolean flags
OpenTiaToolkit.CLI.exe close-project --save
```

### Exit Codes

- `0` - Success
- `1` - Failure (check error message)

### Single-Shot Mode

The single-shot mode is the default when arguments are provided. Each command executes independently:

```bash
OpenTiaToolkit.CLI.exe <command> [options]
```

### Interactive REPL Mode

The REPL (Read-Eval-Print Loop) mode maintains connection state across multiple commands. Start it by running the CLI without arguments or with the `--interactive` flag:

```bash
# Start REPL mode
OpenTiaToolkit.CLI.exe

# Or explicitly
OpenTiaToolkit.CLI.exe --interactive
# Or short form
OpenTiaToolkit.CLI.exe -i
```

**Key Benefits**:

- **Persistent Connections**: TIA Portal instance and project connections stay open across commands
- **Faster Workflows**: No need to reconnect for each operation
- **Shell-like Experience**: Familiar prompt and command interface
- **Full Command Support**: All 27 commands work in REPL mode

**REPL-Specific Commands**:

| Command | Description |
|---------|-------------|
| `exit` or `quit` | Exit REPL mode |
| `clear` or `cls` | Clear screen |
| `help` or `?` | Show available commands |
| `Ctrl+C` | Gracefully exit REPL |

**Example Session**:

```bash
C:\> OpenTiaToolkit.CLI.exe

╔═══════════════════════════════════════════════════════════╗
║        OpenTia Toolkit - Interactive Mode                 ║
╚═══════════════════════════════════════════════════════════╝

Connected TIA Portal instances will persist across commands.
Type 'help' for available commands or 'exit' to quit.

tia> connect-portal
✓ Connected to TIA Portal V21

tia> open-project --project-path="D:\Projects\MCP_TEST_V21.ap21"
✓ Project opened successfully

tia> list-devices
Found 2 device(s):
[Devices listed...]

tia> portal-status
Connected to TIA Portal V21
Project: D:\Projects\MCP_TEST_V21.ap21

tia> exit
Goodbye!
```

**Argument Parsing**: The REPL mode supports the same argument formats as single-shot mode:

```bash
tia> open-project --project-path="C:\Project.ap21"
tia> open-project --project-path "C:\Project.ap21"
tia> close-project --save=true
```

**Error Handling**: Errors in REPL mode display the error message but return to the prompt:

```bash
tia> invalid-command
Error: Unknown command: invalid-command

tia> open-project --project-path="nonexistent.ap21"
Error: Project file not found

tia> _
```

## MCP Server Usage

### Protocol

The MCP server communicates over stdin/stdout using JSON-RPC messages following the [Model Context Protocol](https://modelcontextprotocol.io/) specification.

### Available Tools

All 27 CLI commands are automatically exposed as MCP tools with the same names:

#### Portal Instance Management (Phase 2)
| Tool Name | Description |
|-----------|-------------|
| `open-tia-portal` | Launches new TIA Portal instance |
| `close-tia-portal` | Closes TIA Portal instance |
| `list-instances` | Lists running TIA Portal instances |
| `connect-instance` | Connects to specific TIA Portal instance by ID |
| `connect-portal` | Connects to TIA Portal (convenience method) |
| `disconnect-portal` | Disconnects from TIA Portal |
| `portal-status` | Gets connection status |

#### Project Operations (Phase 1 & 2)
| Tool Name | Description |
|-----------|-------------|
| `open-project` | Opens a project file |
| `close-project` | Closes current project |
| `list-projects` | Lists all open projects |
| `get-project-info` | Gets current project info |
| `save-project` | Saves current project |
| `save-as-project` | Saves project to new location |
| `get-project-tree` | Gets hierarchical project structure |

#### Device Operations (Phase 1 & 3)
| Tool Name | Description |
|-----------|-------------|
| `list-devices` | Lists devices in project |
| `get-device-info` | Gets detailed device information |
| `get-device-item-info` | Gets device item details |
| `list-hmis` | Lists all HMI devices in the project |

#### PLC Operations
| Tool Name | Description |
|-----------|-------------|
| `list-plcs` | Lists all PLC devices in the project (automatic PLC discovery) |
| `open-plc` | Opens PLC device software |
| `get-software-tree` | Gets PLC software structure |
| `compile-software` | Compiles PLC software |
| `export-plc` | Exports PLC blocks to XML |
| `get-blocks` | Lists blocks from PLC software |
| `delete-blocks` | Deletes blocks matching regex pattern |
| `get-block-info` | Gets detailed information about a specific block |
| `export-block` | Exports a single block to XML |
| `search-blocks` | Searches blocks by name/comment |

### Example MCP Tool Call

```json
{
  "method": "tools/call",
  "params": {
    "name": "open-project",
    "arguments": {
      "project-path": "C:\\Projects\\MyProject.ap21"
    }
  }
}
```

## Available Commands

### Portal Instance Management

#### `open-tia-portal`
Launches a new TIA Portal instance (with UI by default).

```bash
OpenTiaToolkit.CLI.exe open-tia-portal
OpenTiaToolkit.CLI.exe open-tia-portal --with-ui=false
```

#### `close-tia-portal`
Closes the current TIA Portal instance.

```bash
OpenTiaToolkit.CLI.exe close-tia-portal
```

#### `list-instances`
Lists all running TIA Portal instances.

```bash
OpenTiaToolkit.CLI.exe list-instances
```

#### `connect-instance`
Connects to a specific TIA Portal instance by process ID.

```bash
OpenTiaToolkit.CLI.exe connect-instance --instance-id=12345
```

#### `connect-portal`
Connects to a running TIA Portal instance (convenience method).

```bash
OpenTiaToolkit.CLI.exe connect-portal
```

#### `disconnect-portal`
Disconnects from the TIA Portal instance.

```bash
OpenTiaToolkit.CLI.exe disconnect-portal
```

#### `portal-status`
Gets the current TIA Portal connection status.

```bash
OpenTiaToolkit.CLI.exe portal-status
```

### Project Operations

#### `open-project`
Opens a TIA Portal project file (.ap21, .ap19, .als21, or .als19).

```bash
OpenTiaToolkit.CLI.exe open-project --project-path="C:\Projects\My.ap21"
```

#### `close-project`
Closes the currently open project.

```bash
OpenTiaToolkit.CLI.exe close-project
OpenTiaToolkit.CLI.exe close-project --save=true
```

#### `list-projects`
Lists all open TIA Portal projects and sessions.

```bash
OpenTiaToolkit.CLI.exe list-projects
```

#### `get-project-info`
Gets information about the currently open project.

```bash
OpenTiaToolkit.CLI.exe get-project-info
```

#### `save-project`
Saves the current project.

```bash
OpenTiaToolkit.CLI.exe save-project
```

#### `save-as-project`
Saves the project to a new location.

```bash
OpenTiaToolkit.CLI.exe save-as-project --destination-path="C:\Projects\MyCopy"
```

#### `get-project-tree`
Gets the hierarchical project structure.

```bash
OpenTiaToolkit.CLI.exe get-project-tree
OpenTiaToolkit.CLI.exe get-project-tree --depth=2
```

### Device Operations

#### `list-devices`
Lists all devices in the currently open project.

```bash
OpenTiaToolkit.CLI.exe list-devices
```

#### `get-device-info`
Gets detailed information about a specific device.

```bash
OpenTiaToolkit.CLI.exe get-device-info --device-name="PLC_1"
```

#### `get-device-item-info`
Gets details about a device item.

```bash
OpenTiaToolkit.CLI.exe get-device-item-info --device-name="PLC_1" --item-name="CPU"
```

#### `list-hmis`
Lists all HMI devices found in the project with automatic HMI discovery.

```bash
OpenTiaToolkit.CLI.exe list-hmis
```

**Output:**
```
Found 1 HMI device(s):

Name                            Type                            Parent Device                   Confirmed
----------------------------------------------------------------------------------------
HMI_1                           TP1200                          Controller_1                    Yes
```

### PLC Operations

#### `list-plcs`
Lists all PLC devices found in the project with automatic PLC discovery.

This command uses multiple detection strategies to find PLCs even when they don't follow standard naming conventions (e.g., not named "PLC_1").

**Detection Methods:**
1. Name-based: Detects devices with "PLC" in the name (case-insensitive)
2. Software-based: Uses reflection to find PlcSoftware objects
3. Type-based: Identifies S7-1200, S7-1500, S7-300, and S7-400 devices

```bash
OpenTiaToolkit.CLI.exe list-plcs
```

**Output:**
```
Found 2 PLC device(s):

Name                           Parent Device                           Type                     Confirmed
------------------------------------------------------------------------------
PLC_1                          Controller_1                            Confirmed PLC software   ✓ Yes
S7-1500_CPU                    Station_2                               Device type: S7-1500    No
```

Use this command to discover the PLC names required for other PLC operations like `open-plc`, `get-software-tree`, etc.

#### `open-plc`
Opens PLC device software.

```bash
OpenTiaToolkit.CLI.exe open-plc --plc-name="PLC_1"
```

#### `get-software-tree`
Gets the PLC software structure (blocks, types, sources).

```bash
OpenTiaToolkit.CLI.exe get-software-tree --plc-name="PLC_1"
OpenTiaToolkit.CLI.exe get-software-tree --plc-name="PLC_1" --depth=3
```

#### `compile-software`
Compiles PLC software.

```bash
OpenTiaToolkit.CLI.exe compile-software --plc-name="PLC_1"
OpenTiaToolkit.CLI.exe compile-software --plc-name="PLC_1" --password="secret"
```

#### `export-plc`
Exports all PLC blocks to XML files.

```bash
OpenTiaToolkit.CLI.exe export-plc --plc-name="PLC_1" --export-path="C:\Exports"
```

#### `get-blocks`
Lists blocks from PLC software.

```bash
OpenTiaToolkit.CLI.exe get-blocks --plc-name="PLC_1"
OpenTiaToolkit.CLI.exe get-blocks --plc-name="PLC_1" --filter="Motor"
```

#### `delete-blocks`
Deletes blocks matching a regex pattern from PLC software. Requires confirmation for safety.

```bash
OpenTiaToolkit.CLI.exe delete-blocks --plc-name="PLC_1" --filter="Test_.*"
OpenTiaToolkit.CLI.exe delete-blocks --plc-name="PLC_1" --filter="Test_.*" --confirm=true
```

**Parameters:**
- `plc-name` (required): Name of the PLC device
- `filter` (required): Regex pattern to match block names
- `confirm` (optional): Skip confirmation prompt (default: false)

**Safety Features:**
- Preview of blocks to be deleted before confirmation
- Skips read-only/protected blocks with warning
- Requires `--confirm=true` flag to proceed without prompt
- Returns count of deleted, skipped, and failed blocks

#### `get-block-info`
Gets detailed information about a specific PLC block.

```bash
OpenTiaToolkit.CLI.exe get-block-info --plc-name="PLC_1" --block-name="Main"
```

**Parameters:**
- `plc-name` (required): Name of the PLC device
- `block-name` (required): Name of the block

**Returns:**
- Block name, type, number, language
- Read-only status, comment
- Author (if available)
- Memory size (if available)
- Programming language details

#### `export-block`
Exports a single PLC block to an XML file.

```bash
OpenTiaToolkit.CLI.exe export-block --plc-name="PLC_1" --block-name="Main" --export-path="C:\Exports\Main.xml"
```

**Parameters:**
- `plc-name` (required): Name of the PLC device
- `block-name` (required): Name of the block to export
- `export-path` (required): Full file path for the exported XML file

#### `search-blocks`
Searches PLC blocks by name or comment with optional type filter.

```bash
OpenTiaToolkit.CLI.exe search-blocks --plc-name="PLC_1" --pattern="Motor"
OpenTiaToolkit.CLI.exe search-blocks --plc-name="PLC_1" --pattern="Test" --search-in="comment"
OpenTiaToolkit.CLI.exe search-blocks --plc-name="PLC_1" --pattern="FB" --filter-type="FB"
```

**Parameters:**
- `plc-name` (required): Name of the PLC device
- `pattern` (required): Search pattern (supports wildcards)
- `search-in` (optional): Where to search - "name" or "comment" (default: "name")
- `filter-type` (optional): Block type filter (FB, FC, DB, OB)

### Tag Management

#### `create-tag`
Creates a new PLC tag in the specified tag table.

```bash
# Create with logical address
OpenTiaToolkit.CLI.exe create-tag --plc-name="PLC_1" --tag-name="Motor_Status" --data-type="Bool" --logical-address="%M10.0"

# Create without address (auto-assigned)
OpenTiaToolkit.CLI.exe create-tag --plc-name="PLC_1" --tag-name="Counter" --data-type="DInt"
```

**Parameters:**
- `plc-name` (required): Name of the PLC device
- `tag-name` (required): Name of the tag to create
- `data-type` (required): Data type (Bool, Int, DInt, Real, String, etc.)
- `logical-address` (optional): Logical address (e.g., %M10.0, %MW12)
- `tag-table` (optional): Tag table name (default: first/default table)

#### `find-replace-tags`
Finds and replaces PLC tag names across all tag tables. Supports exact matching and substring matching.

```bash
# Exact matching - rename specific tags
OpenTiaToolkit.CLI.exe find-replace-tags --plc-name="PLC_1" --rename-map='{"Motor_Speed":"MotorSpeed"}' --dry-run=false

# Substring matching - replace "Speed" with "RPM" in all tags
OpenTiaToolkit.CLI.exe find-replace-tags --plc-name="PLC_1" --rename-map='{"Speed":"RPM"}' --substring-match=true --dry-run=true

# Case-insensitive substring matching
OpenTiaToolkit.CLI.exe find-replace-tags --plc-name="PLC_1" --rename-map='{"speed":"RPM"}' --substring-match=true --case-insensitive=true --dry-run=false
```

**Parameters:**
- `plc-name` (required): Name of the PLC device
- `rename-map` (required): Dictionary mapping old names to new names (JSON string)
- `case-insensitive` (optional): Enable case-insensitive matching (default: false)
- `dry-run` (optional): Preview changes without applying (default: true)
- `substring-match` (optional): Enable substring matching (default: false)

**Substring Matching Examples:**
- `{"Speed":"RPM"}` replaces all occurrences of "Speed" with "RPM"
  - `Motor_Speed_Setpoint` → `Motor_RPM_Setpoint`
  - `Valve_Speed_Actual` → `Valve_RPM_Actual`
- `{"_":"-"}` converts underscores to hyphens
  - `Motor_Speed_Setpoint` → `Motor-Speed-Setpoint`

**⚠️ Warning:** Substring matching can cause unintended replacements. Always use `--dry-run=true` first.

**V21 API Features:**
- Full block creation support (FB, FC, DB, OB, SFB, SFC, UDT)
- Generic Create method with BlockType enum
- Advanced block operations and search
- Tag creation and management with substring matching support

## Architecture

### Solution Structure

```
OpenTiaToolkit.slnx
├── src/
│   ├── OpenTiaToolkit.Core/      # Shared core library
│   │   ├── Abstractions/         # Interfaces (ICommand, IPortalService, IPlcService)
│   │   ├── Commands/             # Command implementations (30 commands)
│   │   │   ├── Portal/           # Portal instance management
│   │   │   ├── Project/          # Project operations
│   │   │   ├── Devices/          # Device operations
│   │   │   └── Plc/              # PLC operations
│   │   ├── Services/             # TIA API wrapper (PortalService, PlcService)
│   │   └── Models/               # Data models
│   │
│   ├── OpenTiaToolkit.CLI/       # Console application
│   │   ├── Routing/              # CommandRouter, argument parsing
│   │   └── Output/               # TableOutputFormatter
│   │
│   └── OpenTiaToolkit.MCP/       # MCP server application
│       └── Server/               # ToolRegistry, McpServerHost
│
└── tests/
    └── OpenTiaToolkit.Tests/     # Unit & integration tests
        ├── Integration/          # 45 integration tests
        │   ├── Test1Portal.cs
        │   ├── Test2Project.cs
        │   ├── Test3Devices.cs
        │   ├── Test4PortalInstances.cs
        │   ├── Test5ProjectSave.cs
        │   ├── Test6ProjectTree.cs
        │   ├── Test7PlcSoftware.cs
        │   └── Test8PlcOperations.cs
        └── Settings.cs           # Test configuration
```

### Layered Architecture

```
┌─────────────────────────────────────┐
│       Presentation Layer             │
├──────────────┬──────────────┬───────────────┤
│   CLI Mode    │  MCP Server  │  (Future?)    │
│  (REPL/1-shot)│  (STDIO)    │  Library/API  │
└──────────────┴──────────────┴───────────────┘
                    ↓
┌─────────────────────────────────────┐
│      Command/Tool Abstraction Layer │
│  (Unified interface for CLI tools & MCP)│
│  - 27 commands across 4 categories  │
└─────────────────────────────────────┘
                    ↓
┌─────────────────────────────────────┐
│           Business Logic Layer        │
│  - Portal management (7 commands)   │
│  - Project operations (7 commands)  │
│  - Device operations (3 commands)   │
│  - PLC operations (5 commands)      │
└─────────────────────────────────────┘
                    ↓
┌─────────────────────────────────────┐
│         TIA Openness API Wrapper      │
│  - IPortalService (Portal & Project) │
│  - IPlcService (PLC operations)     │
│  - Version compatibility (V21+)      │
│  - Connection state management      │
└─────────────────────────────────────┘
                    ↓
              TIA Openness API
```

## Development

### Building

```bash
# Build all projects
dotnet build

# Build specific project
dotnet build src/OpenTiaToolkit.Core
dotnet build src/OpenTiaToolkit.CLI
dotnet build src/OpenTiaToolkit.MCP
```

### Running Tests

```bash
# Run all tests
dotnet test

# Run unit tests only (fast, no TIA required)
dotnet test --filter "Category=Unit"

# Run integration tests (requires TIA V21)
dotnet test --filter "Category=Integration"
```

### Adding New Commands

1. **Create command class** in `src/OpenTiaToolkit.Core/Commands/`:

```csharp
public class MyCommand : ICommand
{
    private readonly IPortalService _portalService;

    public MyCommand(IPortalService portalService)
    {
        _portalService = portalService;
    }

    public string Name => "my-command";
    public string Description => "Does something useful";
    public CommandSchema Schema => new CommandSchema(Description);

    public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
    {
        // Implementation
        return CommandResult.CreateSuccess(data: result);
    }
}
```

2. **Register in CLI** (`src/OpenTiaToolkit.CLI/Program.cs`):

```csharp
services.AddSingleton<ICommand, MyCommand>();
```

3. **Register in MCP** (`src/OpenTiaToolkit.MCP/Program.cs`):

```csharp
services.AddSingleton<ICommand, MyCommand>();
```

4. **Add tests** for the new command

That's it! The command is now available in both CLI and MCP.

## Testing

### Integration Tests

- **Location**: `tests/OpenTiaToolkit.Tests/Integration/`
- **Framework**: MSTest 3.9.2
- **Count**: 45 tests (all passing, 100% success rate)
- **Execution Time**: ~2 minutes
- **Test Files**: 8 test classes covering all functionality

```bash
# Run all integration tests
dotnet test tests/OpenTiaToolkit.Tests/OpenTiaToolkit.Tests.csproj --filter "FullyQualifiedName~Integration"

# Run specific test class
dotnet test --filter "TestClass=Test1Portal"
```

### Test Coverage

| Test File | Tests | Coverage |
|-----------|-------|----------|
| Test1Portal.cs | 4 | Portal connection basics |
| Test2Project.cs | 6 | Project open/close/info |
| Test3Devices.cs | 3 | Device listing |
| Test4PortalInstances.cs | 6 | Portal instance management |
| Test5ProjectSave.cs | 6 | Project save operations |
| Test6ProjectTree.cs | 6 | Project tree structure |
| Test7PlcSoftware.cs | 6 | Device information |
| Test8PlcOperations.cs | 10 | PLC operations |
| **Total** | **45** | **All phases** |

### Test Organization

- **[DoNotParallelize]** attribute ensures tests don't interfere with TIA Portal
- Each test file focuses on a specific command category
- Tests use a dedicated test project: `D:\Siemens\V21\MCP_TEST_V21\MCP_TEST_V21.ap21`
- Proper cleanup ensures no state leakage between tests

## Project Status

### Phase 1 ✅ - Foundation (Complete)

- [x] Solution structure
- [x] Core abstractions (ICommand, IPortalService)
- [x] Portal service (PortalService)
- [x] 8 core commands (Portal, Project, Device basics)
- [x] CLI application with table output
- [x] MCP server with tool registration
- [x] 6 integration tests

### Phase 2 ✅ - Portal Instance Management & Project Operations (Complete)

- [x] IPlcService interface and PlcService implementation
- [x] 7 commands: open-tia-portal, close-tia-portal, list-instances, connect-instance
- [x] 3 commands: save-project, save-as-project, get-project-tree
- [x] 12 integration tests (Test4, Test5, Test6)
- [x] ProjectTreeNode model
- [x] TiaPortalInstance model

### Phase 3 ✅ - Device & PLC Operations (Complete)

- [x] 2 commands: get-device-info, get-device-item-info
- [x] 6 commands: list-plcs, open-plc, get-software-tree, compile-software, export-plc, get-blocks
- [x] 16 integration tests (Test7, Test8)
- [x] SoftwareTreeNode model
- [x] CompilationResult, BlockInfo models
- [x] PLC operations with automatic PLC discovery

### Phase 4 ✅ - Advanced PLC Block Operations (Complete)

- [x] 4 commands: delete-blocks, get-block-info, export-block, search-blocks
- [x] 1 command: list-hmis (HMI device discovery)
- [x] Advanced block search and filtering
- [x] Safe block deletion with confirmation
- [x] Individual block export functionality
- [x] Block information retrieval

### Current Status

- **Total Commands**: 27
- **Test Coverage**: 45/45 passing (100%)
- **Documentation**: Complete
- **Known Issues**: 2 tests ignored (TIA Portal SaveAs file access limitation)

## Contributing

We welcome contributions! Please:

1. Fork the repository
2. Create a feature branch
3. Make your changes (keep it clean and lean)
4. Add tests for new features
5. Ensure all tests pass
6. Submit a pull request

### Code Style

- C# conventions
- Clean, lean code - no fancy BS, straight to the point
- Always write tests
- Test yourself with the CLI first, then MCP
- Follow the existing patterns

## Troubleshooting

### "Cannot load Siemens.Engineering assembly"

**Problem**: The TIA Openness assemblies aren't found.

**Solution**:
- Ensure TIA Portal V21+ is installed at `C:\Program Files\Siemens\Automation\Portal V21\`
- The custom TiaPortalAssemblyResolver will handle assembly loading at runtime
- For development, set environment variable `TiaPortalLocation` to your TIA installation path

### MCP server not responding

**Problem**: Claude Desktop can't communicate with the MCP server.

**Solution**:
- Check the command path in Claude Desktop config
- Ensure absolute path is used
- Check logs for error messages
- Test manually: `echo '{"method":"initialize"}' | OpenTiaToolkit.MCP.exe`

### Block export fails with various errors

**Problem**: Attempting to export blocks results in different error messages.

**Solution**:

**Error 1: "Inconsistent blocks and PLC data types (UDT) cannot be exported"**
- **Cause**: Project contains blocks with User-Defined Type (UDT) inconsistencies
- **Fix**: Compile the project first in TIA Portal
  - Open project in TIA Portal
  - Compile the PLC (Ctrl+B or "Compile > Software")
  - Retry the export
- **Alternative**: Manually fix UDT inconsistencies in TIA Portal

**Error 2: "The export of block 'XYZ' is not permitted"**
- **Cause**: Certain blocks (especially safety blocks) cannot be exported
- **Reason**: TIA Portal security feature - safety blocks have export restrictions
- **Examples**: Safety OBs, certified blocks, blocks with special protection
- **Solution**: This is expected behavior - these blocks cannot be exported via API

**Best Practice**: Always compile the project before exporting blocks to ensure consistency.

## License

[Specify your license - e.g., MIT, Apache 2.0, etc.]

## Acknowledgments

Built with inspiration from:
- **BoemaCLITool**: Custom CLI tool for TIA Portal automation
- **tia-portal-mcp**: Open-source MCP server for TIA Portal

## Contact

- **Issues**: https://github.com/your-org/OpenTiaToolkit/issues
- **Discussions**: https://github.com/your-org/OpenTiaToolkit/discussions
