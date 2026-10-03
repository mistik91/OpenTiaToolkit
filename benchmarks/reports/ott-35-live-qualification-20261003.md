# OTT-35 live qualification — 2026-10-03

## Outcome: PASS for candidate and controls; model trials pending

Branch: `codex/ott-35-plcsim-behavior-benchmark`, continuing fix commit `4ed3c53`
and scaffold `401c230`. No skills were edited or task-specific examples added.
No original-project probe mutation, push, PR, or merge was performed.

The Release worker was registered through a scoped elevated helper, followed by
read-only discovery of the user's original V21 Update 2 instance PID 15780 and
project `E:/Documents/OpenTiaBench/Gold/20261002-ott35-preflight/OTT_Benchmark/OTT_Benchmark.ap21`.
Every qualification used its own worker/headless TIA and independent archive
restore; the original was not the target of imports, deletes, or downloads.

## Live controls

Evidence root: `E:/Documents/OpenTiaBench/Artifacts/ott35-instance-db-fix/`.
Every row has offline and simulator cleanup proven. `Failed` is the expected
negative outcome, never a gold pass or reusable qualification.

| Run | Evidence ID | Outcome |
| --- | --- | --- |
| Initial gold, `gold-1` | `435cd0bf911649249a87a7fe566fd690` | Rejected at 0 errors / 1 warning; exact instance-DB rollback proven, no probe download |
| Corrected gold, `gold-2` | `d383295c162a4bddb47617cc0c82c561` | Fresh Verified, 15 steps / 26 assertions passed |
| Later compile failure | `26039933d75d470f83ee7bddd01b7954` | Final 1 error / 0 warnings after preceding operations; rollback verified, no probe download |
| 100 ms timer | `2f2ac3851bd1479992ac17cab7a87b60` | Failed `partial-dwell`: Active incorrectly true |
| Permit ignored | `f9a85d9df75244188d262e4b23e7da92` | Failed `request-without-permit`: Active incorrectly true |
| Reset ignored | `510e40f146fd48beb822ecd31aeb7ee1` | Failed `reset-has-priority`: Active incorrectly true |
| Count every scan | `e7bc0ec26a8746baa393e8afcacffc3c` | Failed `first-activation`: counter 2023 instead of 1 |
| Fresh gold repeat, `gold-3` | `25ec6b6b7ceb4848992497201b7f500b` | Fresh Verified, all 26 assertions passed after negative controls |
| Post-soak build, `gold-4` | `059f9edf3d48425db77621f0a890fd6d` | Fresh Verified, all 26 assertions passed with final registered worker |

Each successful/expected-negative qualification freshly compared block/source
inventories, original Main export hash, zero-error/zero-warning cleanup compile,
and exact evidence reuse classification, then closed without saving. Runtime runs
proved zero offline/online differences before, after probe download, and after
cleanup. No disposable runtime tags remained. The later compile failure restored
the FB/instance/global DB and imported source baselines, with instance-before-FB
deletion ordering; the original Main remained unchanged.

The initial warning was traced with a separate offline manual diagnostic, not a
promotable probe: `The parameter '#ActivationCount' might not be initialized.`
The candidate now retains InternalCount in FB statics and assigns ActivationCount
unconditionally each scan. The warning policy and vector were not relaxed. Diagnostic
cleanup removed exactly four new blocks and two sources, compiled at 0/0, matched
Main's original hash, and closed without save. Summary: `warning-diagnostic/summary.json`.

## Harness protections

- `workspaceInputs`: exact raw SHA-256, case/workspace confinement, no traversal,
  streams, reserved paths, device names, junctions, overwrite, or overlapping targets;
  20-file / 1-MiB-per-file / 4-MiB-total limits. Complete preflight precedes staging
  and TIA fixture restoration. Only the public vector is staged, not gold code.
- `.gitattributes` pins this frozen vector to LF so Windows checkout cannot silently
  invalidate the raw-byte hash `1d6ef01bfd7c8b8ff2bfacb7136aeab3b80de08cf2247a5c2be48455ece66724`.
- `plcBehaviorEvidence`: one authentic completed probe result, non-reused evidence
  within the current trial, exact vector path/hash/ID and PLC identity, complete
  ordered steps/writes/typed assertions and observations, canonical coverage,
  clean compile proofs, actual simulator downloads, matching transaction/rollback
  proofs, and both cleanups. No cross-call fragment aggregation or proof-file claims.
- This case requires FB, InstanceDB, GlobalDB and OB evidence, and forbids standalone
  mutation/download/save bypasses. No specific FB name or algorithm is prescribed.
- The prompt receives the selected TIA PID and exact restore root, avoiding ambiguity
  when the user's original and campaign project have the same name.

## Validation and environment audit

- Release build after diagnostic sessions closed: zero errors, zero warnings.
  An earlier soak attempt hit owned CLI/worker file locks; it was not counted as
  passed. Closing those sessions and repeating the build resolved it.
- Post-build suites: **688 unit** and **54 architecture** passed, no failed/skipped.
  Durable TRX: `post-soak-unit/ott35-post-soak-unit.trx` and
  `post-soak-architecture/ott35-post-soak-architecture.trx` under the evidence root.
- Complete benchmark regression suite passed; new focused tests cover 21 staging
  denials (including per-file/total limits and a real junction), 28 invalid-evidence controls, JSON-text/structured positives, and actual
  scorer positive/negative integration. Tests run on Windows PowerShell and PowerShell 7.
- npm from PowerShell 7 required a process-scoped native Windows PowerShell module
  path; the initial mixed-module launcher failure was not counted as a suite pass.
- Command reference current at 186 commands; modern/legacy MCP smoke passed with
  189 tools and SDK 2.0; packaged skill drift zero. No live-project smoke flag used.
- Definitions now: 28 cases, 64 campaigns, 15 fixtures.
- Fresh worker/TIA reopen of the closed gold-3 baseline: only Main, no sources,
  clean 0/0 compile, original Main SHA-256
  `6B6CAE0B8130241248E16ADA2328E1DB0586ED00679BF236ECCB03A9FA066518`.
  Export: `final-health/Main.s7dcl`. Closed without save.
- Final worker build: UTC modification `2026-10-03T09:25:32.6537937Z`, SHA-256
  `7CA215FEBE464BF37B62C956300715CE62ABC928AC18C5206DAEE2DE7BB8C2B6`.
  Scoped elevated registration helper exited 0, followed by Release read-only discovery.

Immutable archives remained read-only and byte-identical:

- Behavior: 5,633,615 bytes, SHA-256
  `D2A66124EC3269132DAD9FF5149DA0E73E8C6EA2897E9A62DDA80F7E9F3017FE`.
- Parent: SHA-256 `1C17F223F00F0A43EFD5E51A7C8E2DBE4DD424540AE0C9A799F102997205AB74`.

All qualification/diagnostic workers ended after known cleanup. The user's TIA and
chat-managed Debug MCP workers are not disposal targets. Separate visible campaign
TIA PID 32536 is reserved for model trials, and bounded simulator owner PID 44824
keeps `OTT35_Bench` alive until its lease expires (approximately 14:49 Europe/Rome).
Do not end that lease during a probe or recovery. Model trials and final campaign
process teardown remain pending; there is no unresolved quarantine.
