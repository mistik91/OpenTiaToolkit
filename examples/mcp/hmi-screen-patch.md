# MCP Example: Safe HMI Screen Patch

This example shows the intended safe flow for a small Unified HMI screen edit.

1. Connect and open the target project:

```text
connect-portal
open-project --project-path="D:\Siemens\V21\HmiTest\HmiTest.ap21"
open-unified-hmi --hmi-name="HMI_1"
```

2. Read a focused snapshot:

```json
{
  "command": "get-hmi-screen-json",
  "args": {
    "hmi-name": "HMI_1",
    "screen-path": "1001_Main",
    "item-names": ["Button_Start"],
    "properties": ["Text"]
  }
}
```

3. Dry-run the patch with the returned `baseHash`:

```json
{
  "command": "apply-hmi-screen-json-patch",
  "args": {
    "hmi-name": "HMI_1",
    "screen-path": "1001_Main",
    "base-hash": "<baseHash from snapshot>",
    "item-names": ["Button_Start"],
    "properties": ["Text"],
    "dry-run": true,
    "operations": [
      {
        "op": "set-property",
        "item": "Button_Start",
        "property": "Text",
        "value": "Start"
      }
    ]
  }
}
```

4. If guidance says the dry-run is safe, replay the same payload with `dry-run` set to `false`.

5. Verify with a fresh `get-hmi-screen-json` call using the same item and property filters.

