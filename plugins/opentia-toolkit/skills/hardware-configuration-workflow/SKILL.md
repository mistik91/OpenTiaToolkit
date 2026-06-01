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
2. Discover the current hardware shape with `get-hardware-tree`, scoped by `device-name` when possible.
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
7. Build `apply-hardware-patch` operations with expected-value guards wherever the operation supports them.
8. Run `apply-hardware-patch` with `dry-run=true`.
9. Apply with `dry-run=false` only after the dry-run validates and the user has confirmed destructive operations when needed.
10. Verify by rereading the affected device tree, network, addresses, telegrams, attributes, or GSD parameter bytes.

## Patch Safety

Keep patch operations small and auditable. Prefer several focused patches over one broad hardware rewrite.

Use reusable hardware targets returned by `get-hardware-tree`. Include `expected-type-identifier` when a target might be ambiguous or when writing to a specific module/device class.

Treat these as destructive and require explicit user confirmation before apply:

- Deleting devices, modules, or submodules.
- Disconnecting subnets or IO systems.
- Erasing Startdrive telegrams.
- Overwriting GSD/GSDML parameter bytes without a known before-state.

For GSD/GSDML parameters, read current bytes first and record the dataset number, byte offset, length, and expected old value. Do not infer vendor-specific byte meanings from the generic toolkit.

## PROFINET And IO Notes

PROFINET station name is not the same as the TIA display `Name`. Use the patch operation field intended for station identity when setting station name, and use guarded attribute writes for display names.

Before assigning IO addresses, read existing process-image addresses and avoid collisions. After applying, verify both start address and IO type/length.

Before wiring a subnet or IO system, use `get-hardware-networks` to identify nodes, IO controllers, IO connectors, and existing connections.

## Failure Recovery

If dry-run fails, fix the target path, type identifier, position, expected value, or operation shape. Do not bypass by removing guards unless the user explicitly accepts that risk.

If an apply fails or times out, do not replay immediately. Reread the affected hardware state first; TIA operations may have rolled back or may still be completing.

If the MCP server reports another active operation, wait for it to finish before issuing another hardware command.
