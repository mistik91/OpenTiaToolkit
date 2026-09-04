# GLM-5.3 High Unified alarm reconciliation

Date: 2026-09-04

## Outcome

GLM-5.3 High completed all three `ott-28` trials from independent restores and
performed every required Unified discrete-alarm operation correctly. The raw
campaign scored 100, 97, and 97 because the original tag-discovery assertion
accepted only one full-scan call shape. Trials 2 and 3 instead used successful
exact-path reads for all three required internal HMI tags.

Commit `e58ba08` corrects the assertion to score the returned tag evidence
rather than prescribe full-scan versus exact-path discovery. Re-evaluating the
unchanged traces produces 100/100/100 and a 3/3 pass rate. The original result
files remain unchanged for audit.

DeepSeek V4 Flash Free was attempted first at maximum effort. A refreshed
OpenCode catalog did not contain `opencode/deepseek-v4-flash-free`, so
preflight stopped before model inference or TIA access. Its frozen campaign is
retained for a later availability retry.

## Configuration

- OpenCode: `1.18.10`
- Model: `zai-coding-plan/glm-5.3`
- Reasoning effort / variant: `high` / `high`
- Cost class: `subscription`
- Campaign source commit: `6445f8501d1e3b5658f96b3de49610bcb7d1b589`
- Case: `ott-28`
- Fixture: `tia-v21-unified-alarms-8`
- TIA Portal: V21 Update 2
- Fixture archive SHA-256:
  `316B538B73F1DF41FBC07DBD0DA5963F039E552568EB1984604FE854DBE9BD52`

The catalog reported the model active with tool calling, variants `low`,
`high`, and `max`, and zero request pricing through the Z.AI subscription
route. Zero harness cost is not an estimate of the subscription's underlying
economic cost.

## Results

| Trial | Raw score | Adjudicated score | Passed | Duration | Reasoning tokens | Harness cost |
| ---: | ---: | ---: | --- | ---: | ---: | ---: |
| 1 | 100 | 100 | yes | 543.734 s | 2,806 | $0 |
| 2 | 97 | 100 | yes | 524.391 s | 2,937 | $0 |
| 3 | 97 | 100 | yes | 177.823 s | 2,833 | $0 |
| Mean / total | 98 raw | 100 adjudicated | 3/3 | 415.316 s | 2,858.67 mean | $0 total |

All three trials were valid for model comparison. There were no timeouts,
provider failures, rate limits, validator interventions, or benchmark MCP
policy failures.

## Assertion correction

The original critical assertion required exactly one `list-hmi-tags` call
with both `allow-full-scan=true` and `include-details=true`. That was stricter
than the task outcome and rejected semantically equivalent discovery:

- trial 1 recovered from an unsupported `filter` attempt by using one complete
  scan and passed the original assertion;
- trials 2 and 3 recovered by reading `MixerAlarmBits`, `MixerSpeed`, and
  `BatchNumber` individually with `root-path` and returned the required names
  and data types; and
- every trial subsequently created and referenced the correct internal HMI
  tags, passed the atomic alarm preview/apply checks, verified fresh canonical
  state, retired only the exact legacy alarm, and preserved the near-name
  distractor.

The corrected assertion still requires at least one `list-hmi-tags` call for
the exact HMI and returned evidence for all three tag names and data types,
including the fixture table for `MixerAlarmBits`. New regressions prove both a
complete targeted-read sequence and rejection when one required tag is absent.
It adds no task example or answer to a model-visible skill or prompt.

All three traces first attempted the unsupported `filter` argument and then
self-corrected. That repeated inefficiency is a reasonable candidate for a
future generic skill A/B evaluation, but it did not compromise correctness and
no skill was changed during this campaign.

## Validation, artifacts, and cleanup

The full benchmark suite passed after the correction: 21 cases, 47 campaigns,
9 fixtures, and every focused evaluator and batch-checkpoint regression.

Authoritative ignored artifacts:

- V4-free availability preflight:
  `benchmarks/artifacts/20260904T114038096Z-campaign-hmi-alarm-reconciliation-deepseek-v4-flash-free-max-20260904`;
- GLM preflight:
  `benchmarks/artifacts/20260904T114123184Z-campaign-hmi-alarm-reconciliation-glm-5-3-high-20260904`;
- completed three-trial batch and immutable raw merge:
  `benchmarks/artifacts/20260904T114202043Z-batch-hmi-alarm-reconciliation-glm-5-3-high-20260904`; and
- each trial directory in that batch contains `adjudicated-result.json`
  produced from its unchanged `events.jsonl` and `metadata.json`.

The final disposable project was closed without saving the model mutations,
and PID 13856 reported no open project. The isolated OpenCode, MCP, and worker
processes exited; the pre-existing MCP/worker pair and TIA's own child
processes were preserved. The trial-1 and trial-2 restore roots were moved to
the Windows Recycle Bin. Recycle Bin cleanup rejected the trial-3 path, and a
subsequent direct recursive deletion was blocked before execution by the host
policy, so that one closed restore root remains under `C:\OTT\Bench`. The
read-only fixture archive retained its recorded size and SHA-256.
