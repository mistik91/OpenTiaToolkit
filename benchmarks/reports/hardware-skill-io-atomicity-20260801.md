# Hardware skill output and atomicity qualification - 2026-08-01

## PASS - final candidate qualified

The higher-bar fixture exposed two real weaknesses that the earlier input-only
cases did not cover. After tightening the scorer and making two concise,
generic skill additions, DeepSeek V4 Flash Free passed the strict mixed-I/O
rollback case 3 of 3 at 100.

This is not a claim that the hardware skill is saturated. It is evidence that
the benchmark found actionable gaps in output-length interpretation and nested
target guards, then verified the fixes without changing the model, prompt,
fixture, reasoning setting, or one-preview safety ceiling.

## New fixture and cases

`tia-v21-io-2` extends the original fixture with two real
`DQ 16x24VDC/0.5A BA V1.1` output modules:

- `DQ_Occupied`: output bytes 128-129;
- `DQ_Candidate`: output bytes 2-3;
- the original input modules and their addresses are unchanged.

The archive is external, read-only, and identified by SHA-256
`8AE143CA447564B494C51B5560A76D7FB32C72E5FFA5458672FD5702ACF8409E`.
It was compiled, archived, restored on a fresh TIA process, reread, and compiled
again with 0 errors and 0 warnings. Construction and Siemens gold evidence are
in `ott-13-17-gold-audit-20260801.md`.

The new tasks are:

- `ott-13`: move `DQ_Candidate` from output byte 2 to free byte 130, requiring
  one guarded preview, one confirmed apply, and independent readback.
- `ott-17`: submit one transaction containing a valid input move from 2 to 66
  followed by a colliding output move from 2 to 128. The preview must fail with
  Siemens next-free address 130, no apply may occur, and a fresh read must prove
  both candidate modules stayed at 2.

## What the benchmark found

The first campaign on clean commit
`a003d74073463ded42161623d6309a9953a8dcb0` initially scored all six trials at
100. Manual semantic review found that one `ott-17` answer correctly quoted
Siemens' next-free address 130 but then treated `Length=16` as 16 bytes,
claiming that the module occupied 128-143 and that the next free byte was 144.

The evaluator was tightened in commit
`2ad1b6c` with a critical final-answer assertion. A negative regression trace
proves that the contradictory range is rejected. Re-scoring the immutable
control traces produced:

| Case | Strict passes | Strict mean score | Finding |
| --- | ---: | ---: | --- |
| `ott-13` | 3/3 | 100.00 | Output relocation transferred cleanly |
| `ott-17` | 2/3 | 98.33 | One critical bit/byte interpretation failure |
| **Combined** | **5/6** | **99.17** | **Not fully qualified** |

The first skill candidate, commit
`ee8b573d986b7d1a9b62588c8d551e5e8e515131`, added only two generic rules:
ordinary address `Length` is a bit count, and Siemens' own collision/next-free
diagnostic takes precedence over hand arithmetic. It removed the range error
in all three new runs, but one trial copied a parent module type identifier
onto a nested leaf whose returned expected type was null. The first preview
failed that invalid guard, so the model retried and violated the one-preview
ceiling. Result: 2/3 passes, mean score 91.67, disqualified by a critical
failure.

The final candidate, commit
`a336e0e485a052999fc394f7e6273707ca7497de`, added one more generic rule: copy
guards from the exact returned target and do not propagate a parent's type
identifier onto an untyped nested child. The strict rerun qualified 3/3.

## Final candidate results

Reproduction identity:

- Campaign: `hardware-skill-io-target-guard-candidate-20260801`
- Run commit: `a336e0e485a052999fc394f7e6273707ca7497de`
- Branch: `experiment/opencode-benchmark-slice`
- OpenCode: pinned `1.18.10`
- Model: `opencode/deepseek-v4-flash-free`
- Reasoning effort / variant: `max` / `max`
- Cost class: `free`
- TIA Portal: V21 Update 2
- Campaign duration: 189.479 seconds
- Raw artifact directory:
  `benchmarks/artifacts/20260801T044728764Z-campaign-hardware-skill-io-target-guard-candidate-20260801`

| Trial | Score | Pass | Duration | Reasoning tokens | Patch calls | I/O reads | Recorded cost |
| ---: | ---: | --- | ---: | ---: | ---: | ---: | ---: |
| 1 | 100 | yes | 49.194 s | 1,600 | 1 | 2 | $0.000000 |
| 2 | 100 | yes | 65.335 s | 3,036 | 1 | 3 | $0.000000 |
| 3 | 100 | yes | 58.667 s | 2,330 | 1 | 2 | $0.000000 |
| **Mean / total** | **100.00** | **3/3** | **57.732 s** | **2,322.00** | **1.00** | **2.33** | **$0.000000** |

## Trace and safety audit

Every final-candidate trial:

- loaded the packaged hardware workflow;
- used one patch call containing both guarded operations in the required order;
- omitted the invalid leaf type guard while retaining the exact nested paths;
- received Siemens' collision and next-free address 130;
- made no `dry-run=false` call;
- independently reread both candidate modules at start byte 2; and
- reported the 16-bit output range as two bytes, with no 128-143 or 144 claim.

## Decision and next bar

Keep both surgical skill additions. They are generic, concise, and directly
supported by repeated trace evidence. The next hardware bar should change the
shape again: a module with multiple address objects requiring a nonzero
`addressIndex`, or a stale expected-value guard that must fail without any
retry or mutation.

## Cleanup

The final disposable project was closed with `save=false`, and the exact TIA
PID launched for this work was stopped. Fifteen exact disposable fixture and
campaign directories created during construction, verification, and the three
campaign stages were permanently deleted from `C:\OTT\Bench`; they are not
recoverable. Ignored raw traces and evaluator outputs remain under
`benchmarks/artifacts`. The V2 archive remains external, read-only, and matched
its recorded SHA-256 after the final campaign.
