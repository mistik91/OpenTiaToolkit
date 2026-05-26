# Development Guide

Guide for developers contributing to OpenTiaToolkit.

## Table of Contents

- [Development Setup](#development-setup)
- [Project Structure](#project-structure)
- [Architecture](#architecture)
- [Adding New Commands](#adding-new-commands)
- [Testing Strategy](#testing-strategy)
- [Code Style Guidelines](#code-style-guidelines)
- [Build Process](#build-process)
- [Release Process](#release-process)

## Development Setup

### Prerequisites

1. Install **TIA Portal V21**.
   V21 is the primary supported version for development and integration validation.
2. Install **Visual Studio 2019+** with:
   - .NET desktop development workload
   - MSTest adapter
3. Clone the repository.

### Getting Started

```bash
# Clone the repository
git clone https://github.com/mistik91/OpenTiaToolkit.git
cd OpenTiaToolkit

# Open in Visual Studio
OpenTiaToolkit.sln

# Build to verify setup
dotnet build OpenTiaToolkit.sln
```

If you want to pre-authorize a freshly built executable for TIA Openness, run [Register-OpennessAllowList.ps1](/E:/Documents/Codex/OpenTiaToolkit/scripts/Register-OpennessAllowList.ps1) from an elevated PowerShell session after the build.

### Recommended Visual Studio Extensions

- .NET Productivity Tools
- ReSharper (optional)

## Project Structure

### Solution Layout

```
OpenTiaToolkit.sln
├── src/
│   ├── OpenTiaToolkit.Core/      # Shared commands, services, builders, models
│   ├── OpenTiaToolkit.CLI/       # Console host
│   └── OpenTiaToolkit.MCP/       # MCP host
└── tests/
    └── OpenTiaToolkit.Tests/     # Unit, composition, and integration tests
```

### Layer Responsibilities

| Layer | Responsibility |
|-------|----------------|
| `OpenTiaToolkit.Core` | Shared abstractions, commands, services, builders, models |
| `OpenTiaToolkit.CLI` | CLI routing, REPL flow, output formatting |
| `OpenTiaToolkit.MCP` | MCP transport and tool registry |
| `OpenTiaToolkit.Tests` | Fast tests plus real TIA integration validation |

## Architecture

### Shared Host Composition

Both executable hosts reuse the same registrations from [ServiceCollectionExtensions.cs](/E:/Documents/Codex/OpenTiaToolkit/src/OpenTiaToolkit.Core/ServiceCollectionExtensions.cs):

- `AddOpenTiaToolkitCoreServices()`
- `AddOpenTiaToolkitCommands()`

That means command and service registration happens once in Core and is automatically shared by CLI and MCP.

### Shared Session State

[EngineeringSessionContext.cs](/E:/Documents/Codex/OpenTiaToolkit/src/OpenTiaToolkit.Core/Services/EngineeringSessionContext.cs) is the single source of truth for:

- attached `TiaPortal`
- current project or local session
- current PLC software
- ownership flags used to decide whether close/dispose operations are allowed

The main goal is to keep portal/project/PLC state consistent across services.

### Service Boundaries

The public service interfaces stay stable, but the implementation is now split into capability services:

- [PortalService.cs](/E:/Documents/Codex/OpenTiaToolkit/src/OpenTiaToolkit.Core/Services/PortalService.cs): thin facade for the public portal API
- [PortalSessionService.cs](/E:/Documents/Codex/OpenTiaToolkit/src/OpenTiaToolkit.Core/Services/PortalSessionService.cs): portal attachment, open/close, ownership, save flows
- [PortalInspectionService.cs](/E:/Documents/Codex/OpenTiaToolkit/src/OpenTiaToolkit.Core/Services/PortalInspectionService.cs): read-only project/device/PLC/HMI inspection
- [PlcService.cs](/E:/Documents/Codex/OpenTiaToolkit/src/OpenTiaToolkit.Core/Services/PlcService.cs): thin facade for the public PLC API
- [PlcBrowseService.cs](/E:/Documents/Codex/OpenTiaToolkit/src/OpenTiaToolkit.Core/Services/PlcBrowseService.cs): read-only browse/search/info operations
- [PlcCompileService.cs](/E:/Documents/Codex/OpenTiaToolkit/src/OpenTiaToolkit.Core/Services/PlcCompileService.cs): software compilation
- [PlcBlockMutationService.cs](/E:/Documents/Codex/OpenTiaToolkit/src/OpenTiaToolkit.Core/Services/PlcBlockMutationService.cs): block creation/deletion
- [PlcTagService.cs](/E:/Documents/Codex/OpenTiaToolkit/src/OpenTiaToolkit.Core/Services/PlcTagService.cs): tag creation and update flows
- [PlcMemberRenameService.cs](/E:/Documents/Codex/OpenTiaToolkit/src/OpenTiaToolkit.Core/Services/PlcMemberRenameService.cs): DB/UDT member rename flows
- [PlcImportExportService.cs](/E:/Documents/Codex/OpenTiaToolkit/src/OpenTiaToolkit.Core/Services/PlcImportExportService.cs): XML and document import/export flows
- [VciService.cs](/E:/Documents/Codex/OpenTiaToolkit/src/OpenTiaToolkit.Core/Services/VciService.cs): VCI workspaces, mappings, comparison state, and surgical/bulk synchronization

### Builders

Tree-shaping logic is being extracted from services into dedicated builders:

- [ProjectTreeBuilder.cs](/E:/Documents/Codex/OpenTiaToolkit/src/OpenTiaToolkit.Core/Services/ProjectTreeBuilder.cs)
- [SoftwareTreeBuilder.cs](/E:/Documents/Codex/OpenTiaToolkit/src/OpenTiaToolkit.Core/Services/SoftwareTreeBuilder.cs)

These builders are intended to be testable with fake object graphs so traversal behavior can be verified without a live TIA session.

### V21 Adapter Boundary

[V21OpennessAdapter.cs](/E:/Documents/Codex/OpenTiaToolkit/src/OpenTiaToolkit.Core/Services/V21OpennessAdapter.cs) is the current boundary for version-sensitive Siemens reflection. It centralizes:

- V21 type and service discovery
- compile-service invocation
- V21 block creation helpers
- export/import method selection
- tag-table access, tag creation, logical-address updates, and comment access
- recursive Global DB / UDT discovery and nested member traversal
- PLC and HMI service detection

When new V21-specific Siemens reflection is needed, prefer adding it there instead of duplicating assembly/type lookup in feature services.

### Parallel Development Guidance

If you are adding or changing features, prefer touching one capability area instead of the public facades.

Good work lanes:

- portal lifecycle
- portal inspection/tree shaping
- PLC browse/info
- PLC compile
- PLC import/export
- VCI workspace and synchronization flows
- PLC tags and rename flows
- CLI/MCP presentation layers

The main refactoring rule is: new behavior should usually require edits in one feature service, one command, and one test area.

## Adding New Commands

### Step 1: Create the Command

Create the command in `src/OpenTiaToolkit.Core/Commands/...`:

```csharp
using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Portal
{
    public class MyNewCommand : ICommand
    {
        private readonly IPortalService _portalService;

        public MyNewCommand(IPortalService portalService)
        {
            _portalService = portalService;
        }

        public string Name => "my-new-command";
        public string Description => "Does something useful";

        public CommandSchema Schema => new CommandSchema(
            Description,
            new[]
            {
                new CommandParameter("my-param", CommandParameterType.String, "Example parameter")
            });

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            logger.LogInformation("Executing {CommandName}", Name);
            return CommandResult.CreateSuccess(data: new { ok = true });
        }
    }
}
```

### Step 2: Register It Once

Register the command in [ServiceCollectionExtensions.cs](/E:/Documents/Codex/OpenTiaToolkit/src/OpenTiaToolkit.Core/ServiceCollectionExtensions.cs):

```csharp
services.AddSingleton<ICommand, MyNewCommand>();
```

If the command introduces a new service dependency, register that service in `AddOpenTiaToolkitCoreServices()`.

### Step 3: Add Tests

Prefer fast tests first:

- command tests for argument handling and result shaping
- builder tests if the feature adds traversal logic
- composition tests if the feature adds registrations

Then add or extend integration tests if real TIA behavior needs validation.

### Step 4: Verify Both Hosts

Because CLI and MCP share registrations, you usually do not need separate registration edits.

Typical local verification:

```bash
dotnet build OpenTiaToolkit.sln
dotnet test tests/OpenTiaToolkit.Tests/OpenTiaToolkit.Tests.csproj --filter "TestCategory!=Integration"
```

## Testing Strategy

### Fast Tests

Fast tests live under `tests/OpenTiaToolkit.Tests/` and should not require a live TIA session.

Typical coverage:

- command argument handling
- command result shaping
- routing and output formatting
- dependency-injection composition
- extracted builders
- MCP protocol plumbing

### Integration Tests

Integration tests live under `tests/OpenTiaToolkit.Tests/Integration/` and validate real TIA behavior.

They assume:

- TIA Portal V21 is installed
- a real test project is available
- the executable or `testhost` is authorized for Openness access
- test classes marked with `[TestCategory("Integration")]` are selected explicitly

Configurable environment variables:

- `OPEN_TIA_TEST_PROJECT_PATH`
- `OPEN_TIA_TEST_EXPORT_PATH`
- `OPEN_TIA_TEST_OUTPUT_PATH`

### Test Commands

```bash
# Fast tests only
dotnet test tests/OpenTiaToolkit.Tests/OpenTiaToolkit.Tests.csproj --filter "TestCategory!=Integration"

# All tests
dotnet test tests/OpenTiaToolkit.Tests/OpenTiaToolkit.Tests.csproj

# Integration tests only
dotnet test tests/OpenTiaToolkit.Tests/OpenTiaToolkit.Tests.csproj --filter "TestCategory=Integration"
```

### CLI Automation

Use [Test-CLI.ps1](/E:/Documents/Codex/OpenTiaToolkit/scripts/Test-CLI.ps1) for end-to-end CLI automation against the built executable.

See [CLI_TESTING.md](/E:/Documents/Codex/OpenTiaToolkit/docs/CLI_TESTING.md) for the full suite matrix, artifact layout, expected skips, and current V21 coverage notes.

- `Help` validates general help plus `help <command>` for every command currently exposed by the CLI host
- `ReadOnly` drives the real CLI through scripted REPL batches so project and PLC state stay alive inside one process
- `Mutation` creates a disposable project copy under `artifacts/cli-tests/` before running block/tag/import-export roundtrips
- `Negative` covers argument validation and unknown-command behavior

Examples:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Test-CLI.ps1 -Suite Help
powershell -ExecutionPolicy Bypass -File .\scripts\Test-CLI.ps1 -Suite ReadOnly -ProjectPath $env:OPEN_TIA_TEST_PROJECT_PATH -PlcName "PLC_1"
```

Run build and test serially in the same workspace on Windows. Parallel build/test runs can leave `testhost` holding copied binaries and cause temporary file-lock failures.

## Code Style Guidelines

### C# Conventions

- `PascalCase` for classes, methods, and properties
- `kebab-case` for command names
- `_camelCase` for private fields
- prefix interfaces with `I`

### Error Handling

- log the exception with useful context
- return a structured failure result when possible
- avoid swallowing exceptions unless the fallback behavior is intentional

### Logging

- `Information`: normal operation flow
- `Warning`: unexpected but handled conditions
- `Error`: operation failed
- `Debug`: detailed diagnostics

### Documentation

Keep XML comments focused on behavior and important constraints, not on obvious syntax.

## Build Process

### Local Build

```bash
dotnet clean
dotnet build OpenTiaToolkit.sln
```

### Release Build

```bash
dotnet build OpenTiaToolkit.sln -c Release
dotnet test tests/OpenTiaToolkit.Tests/OpenTiaToolkit.Tests.csproj -c Release
```

## Release Process

### Versioning

Follow [Semantic Versioning](https://semver.org/):

- `MAJOR`: breaking changes
- `MINOR`: backwards-compatible features
- `PATCH`: backwards-compatible fixes

### Pre-Release Checklist

- all fast tests pass
- integration tests pass against a real V21 project
- documentation reflects the current command surface and architecture
- version metadata is updated where needed
- release build completes successfully
