# HMI Unified Agent Workflow Todos

Last updated: 2026-05-06

## Open

### SensorPV V0.0.9 interface writes fail at apply time

- **Status:** Reproduced live on project `19089Q1`, HMI `HMI_RT_1`, screen `1003_SecondPage`.
- **Scope:** `HmiFaceplateContainer` items with `ContainedType = V0.0.9\SensorPV`.
- **Probe:** `Faceplate container_10`, interface property `GEN`, attempted value `Analog_PT355_GEN_PROBE`.
- **Observed behavior:** `apply-hmi-screen-json-patch` dry-run passes and reports the operation as valid, but real apply fails with:
  `Error when calling method 'set_Value' of type 'Siemens.Engineering.HmiUnified.UI.Parts.HmiFaceplateInterface'.`
- **Impact:** The JSON patch/import preflight is too optimistic for SensorPV interface values because it validates shape/existence but not whether Siemens Openness accepts `HmiFaceplateInterface.Value` writes for that faceplate type.
- **Additional observation:** After the failed real apply, the TIA Portal instance was no longer visible through `list-instances`, which supports the report's stale/disposed-session concern.
- **Direct probe result:** `probe-hmi-faceplate-interface` showed `GEN` is exposed as `System.Object`, readable and writable, and a direct temporary write to `Analog_PT355_GEN_PROBE` succeeded with restore back to `Analog_PT355_GEN`.
- **Patch/import result:** The same changed value through `apply-hmi-screen-json-patch` is still blocked at real apply. The import report records `Interface.GEN` as failed and then the current HMI software object becomes disposed.
- **Current interpretation:** SensorPV `GEN` is not globally non-writable through Openness. The failure is specific to the JSON patch/import execution path or Siemens object lifetime during that path.
- **Next investigation:** Reacquire the screen/item immediately before the import-time `Interface` write, then re-run the changed-value patch/import test.
- **Possible fixes:** Add import-time per-property diagnostics for `Interface`, reacquire screen/item before faceplate interface writes, detect disposed HMI/TIA sessions and return a structured recovery action, and mark known unsafe import paths during preflight when detectable.

### Stale or corrupted TIA/HMI sessions are hard to diagnose

- **Status:** Confirmed after failed SensorPV changed-value apply.
- **Observed behavior:** After the failed import, follow-up patch/probe commands threw `EngineeringObjectDisposedException` for `Siemens.Engineering.HmiUnified.HmiSoftware`, and `list-instances` returned zero instances.
- **Impact:** Agents cannot reliably distinguish slow exports from stale COM/session state.
- **Possible fixes:** Add command-level timeouts around HMI open/export/import phases and expose a `portal-status` or `hmi-status` diagnostic command.

### Bulk proximity matching needs auditable output

- **Status:** Reported from bulk valve interface copy workflow.
- **Impact:** Nearest-text matching is heuristic and can duplicate label matches.
- **Possible fixes:** Emit a mapping report containing target item, matched label item, distance, duplicate-label flags, and skipped candidates before apply.

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
