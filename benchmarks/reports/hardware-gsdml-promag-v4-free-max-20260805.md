# FAIL - DeepSeek V4 Flash Free maximum-effort Promag GSDML qualification

Date: 2026-08-05

## Decision

The repaired OpenCode free endpoint is usable again. All three corrected
`ott-26` trials reached the model, completed normally, and were valid for model
comparison. DeepSeek V4 Flash Free at `max` scored 80, 80, and 90 for a mean of
83.33, but qualified 0/3 because every trial missed at least one critical
workflow assertion. The campaign is therefore
`disqualified-critical-failure` even though every trial produced the requested
hardware state.

Do not change the generic hardware skill from this campaign. It already says
to keep an atomic topology batch intact, use the complete dry-run as the
capability preflight for generated objects, and avoid a disposable partial
station. V4 nevertheless added a creation-only discovery dry-run in trials 2
and 3. Trial 1 kept the batch intact but did not immediately reuse the generated
paths returned in the first complete dry-run. Restating those rules or adding
Promag-specific paths would overfit this case rather than close a new generic
skill gap.

The free endpoint succeeded, so no paid-model fallback was run.

## Frozen campaign

- campaign: `hardware-gsdml-promag-deepseek-v4-flash-free-max-20260805`;
- manifest commit: `853f47f`;
- model: `opencode/deepseek-v4-flash-free`;
- OpenCode reasoning effort / variant: `max` / `max`;
- OpenCode: `1.18.10`;
- refreshed catalog name: `DeepSeek V4 Flash Free (New)`;
- catalog status / release: active / 2026-07-31;
- advertised variants: `low`, `high`, and `max`;
- tool calls: supported;
- context / output limits: 200,000 / 128,000 tokens;
- authentication: isolated OpenCode provider endpoint, no user credential;
- catalog and recorded request price: zero input, output, cache, and run cost;
- case and prompt SHA-256:
  `8D2A7D16D7B7D92F92A11B890BD5AA7760B4C49D668A4D8122FCCA6E527252F7` /
  `935CAD2FDA0D87AE3DD66D222306B5A6783EB642D522A7F29E9FF95B15855F57`;
- campaign SHA-256:
  `16483690DFF746BE2E1F1F5916C4DD93FC2B6120E20FB2A994A6E516F71E15EB`;
- hardware skill SHA-256:
  `B392950EAE5F305E89058A05DE24D4B7EE628207BDC2900561B48ED46C00D5FC`;
- fixture: `tia-v21-topology-6`, freshly restored for every trial; and
- fixture SHA-256:
  `33193A83BE565BBD63486D66FFDE39404C76D9F8F01178DF28D435DBE3CC5732`.

## Results

| Trial | Score | Passed | Duration | Reasoning tokens | Cost | Validator issues |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 80 | no | 637.096 s | 9,351 | $0 | 0 |
| 2 | 80 | no | 573.281 s | 14,787 | $0 | 0 |
| 3 | 90 | no | 703.900 s | 395 | $0 | 0 |
| Mean / total | 83.33 | 0/3 | 638.092 s | 8,177.67 mean | $0 total | 0 total |

All trials had exit code zero, did not time out, and had no runtime, endpoint,
or structural-validator failure. Every trial:

- used one focused Promag catalog search;
- read the baseline and final tree, network, and IO state;
- applied the exact corrected ten-operation transaction once;
- connected `FT_101` to `CellPN` and `CellIO` with the required identity;
- assigned the requested input and output starts;
- guarded the GSD write with `dataHex=80AF` and
  `expectedDataHex=80DF`;
- reread dataset 32752 and observed the requested bytes; and
- compiled `PLC_Main` successfully.

The provider reported only 395 reasoning tokens for trial 3 despite its longest
duration and substantial tool use. The harness records that telemetry as
reported; it should not be interpreted as a precise measure of internal work.

## Trace audit

- Trial 1 made five complete dry-runs and one apply. The first two dry-runs
  correctly learned the independent `TotalizerControl` and `TotalizerMode`
  displacements. It then tried the GSD record at the device root and at another
  incorrect generated target before selecting `FT_101/Promag 100 PNIO`. It
  lost the 10-point exact recovery count and the 10-point strict sequence.
- Trial 2 made one creation-only DAP discovery dry-run, three failing complete
  dry-runs, one reduced two-operation GSD-owner probe, one successful complete
  preview, and one apply. It found the right state but violated the task and
  skill requirement to use the complete atomic batch as the generated-object
  preflight. It lost the same two assertions.
- Trial 3 made one creation-only DAP discovery dry-run followed by the exact
  strict displacement sequence: control failure, mode failure, successful
  corrected preview, then one unchanged apply. It passed the strict sequence
  assertion but the extra partial dry-run made the recovery count four instead
  of exactly three, costing the remaining 10 points.

This is model execution variance, not a missing toolkit capability. The live
API exposed every required operation and returned sufficient generated-tree
evidence from the complete dry-run. No validator intervention was needed.

## Comparison with GPT-5.6 Sol max

The directly comparable final Sol skill candidate scored 80, 100, and 80
(mean 86.67), passed 1/3, averaged 431.988 seconds, and averaged 7,370.33
reasoning tokens. Free V4 was 3.34 mean-score points lower, had no passing
trial, averaged 206.104 seconds longer, and reported 807.34 more mean reasoning
tokens. Both campaigns were disqualified under the all-trials qualification
rule and both had zero validator issues.

These three-trial differences are descriptive, not estimates of long-run model
quality. The stronger result is behavioral: Sol produced one exact 100-point
trace, while free V4 repeatedly added at least one unnecessary discovery or
target-recovery attempt.

## Endpoint recovery, cleanup, and artifacts

The preflight refreshed the model catalog successfully, and all three live
requests ran well past the old immediate-503 outage signature. The endpoint
completed the full 32-minute 52-second campaign without a provider failure.

The final disposable project was closed with `save=false`. The graceful portal
close left the exact toolkit-started TIA PID 18112 and its three verified child
processes alive, so only that audited process tree was force-stopped. All three
campaign restore workspaces were sent to the Windows Recycle Bin. Captured
benchmark artifacts remain available, and the read-only fixture archive kept
its original hash.

- catalog-only preflight:
  `benchmarks/artifacts/20260805T010110098Z-campaign-hardware-gsdml-promag-deepseek-v4-flash-free-max-20260805`
- completed campaign:
  `benchmarks/artifacts/20260805T010314809Z-campaign-hardware-gsdml-promag-deepseek-v4-flash-free-max-20260805`
