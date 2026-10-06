# PASS — ott-29 Unified trend-logging gold audit

- Date: 2026-09-05
- Branch: `codex/hmi-trend-logging-benchmark`
- TIA Portal: V21 Update 2, visible instance PID 13856
- Project: disposable `OTT_Benchmark` copies only
- HMI: `HMI_RT_1`

## Immutable fixture

| Field | Value |
| --- | --- |
| Fixture ID | `tia-v21-unified-trends-9` |
| Archive | `C:\OpenTiaBench\Archives\OTT_Benchmark_V21_Trends_20260905.zap21` |
| Bytes | 5,641,130 |
| SHA-256 | `7078FA79558948F375229C94AB69FF00D171646CAE874D51DA9B8453D35C82EB` |
| Read-only | yes |
| Parent fixture | `tia-v21-unified-alarms-8` |

The source archive was independently restored to a second short-path root.
The restored project, HMI identity, tag data types, logging counts, two exact
active associations, and one orphaned archive were verified before the gold
mutation. The archive hash was unchanged after verification.

## Baseline

| HMI tag | Type | Logging count | Exact association |
| --- | --- | ---: | --- |
| `MixerSpeed` | `Real` | 1 | `MixerLegacyLog`, cyclic `T10s` |
| `BatchNumber` | `DInt` | 0 | none |
| `MixerAlarmBits` | `Int` | 1 | `MixerProtectedLog`, cyclic `T1s` |

`MixerBatchRetireLog` existed with no active logging-tag references. A
two-archive deletion dry-run returned exactly one planned item and one blocked
item: `MixerLegacyLog` was blocked by `MixerSpeed`, while
`MixerBatchRetireLog` was eligible. The mixed batch was not applied.

## Gold reconciliation

1. One two-item `create-hmi-trends` preview planned both configurations with
   zero blocked, not-found, or failed items.
2. The same semantic batch was applied once with confirmation:
   - `MixerSpeed` retained its existing derived logging-tag identity, moved to
     `MixerProcessLog`, and became cyclic `T5s`.
   - `BatchNumber` received a new derived logging tag in the same
     `MixerProcessLog` with `OnChange` mode and no cycle.
   - the shared data log reported size 4096, duration 60 days, and segment size
     512 for both projected results.
3. One association-bound `remove-hmi-logging-tags` preview and confirmed apply
   removed `MixerAlarmBits` from `MixerProtectedLog`.
4. One ordered three-archive deletion preview planned `MixerLegacyLog`,
   `MixerBatchRetireLog`, and `MixerProtectedLog` with no blocked, not-found,
   or failed items. The same semantic batch applied all three once.

## Fresh final verification

| HMI tag | Final logging count |
| --- | ---: |
| `MixerSpeed` | 1 |
| `BatchNumber` | 1 |
| `MixerAlarmBits` | 0 |

- A two-item logging-tag removal dry-run planned both `MixerSpeed` and
  `BatchNumber` only when their expected data log was `MixerProcessLog`,
  proving both associations without removing them.
- A `MixerProcessLog` deletion dry-run was blocked by exactly the two active
  tag references.
- A fresh dry-run for the three retired archives reported all three not found.
- The project was closed without saving, restoring the immutable fixture as
  the source of truth.

## Scope and safety

No PLC connection, PLC object, hardware object, HMI screen, discrete alarm, or
unrelated HMI object was changed. The fixture-construction copy was saved only
to create the immutable archive. The independent fixture audit and gold task
copy were both closed without saving.
