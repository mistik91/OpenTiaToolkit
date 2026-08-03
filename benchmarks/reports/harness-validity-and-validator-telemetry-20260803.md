# PASS - benchmark validity and validator telemetry

## Outcome

The benchmark harness now separates endpoint/runtime validity from model task
quality and records hardware-patch validator interventions as structured
evidence. Raw scores and costs remain auditable, while terminal provider errors
no longer contaminate model score aggregates.

## Validity policy

Each case result records `validForModelComparison` and a nullable
`executionFailure` with a stable kind, optional HTTP status, and sanitized
message.

- A terminal provider error, nonzero process exit without a final stop,
  non-provider runtime error, or exit-0 incomplete trace is invalid for model
  comparison.
- A versioned case timeout remains a valid negative model outcome because
  completion within the case budget is part of model capability.
- Invalid trials retain their raw score, duration, token usage, and cost.
- Valid-only aggregates expose valid/invalid/endpoint-invalid counts and keep a
  separate raw mean. When no valid trial exists, comparison means are null.
- Campaign, recovery, and merge paths use the same status logic; a model with a
  terminal provider failure is `endpoint-invalid`, not critically
  disqualified by the task scorer.

Historical schema-v2 results without the new fields remain valid and default
to comparison-valid when recovered or merged.

## Validator telemetry

Each case result now includes `validatorInterventions`:

- blocked call and issue counts;
- stable issue-code counts;
- per-call hardware-patch index and operation count;
- issue code, operation index, and optional field;
- whether rejection happened before TIA/transaction entry; and
- whether the immediately following patch cleared each indexed diagnostic.

Suite and campaign summaries retain compact blocked-call and issue totals while
the full case result preserves per-call detail.

## Immutable-trace replay

No model was rerun for this proof. Existing V4 traces were re-evaluated into
ignored `result.reclassified.json` artifacts.

| Campaign | Trial | Raw score | Comparison-valid | Failure | Validator calls/issues/corrected |
| --- | ---: | ---: | :---: | --- | --- |
| V4 Free | 1 | 0 | no | provider 503 | 0 / 0 / 0 |
| V4 Free | 2 | 10 | no | provider 503 | 0 / 0 / 0 |
| V4 Free | 3 | 10 | no | provider 503 | 1 / 2 / 1 |
| Paid V4 `xhigh` | 1 | 55 | yes | none | 2 / 6 / 2 |
| Paid V4 `xhigh` | 2 | 55 | yes | none | 1 / 2 / 1 |
| Paid V4 `xhigh` | 3 | 75 | yes | none | 1 / 2 / 1 |

The V4 Free aggregate is now three recorded trials, zero valid trials, three
endpoint-invalid trials, null comparison mean, and raw mean 6.67. The paid
scores remain 55/55/75 and expose four pre-TIA blocked calls, ten issues, and
four immediate corrections without manual trace parsing.

## Validation

- Pinned benchmark suite: all definition, common-helper, evaluator, case
  assertion, merge, and recovery tests passed.
- New synthetic coverage proves provider-503 invalidation, timeout validity,
  valid-only aggregation, backward-compatible merge/recovery, and multi-issue
  validator correction telemetry.
- Release solution build: 0 warnings, 0 errors.
- Non-integration tests: 641 passed.
- Architecture tests: 54 passed.
- Generated command reference: 182 commands, current.
- Stable MCP 2.0 stdio smoke: 185 tools exposed, including 182 runtime and 3
  infrastructure tools.
- Modified PowerShell files and JSON schemas parse cleanly; `git diff --check`
  passed.
