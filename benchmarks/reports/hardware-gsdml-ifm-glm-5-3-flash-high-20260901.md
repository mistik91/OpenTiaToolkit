# FAIL - GLM-5.3 Flash High held-out IFM GSDML transfer

Date: 2026-09-01

## Decision

The held-out `ott-27` case is valid, live-solvable, and not saturated.
Subscription-backed GLM-5.3 Flash at declared reasoning effort `high` and
OpenCode variant `high` scored 60, 70, and 90 for a mean of 73.33. All three
trials reached the requested final hardware state, passed independent
tree/network/IO/GSD readback, and compiled with zero errors and warnings.
Nevertheless, all three failed at least one critical transaction-discipline
assertion, so the model status is `disqualified-critical-failure` with 0/3
qualified trials.

Do not change the generic hardware skill from this campaign. It already
requires exact result-reference targets, relative generated-child paths, a
complete atomic batch, evidence-driven sequential displacement recovery, and
exactly one apply after the successful corrected preview. The failed traces
violated those existing generic rules; adding IFM names or this case's paths
would teach the benchmark rather than improve transfer.

## Frozen campaign

- campaign: `hardware-gsdml-ifm-glm-5-3-flash-high-20260901`;
- execution commit: `c1dea923f61f8de75fdac51df27e994279dcf4b7`;
- model: `zai-coding-plan/glm-5.3-flash`;
- OpenCode reasoning effort / variant: `high` / `high`;
- OpenCode: `1.18.10`;
- provider class and authentication: subscription-backed Z.AI credential;
- advertised tool calls: supported;
- advertised variants: `low`, `high`, and `max`;
- advertised context limit: 1,000,000 tokens;
- case / prompt SHA-256:
  `E03C57287EEF9D2A35E7D8A458F3942DA5293B46187F69689CC34825CA6D9E69` /
  `23148A5B0FA237A3D033B0F858DDB42D78766B18FB98783BFF1C2F8FE859F6CC`;
- campaign SHA-256:
  `9FF7501F9431A7A0E35B83B71025B79C421F2A5CB0343D9681DE1C2F76D9FAC8`;
- fixture: `tia-v21-topology-6`, freshly restored for every trial; and
- fixture SHA-256:
  `33193A83BE565BBD63486D66FFDE39404C76D9F8F01178DF28D435DBE3CC5732`.

The skills were frozen during case design, gold qualification, scoring, and
all model trials. Every trial loaded `opentia-toolkit-workflow`,
`basic-tia-interaction`, and `hardware-configuration-workflow`.

## Results

| Trial | Score | Qualified | Duration | Reasoning tokens | Harness cost | Validator blocks / issues |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 60 | no | 547.838 s | 10,702 | $0 | 1 / 13 |
| 2 | 70 | no | 597.362 s | 9,436 | $0 | 1 / 5 |
| 3 | 90 | no | 439.660 s | 8,383 | $0 | 1 / 2 |
| Mean / total | 73.33 | 0/3 | 528.287 s | 9,507 mean | $0 total | 3 / 20 |

All trials had exit code zero, did not time out, and were valid for model
comparison. There were no provider, endpoint, or harness runtime failures.
The Z.AI subscription endpoint reports zero per-request price to OpenCode, so
`$0` means no metered cost was exposed to the harness; it is not an estimate
of the subscription's underlying economic cost. The campaign's paid-cost
guard was `$0.01`.

The raw OpenCode `usage.tokens.total` values were 2,014,642, 2,201,323, and
2,145,374. Those totals include repeatedly reported/cached conversation input
and should not be read as newly billed tokens. Reasoning-token values above
are recorded separately exactly as the provider reported them.

## Trace audit

Every trial ultimately created the correct IFM AL1400 topology, preserved the
generated module and proxy, connected the station, assigned all five IO starts,
guarded and changed the vendor byte, independently reread the result, and
compiled `PLC_Main`. Qualification failed because the path to that state was
not the required strict transaction sequence.

- Trial 1 first submitted a malformed 12-operation patch with missing target
  and `itemName` fields; the mechanical validator rejected it before TIA. It
  then learned the two independent displacements in the correct order and
  produced the exact corrected preview. The first unchanged apply encountered
  a live Siemens transient saying an IO connector was not on the IO system's
  subnet; the transaction rolled back, and an unchanged retry succeeded. It
  lost the strict placement-call count, exact operation-set, and single-apply
  assertions. Part of its 60-point result is therefore attributable to the
  live transient, although its initial malformed call was a real model error.
- Trial 2 learned both displacements sequentially, then used the wrong relative
  generated network path `PN-IO`, which rolled back with `NotFound`. After a
  successful corrected preview it submitted an apply whose generated-child
  targets omitted required relative `itemPath` fields; the validator rejected
  five issues before TIA. The next corrected apply succeeded. It lost the
  required recovery sequence and single-apply assertions.
- Trial 3 began with a plug operation missing `itemName`, which the validator
  rejected, and then addressed the generated parent as
  `IOL_Master_01/4 Ports_1` instead of the required result-relative
  `4 Ports_1`. It subsequently executed the exact strict Flow failure, Level
  failure, corrected preview, and one apply. Its only remaining failure was
  the extra placement-attempt count, yielding 90 points.

The progression from 60 to 90 shows useful model variance and confirms that
the benchmark is measuring more than final-state completion. Trial 3 also
shows the model can execute the intended workflow, but not yet reliably enough
to qualify over three trials.

## Aggregation recovery

All three authoritative `result.json` files were written successfully. The
outer campaign process then failed while aggregating the standard single arm:
PowerShell unwrapped the one-element `skillEvaluationArms` value to a scalar,
and the script accessed a missing scalar `.Count` property.

`Recover-OpenCodeCampaignResult.ps1` validated the three case results and
produced `campaign-result.json` with status `completed-recovered`. The harness
was then repaired by moving evaluation-arm multiplication into
`Get-BenchmarkExpectedTrialCount` and coercing the arm input to an array. A
paired-arm regression now proves the expected count of 12 for two cases,
three trials, and two arms. This failure happened after trial execution and
does not affect trial validity or scores.

## Cleanup and artifacts

The campaign-owned TIA root and child processes were stopped after closing the
project, and all three exact disposable restore directories were removed. The
pre-existing Codex MCP and worker processes were left untouched. The source
archive retained its recorded size and SHA-256.

The ignored authoritative artifact directory is:

`benchmarks/artifacts/20260901T145544450Z-campaign-hardware-gsdml-ifm-glm-5-3-flash-high-20260901`
