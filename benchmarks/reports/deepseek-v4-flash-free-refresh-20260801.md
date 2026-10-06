# DeepSeek V4 Flash Free refresh — 2026-08-01

## FAIL — no qualification improvement

The refreshed OpenCode Zen serving passed 6 of 9 trials, exactly matching the
corrected historical pass count. Its overall mean score also remained `81.67`.
The endpoint was slightly more efficient in reasoning-token use, but it did not
solve the discriminating hardware task: all three `ott-14` trials again reached
the 420-second timeout without finding the required patch operation.

This result does not support a claim that the refresh materially improved
OpenTiaToolkit agent performance. Minor score and latency movements on the two
already-passing cases are consistent with ordinary three-trial variation.

## Reproduction identity

- Campaign: `deepseek-v4-flash-free-refresh-20260801`
- Clean run commit: `c3bc26847f9adea55130d103f546937b3c81dcdd`
- Branch: `experiment/opencode-benchmark-slice`
- OpenCode: pinned `1.18.10`
- Model alias: `opencode/deepseek-v4-flash-free`
- Catalog name: `DeepSeek V4 Flash Free (New)`
- Provider/authentication: OpenCode Zen API credential, provider-scoped
- Reasoning effort: `max`
- OpenCode variant: `max`
- Cost class: `free`
- TIA Portal: V21 Update 2
- Fixture SHA-256:
  `1C17F223F00F0A43EFD5E51A7C8E2DBE4DD424540AE0C9A799F102997205AB74`
- Campaign duration: 1,642.144 seconds (27m 22.144s)
- Raw artifact directory:
  `benchmarks/artifacts/20260801T022211470Z-campaign-deepseek-v4-flash-free-refresh-20260801`

The OpenCode alias does not disclose a dated backend revision. This campaign
therefore records the observed serving epoch and catalog label without claiming
that Zen used the separately versioned OpenRouter `0731` model.

## Refreshed results

| Case | Passes | Scores | Mean score | Mean duration | Mean reasoning tokens | Recorded cost |
|---|---:|---|---:|---:|---:|---:|
| `ott-01` | 3/3 | 100, 90, 100 | 96.67 | 51.826 s | 719.67 | $0.000000 |
| `ott-03` | 3/3 | 90, 90, 100 | 93.33 | 55.241 s | 953.00 | $0.000000 |
| `ott-14` | 0/3 | 55, 55, 55 | 55.00 | 420.204 s | 19,927.67 | $0.000000 |
| **Overall** | **6/9** |  | **81.67** | **175.757 s** | **7,200.11** | **$0.000000** |

Per-trial detail:

| Case | Trial | Score | Pass | Duration | Reasoning tokens | Recorded cost |
|---|---:|---:|---|---:|---:|---:|
| `ott-01` | 1 | 100 | yes | 58.952 s | 769 | $0.000000 |
| `ott-01` | 2 | 90 | yes | 49.413 s | 654 | $0.000000 |
| `ott-01` | 3 | 100 | yes | 47.114 s | 736 | $0.000000 |
| `ott-03` | 1 | 90 | yes | 63.140 s | 1,252 | $0.000000 |
| `ott-03` | 2 | 90 | yes | 42.077 s | 438 | $0.000000 |
| `ott-03` | 3 | 100 | yes | 60.505 s | 1,169 | $0.000000 |
| `ott-14` | 1 | 55 | no — timeout | 420.229 s | 18,490 | $0.000000 |
| `ott-14` | 2 | 55 | no — timeout | 420.183 s | 18,289 | $0.000000 |
| `ott-14` | 3 | 55 | no — timeout | 420.200 s | 23,004 | $0.000000 |

The zero cost is a real provider-reported task price for this temporary free
endpoint. It should not be projected forward: OpenCode documents the free model
as time-limited.

## Corrected before/after comparison

The historical traces were replayed against the current format-neutral
`ott-03` evaluator before comparison. The original model was not rerun, and its
raw results were not overwritten.

| Metric | Historical serving | Refreshed serving | Change |
|---|---:|---:|---:|
| Passes | 6/9 | 6/9 | none |
| Critical failures | 3 | 3 | none |
| Runtime failures | 3 | 3 | none |
| Mean score | 81.67 | 81.67 | none |
| Mean duration | 174.326 s | 175.757 s | +1.431 s (+0.8%) |
| Mean reasoning tokens | 7,933.56 | 7,200.11 | -733.45 (-9.2%) |

Case-level movement:

| Case | Historical | Refreshed | Interpretation |
|---|---|---|---|
| `ott-01` | 3/3, mean 100 | 3/3, mean 96.67 | One refreshed trial skipped the non-critical PLC discovery call |
| `ott-03` | 3/3, mean 90 | 3/3, mean 93.33 | One refreshed trial added the non-critical `open-plc` step |
| `ott-14` | 0/3, mean 55 | 0/3, mean 55 | No functional change; every trial timed out |

The refreshed endpoint reduced mean reasoning use by 19.0% on discovery and
9.8% on hardware, but increased it by 14.5% on tag mutation. With only three
trials and no pass-rate or hard-case gain, these token changes are efficiency
signals rather than capability evidence.

## Hardware trace audit

Every `ott-14` trial safely:

- connected to the disposable project;
- gathered the hardware tree and complete I/O map;
- identified that byte `64` was occupied;
- used only `dry-run=true` patch attempts; and
- never applied the conflicting address.

However, the refreshed model made 46, 52, and 52 `apply-hardware-patch` calls
without discovering the valid `set-io-address-start` operation. The historical
serving made 40, 34, and 50 attempts. The refresh therefore increased total
schema-guessing calls from 124 to 150 while leaving the outcome unchanged.

This isolates the next useful experiment: keep this model, fixture, prompt, and
reasoning setting fixed, then A/B the current hardware skill against a version
that explicitly exposes the supported I/O address operation vocabulary.

## Recovery and cleanup

The final timeout left its disposable project open. It was closed with
`save=false`, the exact benchmark-owned TIA PID was stopped, and all nine exact
trial directories under `C:\OTT\Bench` were permanently deleted. Those
disposable project copies are not recoverable; their ignored raw traces and
results remain in the campaign artifact directory. No TIA or OpenCode process
remains, and the read-only fixture archive retained its original SHA-256.
