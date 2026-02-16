# Development Guide

Guide for developers contributing to OpenTiaToolkit.

## Table of Contents

- [Development Setup](#development-setup)
- [Project Structure](#project-structure)
- [Adding New Commands](#adding-new-commands)
- [Testing Strategy](#testing-strategy)
- [Code Style Guidelines](#code-style-guidelines)
- [Build Process](#build-process)
- [Release Process](#release-process)

## Development Setup

### Prerequisites

1. **Install TIA Portal V19+** (for integration testing)
2. **Install Visual Studio 2019+** with:
   - .NET desktop development workload
   - MSTest adapter
3. **Clone the repository**

### Getting Started

```bash
# Clone the repository
git clone https://github.com/your-org/OpenTiaToolkit.git
cd OpenTiaToolkit

# Open in Visual Studio
OpenTiaToolkit.sln

# Build to verify setup
dotnet build
```

### Recommended Visual Studio Extensions

- .NET Productivity Tools
- ReSharper (optional but recommended)

## Project Structure

### Solution Architecture

```
OpenTiaToolkit.slnx
├── src/
│   ├── OpenTiaToolkit.Core/      # Shared library (no UI)
│   ├── OpenTiaToolkit.CLI/       # Console application
│   └── OpenTiaToolkit.MCP/       # MCP server (STDIO)
└── tests/
    └── OpenTiaToolkit.Tests/     # All tests (unit + integration)
```

### Layer Responsibilities

| Layer | Responsibility | Dependencies |
|-------|---------------|--------------|
| **Core** | Business logic, commands, services | TIA Openness API, Logging |
| **CLI** | User interface, argument parsing, table formatting | Core, Spectre.Console |
| **MCP** | MCP protocol, tool registry, JSON handling | Core, ModelContextProtocol |
| **Tests** | Unit tests (mocks) + Integration tests (real TIA) | All projects |

## Adding New Commands

### Step 1: Create Command Class

Create in `src/OpenTiaToolkit.Core/Commands/`:

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
                new CommandParameter(
                    "my-param",
                    CommandParameterType.String,
                    "Description of my parameter",
                    required: false,
                    defaultValue: "default-value"
                )
            }
        );

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            logger.LogInformation("Executing {CommandName}", Name);

            try
            {
                var myParam = args.GetString("my-param", "default-value");

                // Your logic here
                var result = DoSomething(myParam);

                return CommandResult.CreateSuccess(
                    data: result,
                    message: "Command completed successfully"
                );
            }
            catch (System.Exception ex)
            {
                logger.LogError(ex, "Error executing {CommandName}", Name);
                return CommandResult.CreateFailure(
                    $"Error: {ex.Message}",
                    ex
                );
            }
        }

        private object DoSomething(string param)
        {
            // Implementation
            return new { result = "done" };
        }
    }
}
```

### Step 2: Register in CLI

Edit `src/OpenTiaToolkit.CLI/Program.cs`:

```csharp
private static IServiceProvider BuildServiceProvider()
{
    var services = new ServiceCollection();

    // ... existing registrations ...

    // Add your new command
    services.AddSingleton<ICommand, MyNewCommand>();

    return services.BuildServiceProvider();
}
```

### Step 3: Register in MCP

Edit `src/OpenTiaToolkit.MCP/Program.cs`:

```csharp
private static IServiceProvider BuildServiceProvider()
{
    var services = new ServiceCollection();

    // ... existing registrations ...

    // Add your new command
    services.AddSingleton<ICommand, MyNewCommand>();

    return services.BuildServiceProvider();
}
```

### Step 4: Write Tests

Create `tests/OpenTiaToolkit.Tests/Commands/MyNewCommandTests.cs`:

```csharp
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OpenTiaToolkit.Core.Commands;

[TestClass]
public class MyNewCommandTests
{
    [TestMethod]
    public async Task ExecuteAsync_ReturnsSuccess_WhenParamValid()
    {
        // Arrange
        var portalMock = new Mock<IPortalService>();
        var command = new MyNewCommand(portalMock.Object);
        var loggerMock = new Mock<ILogger>();
        var args = new CommandArgs(new Dictionary<string, object?>
        {
            { "my-param", "test-value" }
        });

        // Act
        var result = await command.ExecuteAsync(args, loggerMock.Object);

        // Assert
        Assert.IsTrue(result.Success);
    }
}
```

### Step 5: Test Manually

```bash
# Build
dotnet build

# Test CLI
src\OpenTiaToolkit.CLI\bin\Debug\net48\OpenTiaToolkit.CLI.exe help
src\OpenTiaToolkit.CLI\bin\Debug\net48\OpenTiaToolkit.CLI.exe my-new-command

# Test MCP (if Claude Desktop configured)
echo '{"method":"tools/list"}' | src\OpenTiaToolkit.MCP\bin\Debug\net48\OpenTiaToolkit.MCP.exe
```

### Step 6: Run Tests

```bash
dotnet test tests/OpenTiaToolkit.Tests/OpenTiaToolkit.Tests.csproj
```

## Testing Strategy

### Unit Tests

**Purpose**: Test framework logic without TIA dependencies

**Location**: `tests/OpenTiaToolkit.Tests/`

**Characteristics**:
- Fast execution (~3 seconds total)
- Mock all dependencies (Moq)
- Test one thing per test
- No TIA Portal installation required

**Categories**:
- Command logic tests
- Argument parsing tests
- Output formatting tests
- Routing tests
- MCP protocol tests

### Integration Tests

**Purpose**: Test with real TIA Portal instance

**Location**: `D:\Siemens\V19\MCP_TEST\MCP_TEST`

**Characteristics**:
- Slower execution (requires TIA Portal)
- Real project files
- Test in numbered sequence (Test1, Test2, etc.)
- Use `[DoNotParallelize]` attribute

**Approach**:
1. Start with existing test project structure
2. Follow patterns from `tiaportal-mcp` tests
3. Test against real TIA V19 projects

### Test Execution

```bash
# Unit tests only (fast, no TIA)
dotnet test --filter "FullyQualifiedName~Unit"

# All tests (requires TIA)
dotnet test

# Specific test class
dotnet test --filter "FullyQualifiedName~CommandArgsTests"
```

## Code Style Guidelines

### C# Conventions

- **PascalCase** for classes, methods, properties
- **kebab-case** for command names
- **_camelCase** for private fields
- **Interfaces**: Prefix with `I` (e.g., `ICommand`, `IPortalService`)

```csharp
// Good
public class PortalService : IPortalService
{
    private readonly ILogger _logger;

    public async Task<bool> ConnectAsync()
    {
        return await Task.Run(() => { /* ... */ });
    }
}
```

### Error Handling

```csharp
public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
{
    try
    {
        // Do work
        return CommandResult.CreateSuccess(data);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error in {Command}", Name);
        return CommandResult.CreateFailure(
            $"Error: {ex.Message}",
            ex
        );
    }
}
```

### Logging

```csharp
// Information level - normal operation flow
logger.LogInformation("Connecting to TIA Portal...");

// Warning level - something unexpected but handled
logger.LogWarning("No project is currently open");

// Error level - actual error
logger.LogError(ex, "Failed to open project: {Path}", path);

// Debug level - detailed diagnostics
logger.LogDebug("Parsed {Count} arguments", args.Keys.Count());
```

### Documentation

```csharp
/// <summary>
/// Connects to a TIA Portal instance.
/// </summary>
/// <param name="projectPath">The full path to the .ap19 file.</param>
/// <returns>True if connection succeeded.</returns>
public async Task<bool> ConnectAsync(string projectPath)
{
    // ...
}
```

## Build Process

### Local Build

```bash
# Clean and rebuild
dotnet clean
dotnet build

# Build specific configuration
dotnet build --configuration Release
```

### Release Build

```bash
# Build release version
dotnet build -c Release

# Run tests
dotnet test -c Release
```

## Release Process

### Version Numbering

Follow [Semantic Versioning](https://semver.org/):

- **MAJOR**: Breaking changes
- **MINOR**: New features (backwards compatible)
- **PATCH**: Bug fixes

Update version in all `.csproj` files:

```xml
<PropertyGroup>
  <AssemblyVersion>1.0.0</AssemblyVersion>
  <FileVersion>1.0.0.0</FileVersion>
</PropertyGroup>
```

### Pre-Release Checklist

- [ ] All tests pass
- [ ] Documentation updated
- [ ] CHANGELOG.md updated
- [ ] Version numbers updated
- [ ] Built in Release mode
- [ ] Tested with TIA V19 project

### Creating Release

```bash
# Tag the version
git tag -a v1.0.0 -m "Release 1.0.0: Phase 1 complete"
git push origin v1.0.0

# Create GitHub release (via web interface)
# Attach binaries:
# - OpenTiaToolkit.CLI.exe
# - OpenTiaToolkit.MCP.exe
# - OpenTiaToolkit.Core.dll
```

## Architecture Patterns

### Dependency Injection

All projects use `Microsoft.Extensions.DependencyInjection`:

```csharp
// Core library - no DI
public class PortalService : IPortalService
{
    // Manual injection
}

// CLI/MCP - containerized
services.AddSingleton<IPortalService, PortalService>();
services.AddSingleton<ICommand, MyCommand>();
```

### Service Lifetime

- **Singleton**: Services that maintain state (PortalService)
- **Transient**: Commands (stateless)

### Async/Await

All I/O operations should be async:

```csharp
public async Task<bool> ConnectAsync()
{
    return await Task.Run(() =>
    {
        // TIA API calls
    });
}
```

## Debugging

### Attaching Debugger

1. Set OpenTiaToolkit.CLI as startup project
2. F5 to debug
3. Set breakpoints in command execution

### Debugging MCP Server

1. Set OpenTiaToolkit.MCP as startup project
2. Use a test client to send JSON-RPC messages:

```bash
# Initialize
{"jsonrpc":"2.0","id":1,"method":"initialize"}
```

3. Use `Console.WriteLine()` in MCP server for debugging

## Common Tasks

### Adding Dependencies

1. Add to project file:
```xml
<PackageReference Include="PackageName" Version="1.0.0" />
```

2. Restore packages:
```bash
dotnet restore
```

### Updating TIA References

When adding TIA functionality:

1. Add `using` statements for TIA namespaces
2. Update TIA assembly references if needed
3. Test with real TIA projects in integration tests
4. Document the new functionality

### Refactoring Commands

1. Make changes to command class
2. Update tests
3. Verify CLI still works
4. Verify MCP still works (tools/list should show changes)

## Getting Help

### Questions?

- **Issues**: https://github.com/your-org/OpenTiaToolkit/issues
- **Discussions**: https://github.com/your-org/OpenTiaToolkit/discussions

### Resources

- [TIA Openness API Documentation](https://support.industry.siemens.com/cs/ww/en/posts/209744803-tia-portal-openness)
- [Model Context Protocol](https://modelcontextprotocol.io/)
- [.NET Framework 4.8 Documentation](https://docs.microsoft.com/en-us/dotnet/framework/)

---

Remember: **Keep it clean, keep it lean, no fancy BS - straight to the point. Always write tests and TEST IT YOURSELF.**
