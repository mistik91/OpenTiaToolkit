# Hardware skill mixed-module address-index qualification - 2026-08-01

## PASS - qualified without a skill change

DeepSeek V4 Flash Free passed `ott-12` 3 of 3 at 100 on the corrected clean
benchmark commit. The new case removes the single-address-object shortcut: the
same `DIDQ_Candidate` target exposes input index 0 and output index 1, and only
the output row may move.

The existing hardware skill transferred without modification. This is useful
positive evidence for its instruction to select `addressIndex` from the row
matching the observed `IoType`, but it is not evidence that broader
multi-address behavior is saturated.

## Fixture and task

`tia-v21-multi-address-3` adds a real
`DI 16x24VDC/DQ 16x24VDC/0.5A BA V1.1` mixed module:

- target: `PLC_Main / DIDQ_Candidate / DIDQ_Candidate`;
- index 0: Input, Length 16 bits, start byte 0;
- index 1: Output, Length 16 bits, start byte 0.

`ott-12` requires one guarded preview and one confirmed apply that move only
the output row to byte 132, followed by a fresh read that proves input index 0
remained at byte 0 and output index 1 became byte 132. A critical negative
regression rejects any attempted mutation of index 0.

The external archive is read-only, size 5,582,000 bytes, and identified by
SHA-256
`3B4F832B1F32ECDF20C8905E8CE07C429DC808F00C3C15B03688E0AB7E168585`.
The construction and independent Siemens restore evidence are in
`ott-12-gold-audit-20260801.md`.

## Scorer audit

The first 3-trial execution produced displayed scores of 100, 80, and 100.
Manual trace review showed that the 80-point trial was a false negative: it
correctly selected index 1, previewed and applied once, and freshly verified
input 0 / output 132. Its only difference was requesting `StartAddress` before
`IoType`, while the initial regular expression assumed the reverse output
order.

Commit `fc6fe49` made both baseline and final two-row assertions independent of
attribute order and changed the representative trace to preserve the exact
ordering that exposed the bug. The full benchmark tests passed, and the
immutable failed trace rescored to 100 before any rerun. No case threshold was
lowered and no required behavior was removed.

## Clean qualification results

Reproduction identity:

- Campaign: `hardware-skill-io-address-index-20260801`
- Run commit: `fc6fe49a128751a6478648b5b91d1c3efdfaf719`
- Git state SHA-256: `B9D712A26B4F49A6CCFE3DAEE6AD8242A25B586A405BDB37BC99111315E56DFC`
- Branch: `experiment/opencode-benchmark-slice`
- OpenCode: pinned `1.18.10`
- Model: `opencode/deepseek-v4-flash-free` (`DeepSeek V4 Flash Free (New)`)
- Reasoning effort / variant: `max` / `max`
- Cost class and catalog price: `free`; input/output/cache read/cache write all 0
- TIA Portal: V21 Update 2
- Campaign duration: 158.707 seconds
- Raw artifact directory:
  `benchmarks/artifacts/20260801T052344490Z-campaign-hardware-skill-io-address-index-20260801`

| Trial | Score | Pass | Duration | Reasoning tokens | Patch calls | I/O reads | Recorded cost |
| ---: | ---: | --- | ---: | ---: | ---: | ---: | ---: |
| 1 | 100 | yes | 51.815 s | 1,152 | 2 | 3 | $0.000000 |
| 2 | 100 | yes | 45.338 s | 1,081 | 2 | 3 | $0.000000 |
| 3 | 100 | yes | 44.180 s | 866 | 2 | 3 | $0.000000 |
| **Mean / total** | **100.00** | **3/3** | **47.111 s** | **1,033.00** | **2.00** | **3.00** | **$0.000000** |

The two patch calls per trial are exactly one `dry-run=true` preview and one
`dry-run=false, confirm=true` apply. OpenCode reported zero monetary cost for
every trial, so recorded price per model/task trial and cost per passing trial
were both $0.000000.

## Trace and safety audit

Every clean trial:

- discovered both address rows before writing;
- selected `addressIndex: 1` from the observed Output row;
- used `startAddress: 132` and `expectedStartAddress: 0` in both calls;
- made exactly one preview and one confirmed apply;
- made no patch operation against index 0;
- independently reread the map after apply;
- reported input byte 0 unchanged and output byte 132 verified; and
- left the other hardware modules unchanged.

There were no runtime failures, timeouts, critical failures, or paid requests.

## Decision and next bar

Keep the existing skill unchanged. The only defect found at this bar was in
the evaluator, and the live trace plus regression now cover it. A worthwhile
next step is another shape change rather than more same-shape trials: use two
multi-address modules in one atomic transaction, or deliberately stale the
expected value after discovery and require the model to stop without retrying
or mutating.

## Cleanup

The final disposable project was closed with `save=false`, and the exact TIA
PID launched for this work was stopped. Eight exact fixture-construction,
independent-verification, and campaign trial directories plus the temporary
rescore output were permanently deleted from `C:\OTT\Bench`; they are not
recoverable. Earlier directories were left untouched. Ignored raw campaign
traces remain under `benchmarks/artifacts`, and the V3 archive remains external,
read-only, and matched its recorded size and SHA-256 after the final campaign.
