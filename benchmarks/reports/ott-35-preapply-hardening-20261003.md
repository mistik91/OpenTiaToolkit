# OTT-35 pre-apply and output-capture hardening — 2026-10-03

## Outcome: safety fixes qualified; Flash timed out; remaining trials paused

Branch `codex/ott-35-plcsim-behavior-benchmark`, following `215d184`.
No skills, acceptance vector, compile thresholds, gold solutions, or model-visible
task instructions changed. No push, PR, or merge. Earlier traces, scores, paused
campaigns, and quarantined incident evidence remain unchanged.

## Generic changes

Behavior query/run now check each artifact's exact capability route, object kind,
type, and destination group before creating evidence or a transaction or running
download preflight. They reuse the same gate as operation assembly; a mistaken
SCL capability for an S7DCL artifact remains rejected, not silently rediscovered.

Untouched pre-apply transactions can establish cleanup without a fictitious
rollback or candidate download. Proof requires an eligible pre-apply state,
Pending operations with no canonical/restoration evidence, no final/rollback
compile, an entirely recognized pre-apply journal starting at Lifecycle/Draft,
and byte-equivalence between the manifest journal and the separate durable
journal. A write-ahead Apply entry with an older Draft/Planned manifest fails
closed. Missing transactions and unrecognized/inconsistent states remain
quarantined. A retained baseline package is normal at planning and is not itself
evidence that mutation began. Recovery never promotes behavioral evidence.

Native output capture distinguishes the observed OpenCode built-in `glob`
listing cap from file-backed truncation (`count=100`, completed, boolean
`truncated=true`, exact call identity, no outputPath or byte/file marker).
The fresh trial also exposed native `read` slices marked `truncated=true`.
An explicit positive integer limit, matching file/display identity and line
range, and the exact native wrapper/continuation footer must all agree before
a requested slice is recognized. Both cases leave model-visible content
unchanged. The reader never expands the source file behind a requested slice.
MCP, unknown tools, other metadata shapes, and any explicit outputPath still
require the existing call-bound, isolated-path, size/hash/prefix-verified capture.

## Validation checkpoint

Release solution build: zero warnings/errors. Final full runs and the end-of-pass
soak passed **731 unit and 54 architecture tests**. Focused pre-apply tests cover
route/type/group
rejection before simulator access, failure after one draft operation, planning
failure, impact review refusal, persisted cleanup/recovery, and missing or
write-ahead/inconsistent manifests. No import, runtime execution, or real
download is permitted by those negative controls.

PowerShell 5.1 and 7.6.5 capture regressions passed: 22 existing adversarial
controls, 10 glob controls, 17 read-slice controls, durable replay without native
files, explicit-path glob/read capture, offset/CRLF reads, scorer integration,
and unchanged raw traces. Full `npm test` benchmark regressions and the final
soak passed. Packaged skill drift is zero; definitions validate 28 cases,
68 campaigns, and 15 fixtures. The modern/legacy MCP SDK 2.0 smoke passed with
186 runtime commands and 189 tools. Generated references are current under the
Windows PowerShell 5.1 generator; the PowerShell 7 checker reports a sort-order
difference (`list-hmis` versus `list-hmi-screens`), not a changed command surface.

Initial test development failures are not passes: a missing constructor argument,
incomplete fake environment identity, a review fixture with no unresolved
dependency, the retained-baseline flag distinction, and a synthetic glob event
missing native input were corrected. Durable failed/passing TRX files remain
under `C:/OpenTiaBench/Artifacts/ott35-preapply-hardening/tests/`.

## Independent live qualification

The route, review, failure, and temporal controls each used a separate restore
of the immutable behavior archive and an owned session, closed without saving.
All used the Release worker built at clean commit `4d8c311`, SHA-256
`7CAD80A11C5DE3A59EAA03FE42DBF036204BE04E20F76D7BA3CE19379A2A9E16`,
after exact-binary Openness allow-list registration. The final pre-commit build
retained that hash. Later native-read changes affect the harness only. A
post-commit metadata rebuild and its exact-binary registration/smoke are tracked
separately in the external `final-build-provenance.json`; they do not relabel
the binary used by the mutating live controls above.

| Control | Observed outcome |
|---|---|
| Wrong capability route | Query and run reject before evidence/transaction creation; transaction list unchanged. |
| Actual impact-review refusal | Expected Failed evidence `4f8843bb300f46e3bab66804cc24c590`; no apply, rollback, cleanup compile, or candidate download. Both cleanup proofs true and draft discarded. |
| Real post-apply compile failure | Expected Failed evidence `7f94e74117e843d6acef1fdcf34eaf9a`; undefined-tag error, all three operations rolled back/restoration verified, cleanup compile clean, no candidate download. |
| Known-good temporal package | Verified evidence `4f76f64b85834a69b9009df6e8eeafa0`; 15 steps, 26 assertions, zero failures, reset and cleanup proven. |

