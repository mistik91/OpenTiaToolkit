---
name: hmi-unified-agent-editing
description: Use when editing Siemens TIA Portal Unified HMI screens with OpenTiaToolkit agent workflows, especially screen JSON snapshots, targeted JSON patches, dry-run/apply safety, stale base-hash recovery, item lifecycle edits, layout/property edits, event handlers, property event handlers, and dynamizations.
---

# HMI Unified Agent Editing

## Overview

This skill guides safe, agent-oriented editing of TIA Portal Unified HMI screens through OpenTiaToolkit. Prefer compact screen JSON patch workflows over manual live edits when the target is a single screen or a bounded set of items.

Use this skill when a task mentions Unified HMI screen edits, screen items, `get-hmi-screen-json`, `get-hmi-screen-summary`, `apply-hmi-screen-json-edit`, `apply-hmi-screen-json-patch`, `inspect-hmi-screen-json-capabilities`, `probe-hmi-faceplate-interface`, `hmi-status`, `audit-hmi-label-proximity`, or agent-safe import/export editing.

## Standard Workflow

1. Establish the exact target: project, HMI name, screen path, item names, and whether TIA Portal is already open. If the correct TIA instance/project context is not already proven in this turn, use `basic-tia-interaction` first.
2. Use `get-hmi-screen-summary` first for large or unfamiliar screens, then use `inspect-hmi-screen-json-capabilities` when the item type, writable properties, event types, faceplate interface, or dynamization shape is unfamiliar.
3. Use `get-hmi-screen-json` when you only need a focused snapshot and already know the supported operations.
4. Reuse the returned `RecommendedPatchArgs` for `baseHash`, `itemNames`, and `properties` instead of inventing filters.
5. Prefer `apply-hmi-screen-json-patch` for targeted changes; run with `dry-run=true` first unless the user explicitly requested otherwise.
6. Apply only when the dry-run result is safe, then rerun with `dry-run=false` and backup enabled unless the user has asked for no backup.
7. Verify the result through `VerifiedScreen`, `VerifyExportPath`, a fresh screen snapshot, or specific property reads.
8. If the result includes `Guidance`, follow `RecommendedNextAction` before guessing. It is designed for agents.

## Filter Invariant

When using `base-hash`, the snapshot, dry-run apply, real apply, and verification read must use the same `item-names` and `properties` unless the command returns `re-read-screen` or explicitly instructs new filters. Do not omit filters from `apply-hmi-screen-json-patch` just because the operations mention the items/properties; with a `base-hash`, omitted filters change the snapshot shape and can cause stale-base or unsafe diffs.

For generated item operations, include new target names in `item-names` before the item exists. For rename and duplicate operations, include both the old/source name and the new/target name.

Geometry fields are filtered properties too. Whenever an operation sets `left`, `top`, `width`, or `height`, include the corresponding `Left`, `Top`, `Width`, and/or `Height` names in `properties`. This applies to rectangular item types in `create-item`, `duplicate-item`, `move-item`, `resize-item`, and `set-layout`.

Circle geometry is different in the HMI JSON patch/import surface. For `HmiCircle` / `circle`, use `CenterX`, `CenterY`, and `Radius` in `properties` and set them through the operation `properties` object. Do not use `left`, `top`, `width`, `height`, `Left`, `Top`, `Width`, or `Height` for circles in `apply-hmi-screen-json-patch` or `apply-hmi-screen-json-edit`; preflight should block those as not writable. If a lower-level `create-hmi-item` command accepts rectangular placement for a circle, treat that as command-specific convenience, not as the JSON patch shape.

## Command Schema Rules

Use the OpenTiaToolkit command schema exactly. For CLI-shaped payloads, prefer kebab-case parameter names such as `hmi-name`, `screen-path`, `item-names`, `base-hash`, `dry-run`, and `working-path`.

Do not invent parameters such as `project`, `hmiName`, `screenPath`, `itemNames`, or `includeBaseHash` unless the active MCP bridge explicitly documents those aliases. The project is opened or connected outside these HMI screen JSON commands, and `baseHash` is returned automatically by `get-hmi-screen-json` and capability inspection.

