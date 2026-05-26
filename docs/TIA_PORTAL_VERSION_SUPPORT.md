# TIA Portal Version Support

## Overview

OpenTiaToolkit is primarily developed and validated against **TIA Portal V21**.

That is the version assumed by:

- the project references in `src/`
- the default V21 Openness API reference path used during build
- the default integration-test paths
- the current assembly resolver preference order

Older TIA versions may still work for some reflection-based operations, but they are considered **best-effort compatibility paths**, not the main supported target.

## Support Levels

| Version | Status | Notes |
|---------|--------|-------|
| **V21** | Primary supported version | Main development target and expected runtime environment |
| **V20** | Limited / best effort | Some reflection-based paths may work; SIMATIC SD document features are V20+ |
| **V19 and earlier** | Legacy / best effort | Not the primary validation target; expect gaps and less coverage |

## V21 API Surface Used By This Repo

The repo targets the V21 modular Openness API layout:

- `Siemens.Engineering.Base.dll`
- `Siemens.Engineering.Step7.dll`
- `Siemens.Engineering.WinCC.dll`
- `Siemens.Engineering.WinCC.Extension.dll`

Common V21 PLC surfaces used by the toolkit include:

- `PlcSoftware.BlockGroup`
- `PlcSoftware.TypeGroup`
- `PlcSoftware.TagTableGroup`
- `PlcSoftware.ExternalSourceGroup`
- `PlcSoftware.TechnologicalObjectGroup`
- `GetService<ICompilable>()` for software compilation
- `Project.GetService<VersionControlInterface>()` for VCI workspaces and synchronization

Refer to the official Siemens Openness documentation for the detailed V21 API surface.

## Feature Notes

### PLC software tree

`get-software-tree` is implemented against the V21 PLC software containers and currently enumerates:

- blocks
- types
- tag tables
- external source files

The tree is built from the actual V21 software object model instead of placeholder nodes.

### PLC compilation

`compile-software` uses the V21 compile service exposed via `ICompilable`.

Current behavior:

- compilation is executed against the open PLC software
- error and warning counts come from the Siemens compiler result
- the optional `password` argument is currently accepted for command compatibility but is not used by the V21 compile-service call path

### Block creation

The public `create-block` command is not currently enabled in the CLI/MCP registrations.

During V21 validation on the current test project, the loaded `PlcBlockComposition` surface exposed `CreateFB(...)` and `CreateInstanceDB(...)`, and `CreateFB(...)` only accepted the `ProDiag` language in this context. Treat generic FC/DB/OB/UDT creation as unsupported until a broader V21-safe implementation is added and validated.

### SIMATIC SD document import/export

Document-based block import/export requires **V20 or newer**. In practice, this repo is mainly validated on V21.

### Version Control Interface (VCI)

The VCI command surface is validated against TIA Portal V21 and uses `Siemens.Engineering.VersionControl` from `Siemens.Engineering.Base.dll`.

Validated V21 behavior includes:

- workspace creation, listing, deletion, and reconnecting an existing `.vci` repository
- S7DCL (`SimaticSD`) mapping for individual FC and GlobalDB objects
- filtered block synchronization and PLC traversal across blocks, PLC types, tag tables, and technological instance DBs
- compact bulk results by default, with skipped unmapped object details available through `include-skipped=true`
- comparison state handling: Siemens rejects `Synchronize(...)` for an `Equal` mapped object, so equal content is treated as an already synchronized success
- repository reopening: omit the workspace language when attaching an existing repository whose `.vci` metadata already defines it

## Build Reference Path

The project files use the `TiaPortalPublicApiPath` MSBuild property for Siemens Openness references.

The default is the standard TIA Portal V21 installation path:

```text
C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48
```

Most users do not need to configure anything. For non-standard installations, override it at build time:

```powershell
dotnet build OpenTiaToolkit.sln /p:TiaPortalPublicApiPath="D:\Siemens\Portal V21\PublicAPI\V21\net48"
```

## Assembly Loading

`TiaPortalAssemblyResolver` is V21-first:

1. V21 PublicAPI
2. V20 PublicAPI
3. V19 fallback locations
4. TIA `Bin` infrastructure folders

This matches the repo's current V21-first support stance.

## Testing Guidance

Integration tests are designed for a locally installed TIA environment and a real test project.

You can override the default paths with:

- `OPEN_TIA_TEST_PROJECT_PATH`
- `OPEN_TIA_TEST_EXPORT_PATH`
- `OPEN_TIA_TEST_OUTPUT_PATH`

When in doubt, validate new PLC behavior against **TIA Portal V21** first.
