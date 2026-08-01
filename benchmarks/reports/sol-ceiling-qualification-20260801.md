# GPT-5.6 Sol ceiling qualification — 2026-08-01

## Outcome

GPT-5.6 Sol passed 8 of 9 trials after correcting one proven evaluator format
mismatch. It was perfect on discovery, completed all three guarded PLC tag
mutations, and passed two of three hardware-collision trials. The remaining
failure was a genuine 420-second timeout after excessive hardware operation
schema guessing.

This campaign is not saturated: `ott-14` still exposed a repeatable
skill/tool-discoverability problem, while `ott-03` distinguished strict workflow
compliance from successful engineering work.

## Reproduction identity

- Recorded run commit: `48f2518bc81a24991527f382b2cf45712f5eaeb7`
- Branch: `experiment/opencode-benchmark-slice`
- Git state during every trial: clean
- OpenCode: pinned `1.18.10`
- Model: `openai/gpt-5.6-sol`
- Reasoning effort: `max`
- OpenCode variant: `max`
- Provider/authentication: OpenAI OAuth
- Cost class: `subscription`
- TIA Portal: V21 Update 2
- Fixture SHA-256:
  `1C17F223F00F0A43EFD5E51A7C8E2DBE4DD424540AE0C9A799F102997205AB74`
- Trials: 3 per case, 9 total
- Campaign duration: 1,769.4 seconds (29m 29.4s)
- Raw artifact directory:
  `benchmarks/artifacts/20260801T012948857Z-campaign-sol-ceiling-qualification`

## Corrected results

| Case | Passes | Scores | Mean score | Mean duration | Mean reasoning tokens | Recorded task cost |
|---|---:|---|---:|---:|---:|---:|
| `ott-01` | 3/3 | 100, 100, 100 | 100.00 | 112.586 s | 1,459.67 | $0.000000 (subscription) |
| `ott-03` | 3/3 | 100, 90, 90 | 93.33 | 154.697 s | 1,505.00 | $0.000000 (subscription) |
| `ott-14` | 2/3 | 100, 100, 95 | 98.33 | 302.320 s | 4,808.67 | $0.000000 (subscription) |
| **Overall** | **8/9** |  | **97.22** | **189.868 s** | **2,591.11** | **$0.000000 (subscription)** |

Per-trial detail:

| Case | Trial | Score | Pass | Duration | Reasoning tokens | Recorded cost |
|---|---:|---:|---|---:|---:|---:|
| `ott-01` | 1 | 100 | yes | 133.771 s | 1,436 | $0.000000 |
| `ott-01` | 2 | 100 | yes | 68.061 s | 920 | $0.000000 |
| `ott-01` | 3 | 100 | yes | 135.926 s | 2,023 | $0.000000 |
| `ott-03` | 1 | 100 | yes | 161.748 s | 1,684 | $0.000000 |
| `ott-03` | 2 | 90 | yes | 147.422 s | 1,229 | $0.000000 |
| `ott-03` | 3 | 90 | yes | 154.922 s | 1,602 | $0.000000 |
| `ott-14` | 1 | 100 | yes | 241.257 s | 4,050 | $0.000000 |
| `ott-14` | 2 | 100 | yes | 245.439 s | 3,710 | $0.000000 |
| `ott-14` | 3 | 95 | no — timeout | 420.263 s | 6,666 | $0.000000 |

OpenCode's model catalog and every trace reported zero token cost for this OAuth
subscription model. Therefore `$0.000000` means no metered marginal charge was
reported for the task; it does **not** mean the model is free. OpenCode does not
expose subscription price, consumed quota, or an amortized per-task cost, so
those values cannot be derived by this harness.

## Evaluator correction

The completed campaign initially reported 6/9 passes and a 92.22 mean score.
That aggregate was provisional because all three `ott-03` trials lost the same
15-point readback assertion despite making a post-apply `export-tags` call whose
structured result proved the exact final tag state:

```text
Default tag table / OccupiedBefore / Bool / %M99.7 /
Updated by benchmark OTT-03
```

The assertion recognized only the CSV representation. The MCP tool returned
equivalent inline JSON. The corrected, format-neutral assertion accepts either
representation and still requires at least two `export-tags` calls, preserving
the requirement for a baseline read followed by independent readback. A focused
regression test covers JSON, CSV, and rejection of a single-read trace.

The nine original event streams were replayed against the corrected rubric;
the model was not rerun and the original result files were not overwritten.
Only `ott-03` changed:

| Trial | Original | Corrected | Reason |
|---:|---:|---:|---|
| 1 | 85, pass | 100, pass | Correct structured readback restored 15 points |
| 2 | 75, fail | 90, pass | Correct readback restored 15 points; `open-plc` remained absent |
| 3 | 75, fail | 90, pass | Correct readback restored 15 points; `open-plc` remained absent |

The `open-plc` deduction remains unchanged. Trials 2 and 3 successfully used
the PLC-scoped commands without that explicit workflow step, so the deduction
measures skill/process compliance rather than a failed engineering outcome.

## Case interpretation

### `ott-01`

Discovery appears solved for this model/configuration: all three trials scored
100. More repetitions are not a priority until the benchmark adds harder
discovery ambiguity.

### `ott-03`

All three trials safely previewed, applied, read back, and compiled the tag
change. Two skipped `open-plc`, leaving useful headroom for stricter workflow
compliance while confirming the mutation task itself was reliable.

### `ott-14`

The prior gold audit proved that the live fixture and expected Siemens response
are correct. Sol then passed twice, but required six hardware patch attempts in
each successful run before discovering `set-io-address-start`.

The third trial made 35 patch attempts with many guessed operation names. It
eventually reached the correct guarded dry-run, received the expected
`already being used` / next-free-address `66` result, and did not apply the
mutation. It then timed out during final readback before producing a completed
answer. Its 95 score does not override the critical runtime failure.

This is strong evidence for a later skill benchmark: hold the model and task
constant, then compare the current hardware skill against a version that makes
the exact patch operation vocabulary discoverable. The skill is deliberately
not changed in this model-qualification result.

## Recovery and cleanup

The timed-out final trial left its disposable project open. It was closed with
`save=false`, the benchmark-owned TIA process was stopped, and all nine exact
trial workspaces under `C:\OTT\Bench` were removed. Those disposable copies are
not recoverable, but their ignored raw traces and results remain in the campaign
artifact directory above. No OpenCode or TIA process remains. The read-only
fixture archive retained its original SHA-256 after the campaign.
