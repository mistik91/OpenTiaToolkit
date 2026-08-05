# Hardware skill dual-space compaction - 2026-08-01

## PASS - behaviorally saturated

`ott-19` raised the hardware-only task from one explicit address move to four
computed moves across independent Input and Output spaces. DeepSeek V4 Flash
Free still completed the actual engineering behavior correctly in all three
trials at `max` reasoning effort and variant.

The original displayed scores were 95/95/95 because a redundant final-answer
assertion required the prose to say `atomic`, `one patch`, or `single
transaction`. Tool assertions already proved exactly one complete preview and
one complete apply. Commit `9647c8f` removed that wording dependency, changed
the positive regression to omit the phrase, and rescored all three immutable
traces to 100 without relaxing any engineering behavior.

| Trial | Corrected score | Pass | Duration | Reasoning tokens | Cost |
| ---: | ---: | --- | ---: | ---: | ---: |
| 1 | 100 | yes | 80.360 s | 3,101 | $0.000000 |
| 2 | 100 | yes | 71.257 s | 3,480 | $0.000000 |
| 3 | 100 | yes | 68.742 s | 2,714 | $0.000000 |
| **Mean / total** | **100.00** | **3/3** | **73.453 s** | **3,098.33** | **$0.000000** |

Every trace derived Input starts 66/68 and Output starts 130/132 from 16-bit
lengths, selected both indices of `DIDQ_Candidate`, used four guarded operations
in exactly two patch calls, preserved both anchors, and verified all six rows.

Conclusion: keep `ott-19` as a strict regression, but do not use it as the
current model discriminator. Its saturation motivated the cross-domain
hardware/tag migration in `ott-20`.

Raw artifacts:
`benchmarks/artifacts/20260801T054440603Z-campaign-hardware-skill-dual-space-compaction-20260801`.
