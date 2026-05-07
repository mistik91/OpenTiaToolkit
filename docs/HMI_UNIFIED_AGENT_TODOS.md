# HMI Unified Agent Workflow Todos

Last updated: 2026-05-06

## Open

No open HMI Unified agent workflow TODOs from the current report batch.

## Completed

### File-backed patch operations

- **Status:** Implemented.
- **Command:** `apply-hmi-screen-json-patch --operations-path=<file.json>`.
- **Reason:** Avoids REPL JSON quote stripping and command-line payload size limits for bulk patch operations.

### Ambiguous EventHandlers reflection during backup export

- **Status:** Implemented.
- **Fix:** Replaced ambiguous case-insensitive `GetProperty` lookups in the Unified HMI adapter with a deterministic public instance property resolver.
- **Validation:** A backup-enabled no-op patch on `HMI_RT_1/1002_FirstPage` item `V114` completed with a real `backupPath` and no `AmbiguousMatchException`.

### Screen summary before large exports

- **Status:** Implemented.
- **Command:** `get-hmi-screen-summary`.
- **Validation:** `HMI_RT_1/1003_SecondPage` returned compact item and faceplate contained-type counts, including `25` `V0.0.9\SensorPV` and `22` `V0.0.16\Valve_Simple`.

### Faceplate interface probe command

- **Status:** Implemented.
- **Command:** `probe-hmi-faceplate-interface`.
- **Reason:** Separates direct Siemens Openness setter behavior from JSON patch/import behavior and captures interface entry CLR metadata.
- **Validation:** On `HMI_RT_1/1003_SecondPage`, `Faceplate container_10` exposed `GEN = Analog_PT355_GEN` as `System.Object`; direct no-op and temporary write/restore probes both succeeded.

### SensorPV V0.0.9 interface import writes

- **Status:** Fixed and validated live.
- **Root cause:** The import synchronizer wrote every faceplate interface entry when any entry changed. `V0.0.9\SensorPV` accepts `GEN` writes but rejects rewriting entries such as `Color`, which made a valid `GEN` patch fail and could leave the TIA/HMI session disposed.
- **Fix:** Reacquire the item before import-time `Interface` writes and write only changed interface entries.
- **Validation:** On `HMI_RT_1/1003_SecondPage`, `Faceplate container_10`, `apply-hmi-screen-json-patch` successfully set `Interface.GEN` from `Analog_PT355_GEN` to `Analog_PT355_GEN_PROBE`, then restored it to `Analog_PT355_GEN`. A final `probe-hmi-faceplate-interface` confirmed the restored value and `list-instances` still showed the TIA instance.

### HMI session health diagnostics

- **Status:** Implemented.
- **Command:** `hmi-status`.
- **Reason:** Distinguishes healthy sessions from stale/disposed HMI software references and reports running TIA instances, project path, current HMI, optional open attempt, and optional screen probe.
- **Validation:** Against TIA PID `33108`, project `E:\Documents\Automation\19089Q1\19089Q1.ap21`, `hmi-status --hmi-name=HMI_RT_1 --screen-path=1003_SecondPage --open-if-needed=true` returned `healthy=true` and resolved the screen.

### Bulk proximity mapping audit

- **Status:** Implemented.
- **Command:** `audit-hmi-label-proximity`.
- **Reason:** Nearest-label matching is heuristic; the command emits target item, matched label item, label text, distance, deltas, duplicate-label flags, candidate count, and skipped candidate count before any apply.
- **Validation:** Generated `artifacts/hmi_exports/HMI_RT_1/1002_FirstPage_v116_proximity_audit.json` from `1002_FirstPage.json`. With `label-texts=V116`, the command extracted `V116` from the HmiText tag `Valve_V116_GEN.AV` and flagged duplicate label use when all faceplate targets were considered.
