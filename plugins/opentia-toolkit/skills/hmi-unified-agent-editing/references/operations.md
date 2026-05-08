# HMI Unified Screen JSON Operation Reference

Use these examples as intent templates, not as copy-paste substitutes for capability inspection. Keep `baseHash`, `itemNames`, and `properties` aligned with the latest snapshot.

This is not RFC 6902 JSON Patch. Do not use `replace`, `add`, `remove`, `target`, `path`, or generic nested `value` payloads. `apply-hmi-screen-json-patch` accepts OpenTiaToolkit operations only.

Use canonical operation field names in new payloads: `op`, `item`, `itemName`, `itemType`, `targetName`, `eventType`, `scriptCode`, and `dynamizationType`. Kebab-case aliases such as `item-type` are compatibility fallbacks, not the preferred shape for fresh agent output.

When using `base-hash`, repeat the same `item-names` and `properties` from the snapshot on both dry-run and real apply. Do not rely on the operation list to imply filters.

## Scalar and Layout Edits

Most layout examples below use rectangular geometry. Circles are the exception: for `HmiCircle` / `circle`, use `CenterX`, `CenterY`, and `Radius` in `properties`; do not use `Left`, `Top`, `Width`, or `Height` in JSON patch/import payloads for circles.

Set one property:

```json
{
  "op": "set-property",
  "itemName": "Button_1",
  "property": "Text",
  "value": "Start"
}
```

Set several properties on one item:

```json
{
  "op": "set-properties",
  "itemName": "Button_1",
  "properties": {
    "Text": "Start",
    "Visible": true
  }
}
```

Move and resize:

```json
[
  { "op": "move-item", "itemName": "Button_1", "left": 120, "top": 80 },
  { "op": "resize-item", "itemName": "Button_1", "width": 180, "height": 48 }
]
```

Set full layout in one operation:

```json
{
  "op": "set-layout",
  "itemName": "Button_1",
  "left": 120,
  "top": 80,
  "width": 180,
  "height": 48
}
```

## Item Lifecycle

Create an item:

```json
{
  "op": "create-item",
  "itemName": "Button_Start",
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

For `create-item` with `base-hash`, include the new item name in `item-names` even though it does not exist yet, and repeat that filter on dry-run and apply.

If the create sets rectangular geometry, include `Left`, `Top`, `Width`, and `Height` in `properties` along with any scalar overrides such as `Text`.

Create a circle:

```json
{
  "op": "create-item",
  "itemName": "KiwiCircle1",
  "itemType": "circle",
  "properties": {
    "CenterX": "160",
    "CenterY": "300",
    "Radius": "60",
    "BackColor": "#C5E17A"
  }
}
```

For that circle create, snapshot and apply filters should include `item-names: ["KiwiCircle1"]` and `properties: ["CenterX", "CenterY", "Radius", "BackColor"]`. If a circle payload fails on `Left`, `Top`, `Width`, or `Height`, correct the geometry model instead of bypassing preflight.

Duplicate and rename:

```json
[
  { "op": "duplicate-item", "itemName": "Button_Start", "targetName": "Button_Stop" },
  { "op": "rename-item", "itemName": "Button_Stop", "targetName": "Button_Reset" }
]
```

`duplicate-item` accepts `item`, `target`, `targetItem`, or `targetName` for the new item. Prefer `targetName` when the source is already provided with `itemName`.

Delete:

```json
{
  "op": "delete-item",
  "itemName": "Button_Reset"
}
```

## Event Handlers

Set an item event handler:

```json
{
  "op": "set-event-handler",
  "itemName": "Button_1",
  "eventType": "Click",
  "scriptCode": "return 42;",
  "globalDefinitionAreaScriptCode": "",
  "async": false
}
```

Do not use an empty `scriptCode`. If the script is unknown, ask for it before producing a ready-to-run apply payload.

Remove an item event handler:

```json
{
  "op": "remove-event-handler",
  "itemName": "Button_1",
  "eventType": "Click"
}
```

For screen-level event handlers, omit `itemName`.

## Property Event Handlers

Set a property event handler:

```json
{
  "op": "set-property-event-handler",
  "itemName": "Button_1",
  "property": "Text",
  "eventType": "Change",
  "scriptCode": "return 24;",
  "globalDefinitionAreaScriptCode": "",
  "async": false
}
```

Remove it:

```json
{
  "op": "remove-property-event-handler",
  "itemName": "Button_1",
  "property": "Text",
  "eventType": "Change"
}
```

## Dynamizations

Set a tag dynamization:

```json
{
  "op": "set-dynamization",
  "itemName": "Button_1",
  "property": "Text",
  "dynamizationType": "Tag",
  "tag": "HmiTag_1"
}
```

Filter repair after a `Dynamizations` failure means re-reading with the root composite property:

```json
{
  "hmi-name": "HMI_1",
  "screen-path": "Codex/Main",
  "item-names": ["BtnCopy"],
  "properties": ["Dynamizations"]
}
```

Then retry a supported operation, for example:

```json
{
  "op": "set-dynamization",
  "item": "BtnCopy",
  "property": "Visible",
  "tag": "HmiTag_Bool"
}
```

If the original intent was removal instead, use:

```json
{
  "op": "remove-dynamization",
  "item": "BtnCopy",
  "property": "Visible"
}
```

If the original failed operation is not available, do not invent one. Ask for the original operation or present the valid set/remove alternatives.

Remove it:

```json
{
  "op": "remove-dynamization",
  "itemName": "Button_1",
  "property": "Text"
}
```

## Result Interpretation

## Multi-Screen File Review

If the user asks for changes across multiple screens and a human-reviewable file diff before applying, use this workflow instead of screen patch:

1. `export-hmi-json` with screen filters.
2. Edit the exported JSON files in a focused way.
3. `diff-hmi-json` to generate a reviewable patch file.
4. `import-hmi-json` with `dry-run=true`.
5. Apply with `dry-run=false` only after review and clean preflight.

`Guidance.RecommendedNextAction` is the first thing an agent should read after `Success`, `SafeToApply`, or `Applied`.

`apply-with-dry-run-false` means the dry-run is acceptable. Replay the same patch with `dry-run=false`.

`verify-result` means the command applied successfully and the next step is confirmation from `VerifiedScreen`, `VerifyExportPath`, or a fresh snapshot.

`re-read-screen` means `baseHash` is stale. Take a new snapshot with equivalent filters and rebase the patch.

`fix-patch-operations` means at least one operation is invalid or under-filtered. Use `RequiredItemNames`, `RequiredProperties`, failed operations, and capability inspection to repair it.

`inspect-safety-block` means preflight is not safe. Do not apply until failures and warnings are understood.
