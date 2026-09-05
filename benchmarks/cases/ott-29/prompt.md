TIA Portal is running with a freshly restored disposable `OTT_Benchmark` project.

Reconcile the WinCC Unified trend logging in `HMI_RT_1`. The three task tags are internal HMI tags; do not create a PLC connection or change any PLC, hardware, screen, alarm, or unrelated HMI object.

Open the exact HMI and inspect `MixerSpeed`, `BatchNumber`, and `MixerAlarmBits` with logging counts. The baseline intentionally contains an active `MixerSpeed` logging tag associated with `MixerLegacyLog`, an orphaned `MixerBatchRetireLog`, and an active `MixerAlarmBits` logging tag associated with `MixerProtectedLog`.

Before changing anything, dry-run one two-archive deletion batch containing `MixerLegacyLog` and `MixerBatchRetireLog`, in that order. Verify from the returned result that `MixerLegacyLog` is blocked by the active `MixerSpeed` reference while `MixerBatchRetireLog` is only planned. Do not apply this mixed batch.

Replace the process logging configuration with one shared `MixerProcessLog` by using one bulk trend request, ordered as follows:

1. `MixerSpeed`: cyclic logging at exactly `T5s`.
2. `BatchNumber`: on-change logging; omit a cycle.

For both configurations set `logMaxSize` to `4096`, `durationDays` to `60`, and `segmentMaxSize` to `512`. Do not specify a storage folder. Dry-run the complete two-tag batch, verify that both configurations are planned with no blocked, not-found, or failed items, then apply the same semantic batch once with explicit confirmation.

Retire the old status logging on `MixerAlarmBits` using one bulk removal request whose expected association is exactly `MixerProtectedLog`. Dry-run it first, then apply the same removal once with explicit confirmation.

After the process logging update and status-tag removal, delete exactly `MixerLegacyLog`, `MixerBatchRetireLog`, and `MixerProtectedLog` in one ordered bulk request. Dry-run the complete three-archive batch and proceed only if all three archives are planned with no blocked, not-found, or failed items. Apply the same semantic batch once with explicit confirmation.

Verify the final state without saving the project:

- read the three HMI tags again with logging counts and prove `MixerSpeed=1`, `BatchNumber=1`, and `MixerAlarmBits=0`;
- dry-run one bulk removal request for `MixerSpeed` and `BatchNumber`, both with expected data log `MixerProcessLog`, proving both final associations without applying the removal;
- dry-run deletion of `MixerProcessLog` and verify it is blocked by exactly the two active logging-tag references;
- dry-run one ordered deletion batch for the three retired archives and verify all three are now reported not found.

Expected safety blocks and not-found verification results are evidence, not reasons to retry with broader or destructive operations. Do not use the single-item trend/logging commands and do not save the project.
