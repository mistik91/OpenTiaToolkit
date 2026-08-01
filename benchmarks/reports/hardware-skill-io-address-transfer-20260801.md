# Hardware skill I/O address transfer qualification — 2026-08-01

## PASS — 9 of 9 at 100

The surgical `hardware-configuration-workflow` candidate transferred cleanly
beyond its original `ott-14` task. DeepSeek V4 Flash Free passed all nine
trials across the original collision, a successful guarded relocation, and a
reversed collision with a different target, owner, and next-free response.

This clears the intended transfer bar. It also saturates this small I/O-address
subset for the current model/skill pair, so further difficulty should come
from a new hardware dimension rather than more wording around these modules.

## Frozen skill and expanded tasks

The candidate skill introduced in commit
`598f17e52ef99b0ad3bfd6ce245ba7888c127d4d` was not changed for this campaign.
The clean run commit `d4f81d9dbbcd8f6e65f7e53abc0083420295cab3`
added only benchmark cases, evaluator assertions, and their synthetic tests:

- `ott-14`: move `DI_Candidate` from `2` into `DI_Occupied` at `64`; preview
  must fail with next free address `66`, with no apply.
- `ott-15`: move `DI_Candidate` from `2` to free address `66`; require one
  guarded preview, one confirmed apply, and fresh I/O readback.
- `ott-16`: move `DI_Occupied` from `64` into `DI_Candidate` at `2`; preview
  must fail with next free address `4`, with no apply.

The two new cases have `passScore: 85` and treat the exact guarded operation,
apply/refusal behavior, and readback as critical. They require no more than one
preview, preventing schema-guessing loops from receiving a passing result.

## Live gold evidence

Before versioning the cases, both new Siemens behaviors were exercised directly
against an independently restored disposable fixture:

- the reversed collision was rejected with “already being used” and next free
  address `4`;
- the free relocation dry-ran successfully, applied from `2` to `66`, and was
  verified by a fresh address read; and
- an inverse guarded dry-run/apply returned the module from `66` to `2`, with
  a final read confirming the restored baseline.

The gold-audit evidence is retained under the ignored artifact directory
`benchmarks/artifacts/20260801T032147967Z-ott-14-trial-901-opencode_deepseek-v4-flash-free/gold-audit`.

## Reproduction identity

- Campaign: `hardware-skill-io-address-transfer-20260801`
- Clean run commit: `d4f81d9dbbcd8f6e65f7e53abc0083420295cab3`
- Branch: `experiment/opencode-benchmark-slice`
- OpenCode: pinned `1.18.10`
- Model: `opencode/deepseek-v4-flash-free`
- Catalog name: `DeepSeek V4 Flash Free (New)`
- Reasoning effort / variant: `max` / `max`
- Cost class: `free`
- TIA Portal: V21 Update 2
- Fixture SHA-256:
  `1C17F223F00F0A43EFD5E51A7C8E2DBE4DD424540AE0C9A799F102997205AB74`
- Campaign duration: 453.431 seconds (7m 33.431s)
- Raw artifact directory:
  `benchmarks/artifacts/20260801T033034169Z-campaign-hardware-skill-io-address-transfer-20260801`

## Results

| Case | Passes | Mean score | Mean duration | Mean reasoning tokens | Recorded cost |
|---|---:|---:|---:|---:|---:|
| `ott-14` | 3/3 | 100.00 | 45.342 s | 1,146.67 | $0.000000 |
| `ott-15` | 3/3 | 100.00 | 44.504 s | 772.00 | $0.000000 |
| `ott-16` | 3/3 | 100.00 | 42.982 s | 1,032.33 | $0.000000 |
| **Overall** | **9/9** | **100.00** | **44.276 s** | **983.67** | **$0.000000** |

Per-trial detail:

| Case | Trial | Score | Duration | Reasoning tokens | Preview calls | Apply calls |
|---|---:|---:|---:|---:|---:|---:|
| `ott-14` | 1 | 100 | 41.246 s | 851 | 1 | 0 |
| `ott-14` | 2 | 100 | 49.888 s | 1,284 | 2 | 0 |
| `ott-14` | 3 | 100 | 44.893 s | 1,305 | 1 | 0 |
| `ott-15` | 1 | 100 | 48.931 s | 808 | 1 | 1 |
| `ott-15` | 2 | 100 | 41.692 s | 955 | 1 | 1 |
| `ott-15` | 3 | 100 | 42.890 s | 553 | 1 | 1 |
| `ott-16` | 1 | 100 | 40.598 s | 955 | 1 | 0 |
| `ott-16` | 2 | 100 | 46.545 s | 1,102 | 1 | 0 |
| `ott-16` | 3 | 100 | 41.803 s | 1,040 | 1 | 0 |

## Trace and safety audit

Every trial loaded the candidate hardware skill exactly once and used
`set-io-address-start` with the observed `addressIndex`, `startAddress`, and
`expectedStartAddress` values.

All three positive trials made exactly one preview and one confirmed apply,
then reread the I/O map at address `66`. All six collision trials made zero
apply calls and reported the correct owner and next-free evidence.

One `ott-14` trial used two safe previews. Its first attempt copied the parent
module's type identifier onto the nested address target, which correctly
failed the type guard. It removed that mismatched guard and then received the
expected collision. This is residual target-guard friction, not operation-name
guessing, and it caused no mutation. The stricter new cases each met their
one-preview ceiling.

The known `ott-14` final-answer matcher was broadened before this campaign to
accept semantically equivalent phrases such as “no mutation applied.” A
representative regression trace covers that wording; no live result was
rescored after the campaign.

## Decision and next bar

Keep the skill candidate unchanged and accept the two transfer cases into the
suite. The next meaningful skill benchmark should use a fixture with at least
one new dimension, preferably:

- an output-address object or mixed input/output module;
- multiple address objects requiring nonzero `addressIndex` selection; or
- a stale expected-value guard that must fail without mutation.

That will test whether the workflow generalizes beyond the single-input-object
shape while retaining the same discover → guard → preview → apply/refuse →
verify discipline.

## Cleanup

The final disposable project was closed with `save=false`, the exact
campaign-owned TIA PID was stopped, and all nine exact campaign directories
under `C:\OTT\Bench` were permanently deleted. Those disposable project copies
are not recoverable. Ignored raw traces and evaluator results remain in the
artifact directory. No TIA Portal or OpenCode process remains, and the
read-only fixture archive retained its original SHA-256.
