# OTT-36 portable blueprint schema: fresh Flash High rerun

## Outcome

**3/3 qualified model trials, each 100/100**, at frozen inference/evaluation
revision `ddb4b35c00970f1e4e6577b9a4a71ef6a34e70d2`, on
`codex/ott-36-blueprint-benchmark`. Each trial used a fresh disposable restore,
read the bundled schema, validated all three blueprints without a format repair,
and passed a separate live final-state readback with **0 compile errors / warnings**.
No GLM-5.3 or Sol fallback was needed. No provider failure or rate-limit signal
appeared. Nothing was pushed or merged.

This completes the fresh campaign requested after the
[portable-skill review gate](ott-36-blueprint-flash-high-skill-review-20261004.md).
That historical two-trial checkpoint and its scorer replays remain unchanged;
the new campaign has its own checkpoint and immutable captures.

## Generic skill change

The skill previously referred to repository-root `schemas/` and `examples/`
resources absent from isolated skill installations. The fix:

- Bundles the existing `engineering-blueprint.schema.json` unchanged under the
  canonical skill's `references/` directory and all four shipped plugin variants.
- Links the real skill-local file and distinguishes blueprint step `arguments`
  from standalone command envelopes using `args`. Domain parameters still come
  from the active command schemas.
- Checks source/bundle byte equivalence and the skill-local link before skill
  synchronization. This catches an identically broken canonical/package set,
  not just differences between copies. CI runs the focused regressions too.

No case-specific names, addresses, comments, step recipes or solution examples
were added to the skill. The public task, acceptance contract, oracle, runtime
binaries and other skills were not changed for this rerun. The skill-creation
workflow kept the fix resource-focused; the development workflow required the
portable-resource tests and packaging checks before model inference. No installed
blueprint skill copy existed to refresh globally; the harness staged the frozen
canonical skill into each isolated trial workspace.

## Model, effort, duration and cost

OpenCode **1.18.10**, `zai-coding-plan/glm-5.3-flash`, declared reasoning effort
**high**, variant **high**, provider-scoped z.ai subscription authentication.
Batch size was **one trial**, with an independent readback between batches.
All three attempts were accepted on their first execution.

| Trial | Score | Duration, seconds | Reported task USD | Blueprint validation errors |
| --- | --- | --- | --- | --- |
| 1 | 100 | 625.475 | 0 | 0 |
| 2 | 100 | 570.997 | 0 | 0 |
| 3 | 100 | 430.038 | 0 | 0 |

Mean duration **542.170 seconds (9.04 minutes)**; median **570.997 seconds**.
Total reported task cost **$0**, excluding amortized subscription cost. This is
not a claim of a free API model; no OpenRouter balance was used.

| Trial | Input tokens | Output tokens | Reasoning tokens |
| --- | --- | --- | --- |
| 1 | 127,313 | 7,615 | 8,611 |
| 2 | 86,337 | 6,474 | 7,239 |
| 3 | 81,368 | 5,963 | 4,147 |

These are OpenCode's separate counters; its total counter includes cache
accounting and is not their sum.

## What changed in observed behavior

The earlier two captures each needed format repair: three rejected
`validate-blueprint` calls in total, plus an empty `Arguments` parse in each
trace caused by authoring `args`. Both eventually reached the correct state and
score 100 under the corrected semantic oracle. In this fresh campaign all
**nine validations succeeded with populated arguments**, and all three trials
actually read the skill-local schema once.

Observed accuracy is therefore unchanged, while the schema-discovery/format
problem did not recur. This is a small, unpaired two-before/three-after comparison,
not a statistical estimate of the skill's effect. The new mean is approximately
**5.4% slower** than the historical 514.419-second mean; no speed gain is claimed.

Trial 2 overlapped read-only PLC/HMI discovery once, encountered the existing
worker `Conflict` guard, and recovered serially. Trials 1 and 3 did not. Serial
TIA operation guidance already existed and was unchanged; this lapse does not
justify another task-shaped skill instruction.

Each trace contains the four **expected** blueprint rejections: stale context,
honest partial failure, failed-token replay and successful-token replay. These
are deliberate lifecycle controls, not format or provider failures.

## Independent verification and validation

The unchanged structured oracle links each authentic blueprint, plan, token and
step result with native phase-specific evidence: no stale mutation, retained
prefix, blocked HMI, stopped sentinel suffix, one-use tokens and fresh recovery.
Separate Release MCP connections then checked the current project under that
trial's exact restore root, the full two-tag CSV inventory, final HMI text and
preserved geometry, canonical Main engineering content, and clean PLC compile.
All three independent checks passed before the next restore or final cleanup.

Before inference: focused resource tests passed in Windows PowerShell 5.1 and
PowerShell 7 (canonical plus four packages, isolated installed-style positive,
missing bundle, changed bundle, changed source and repository-only link negatives).
Skill `quick_validate.py`, full 18-skill sync/drift check, Agent Plugin/launcher
validation, generated 186-command reference check, full `npm test` benchmark
suite including 54 blueprint adversarial controls, and `git diff --check` passed.
This skill/resource-only change did not rebuild the already-qualified worker;
its exact Siemens V21 registration was checked before opening the campaign portal.

The keeper discovered zero existing TIA instances and created owned PID **1160**
(MCP PID **9164**). After the third independent readback, it closed the project
without saving, closed its portal and exited. Those owned PIDs are gone. The
pre-existing app-owned Debug MCP/worker remained untouched, and the immutable
fixture archive hash was rechecked unchanged. Disposable restores and evidence
remain on disk; no user project was modified, saved or downloaded.

## Evidence

Local campaign root:
`C:/OpenTiaBench/Artifacts/ott36-schema-flash-high-20261004/`.
Checkpoint and merged result are under
`20261004T210043440Z-batch-blueprint-lifecycle-glm-5-3-flash-high-20261004/`.
Each segment retains raw `events.jsonl`, `metadata.json`, `result.json`, stdout,
stderr and isolated workspace artifacts. No original capture was rewritten.

Independent readback roots:
`C:/OpenTiaBench/Artifacts/ott36-schema-flash-t{1,2,3}-independent-20261004/`.
Keeper journal, initial-instance inventory and cleanup:
`C:/OpenTiaBench/Artifacts/ott36-schema-keeper-20261004/`.

`evaluation-provenance.json` in the campaign root binds the frozen revision,
task/oracle/skill/schema sources, Release binaries, fixture, all three raw captures,
their staged skill resources, independent journals/readbacks and final checkpoint.
Its SHA-256 is `D6DF7F2A3C19B129477C4156D6EF14D675846BCABDAB258C40CB31C9A0003B53`.
The frozen acceptance contract remains
`868f51f18fba253c4c89858dc000aabc850c34e12bf9c84803f5cc8df035381e`.

OTT-36 is qualified for this Flash High checkpoint. Further skill tuning is not
supported by these captures; the surgical portable-schema fix is sufficient.
