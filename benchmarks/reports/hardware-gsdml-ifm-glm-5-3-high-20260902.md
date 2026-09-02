# PASS - GLM-5.3 High held-out IFM GSDML qualification

Date: 2026-09-02

## Decision

Subscription-backed GLM-5.3 at declared reasoning effort `high` and OpenCode
variant `high` qualified on the held-out `ott-27` case with three perfect
100-point trials. Every trial followed the exact strict placement-recovery
sequence, applied the unchanged corrected transaction once, independently
verified the final hardware state, and compiled with zero errors and warnings.

Do not spend GPT-5.6 Sol allowance on this case. GLM-5.3 High has already
established a clean 3/3 ceiling, while GLM-5.3 Flash High scored 60, 70, and 90
and qualified 0/3 on the same frozen case. Preserve Sol `xhigh` for a harder
held-out case where a less expensive model does not qualify.

No skill or scorer changed between the Flash and non-Flash campaigns. The
generic hardware skill therefore needs no `ott-27`-motivated edit.

## Frozen campaign

- campaign: `hardware-gsdml-ifm-glm-5-3-high-20260902`;
- execution commit: `ffa5bb3779f245b2ab7d6c6ea58e9bed4aee6df9`;
- model: `zai-coding-plan/glm-5.3`;
- OpenCode reasoning effort / variant: `high` / `high`;
- OpenCode: `1.18.10`;
- provider class and authentication: subscription-backed Z.AI API credential;
- refreshed catalog name / status / release: GLM-5.3 / active / 2026-08-14;
- advertised variants: `low`, `high`, and `max`;
- advertised tool calls: supported;
- context / output limits: 1,000,000 / 131,072 tokens;
- catalog and recorded request input/output price: zero;
- case / prompt SHA-256:
  `E03C57287EEF9D2A35E7D8A458F3942DA5293B46187F69689CC34825CA6D9E69` /
  `23148A5B0FA237A3D033B0F858DDB42D78766B18FB98783BFF1C2F8FE859F6CC`;
- campaign SHA-256:
  `FB6B787A92A2BC4CCE8B999FAFD555145CCA65190C2D0EFC08ADA71BB94A34E8`;
- fixture: `tia-v21-topology-6`, freshly restored for every trial; and
- fixture SHA-256:
  `33193A83BE565BBD63486D66FFDE39404C76D9F8F01178DF28D435DBE3CC5732`.

The case, scorer, fixture, prompt, and skills remained frozen throughout the
campaign. All trials loaded `basic-tia-interaction` and
`hardware-configuration-workflow`; trials 1 and 2 also loaded the general
`opentia-toolkit-workflow` router. Trial 3 routed directly to the two required
specialized skills and still produced the exact qualifying trace.

## Results

| Trial | Score | Qualified | Duration | Reasoning tokens | Harness cost | Validator blocks / issues |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 100 | yes | 533.740 s | 6,530 | $0 | 0 / 0 |
| 2 | 100 | yes | 481.130 s | 8,222 | $0 | 0 / 0 |
| 3 | 100 | yes | 693.070 s | 11,978 | $0 | 0 / 0 |
| Mean / total | 100 | 3/3 | 569.312 s | 8,910 mean | $0 total | 0 / 0 |

All trials had exit code zero, did not time out, and were valid for model
comparison. There were no provider, endpoint, harness-runtime, or critical
assertion failures. Campaign wall time including fixture and aggregation
overhead was 1,769.608 seconds.

The Z.AI subscription route exposes zero per-request price to OpenCode, so
`$0` means no metered cost was visible to the harness; it is not an estimate
of the subscription's underlying economic cost. The raw OpenCode
`usage.tokens.total` values were 1,949,129, 2,130,092, and 1,798,433. They
include repeatedly reported or cached conversation input and should not be
interpreted as newly billed tokens.

## Exact trace audit

Every trial made exactly four `apply-hardware-patch` calls with the complete
12-operation transaction:

1. strict preview, which observed only the `FlowPort` displacement;
2. complete preview accepting only `FlowPort`, which observed the independent
   `LevelPort` displacement;
3. one complete corrected preview accepting both observed placements; and
4. one unchanged corrected apply.

There were no malformed calls, partial topology probes, speculative sibling
displacement allowances, extra previews, apply retries, or validator
interventions. Each trial also used exactly the two required catalog searches,
then independently reread the hardware tree, PROFINET topology, IO addresses,
and dataset 47360 byte before compiling `PLC_Main`.

The readbacks confirmed the AL1400, generated four-port module and proxy,
`FlowPort` and `LevelPort`, `CellPN` / `CellIO` connections, requested station
identity, all five IO starts, vendor byte `07`, and a zero-error/zero-warning
compile.

## Comparison with GLM-5.3 Flash High

| Model | Effort | Scores | Qualified | Mean duration | Mean reasoning | Validator blocks / issues |
| --- | --- | --- | ---: | ---: | ---: | ---: |
| GLM-5.3 Flash | `high` | 60 / 70 / 90 | 0/3 | 528.287 s | 9,507 | 3 / 20 |
| GLM-5.3 | `high` | 100 / 100 / 100 | 3/3 | 569.312 s | 8,910 | 0 / 0 |

The non-Flash model gained 26.67 mean-score points and eliminated every
critical and structural-validator failure. Its mean trial was 41.025 seconds
slower, about 7.8%, while reporting 597 fewer mean reasoning tokens. Three
trials do not estimate long-run model quality, but the behavioral separation
on this fixed case is decisive.

## Launch audit, cleanup, and artifacts

The first live launch stopped before fixture restoration because no full TIA
Portal instance was running. It produced no trial, model usage, or project
mutation and is invalid for comparison. A campaign-owned TIA V21 Update 2
instance was then started explicitly, identified by PID, and supplied to the
unchanged campaign.

After completion, the final project was closed with `save=false`. Only the
verified campaign-owned TIA root and child were stopped. The three exact
restore directories were moved to the Windows Recycle Bin, so they remain
recoverable. The pre-existing Codex MCP and worker processes were left
untouched. The immutable source archive retained its original SHA-256.

The ignored authoritative artifact directories are:

- preflight:
  `benchmarks/artifacts/20260902T023456950Z-campaign-hardware-gsdml-ifm-glm-5-3-high-20260902`;
- invalid no-TIA launch:
  `benchmarks/artifacts/20260902T023518199Z-campaign-hardware-gsdml-ifm-glm-5-3-high-20260902`; and
- completed campaign:
  `benchmarks/artifacts/20260902T023622449Z-campaign-hardware-gsdml-ifm-glm-5-3-high-20260902`.
