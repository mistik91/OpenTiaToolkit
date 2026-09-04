TIA Portal is running with a freshly restored disposable `OTT_Benchmark` project.

Reconcile the WinCC Unified discrete alarms in `HMI_RT_1`. The trigger and parameter tags used by this task are internal HMI tags; do not create a PLC connection or change any PLC, hardware, screen, logging, or unrelated HMI object.

Start by opening the exact HMI, reading the complete canonical discrete-alarm inventory with Siemens validation, and inspecting the relevant internal HMI tags. Obtain a fresh exact snapshot for the alarm to be updated; the retirement target is read immediately before its deletion later. Treat alarm names as exact and case-sensitive.

Perform the following update and creation in one versioned `1.0` alarm document:

- Patch `Mixer_Overload` with its fresh canonical hash. Keep its ID, origin, area, trigger tag/bit, trigger mode, parameter-tag order, and all unspecified fields unchanged. Change its class to `Critical`, priority to `14`, English event text to exactly `Mixer overload critical at speed {0} during batch {1}.`, and English info text to exactly `Stop the mixer, inspect the drive, and record the batch before restart.`
- Create `Mixer_SpeedHigh`. Compute the lowest unused alarm ID in the inclusive range 2398-2405 and the lowest unused bit in the inclusive range 0-7 on internal tag `MixerAlarmBits`; do not assume either value without deriving it from the complete inventory. Set class `Critical`, priority `14`, origin `Mixer`, area `Process`, trigger mode `OnRisingEdge`, and ordered parameter tags `MixerSpeed`, then `BatchNumber`. Set English event text to exactly `Mixer speed high at {0} rpm during batch {1}.` and English info text to exactly `Reduce the mixer speed and inspect the drive.`

Write that document inside the isolated benchmark workspace. Dry-run the complete two-alarm document with warning failure enabled. Proceed only if the real transaction reports success, zero warnings, and rollback. Apply the exact same unchanged document once with explicit confirmation and warning failure enabled. Fresh-read both exact alarms with Siemens validation and verify their complete canonical state.

Then retire exactly `Mixer_Trip_Legacy`. Fresh-read its canonical hash immediately before deletion. Dry-run the exact-name deletion without allowing referenced deletion, verify that it reports no cross-references and rolls back, then apply once with the same reviewed hash and explicit confirmation. Do not delete or modify the near-name alarm `Mixer_Trip_Legacy_Display`.

Finally, exact-read `Mixer_Trip_Legacy` and confirm the not-found result, exact-read `Mixer_Trip_Legacy_Display` to prove it is unchanged, and read the complete alarm inventory again with Siemens validation. Do not save the project.