Do not use generic JSON Patch operations or shapes. Never emit `replace`, `add`, `remove`, `target`, `path`, or nested `value` patches for `apply-hmi-screen-json-patch`. Use only the supported OpenTiaToolkit operation names, with fields such as `op`, `item`/`itemName`, `source`, `targetName`, `property`, `propertyName`, `value`, `left`, `top`, `eventType`, `scriptCode`, `dynamizationType`, and `tag`.

For new operation objects, prefer canonical model field names such as `op`, `item`, `itemName`, `itemType`, `targetName`, `eventType`, `scriptCode`, and `dynamizationType`. Kebab-case belongs to command arguments (`hmi-name`, `screen-path`, `item-names`, `base-hash`) and compatibility aliases; do not emit aliases like `item-type` when producing fresh canonical operation payloads.

Reserve `op` for entries inside the `operations` array. When showing whole commands, use a wrapper such as `{ "command": "apply-hmi-screen-json-patch", "args": { ... } }` or list the command name separately.

Never emit an empty `scriptCode` for `set-event-handler`, `set-property-event-handler`, or script dynamizations. If the user did not provide script code, ask for it or use a clearly non-executable placeholder such as `"<scriptCode required>"` in a planning-only answer. Do not present that placeholder as ready to run.

## Canonical Patch Shape

Use this shape for targeted patches:

```json
{
  "command": "get-hmi-screen-json",
  "args": {
    "hmi-name": "HMI_1",
    "screen-path": "Screen/Path",
    "item-names": ["ItemA"],
    "properties": ["Text"]
  }
}
```

```json
{
  "command": "apply-hmi-screen-json-patch",
  "args": {
    "hmi-name": "HMI_1",
    "screen-path": "Screen/Path",
    "base-hash": "<baseHash from snapshot>",
    "item-names": ["ItemA"],
    "properties": ["Text"],
    "dry-run": true,
    "operations": [
      { "op": "set-property", "item": "ItemA", "property": "Text", "value": "New text" }
    ]
  }
}
```

If guidance says `apply-with-dry-run-false`, replay the same `apply-hmi-screen-json-patch` payload with only `dry-run` changed to `false` unless guidance returns different `recommendedNextArgs`.

When asked for full safe payloads, include four calls: snapshot, dry-run apply, real apply, and verify. If summarizing, explicitly state that the real apply is the same payload with only `dry-run=false`; otherwise show the real apply JSON too.

For a create with rectangular geometry, the filters must include the target item and every geometry/text property:

```json
{
  "command": "get-hmi-screen-json",
  "args": {
    "hmi-name": "HMI_1",
    "screen-path": "Screen/Path",
    "item-names": ["Button_Start"],
    "properties": ["Text", "Left", "Top", "Width", "Height"]
  }
}
```

The matching create operation should keep operation fields canonical:

```json
{
  "op": "create-item",
  "item": "Button_Start",
  "itemType": "button",
  "left": 120,
  "top": 80,
  "width": 180,
  "height": 48,
  "properties": {
    "Text": "Start"
  }
}
```

For a circle create, use circle-native geometry instead:

```json
{
  "command": "get-hmi-screen-json",
  "args": {
    "hmi-name": "HMI_1",
    "screen-path": "Screen/Path",
    "item-names": ["KiwiCircle1"],
    "properties": ["CenterX", "CenterY", "Radius", "BackColor"]
  }
}
```

```json
{
  "op": "create-item",
  "item": "KiwiCircle1",
  "itemType": "circle",
  "properties": {
    "CenterX": "160",
    "CenterY": "300",
    "Radius": "60",
    "BackColor": "#C5E17A"
  }
}
```

If a circle preflight fails on `Left`, `Top`, `Width`, or `Height`, do not bypass safety or switch to broad imports. Re-express the circle with `CenterX`, `CenterY`, and `Radius`, re-read the snapshot with those filters, then dry-run again.

## Tool Choice

Use `apply-hmi-screen-json-patch` for small, auditable changes: scalar properties, layouts, item create/delete/duplicate/rename, event handlers, property event handlers, dynamizations, and faceplate interface bindings.

