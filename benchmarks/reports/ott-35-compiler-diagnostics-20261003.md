# OTT-35 compiler diagnostic retention — 2026-10-03

## Outcome: PASS for feedback fix and live qualification

Branch: `codex/ott-35-plcsim-behavior-benchmark`, continuing checkpoint `d47514f`.
No skill changes, task-specific recipes, vector changes, gold-code staging,
push, PR, or merge. The earlier Flash results and append-only output replay remain
unchanged; this is a toolkit feedback improvement, not proof that feedback caused
or will resolve every model failure.

## Generic fix

`PlcCodeTransactionCompileEvidence.Diagnostics` now retains the existing compiler
diagnostic objects: severity, path, name, description, and error code. A shared
converter snapshots the list for baseline, candidate, and rollback compilation.
Absent or explicit-null diagnostics in older JSON load as an empty collection.
The additive field does not change transaction plan hashes or schema versions.

A failed behavior apply can already have rolled back before the probe handles
the failure. Its proven rollback compilation is now copied into persistent probe
evidence before transaction discard. Planning evidence is also retained when
impact review stops execution. Compiler counts, warning/error gates, recovery
rules, download guards, and runtime assertions are unchanged.

## Validation

- Release solution build: 0 warnings / 0 errors.
- **694 non-integration unit tests**, **54 architecture tests** passed. Six new
  test cases cover legacy/null JSON, full field/Unicode round trips, collection
  snapshotting, empty diagnostics, and warning/error rejection with persisted
  three-phase evidence after rollback/discard and no runtime download/execution.
  Existing success/disposal, failed transaction reload, and unproven-recovery
  tests additionally check retained diagnostics.
- An initial test setup incorrectly expected a final compile after an import
  failure; correcting the fake compile sequence produced the full clean rerun.
  Both TRX files are preserved; the initial failure is not reported as a pass.
- Command reference: 186 commands, current. MCP modern/legacy stdio: 189 tools,
  stable SDK 2.0. Packaged skill drift: zero. Full benchmark regression suite
  passed; updated definitions validate 28 cases, 66 campaigns, 15 fixtures.
- Exact rebuilt Release worker registered through a scoped elevated helper
  (exit 0), SHA-256 `693B255F1F3DC77BCA869FA8577EADAB2442DEBE37C092E62E2CC2903AAE82DF`.
  Read-only discovery reconfirmed original TIA PID 15780 and its original project.

Durable tests: `E:/Documents/OpenTiaBench/Artifacts/ott35-compiler-diagnostics/tests/`.
Live journal/probe roots: sibling `warning/`, `compile-failure/`, and `gold/`.
Every run used its own worker/headless TIA and independent immutable archive
restore under `C:/OTT/Live/ott35-diag-*`, never the user's original project.

| Control | Evidence ID | Result |
| --- | --- | --- |
| Uninitialized output warning | `12da6ba870b843fdb6e73efd0b2880e4` | Failed as expected: 0 errors / 1 warning; exact `The parameter '#ActivationCount' might not be initialized.` retained |
| Real later compile error | `8f47bb4acd6b4822ad430c4dd0dae7c9` | Failed as expected after preceding operations; exact `Tag #OTT35_Undeclared_Negative not defined.` retained |
| Known-good temporal candidate | `9341835159ec4dbdb0a8aa640c909c87` | Fresh Verified: 15 steps / 26 assertions passed |

Both negative controls retained clean rollback compilation, proved cleanup, and
made no probe runtime download. All three controls verified fresh block/source
inventories, original Main export hash, cleanup compilation at 0/0, and correct
evidence reuse classification before closing without saving.

Fresh worker/headless TIA reopen of the closed gold restore proved Main only,
zero external sources, compilation at 0/0, online difference count zero, and
unchanged Main SHA-256 `6B6CAE0B8130241248E16ADA2328E1DB0586ED00679BF236ECCB03A9FA066518`.
Closed without saving; durable `fresh-health-cli.log` and `fresh-health/` exports.
Simulator discovery reported two total runtime tags; the behavioral runner proved
absence of all declared disposable DB owners. Total tag count is not asserted to
be zero or used as a substitute for that owner-specific cleanup proof.

Post-qualification Release build again completed at 0 warnings / 0 errors.
All qualification/reopen MCP, CLI, worker, and headless TIA processes exited;
the user's original PID and chat-managed Debug workers were preserved. Only
the bounded owned simulator lease remains for model trials. Immutable archive
remains read-only, 5,633,615 bytes, SHA-256
`D2A66124EC3269132DAD9FF5149DA0E73E8C6EA2897E9A62DDA80F7E9F3017FE`.

## Fresh model cohort

New `plc-behavior-diagnostics-glm-5-3-flash-high-20261003` and full-model fallback
definitions preserve the same three-trial budget, High effort/variant, subscription
provider, and frozen OTT-35 case. They do not resume the historical campaign across
different toolkit commits. Model trials and final process/archive audit are pending
at this implementation checkpoint.
