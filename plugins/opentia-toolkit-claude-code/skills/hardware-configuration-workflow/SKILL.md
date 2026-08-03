---
name: hardware-configuration-workflow
description: Use for OpenTiaToolkit generic Siemens TIA Portal hardware automation, including greenfield project/device bootstrap, hardware catalog lookup, module or submodule plugging, device tree target paths, PROFINET identity and network wiring, IO address assignment, Startdrive telegram changes, GSD/GSDML parameter reads or writes, and apply-hardware-patch dry-run/apply workflows.
---

# Hardware Configuration Workflow

## Overview

Use this skill for generic TIA hardware operations through OpenTiaToolkit. The safest pattern is discover, build a guarded patch, dry-run transactionally, apply only after a clean dry-run, then verify with focused reads.

Device-specific catalog IDs and GSD parameter byte meanings belong in project instructions or device-specific skills, not in this generic workflow.

## Standard Workflow

1. Establish TIA context with `basic-tia-interaction` when the instance/project is not already proven. For greenfield work, create the project first with `create-project --project-path`.
2. Discover the current hardware shape with `get-hardware-tree`, scoped by `device-name` when possible. For PROFINET work, also inspect `get-hardware-networks`; project IO-system `connectedIoDevices` can contain GSD/GSDML IO devices that are not visible as top-level `Project.Devices`.
3. Discover catalog and type identifiers with `find-hardware-catalog-items` or `find-hardware-module-types`.
4. Decide whether creation is standalone or transactional before any create call. If the request also plugs modules, connects networks, sets identity, assigns addresses, or performs other follow-up configuration on the new station, never call `add-device`, even as a probe. Start `apply-hardware-patch` with `create-device` or `create-device-with-item`, assign a `resultId`, and use `targetRef` for every dependent operation. Reserve `add-device` for a request whose intended result ends after standalone device creation. For a proposed plug, call `can-plug-hardware-item` before creating a patch.
5. Inspect feature services with `get-hardware-item-capabilities` before writing attributes.
6. Read current network, IO, telegram, or GSD state using the narrowest command:
   - `get-hardware-networks`
   - `get-hardware-io-addresses`
   - `get-hardware-drive-telegrams`
   - `get-hardware-feature-attributes`
   - `get-hardware-network-item-attributes`
   - `get-gsd-parameter-data`
   - `audit-hardware-tag-mappings`
7. Build `apply-hardware-patch` operations with expected-value guards for
   pre-existing targets whose current values were actually read. For an object
   created earlier in the same patch, use its result reference and do not
   invent an expected value for state that could not exist before the patch.
8. Use `targetRef` for items created earlier in the same patch. When a child item is generated during a plug, combine `targetRef` with a relative `target.itemPath` discovered from the prior dry-run result or from `get-hardware-tree`. If `plug-item` reports that TIA placed a GSD/GSDML subslot at a different actual path, prefer `allowDisplacedPlacement=true` with a `resultId`, then use that `resultId` for follow-up operations instead of adding a new device-specific command.
9. Run `apply-hardware-patch` with `dry-run=true`.
10. Put a user-provided safety password in a process environment variable and pass `plc-name` plus `safety-password-env`; never pass or persist the password itself. Dry-runs still execute Siemens mutations inside a rolled-back transaction and can require safety authorization.
11. Apply with `dry-run=false` only after the dry-run validates and the user has confirmed destructive operations when needed.
12. Verify by rereading the affected device tree, network, addresses, telegrams, attributes, audit result, or GSD parameter bytes.

## Bounded Topology Construction

For a new PROFINET station, build one coherent patch from the documented
operation roles: create the device, plug its modules, connect its node to a
subnet, connect its IO connector to a controller IO system, and configure its
PROFINET identity. Use `create-device` or `create-device-with-item`,
`plug-item`, `connect-node-to-subnet`, `connect-io-connector`, and
`configure-profinet-device` as applicable. Discover every catalog identifier,
target path, index, subnet, and IO-system name from the live project. If an
operation schema is unclear, inspect `describe-api` once; do not use repeated
dry-runs to guess operation names or substitute generic attribute setters for
a dedicated operation.

Use exact hardware-patch JSON field names; unknown members can be ignored by
deserialization and silently weaken an operation. Use `op`, never `operation`.
`create-device-with-item` requires `typeIdentifier`, `deviceName`, and
`itemName`; do not substitute `name`. Put `targetRef` beside `op`, not inside
`target`; use `target.itemPath` only for a relative child path. For
`configure-profinet-device`, use the exact optional fields `ipAddress`,
`subnetMask`, `pnDeviceName`, and `pnDeviceNumber`; `address`, `stationName`,
and `deviceNumber` are not aliases.

Before the first topology preview, check every dependent target explicitly:

- Give each `plug-item` a top-level `targetRef` and the intended parent in
  `target.itemPath`; `targetRef` alone addresses the device root and is not a
  substitute for the rack, module, or submodule parent.
- For a newly created interface child, give `connect-node-to-subnet`,
  `connect-io-connector`, and `configure-profinet-device` the same top-level
  `targetRef` plus the discovered interface `target.itemPath`.
- Set `nodeIndex` on `connect-node-to-subnet` and `connectorIndex` on
  `connect-io-connector`, even when the discovered index is zero. Do not use
  `ioConnectorIndex` as an alias.

