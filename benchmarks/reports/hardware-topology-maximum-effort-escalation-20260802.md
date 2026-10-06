# QUALIFIED - Sol max clears the frozen topology benchmark

## Outcome

The strict maximum-effort escalation reached a qualified model without changing
the case, prompt, evaluator, fixture, or final hardware skill:

| Model/configuration | Scores | Pass | Mean score | Mean duration | Mean reasoning tokens | Recorded cost |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| DeepSeek V4 Flash `xhigh` | 80, 95, 80 | 0/3 | 85.00 | 192.100 s | 5,840.00 | $0.16721925 |
| GPT-5.6 Luna `max` | 20, 20, 20 | 0/3 | 20.00 | 900.204 s | 20,762.67 | $0.22061543 |
| GPT-5.6 Sol `max` | 100, 100, 100 | 3/3 | 100.00 | 317.041 s | 4,408.00 | $0.00000000 (subscription) |

DeepSeek V4 Flash and Luna were disqualified before advancing to the next
stage. Sol passed every critical assertion in all three fresh-restored trials
and is therefore qualified on `ott-22` at `max`.

The two OpenRouter campaigns spent $0.38783468 in total. OpenCode reported zero
monetary request cost for the OpenAI OAuth subscription campaign; that is a
zero metered marginal cost reported by the harness, not a claim that the
subscription is free.

## Frozen evaluation surface

- Recorded run commit: `1052617d8f70960a09f0306cab81e36118490a09`
- Branch: `experiment/opencode-benchmark-slice`
- Git state during every trial: clean
- Git-state SHA-256:
  `B9D712A26B4F49A6CCFE3DAEE6AD8242A25B586A405BDB37BC99111315E56DFC`
- OpenCode: pinned `1.18.10`
- Case: `ott-22`, pass score 85 with critical assertions fail-closed
- Fixture: `tia-v21-topology-6`
- Fixture SHA-256:
  `33193A83BE565BBD63486D66FFDE39404C76D9F8F01178DF28D435DBE3CC5732`
- TIA Portal: V21 Update 2
- Final hardware skill SHA-256:
  `9A6826F13940189D83E79E740C2C8BE71A6A31760E50BE96A77722EA155E4F10`

Canonical, Codex, OpenCode, and Claude hardware skill copies remained
byte-identical. The run added only versioned campaign manifests before live
execution; it did not tune the skill or benchmark against the observed traces.

## Catalog and authentication identity

Each campaign refreshed the OpenCode catalog immediately before execution and
recorded its catalog snapshot.

| Model | Forced effort/variant | Advertised variants | Authentication | Catalog input/output price per million tokens |
| --- | --- | --- | --- | ---: |
| `openrouter/deepseek/deepseek-v4-flash` | `xhigh` | `high`, `xhigh` | OpenRouter API | $0.14 / $0.28 |
| `openrouter/openai/gpt-5.6-luna` | `max` | `none` through `max` | OpenRouter API | $0.10 / $0.60 |
| `openai/gpt-5.6-sol` | `max` | `none` through `max` | OpenAI OAuth | $0.00 / $0.00 subscription accounting |

`xhigh` is the maximum variant advertised for DeepSeek V4 Flash; the model did
not expose a variant named `max`. Luna and Sol both exposed and ran `max`.

## Per-trial results

