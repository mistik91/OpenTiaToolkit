# Behavior package and vector schema 1.0

The package manifest lists 2-20 disposable artifacts. Paths are resolved relative to the manifest. Artifacts may mix SCL and S7DCL because one persistent PLC code transaction applies and rolls them back together.

```json
{
  "schemaVersion": "1.0",
  "packageId": "unique-package-id",
  "artifacts": [
    { "codeFormat": "scl", "inputPath": "Code.scl", "groupPath": "" },
    { "codeFormat": "s7dcl", "inputPath": "Contract.s7dcl", "groupPath": "" },
    { "codeFormat": "scl", "inputPath": "Cycle.scl", "groupPath": "" }
  ]
}
```

The package must declare at least one global DB and one OB. Prefer a native SCL scheduler OB: SIMATIC SD OB import can assign a free cycle-OB number and therefore needs a separately proven target-specific shape. Every object must use a unique name and be disposable.

The vector defines reset writes, ordered steps, bounded assertions, and cleanup writes:

```json
{
  "schemaVersion": "1.0",
  "vectorId": "unique-vector-id",
  "reset": [{ "tag": "Probe_DB.Input", "type": "Int32", "value": 0 }],
  "steps": [{
    "name": "scenario",
    "writes": [{ "tag": "Probe_DB.Input", "type": "Int32", "value": 7 }],
    "settleMilliseconds": 25,
    "assertions": [{
      "tag": "Probe_DB.Output",
      "type": "Int32",
      "operator": "Equal",
      "expected": 13,
      "tolerance": 0,
      "timeoutMilliseconds": 1000,
      "pollMilliseconds": 25,
      "stableMilliseconds": 0
    }]
  }],
  "cleanup": [{ "tag": "Probe_DB.Input", "type": "Int32", "value": 0 }]
}
```

Supported types: `Bool`, signed/unsigned 8/16/32/64-bit integers, `Float`, `Double`, `Char`, `WChar`, `String`, and `WString`.

Supported operators: `Equal`, `NotEqual`, `GreaterThan`, `GreaterThanOrEqual`, `LessThan`, and `LessThanOrEqual`.