Keep the complete candidate batch intact while learning TIA placement. Start
each `plug-item` without `allowDisplacedPlacement`. When a dry-run reports a
different actual path, validate that path, enable displaced placement only for
that plug, and dry-run the complete batch again so execution can reach the next
strict plug. Never infer that a sibling plug will be displaced in the same way:
set `allowDisplacedPlacement=true` only after that exact plug has failed and
reported its own actual path. Each retry must advance past one newly observed
displacement or correct one concrete validation error; otherwise stop and
report the blocker.
After the first full successful preview, issue exactly one apply with the same
operations and values, changing only `dry-run` and required confirmation
fields. Do not recreate, split, or reinterpret the topology between preview
and apply.

## Patch Safety

Keep patch operations small and auditable. Prefer several focused patches over one broad hardware rewrite.

Treat one newly created station and its required child modules, network
connections, and identity as one atomic unit. Do not split that unit merely to
make individual calls smaller; a successful final state does not prove that
the reviewed transaction caused the whole state change.

Use reusable hardware targets returned by `get-hardware-tree`. Include `expected-type-identifier` when a target might be ambiguous or when writing to a specific module/device class.

Copy target guards from the exact returned target. Do not propagate a parent module's type identifier onto a nested child whose returned `ExpectedTypeIdentifier` is null; omit that guard and keep the exact full child path.

Treat these as destructive and require explicit user confirmation before apply:

- Deleting devices, modules, or submodules.
- Disconnecting subnets or IO systems.
- Erasing Startdrive telegrams.
- Overwriting GSD/GSDML parameter bytes without a known before-state.

For GSD/GSDML parameters, read current bytes first and record the dataset number, byte offset, length, and expected old value. Do not infer vendor-specific byte meanings from the generic toolkit.

For GSD/GSDML submodules, TIA may legally place the created item somewhere other than the requested parent path. The generic workaround is to dry-run first without `allowDisplacedPlacement`, inspect the actual path reported by the failure, and only opt in with `allowDisplacedPlacement=true` when that actual layout is acceptable. Put a `resultId` on that `plug-item` and use the `resultId` for subsequent IO address, parameter, delete, or attribute operations. Keep the command surface generic; do not introduce a new command when the workflow can be expressed through actual-path reporting and `resultId`.

## PROFINET And IO Notes

PROFINET station name is not the same as the TIA display `Name`. Use
`configure-profinet-device` for the requested IP address, subnet mask, station
name, and device number, and use guarded attribute writes only for display
names or attributes without a dedicated operation. Omit `subnetMask` and other
optional identity fields when the task does not require them; if TIA rejects an
optional setter, treat it as unsupported for that interface rather than
probing alternative operation names.

Before assigning IO addresses, read existing process-image addresses and avoid collisions. After applying, verify both start address and IO type/length.

Treat `Length` on ordinary hardware I/O address rows as a bit count when translating it into byte ranges: occupied bytes are `ceiling(Length / 8)`, so a 16-bit row spans two bytes. Prefer Siemens' own collision and next-free-address diagnostics over a recomputed next-free value.

For an ordinary module address returned by `get-hardware-io-addresses`, use the exact patch operation `set-io-address-start`; do not guess alternative operation names. Select `addressIndex` from the matching address row, bind the write to the current value with `expectedStartAddress`, and submit the operation through `apply-hardware-patch` with `dry-run=true` first:

```json
{
  "operations": [
    {
      "op": "set-io-address-start",
      "target": {
        "deviceName": "PLC_1",
        "itemPath": ["DI_Module", "DI_Module"]
      },
      "addressIndex": 0,
      "startAddress": 128,
      "expectedStartAddress": 0
    }
  ]
}
```

If the dry-run reports a collision, do not retry with a different address unless the user explicitly authorizes that address. Report the current owner and any next-free-address evidence without applying.

When `set-io-address-start` targets an IO-address child generated by a
`plug-item` earlier in the same patch, omit `expectedStartAddress` on the first
complete preview. There is no independently readable pre-patch value for that
new child, and its Siemens-assigned initial start can depend on operation
order. Never guess `0`, `-1`, or another sentinel as a guard. Include the guard
only when a prior tool result for that exact generated child established the
value and a retry is otherwise justified.

For large topology reads, pass `output-path` to `get-hardware-tree` or `get-hardware-networks`, or use the returned JSON artifact path when the command auto-spills. These nested outputs should stay JSON so they can be reloaded for follow-up target selection and network analysis.

For large IO address reviews, pass `output-path` to `get-hardware-io-addresses` or use the returned CSV artifact path instead of reading a long inline result. For `audit-hardware-tag-mappings`, use `output-path` when the unmatched PLC tag rows should be reviewed in CSV; use the generic JSON artifact returned by MCP when the full audit object is needed.

For Startdrive telegram reviews across multiple inverters, pass `output-path` to `get-hardware-drive-telegrams` or use its returned CSV artifact path. Include `address-attributes` such as `StartAddress`, `IoType`, and `Length` when checking address assignments or collisions.

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

A verified G120C pattern is main telegram `352` and safety telegram `30`, with the safety telegram usually requiring `plc-name` and a `safety-password-env` reference on `apply-hardware-patch`. Catalog type identifiers and concrete addresses remain project-specific; discover them from the target TIA installation and check for address collisions before apply.

## Failure Recovery

If dry-run fails, fix the target path, type identifier, position, expected value, or operation shape. Do not bypass by removing guards unless the user explicitly accepts that risk.

If an apply fails or times out, do not replay immediately. Reread the affected hardware state first; TIA operations may have rolled back or may still be completing.

If the MCP server reports another active operation, wait for it to finish before issuing another hardware command.
