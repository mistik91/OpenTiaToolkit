# OTT-35 behavior preflight — 2026-10-03

## Outcome

Environment qualification and the basic guarded runtime probe passed. The harder
stateful gold candidate exposed a generic native-SCL instance-DB identity defect
before probe download. Qualification is paused; **no model trials ran and no model
quota was used**. There is no published OTT-35 case or campaign yet.

The initial quarantine was fully recovered on the disposable project. Final evidence
is `Failed`, not reusable, with offline and simulator cleanup proven. This is not a
gold pass. No skill or production-code change is part of this checkpoint.

## Environment established

The user's Anonymous-user and simulation settings were saved and archived from
`C:/OpenTiaBench/Gold/20261002-ott35-preflight/OTT_Benchmark/OTT_Benchmark.ap21`.
No probe artifacts were imported into that original open project. Current Release
Openness attachment, PLC compilation, and an independent archive restore succeeded.

- PLC: `PLC_Main`, CPU `6ES7 511-1AL03-0AB0/V3.1`, standard S7-1500.
- Fresh read-only archive: `C:/OpenTiaBench/Archives/OTT_Benchmark_V21_Behavior_20261003.zap21`.
- SHA-256: `D2A66124EC3269132DAD9FF5149DA0E73E8C6EA2897E9A62DDA80F7E9F3017FE`.
- Parent archive remained unchanged: `OTT_Benchmark_V21_20260731.zap21`, SHA-256
  `1C17F223F00F0A43EFD5E51A7C8E2DBE4DD424540AE0C9A799F102997205AB74`.
- PLCSIM Advanced 8.0 instance: `OTT35_Bench`, controller `PLC_Main`, address `192.168.0.1`.
- Simulator fingerprint: `2f9d821eddace2c957c2919e45ef7c02a761e0f0218cf3c2316d985e348c8200`.
- Restored baseline compile: zero errors/warnings; online comparison: zero differences.

The simulator is intentionally held by a four-hour owner lease started around
2026-10-03 00:58 Europe/Rome, expiring around 04:58. This is a bounded fixture owner,
not an orphan. The user's TIA instance remains open; completed disposable sessions
have been closed. Do not reuse historical process IDs for later cleanup.

## Basic runtime control: passed

The existing simple behavior example was independently restored and run through
`run-plc-code-behavior-probe`, with force enabled, `CleanCompile`, zero new warnings,
and guarded simulator download. The local example copy was encoding-normalized to
UTF-8 BOM before validation; canonical skill assets were not modified.

- Evidence: `40598574323645278878314945a0405f`.
- Status: `Verified`; offline cleanup and simulator cleanup both proven.
- Both branch checks and heartbeat observations passed.
- Baseline, probe, and cleanup comparisons had zero differences.
- Original Main export and block/source inventories were unchanged; final compile
  had zero errors/warnings. Fresh exact evidence lookup was qualified.
- Local journal/evidence: `C:/OpenTiaBench/Artifacts/ott35-basic-20261003-v3/`.

This proves the runtime environment and basic workflow, not the harder candidate.

## Stateful candidate: generic toolkit defect

The candidate contains an FB with a TON, native SCL instance DB, global I/O DB,
and cyclic OB. Its independent vector has 15 steps and 26 assertions covering
interrupted dwell, interlock loss, reset priority, counted edges, and restart timing.
Vector raw-byte SHA-256 at this checkpoint:
`1d6ef01bfd7c8b8ff2bfacb7136aeab3b80de08cf2247a5c2be48455ece66724`.
Line-ending or content changes require a fresh hash and independent qualification.

`PlcCodeFormatService.ValidateScl` currently maps every `DATA_BLOCK` declaration to
`GlobalDB`. Siemens generated `OTT35_Instance` as an `InstanceDB`, so transaction
exact-target verification failed:

```text
SCL generation did not produce exact target 'PLC_Main/<root>/GlobalDB:OTT35_Instance'.
```

Rollback then removed the generated FB but missed the differently typed instance
DB. Compilation correctly detected its missing `OTT35_Control` type, and the probe
was quarantined. No harder-candidate probe code was downloaded or executed.

- Evidence: `529c9eee9c44412aaa07ec82aebb42db`.
- Transaction: `434b27c6defa40d8b5d3e6ca93bd177d`.
- Disposable project: `C:/OpenTiaBench/Gold/20261003-ott35-stateful-v2/OTT_Benchmark/OTT_Benchmark.ap21`.
- Journal and original quarantine result: `C:/OpenTiaBench/Artifacts/ott35-stateful-20261003-v2/`.
- Recovery package was copied to `incident-package/` before supported recovery
  discarded its original transaction package after proof.

## Incident recovery: proven

Fresh identity and inventory reads proved the only orphan was `OTT35_Instance`
(`InstanceDB`, DB1) in the disposable project. An unconfirmed exact deletion was
denied without writes. Confirmed deletion used only `^OTT35_Instance$`; the user's
project was never targeted. Supported `recover-plc-code-behavior-probe` then proved:

- Transaction rollback and final compilation: zero errors/warnings.
- Simulator baseline cleanup download succeeded and comparison had zero differences.
- Probe runtime tags were absent after cleanup.
- Final evidence: `Failed`, `CleanupProven=true`, `SimulatorCleanupProven=true`;
  execution is null and the evidence cannot qualify future code.
- Original Main SHA-256 remained
  `6B6CAE0B8130241248E16ADA2328E1DB0586ED00679BF236ECCB03A9FA066518`.

The final record is preserved in `recovered-evidence.json`. The private project was
closed without saving, then its retained owning MCP and worker ended; its headless
TIA process subsequently exited. Only the user's original TIA project remains open.

## Next gate

Make a generic toolkit correction before continuing: distinguish native instance
DBs from global DBs, preserve exact identities and dependency order, verify the
correct canonical export route, and ensure rollback cannot treat a same-name,
wrong-subtype generated object as safely absent. Do not weaken identity checks or
replace this useful FB/instance-DB task with a benchmark-specific workaround.

The development and PLC verification workflows require stopping on unqualified
or quarantined evidence. Recovery is complete, but gold qualification remains blocked
pending that correction. Semantic negative controls and all model campaigns are
still explicitly unrun.

## Checkpoint validation

- `npm test`: all existing benchmark definition and scorer/batching regressions passed
  (27 published cases, 62 campaigns, 14 fixtures; OTT-35 is not counted as published).
- Qualification helper: PowerShell syntax check passed. The basic live qualification
  exercised the helper; the final fail-closed retention and empty-match handling
  adjustments were syntax-checked, not replayed against another live mutation.
- All three candidate code files retain UTF-8 BOM. Release CLI artifact/vector
  validation succeeds with absolute paths and reproduces the wrong `GlobalDB` label;
  static `Classification=verified` is not evidence of a live behavioral pass.
- Both archive hashes and final cleanup records were rechecked; process audit found
  no leftover disposable TIA/MCP/worker session. The chat's managed MCP and bounded
  simulator owner are intentional survivors.
- `git diff --cached --check`: passed.

Production code and skill packages are unchanged, so Release build, non-integration
and architecture tests were not rerun for this scaffold-only checkpoint. Their
preceding environment checkpoint passed with zero build errors/warnings, 674
non-integration tests and 54 architecture tests. A toolkit correction will require
fresh build, test, worker-registration, and disposable live-validation gates.
