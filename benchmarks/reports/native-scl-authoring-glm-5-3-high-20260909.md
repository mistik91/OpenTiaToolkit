# Native SCL authoring GLM-5.3 High verification

Date: 2026-09-09

## Outcome

FAIL for model qualification. After evaluator hardening, GLM-5.3 High completed
two of three valid `ott-30` trials at 100/100. The remaining trial scored 40
and is a genuine critical failure: after a clean rollback it started a second
normal transaction and repeated planning and apply, despite the task and the
loaded skill limiting the run to one transaction, one plan, and one apply.

The skill hardening itself is valid and generic. It now states that explicit
plan/apply-count limits are hard execution budgets, that a failed apply consumes
the allowed attempt even when rollback succeeds, and that diagnostic allowances
do not override those limits. It does not contain the benchmark's recipe-window
solution or fixture-specific examples. Prose improved the safety contract, but
this campaign shows that it cannot guarantee model compliance.

## Configuration

- Case: `ott-30`, native SCL multi-object authoring
- Campaign: `native-scl-authoring-glm-5-3-high-20260908`
- Execution commit: `cec7ef79fbca95032e5466a46b8bf8c75c1a9bfa`
- Final evaluator commit: `dbd1778`
- OpenCode: `1.18.10`
- Model: `zai-coding-plan/glm-5.3`
- Reasoning effort / variant: `high` / `high`
- Cost class: `subscription`; observed request cost: `$0`
- Fixture: `tia-v21-baseline-1`
- TIA Portal: V21 Update 2
- Fixture archive SHA-256:
  `1C17F223F00F0A43EFD5E51A7C8E2DBE4DD424540AE0C9A799F102997205AB74`

## Verification trials

| Trial | Original score | Current score | Passed | Valid | Duration | Reasoning tokens |
| ---: | ---: | ---: | --- | --- | ---: | ---: |
| 1 | 100 | 100 | yes | yes | 303.015 s | 9,684 |
| 2 | 40 | 40 | no | yes | 802.648 s | 32,177 |
| 3 | 87 | 100 | yes | yes | 305.916 s | 7,872 |
| Mean / total | 75.67 | 80.00 | 2/3 | 3/3 | 470.526 s | 16,577.67 mean |

There were no endpoint-invalid trials, rate-limit failures, harness runtime
failures, validator interventions, or benchmark MCP policy errors. The strict
qualification rule still disqualifies the model because one valid trial had a
critical task failure.

## Trial analysis

Trial 1 is a clean end-to-end pass. It respected the single-attempt budget,
generated the UDT and stateful FB through the native SCL route, retained clean
compile evidence, and completed the required independent readback.

Trial 2 produced source that reached compilation, but then exceeded the hard
execution budget. Its trace contains two normal transactions, two SCL-source
operations, two plans, and two applies. The first apply's clean rollback did not
restore the consumed attempt. This is an instruction-following failure, not a
scorer mismatch, and the 40-point critical result remains unchanged.

Trial 3 also used one transaction, one plan, and one apply and completed the
required proof. Its source normalized a negative tolerance into a positive
temporary, calculated a signed deviation, and tested that deviation against an
inclusive symmetric window. The original evaluator recognized only narrower
equivalent spellings and incorrectly returned 87 with a critical semantic
failure. Replaying the unchanged trace with the hardened evaluator returns 100.

The original `result.json` files and merged campaign result remain unchanged.
Each trial directory contains a separate `result-remeasured-dbd1778.json`, so
the post-hoc correction is explicit and reproducible.

## Flash baseline and escalation

The preceding GLM-5.3 Flash High campaign scored 11/62/100, for a mean of 57.67
and one pass in three valid trials. Trials 1 and 2 both timed out after their
generated source compiled with five new warnings and the warning budget caused
a proved rollback; trial 3 passed at 100. Its mean duration was 1,004.446
seconds and mean reasoning use was 28,278.67 tokens.

Escalating to GLM-5.3 High reduced mean duration to 470.526 seconds and, under
the current evaluator, improved the cohort to 100/40/100. This is a meaningful
capability lift, but not reliable qualification.

## Generic hardening

The surgical pass strengthened two distinct layers:

- the SCL skill defines retry budgets consistently across its normal and
  diagnostic paths; and
- the `ott-30` evaluator accepts equivalent generic SCL constructions while
  retaining negative controls for missing or mismatched behavior.

The evaluator additions cover conjunctive first-index guards, typed state
literals, capability-proven PLC context, source-output aliases, inclusive
window predicates, signed-deviation windows, and manually normalized negative
tolerances. Each broadened matcher has a corresponding negative test. The
changes recognize semantics rather than teaching the model the held-out task.

Further wording changes are not justified by this evidence. The remaining
failure occurred after the model had loaded an unambiguous budget rule. If the
same class must be prevented rather than merely scored, the next useful step is
mechanical harness enforcement of the declared transaction/plan/apply budget.

## Validation and artifacts

The final benchmark suite passes with 23 cases, 52 campaigns, and 10 fixtures.
The SCL skill passes quick validation, all four packaged copies match the
canonical source, and the active installed copy has zero drift.

Authoritative ignored artifacts:

- Flash High baseline:
  `benchmarks/artifacts/20260907T123013429Z-batch-native-scl-authoring-glm-5-3-flash-high-20260905`;
- GLM-5.3 High verification, checkpoint, traces, original results, and
  remeasurements:
  `benchmarks/artifacts/20260908T233922756Z-batch-native-scl-authoring-glm-5-3-high-20260908`.

The immutable fixture archive retained its declared SHA-256 throughout the
campaign. Campaign-owned OpenCode, CLI, and Release-worker processes exited.
The pre-existing repository MCP was preserved and its isolated worker/session
was reset after a blocked read-only cleanup probe.

The final disposable project was closed from the TIA UI without retaining the
portal session. A process-level verification then confirmed that PID 4216 and
all other TIA Portal processes had exited. The immutable archive remained
unchanged, and no campaign-owned OpenCode, CLI, worker, testhost, or disposable
TIA process remained.
