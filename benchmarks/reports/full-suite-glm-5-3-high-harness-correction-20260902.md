# GLM-5.3 High full-suite harness correction

Date: 2026-09-02

## Decision

The original `ott-08` results from the 21-trial GLM-5.3 High full-suite
campaign were contaminated by a benchmark MCP file-policy mismatch and must
not be used for model comparison. The harness now grants the MCP access to the
exact isolated trial workspace, proves that access with a raw MCP preflight
before inference, and classifies any future rejection of its own workspace as
an invalid `harness-error`.

The unchanged `ott-08` case was rerun three times after the repair. All three
trials scored 100, passed, and were valid for comparison. Replacing only the
three contaminated rows raises the full-suite mean from 90.24 to 97.14 and the
pass count from 17/21 to 20/21. The remaining failed trial is the pre-existing
critical failure in `ott-10` trial 3, so the corrected model status remains
`disqualified-critical-failure`.

## Root cause and repair

OpenCode ran each trial in `C:\OTT\Bench\<run>\workspace`, while the benchmark
MCP inherited allowed roots that did not contain that workspace. The three old
`ott-08` traces therefore recorded 3, 4, and 3 `PolicyDenied` tool failures;
trial 2 subsequently timed out. None of those trials reached the intended
transaction plan/apply sequence.

The repair at commit `1b37654b6cd0c9a21248ca6172a9ece8bf377ae9`:

- scopes `OPEN_TIA_MCP_ALLOWED_ROOTS` to the exact trial workspace plus the
  OpenTiaToolkit temporary staging directory;
- calls `validate-plc-code-artifact` through raw MCP stdio on a probe inside
  that workspace before starting OpenCode;
- marks an own-workspace policy rejection as `harness-error`, invalid for
  model comparison, and pauses checkpoint batching as
  `paused-harness-error`; and
- continues to score a model-selected path outside the declared workspace as
  an ordinary model outcome.

This preserves the file-policy boundary and does not add task examples,
answers, scorer exceptions, or extra model-visible guidance.

## Corrected `ott-08` trials

Model: `zai-coding-plan/glm-5.3`

Reasoning effort / OpenCode variant: `high` / `high`

OpenCode: `1.18.10`

| Trial | Score | Passed | Valid | Duration | Reasoning tokens | Harness cost |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 100 | yes | yes | 347.872 s | 2,500 | $0 |
| 2 | 100 | yes | yes | 232.227 s | 2,521 | $0 |
| 3 | 100 | yes | yes | 244.668 s | 2,107 | $0 |
| Mean / total | 100 | 3/3 | 3/3 | 274.922 s | 2,376 mean | $0 total |

The Z.AI subscription route exposes zero per-request price to OpenCode, so
`$0` means no metered cost was visible to the harness; it is not an estimate
of the subscription's underlying economic cost.

## Corrected 21-trial aggregate

| Metric | Original merge | Corrected merge |
| --- | ---: | ---: |
| Mean score | 90.24 | 97.14 |
| Passed trials | 17/21 | 20/21 |
| Runtime failures | 1 | 0 |
| Critical failures | 4 | 1 |
| Corrected pass rate | n/a | 95.24% |
| Model status | `runtime-failure` | `disqualified-critical-failure` |

The corrected merge contains the 18 unaffected source results from the
original campaign and the three fresh `ott-08` results. The original merge is
preserved unchanged for audit; it should be read as a contaminated historical
artifact, not as the current model result.

## Validation, artifacts, and cleanup

Validation completed before the rerun:

- focused common-helper, trace-evaluator, and batch-checkpoint tests;
- the full benchmark test suite: 20 cases, 45 campaigns, and 8 fixtures; and
- a live raw-MCP positive preflight against the Release MCP executable.

Authoritative ignored artifacts:

- original batch and immutable contaminated merge:
  `benchmarks/artifacts/20260902T033104024Z-batch-full-suite-baseline-glm-5-3-high-20260902`;
- corrected three-trial batch:
  `benchmarks/artifacts/20260902T100206130Z-batch-full-suite-baseline-glm-5-3-high-20260902`;
- corrected aggregate:
  `benchmarks/artifacts/20260902T033104024Z-batch-full-suite-baseline-glm-5-3-high-20260902/merged-campaign-result-corrected.json`.

The final project was closed with `save=false`. The campaign-owned TIA V21
process tree was stopped, and the three exact restore roots were moved to the
Windows Recycle Bin. The pre-existing MCP and worker processes were left
untouched. The fixture archive retained SHA-256
`1C17F223F00F0A43EFD5E51A7C8E2DBE4DD424540AE0C9A799F102997205AB74`.
