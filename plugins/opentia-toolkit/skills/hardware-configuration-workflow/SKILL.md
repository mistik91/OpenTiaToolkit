---
name: hardware-configuration-workflow
description: Use for OpenTiaToolkit generic Siemens TIA Portal hardware automation, including hardware catalog lookup, module or submodule plugging, device tree target paths, PROFINET identity and network wiring, IO address assignment, Startdrive telegram changes, GSD/GSDML parameter reads or writes, and apply-hardware-patch dry-run/apply workflows.
---

# Hardware Configuration Workflow

## Overview

Use this skill for generic TIA hardware operations through OpenTiaToolkit. The safest pattern is discover, build a guarded patch, dry-run transactionally, apply only after a clean dry-run, then verify with focused reads.

Device-specific catalog IDs and GSD parameter byte meanings belong in project instructions or device-specific skills, not in this generic workflow.

## Standard Workflow

1. Establish TIA context with `basic-tia-interaction` when the instance/project is not already proven.
2. Discover the current hardware shape with `get-hardware-tree`, scoped by `device-name` when possible. For PROFINET work, also inspect `get-hardware-networks`; project IO-system `connectedIoDevices` can contain GSD/GSDML IO devices that are not visible as top-level `Project.Devices`.
3. Discover catalog and type identifiers with `find-hardware-catalog-items` or `find-hardware-module-types`.
4. For a proposed plug, call `can-plug-hardware-item` before creating a patch.
5. Inspect feature services with `get-hardware-item-capabilities` before writing attributes.
6. Read current network, IO, telegram, or GSD state using the narrowest command:
   - `get-hardware-networks`
   - `get-hardware-io-addresses`
   - `get-hardware-drive-telegrams`
   - `get-hardware-feature-attributes`
   - `get-hardware-network-item-attributes`
   - `get-gsd-parameter-data`
   - `audit-hardware-tag-mappings`
7. Build `apply-hardware-patch` operations with expected-value guards wherever the operation supports them.
8. Use `targetRef` for items created earlier in the same patch. When a child item is generated during a plug, combine `targetRef` with a relative `target.itemPath` discovered from the prior dry-run result or from `get-hardware-tree`. If `plug-item` reports that TIA placed a GSD/GSDML subslot at a different actual path, prefer `allowDisplacedPlacement=true` with a `resultId`, then use that `resultId` for follow-up operations instead of adding a new device-specific command.
9. Run `apply-hardware-patch` with `dry-run=true`.
10. Pass `plc-name` and `safety-password` when the transaction may touch fail-safe hardware or Startdrive safety telegram configuration. Dry-runs still execute Siemens mutations inside a rolled-back transaction and can require safety authorization.
11. Apply with `dry-run=false` only after the dry-run validates and the user has confirmed destructive operations when needed.
12. Verify by rereading the affected device tree, network, addresses, telegrams, attributes, audit result, or GSD parameter bytes.

## Patch Safety

Keep patch operations small and auditable. Prefer several focused patches over one broad hardware rewrite.

Use reusable hardware targets returned by `get-hardware-tree`. Include `expected-type-identifier` when a target might be ambiguous or when writing to a specific module/device class.

Treat these as destructive and require explicit user confirmation before apply:

- Deleting devices, modules, or submodules.
- Disconnecting subnets or IO systems.
- Erasing Startdrive telegrams.
- Overwriting GSD/GSDML parameter bytes without a known before-state.

For GSD/GSDML parameters, read current bytes first and record the dataset number, byte offset, length, and expected old value. Do not infer vendor-specific byte meanings from the generic toolkit.

For GSD/GSDML submodules, TIA may legally place the created item somewhere other than the requested parent path. The generic workaround is to dry-run first without `allowDisplacedPlacement`, inspect the actual path reported by the failure, and only opt in with `allowDisplacedPlacement=true` when that actual layout is acceptable. Put a `resultId` on that `plug-item` and use the `resultId` for subsequent IO address, parameter, delete, or attribute operations. Keep the command surface generic; do not introduce a new command when the workflow can be expressed through actual-path reporting and `resultId`.

## PROFINET And IO Notes

PROFINET station name is not the same as the TIA display `Name`. Use the patch operation field intended for station identity when setting station name, and use guarded attribute writes for display names.

Before assigning IO addresses, read existing process-image addresses and avoid collisions. After applying, verify both start address and IO type/length.

Before wiring a subnet or IO system, use `get-hardware-networks` to identify nodes, IO controllers, IO connectors, and existing connections.

For PROFINET IO devices that appear in PLC tags or IO-system references but not in `list-devices` or `get-hardware-tree`, do not conclude they are absent. Read `get-hardware-networks` and inspect project IO-system `connectedIoDevices` entries. Their `nodes[].attributes` can expose IP address, subnet mask, and station-name fields; `connector.attributes` can expose `PnDeviceNumber` when TIA provides it.

When PLC `%I` or `%Q` tags suggest a missing field device, run `audit-hardware-tag-mappings` with the relevant `plc-name`, tag-table filter, and optional catalog hint such as a vendor or device name. Treat tag names and process-image ranges as evidence, not authority: prefer actual network attributes from existing `connectedIoDevices` over inferred sequential patterns before creating or configuring new devices.

When adding a new PROFINET IO device that should follow an existing pattern, first discover the old devices through `connectedIoDevices`, record their IP, station name, device number, and IO address progression, then use guarded patch operations and a dry-run before apply.

## Startdrive Drive Notes

For new SINAMICS Startdrive PROFINET drives, the reliable order is:

1. Create the drive device or plug the rack/device items.
2. Use the created drive `targetRef` plus the discovered relative interface child path for network operations. For G120C drives this child is commonly `PROFINET interface`, but do not hard-code it until the tree or dry-run result proves it.
3. Connect the interface node to the subnet with `connect-node-to-subnet`.
4. Connect the IO connector to the controller IO system with `connect-io-connector`.
5. Configure telegram number and IO starts with `configure-drive-telegram`.
6. Reread `get-hardware-drive-telegrams` or the filtered hardware tree to verify the result.

Use `configure-drive-telegram` when a drive telegram must be changed and assigned process-image starts in one guarded operation. It changes or inserts the requested telegram, verifies that the requested input/output address objects are exposed, then writes the start addresses. Include `expectedTelegramNumber`, `expectedInputStartAddress`, and `expectedOutputStartAddress` when the current state is known.

A verified G120C pattern is main telegram `352` and safety telegram `30`, with the safety telegram usually requiring `plc-name` and `safety-password` on `apply-hardware-patch`. Catalog type identifiers and concrete addresses remain project-specific; discover them from the target TIA installation and check for address collisions before apply.

## Failure Recovery

If dry-run fails, fix the target path, type identifier, position, expected value, or operation shape. Do not bypass by removing guards unless the user explicitly accepts that risk.

If an apply fails or times out, do not replay immediately. Reread the affected hardware state first; TIA operations may have rolled back or may still be completing.

If the MCP server reports another active operation, wait for it to finish before issuing another hardware command.
