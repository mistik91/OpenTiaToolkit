# OTT-35 pre-apply and output-capture hardening — 2026-10-03

## Outcome: implementation qualified; live/model validation pending

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

Native output capture recognizes only the observed OpenCode built-in `glob`
completed listing cap (`count=100`, boolean `truncated=true`, no outputPath and
no byte-truncation/file marker). It leaves that trace content unchanged. MCP,
unknown tools, other metadata shapes, and any explicit outputPath still require
the existing call-bound, isolated-path, size/hash/prefix-verified capture.

## Validation checkpoint

Release solution build: zero warnings/errors. The first full run passed 730 unit
and 54 architecture tests; an additional null-journal negative control is being
included in the final rerun. Focused pre-apply tests cover route/type/group
rejection before simulator access, failure after one draft operation, planning
failure, impact review refusal, persisted cleanup/recovery, and missing or
write-ahead/inconsistent manifests. No import, runtime execution, or real
download is permitted by those negative controls.

PowerShell 5 and 7 capture regression passed: 22 existing adversarial controls,
10 semantic-cap controls, durable replay without native files, explicit-path
glob capture, scorer integration, and unchanged raw traces. Full benchmark
regressions passed; the final added semantic-glob scorer test also passed in
both shells. Command catalog remains 186, packaged skill drift zero; definitions
validate 28 cases, 68 campaigns, 15 fixtures.

Initial test development failures are not passes: a missing constructor argument,
incomplete fake environment identity, a review fixture with no unresolved
dependency, the retained-baseline flag distinction, and a synthetic glob event
missing native input were corrected. Durable failed/passing TRX files remain
under `E:/Documents/OpenTiaBench/Artifacts/ott35-preapply-hardening/tests/`.

Fresh Flash High and full-model High fallback definitions are separate cohorts
from the paused diagnostic campaign and preserve the frozen three-trial budget.
Live route rejection, review refusal, real post-apply failure, known-good temporal
execution, and fresh-session health checks must pass before Flash is launched.

The original TIA PID 15780, its children, and unrelated chat-managed Debug workers
are preserved. No owned campaign or simulator was active at this checkpoint.