Main, declared-owner inventories, and clean compilation were checked after the
controls. The first purported review-refusal fixture had no unresolved
dependency: it genuinely Verified (`5a3c7e380de94b92a7031b203f9577ba`) and its
helper failed the expected-Failed check. That is not counted as review-refusal
qualification. A separate source copy with a neutral unresolved dependency
provided the actual negative control; the failed helper and original evidence
are retained.

Fresh-session health passed on the restored project: Main only, zero external
sources, compilation 0 errors/0 warnings, online comparison zero differences,
and explicit unsaved close followed by zero open projects. An earlier CLI
attempt used the unsupported `go-offline` command and failed; the supported
rerun is retained separately as `fresh-health-cli-rerun.log`. Runtime tag count
is two, not zero; cleanup checks absence of the declared temporary owners.

## Flash High trial and append-only replay

Fresh Flash High and full-model High fallback definitions are separate cohorts
from the paused diagnostic campaign and preserve the three-trial budget.
Only **Flash High trial 1** was launched after live qualification. OpenCode was
pinned to 1.18.10, model `zai-coding-plan/glm-5.3-flash`, variant/effort `high`,
with provider-scoped subscription credentials. No rate-limit/provider failure
was observed. No full-model GLM or Sol run was launched in this pass.

The trial ended at the actual 1800.487-second deadline (exit 124), not an operator
stop. It reported 29,553 reasoning tokens and request cost 0 under the user's
subscription; this does not mean free compute or include subscription cost.
There was no successful runtime behavior proof. One guarded probe Failed with
both cleanup proofs true, evidence `293b0f4028fe4606ae60ffbc2d148e8a`:
Siemens source generation rejected `VERSION` in an instance-DB header. The model
received the diagnostic, edited again, but did not complete another probe.
Post-trial fresh-session health again proved the Main hash, owner absence,
clean compilation, and zero online differences before unsaved close.

Original raw result: 15/100, harness-invalid because a native `read` requested
`limit=3` but supplied slice/continuation metadata rather than an outputPath.
The batch remains `paused-harness-error`, zero accepted and three pending;
its original trace, metadata, result, and checkpoint were not rewritten.

After testing the narrow read-slice correction, an append-only copy was rescored
under `model-read-slice-replay/`: **15/100, validForModelComparison=true**,
no executionFailure, same genuine timeout and usage. Replay metadata records
original hashes, case hash, `freshModelRun=false`, and
`originalOutcomeRewritten=false`. This is diagnostic evidence for one timed-out
attempt, not a completed three-trial campaign or a model ranking.

Source trace SHA-256:
`AD5CD0574FA5B9ECA76A95290DE2BF7E886006A5C95795B884654A3DAD88CDC3`.
Historical capped-glob classification was also replay-checked without changing
its older trace or rescoring its historical result.

## Isolation and retained evidence

Evidence root: `C:/OpenTiaBench/Artifacts/ott35-preapply-hardening/`.
It contains before/after inventories, MCP transcripts, probe reports, TRX files,
fresh-session health logs, replay provenance, and independent cleanup audit.
The model batch root is
`C:/OpenTiaBench/Artifacts/ott35-models/preapply-fixed/20261003T135519953Z-batch-plc-behavior-preapply-glm-5-3-flash-high-20261003/`.

Final immutable input/hash checks:

- Behavior archive (5,633,615 bytes, read-only):
  `D2A66124EC3269132DAD9FF5149DA0E73E8C6EA2897E9A62DDA80F7E9F3017FE`.
- Parent archive:
  `1C17F223F00F0A43EFD5E51A7C8E2DBE4DD424540AE0C9A799F102997205AB74`.
- Frozen acceptance vector:
  `1D6EF01BFD7C8B8FF2BFACB7136AEAB3B80DE08CF2247A5C2BE48455ECE66724`.
- Main canonical document:
  `6B6CAE0B8130241248E16ADA2328E1DB0586ED00679BF236ECCB03A9FA066518`.
- Unchanged skills tree: `4287f2a503a25a0f1f0eacdb18831c428b25bc6c`.

The owned simulator `OTT35_Bench` was identity-checked, powered off, and
unregistered; its lease PID 48252 and empty campaign TIA PID 2516/children were
stopped. The first teardown helper's immediate process check raced asynchronous
exit and is not reported as a passed helper. Subsequent independent read-only
verification recorded zero registered simulators and no owned TIA/lease process
in `cleanup-audit.json`. Original TIA PID 15780 still has the original preflight
project open; its children and unrelated Debug MCP/worker PIDs 32920/47736 were
preserved. Temporary provider auth is absent; no credential values are retained
in the report.

## Next gate

Hold further trials for a separately authorized, generic native instance-DB
guidance pass. The model loaded the intended skills but spent much of its trial
guessing headers. The declaration catalog mentions FB coupling without showing
native instance-DB metadata ordering or the toolkit's exact same-source FB/TYPE
binding requirement. A neutral compiler-qualified FB/instance-DB declaration
example and explicit create-only/same-source constraints are a reasonable next
candidate; no timer/interlock logic, benchmark names, acceptance values, or
frozen vector should enter the skill. This single attempt does not prove that
documentation alone will improve the model. Skills and campaign outcomes remain
unchanged pending that next slice.
