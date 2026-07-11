# TIA Portal Version Support

## Overview

OpenTiaToolkit targets and is validated against **TIA Portal V21**. Later releases are not claimed as supported until their Siemens Openness assemblies and workflows have been tested.

That is the version assumed by:

- the project references in `src/`
- the default V21 Openness API reference path used during build
- the default integration-test paths
- the current assembly resolver preference order

## Support Levels

| Version | Status | Notes |
|---------|--------|-------|
| **V21** | Supported | Current development target and validated runtime environment |
| **Future TIA Portal versions** | Unverified | Validate against each new Siemens Openness release before claiming support |
| **V20 and earlier** | Not supported | Not a target for development or validation |

## V21 API Surface Used By This Repo

The repo targets the V21 modular Openness API layout:

- `Siemens.Engineering.Base.dll`
- `Siemens.Engineering.Step7.dll`
- `Siemens.Engineering.WinCCUnified.dll`
- `Siemens.Engineering.Startdrive.dll`

Common V21 PLC surfaces used by the toolkit include:

- `PlcSoftware.BlockGroup`
- `PlcSoftware.TypeGroup`
- `PlcSoftware.TagTableGroup`
- `PlcSoftware.ExternalSourceGroup`
- `PlcSoftware.TechnologicalObjectGroup`
- `GetService<ICompilable>()` for software compilation
- `Project.GetService<VersionControlInterface>()` for VCI workspaces and synchronization

Common V21 hardware surfaces used by the toolkit include:

- `Project.Devices.CreateWithItem(...)` for catalog device and GSDML DAP instantiation
- `HardwareObject.CanPlugNew(...)` and `HardwareObject.PlugNew(...)` for module/submodule composition
  - `DeviceItem.Addresses` and writable `Siemens.Engineering.HW.Address.StartAddress` for process-image IO assignment
  - Startdrive `DriveObjectContainer`, `DriveObjects`, `Telegrams`, telegram `Addresses`, and guarded telegram mutation methods for native SINAMICS IO assignment
- `Project.HwUtilities.Find("ModuleInformationProvider")` for module identifier discovery
- `DeviceItem.GetService<GsdDeviceItem>()` for raw GSD/GSDML parameter records
- `DeviceItem.GetService<NetworkInterface>()` for subnet and IO-system connectivity
  - `NetworkInterface.Nodes` and `IoConnectors`, with attribute access for PROFINET IP, station name, and device-number configuration

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

The public `create-block` command is enabled in the shared CLI/MCP command registration.

During V21 validation on the current test project, the loaded `PlcBlockComposition` surface exposed `CreateFB(...)` and `CreateInstanceDB(...)`, and `CreateFB(...)` only accepted the `ProDiag` language in this context. Treat generic FC/DB/OB/UDT creation as unsupported until a broader V21-safe implementation is added and validated.

### SIMATIC SD document import/export

Document-based block import/export is available in the supported V21 workflow.

### Version Control Interface (VCI)

The VCI command surface is validated against TIA Portal V21 and uses `Siemens.Engineering.VersionControl` from `Siemens.Engineering.Base.dll`.

Validated V21 behavior includes:

- workspace creation, listing, deletion, and reconnecting an existing `.vci` repository
- S7DCL (`SimaticSD`) mapping for individual FC and GlobalDB objects
- filtered block synchronization and PLC traversal across blocks, PLC types, tag tables, and technological instance DBs
- compact bulk results by default, with skipped unmapped object details available through `include-skipped=true`
- comparison state handling: Siemens rejects `Synchronize(...)` for an `Equal` mapped object, so equal content is treated as an already synchronized success
- repository reopening: omit the workspace language when attaching an existing repository whose `.vci` metadata already defines it

### Generic hardware engineering

The hardware command surface is validated on V21 with an IFM AL1403 PROFINET IO-Link master GSDML and SINAMICS Startdrive devices. The generic toolkit is not tied to those products: it discovers installed catalog identifiers, resolves hierarchical hardware targets, handles transactional topology/network edits, configures PROFINET identity and device-number attributes, relocates process-image IO address assignments, manipulates supported Startdrive telegram configuration, and reads or writes guarded GSD parameter records.

Validated V21 behavior includes:

- creating a GSDML device/DAP in a transaction
- addressing catalog-created child modules and plugging an IO-Link submodule
- reading and guarded-writing a GSD parameter dataset
- connecting the discovered PROFINET `NetworkInterface` item to an existing subnet and IO system
- reading and writing guarded PROFINET address attributes on a node exposed through `NetworkInterface.Nodes`
- configuring PROFINET station name and IO device number through their owning network children
- changing process-image IO start assignments and supported Startdrive telegram properties in dry-run transactions
- rejecting unsupported Startdrive structural edits through device capability checks or TIA safety authorization
- dry-run rollback with no remaining temporary hardware item

See [HARDWARE_AUTOMATION.md](HARDWARE_AUTOMATION.md) for the generic contract and device-skill boundary.

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

`TiaPortalAssemblyResolver` resolves V21 assemblies in this order:

1. the TIA root named by `OPEN_TIA_PORTAL_ROOT` (or the legacy `TiaPortalLocation` variable)
2. the standard V21 installation or registry location
3. V21 `PublicAPI` and TIA `Bin` infrastructure folders below that root

This matches the repo's current V21 support stance.

## Testing Guidance

Integration tests are designed for a locally installed TIA environment and a real test project.

You can override the default paths with:

- `OPEN_TIA_TEST_PROJECT_PATH`
- `OPEN_TIA_TEST_EXPORT_PATH`
- `OPEN_TIA_TEST_OUTPUT_PATH`

When in doubt, validate new PLC behavior against **TIA Portal V21** first.