| Model | Trial | Score | Pass | Runtime result | Duration | Reasoning tokens | Recorded cost |
| --- | ---: | ---: | --- | --- | ---: | ---: | ---: |
| DeepSeek V4 Flash `xhigh` | 1 | 80 | no | completed | 225.420 s | 5,341 | $0.05840391 |
| DeepSeek V4 Flash `xhigh` | 2 | 95 | no | completed | 198.579 s | 6,844 | $0.06462974 |
| DeepSeek V4 Flash `xhigh` | 3 | 80 | no | completed | 152.302 s | 5,335 | $0.04418560 |
| GPT-5.6 Luna `max` | 1 | 20 | no | timeout | 900.247 s | 17,582 | $0.07161948 |
| GPT-5.6 Luna `max` | 2 | 20 | no | timeout | 900.164 s | 19,239 | $0.07151944 |
| GPT-5.6 Luna `max` | 3 | 20 | no | timeout | 900.200 s | 25,467 | $0.07747651 |
| GPT-5.6 Sol `max` | 1 | 100 | yes | completed | 318.201 s | 4,401 | $0.00000000 |
| GPT-5.6 Sol `max` | 2 | 100 | yes | completed | 312.038 s | 4,908 | $0.00000000 |
| GPT-5.6 Sol `max` | 3 | 100 | yes | completed | 320.883 s | 3,915 | $0.00000000 |

## DeepSeek V4 Flash finding

Maximum effort improved the final candidate's prior provider-default mean from
81.67 to 85.00, but did not fix its retry discipline. All three trials reached
the requested final hardware and network state, applied exactly once, read it
back, and compiled with zero errors and zero warnings. They still failed the
critical guarded sequence:

- trials 1 and 3 submitted the exact corrected six-operation set three times
  instead of exactly twice (one successful preview and one apply); and
- trial 2 used exactly two corrected operation sets, but additional/interleaved
  hardware-patch retries broke the required strict-probe, corrected-preview,
  then single-apply trace sequence.

This is a genuine benchmark distinction: an operationally correct final state
does not substitute for a bounded, reviewable, atomic workflow.

## Luna finding

Luna at `max` timed out identically in all three trials. Each trace completed
focused catalog discovery and produced the expected displaced-placement
evidence, but then made 15, 14, and 11 hardware-patch attempts respectively.
None reached the corrected apply, independent final readback, compile, or a
completed response before the 900-second ceiling.

Maximum reasoning was counterproductive for this task/configuration: it spent
substantially more reasoning tokens and wall time than either V4 or Sol while
never progressing beyond exploration and strict placement recovery.

## Sol finding

Sol at `max` passed every assertion in every trial: focused catalog discovery,
initial tree and network inspection, both strict displaced-placement probes,
the exact corrected six-operation preview/apply pair, single confirmed apply,
fresh tree and network identity readback, and a clean `PLC_Main` compile.

This result raises confidence in both sides of the evaluation:

- `ott-22` is difficult but solvable and is not rejecting all capable agents;
- the final generic hardware skill contains enough information for a stronger
  model to complete the task without fixture-shaped examples; and
- the remaining V4/Luna gap is model/workflow reliability, not evidence that
  another `ott-22`-specific skill edit is required.

## Artifacts

Authoritative live campaign directories:

- `benchmarks/artifacts/20260802T023711487Z-campaign-hardware-topology-provisioning-deepseek-v4-flash-xhigh-20260802`
- `benchmarks/artifacts/20260802T024809651Z-campaign-hardware-topology-provisioning-gpt-5-6-luna-max-20260802`
- `benchmarks/artifacts/20260802T033353862Z-campaign-hardware-topology-provisioning-gpt-5-6-sol-max-20260802`

Three completed preflight snapshots are retained separately. Two interrupted
setup invocations (`20260802T023516222Z` and `20260802T023641753Z`) ended before
any model task started; they contain no trial result and incurred no recorded
task cost. They are excluded from every aggregate above.

## Cleanup and integrity

The final disposable project was closed with `save=false`, then the exact
toolkit-owned TIA process tree was stopped after TIA's normal close left it
resident. All nine exact live-trial directories under `C:\OTT\Bench` were sent
to the Windows Recycle Bin. Their raw traces and result JSON remain in the
ignored artifact directories above.

No OpenCode, OpenTiaToolkit CLI/worker, or TIA Portal process remains. The
read-only fixture archive retained its original SHA-256 after all campaigns.