Use `apply-hmi-screen-json-edit` when the intended change is broad, structural, or easier to express by transforming a screen JSON file. Keep the JSON focused with item/property filters first; avoid full-screen monolith edits unless necessary.

Use lower-level `export-hmi-json` / edit / `diff-hmi-json` / `import-hmi-json` when the user explicitly wants a human-reviewable file diff, especially across multiple screens. In that case, do not recommend patch first; recommend file export, focused edits, diff generation, dry-run import, human review, then import apply.

Use `probe-hmi-faceplate-interface` when a faceplate `Interface` apply fails or when you need CLR metadata/writeability for a specific interface entry. Prefer a read-only probe first; if a write probe is needed, use `restore=true`.

Use `hmi-status` after an Openness failure, suspected stale TIA/HMI session, or before long HMI workflows. A healthy result should show the expected HMI, project, running instance, and optional screen probe success.

Use `audit-hmi-label-proximity` before bulk nearest-label edits. Review duplicate-label flags, distances, skipped candidates, and unmatched targets before generating or applying patch operations.

For one screen, snapshot/edit/import can be efficient, but patch remains safer for agents because each intent is explicit and preflight can point to exact missing filters or stale hashes. For multiple screens without a file-review requirement, prefer one focused patch workflow per screen rather than one broad monolith.

See `references/operations.md` for compact operation templates and result interpretation.

## Safety Rules

Always treat `BaseHash` as binding to the exact snapshot shape. If the command reports a stale base, re-read the screen with the same filters, rebase the patch, and retry the dry-run.

Keep filters intentionally narrow. Include every item the operation reads or writes in `itemNames`, and include every touched property in `properties`.

Composite operations touch the root composite property. Use `EventHandlers`, `PropertyEventHandlers`, `Dynamizations`, or `Interface` in `properties`; do not filter only to nested entry names.

For `duplicate-item`, include both the source item and the target item name in `itemNames`. For `create-item` with a base hash, include the new item name so validation can prove the filter was intentional.

When one patch mixes screen-level and item-level changes, take one snapshot with the union of all touched filters. For example, a screen `EventHandlers` edit plus an item `Dynamizations` edit should use `itemNames` for the item targets and `properties` containing both `EventHandlers` and `Dynamizations`; omit `item` only on the screen-level operation itself.

Do not bypass a failed preflight by widening to the entire screen unless the user explicitly wants broad-screen editing. Prefer using `Guidance.RequiredItemNames` and `Guidance.RequiredProperties` to repair the request.

## Recovery Patterns

If `Guidance.RecommendedNextAction` is `re-read-screen`, take a fresh snapshot with the same target filters, update `baseHash`, and replay the intended patch.

If it is `fix-patch-operations`, inspect failed operations, missing items, missing properties, and unsupported item/property types. Repair the patch or run `inspect-hmi-screen-json-capabilities`.

If it is `inspect-safety-block`, do not apply. Read preflight failures, skipped properties, and warnings; then narrow or correct the filters.

If it is `apply-with-dry-run-false`, the dry-run is acceptable. Apply the same patch with the same base hash unless a fresh snapshot changed the screen.

If it is `verify-result`, confirm the applied state from the command output or a fresh snapshot before reporting completion.

When a failure mentions an older or stale `baseHash`, re-read first instead of reusing that hash. If a failure only reports missing filters, re-read with the corrected `item-names` and `properties`, then retry the dry-run using the new `baseHash`.

When the original failed composite intent is unknown, do not invent a generic payload. Show the corrected re-read command and either ask for the original operation or present valid alternatives using `set-dynamization`/`remove-dynamization`, `set-event-handler`/`remove-event-handler`, or `set-property-event-handler`/`remove-property-event-handler`.

If an event-handler failure does not reveal whether the handler is screen-level or item-level, re-read the screen with `properties` containing `EventHandlers` and no `item-names` filter, or inspect likely items if they are known. Remove a handler without `item` only after the screen root owns it; remove it with `item` only after a specific item owns it.

## Validation Expectations

For repo changes, run the relevant unit tests and build first. Before live TIA validation, refresh the Siemens Openness registry for new CLI/MCP binaries, then run the HMI Unified integration tests against the requested project/HMI when available.
