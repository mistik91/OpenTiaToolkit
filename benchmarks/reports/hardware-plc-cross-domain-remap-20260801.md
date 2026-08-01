# Cross-domain hardware and PLC tag remap qualification - 2026-08-01

## PASS - benchmark exposed and verified a real skill gap

`ott-20` crossed from hardware planning into PLC tag semantics. The strict
control failed 0/3 because every trace used unsupported `modify-tags` selector
aliases that Siemens still allowed to reach a successful-looking final state.
A concise PLC-skill correction changed the clean rerun to 3/3 at 100 without
changing the prompt, fixture, model, reasoning configuration, or thresholds.

## Reproduction identity

- Campaign: `hardware-plc-cross-domain-remap-20260801`
- Control run commit: `e9ec6325d8274be3741924749eda6663f5b4ac3e`
- Candidate run commit: `81558825381cb8ff403491f8b4363a1d460dc09c`
- Branch: `experiment/opencode-benchmark-slice`
- OpenCode: pinned `1.18.10`
- Model: `opencode/deepseek-v4-flash-free`
- Reasoning effort / variant: `max` / `max`
- Cost class: `free`
- Fixture: `tia-v21-cross-domain-4`
- Fixture SHA-256: `C89FDAD14AF95CCC9DA216E4C82FD710A60774D9E7C4D9CB07A6185FB350999D`
- TIA Portal: V21 Update 2

## Strict control - 0/3

Manual review found that trial 2's different hardware operation order was
semantically equivalent, so the hardware-plan assertion was made
order-independent. The same review found a more serious false pass: trials 1
and 2 used `match.table`, while trial 3 used `match.table` and
`match.address`. The parser recognizes `tagTable` and `logicalAddress`; unknown
match fields are ignored. Thus all three updates lacked the requested table
guard, and trial 3 also lacked the current-address guard.

The scorer was tightened to require exact `tagTable`, `name`, and
`logicalAddress` fields inside every match. Regressions now accept arbitrary
hardware-operation order but reject `table`, `tagTableName`, `address`, and
missing-address selectors. No threshold or required engineering behavior was
relaxed.

Immutable control traces under the strict scorer:

| Trial | Strict score | Pass | Duration | Reasoning tokens | Finding |
| ---: | ---: | --- | ---: | ---: | --- |
| 1 | 85 | no | 119.250 s | 3,778 | `table` alias silently weakened every match |
| 2 | 85 | no | 115.156 s | 4,652 | Correct hardware order variant; same `table` alias |
| 3 | 80 | no | 121.825 s | `table` + `address`; failed preview, retry, then unguarded apply |
| **Mean / total** | **83.33** | **0/3** | **118.744 s** | **3,962.33** | **3 critical selector failures** |

Control artifacts:
`benchmarks/artifacts/20260801T060033122Z-campaign-hardware-plc-cross-domain-remap-20260801`.

## Surgical skill change

The canonical `plc-block-and-tag-workflow` and all packaged/installed copies
now state two reusable rules:

- logical-address updates must use exact `match.tagTable`, `match.name`,
  `match.logicalAddress`, and `set.logicalAddress`; unsupported aliases can be
  ignored and silently weaken a selector;
- in a coordinated multi-domain change, every preview must complete before the
  first apply, and a schema-error retry is not permission to mutate.

The skill passed `quick_validate.py`, packaged and installed drift checks, and
the complete benchmark test suite before the candidate run.

## Candidate - 3/3

| Trial | Score | Pass | Duration | Reasoning tokens | Hardware calls | Tag calls | Cost |
| ---: | ---: | --- | ---: | ---: | ---: | ---: | ---: |
| 1 | 100 | yes | 126.101 s | 4,429 | 2 | 2 | $0.000000 |
| 2 | 100 | yes | 103.536 s | 3,629 | 2 | 2 | $0.000000 |
| 3 | 100 | yes | 97.741 s | 3,482 | 2 | 2 | $0.000000 |
| **Mean / total** | **100.00** | **3/3** | **109.126 s** | **3,846.67** | **2.00** | **2.00** | **$0.000000** |

Every candidate trace used literal match fields
`logicalAddress,name,tagTable`, exactly one hardware preview/apply and one tag
preview/apply, fresh hardware and tag readback, and one compile with 0 errors
and 0 warnings. There were no retries, timeouts, runtime failures, paid
requests, or critical failures.

Candidate artifacts:
`benchmarks/artifacts/20260801T061200205Z-campaign-hardware-plc-cross-domain-remap-20260801`.

## Decision

Keep the skill change. Unlike the preceding hardware-only cases, `ott-20` was
not saturated before the intervention: it produced a repeatable safety defect
in all three controls and verified the correction in three clean trials. The
next bar should preserve this cross-domain shape while introducing a forced
second-domain apply failure and requiring explicit compensation or a proven
partial-state stop; simple success-path expansion is no longer enough.

## Cleanup

The final disposable project was closed with `save=false`, and the exact owned
TIA process was stopped. Fourteen exact construction, restore, rescore,
control, and candidate directories were permanently deleted from
`C:\OTT\Bench`; they are not recoverable. Earlier directories were left
untouched. Raw traces remain ignored under `benchmarks/artifacts`. The V3 and
V4 archives remain external and read-only.
