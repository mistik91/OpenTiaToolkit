# MAIN IDEA - ✅ COMPLETE

The core idea of this project is to build a tool that can be use both from the user (in a CLI form) and an AI agent (both CLI/bash and MCP {model context protocol}).
This tool should provide an extended set of feature and common workflows for the Siemens TiaPortal environment, leveraging the TIA openness API.

#Tech Stack

Since the TIA openness API are .NET Framework native the project must be written in C# using the .NET Framework 4.8.
The MCP must be implemented accordingly to the MCP standard.

## Inspiration/Past Attempt

This idea comes from 2 different projects where these 2 ideas have been explored:

- **BoemaCLITool**: C:\Users\ferreroluca\VS PROJ\CCtest\Resources\BoemaCLITool
    - A self written CLI tool that does few things in a very spartan way
- **tia-portal-mcp**: C:\Users\ferreroluca\VS PROJ\CCtest\tiaportal-mcp-main
    - A free MCP server for TiaPortal, partially modified with CLI integration but not satisfying

We have successfully taken advantage of these 2 attempts to create OpenTiaToolkit.

# Project Status: ✅ PHASE 1 COMPLETE

## What's Been Built

### Core Components
- ✅ **OpenTiaToolkit.Core** - Shared library with commands, services, models
- ✅ **OpenTiaToolkit.CLI** - Console application with table formatting
- ✅ **OpenTiaToolkit.MCP** - MCP server for AI agents
- ✅ **OpenTiaToolkit.Tests** - 43 unit tests (all passing)

### Features Implemented
- ✅ 8 core commands (portal, project, device operations)
- ✅ Unified command interface (CLI and MCP share same commands)
- ✅ Table-formatted CLI output
- ✅ JSON-based MCP output
- ✅ TIA V19+ compatibility
- ✅ Session-only state management
- ✅ Dependency injection
- ✅ Comprehensive logging
- ✅ Error handling
- ✅ Help system

### Documentation
- ✅ README.md with overview, installation, usage
- ✅ CLI_USAGE.md with detailed command reference
- ✅ DEV_GUIDE.md with development setup and patterns
- ✅ CHANGELOG.md with version history
- ✅ .gitignore configured

### Testing
- ✅ 43 unit tests (framework logic, no TIA required)
- ✅ 13 integration tests (requires TIA V19)
  - Test1Portal: 4 tests for portal connection/disconnection
  - Test2Project: 6 tests for project operations
  - Test3Devices: 3 tests for device listing
  - Uses real TIA project at `D:\Siemens\V19\MCP_TEST\MCP_TEST.ap19`
  - All tests pass successfully

## Style Guide

**Keep it clean, keep it lean, no fancy BS - straight to the point.**
- Always write tests and TEST IT YOURSELF
- Test project located at `D:\Siemens\V19\MCP_TEST\MCP_TEST`
- Test CLI first, then MCP server (ask user to reconnect after build)

### IMPORTANT

This project is expected to work with TiaPortal versions spanning from V19 going forward.

## Next Steps

### Phase 2: Block Operations (Future)
- List blocks, get block, export blocks, compile blocks

### Phase 3: HMI Operations (Future)
- Screen templates from BoemaCLITool
- HMI Advanced/Unified support

### Phase 4: Advanced Features (Future)
- Find and replace operations
- Cross-reference operations
- Multiuser session support

## Quick Start

### CLI Usage
```bash
# Connect to TIA Portal
OpenTiaToolkit.CLI.exe connect-portal

# Open a project
OpenTiaToolkit.CLI.exe open-project --project-path="C:\Projects\My.ap19"

# List devices
OpenTiaToolkit.CLI.exe list-devices
```

### MCP Server Setup
Configure in Claude Desktop `config.json`:
```json
{
  "mcpServers": {
    "open-tia-toolkit": {
      "command": "C:\\Path\\To\\OpenTiaToolkit.MCP.exe"
    }
  }
}
```

---

**Project**: OpenTiaToolkit
**Version**: 1.0.0 (Phase 1)
**Status**: Ready for testing with TIA V19 projects
