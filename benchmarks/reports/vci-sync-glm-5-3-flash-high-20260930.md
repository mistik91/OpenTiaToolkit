# PASS — bidirectional VCI GLM-5.3 Flash High verification

Date: 2026-09-30

## Outcome

GLM-5.3 Flash High qualifies for `ott-31`: three valid trials passed at
98/100/98, with a mean score of 98.67 and mean duration of 271.177 seconds
(4 minutes 31 seconds). Both synchronization directions, validated incoming
source, preservation evidence, final Equal mappings, and clean compilation
passed in every trial. The GLM-5.3 High fallback was not needed or run.

This is qualification for this bounded two-FC SCL workflow, not for every VCI
operation. Existing-workspace ambiguity, stale capabilities, bulk operations,
and additional PLC object formats remain outside this case.

## Configuration

- Branch: `codex/vci-sync-benchmark`
- Execution and evaluator commit: `131cc41cc6073606d05c5589b17cb80fdfc99830`
- Campaign: `vci-sync-glm-5-3-flash-high-20260930`
- Case / fixture: `ott-31` / `tia-v21-vci-10`
- Model: `zai-coding-plan/glm-5.3-flash`
- Reasoning effort / OpenCode variant: `high` / `high`
- OpenCode: pinned `1.18.10`
- Authentication: provider-scoped copy of the user's OpenCode z.ai subscription
- TIA Portal: V21 Update 2, build `2100.0.121.1`
- PLC / project: `PLC_Main` / disposable `OTT_Benchmark` restores
- Archive SHA-256:
  `B058929F708DB5A5781428238D04B627C4611A0F15C40CE46D4CCC742E76C773`

Every trial started from a fresh restore, used a separate filesystem workspace,
and recorded the same clean Git state. The checkpoint completed all three
items without a checkpoint-level retry, endpoint failure, rate-limit pause,
or harness failure.

## Trials and cost

| Trial | Score | Passed / valid | Duration | Reasoning tokens | Reported cost |
| ---: | ---: | --- | ---: | ---: | ---: |
| 1 | 98 | yes / yes | 262.002 s | 2,104 | $0 |
| 2 | 100 | yes / yes | 286.101 s | 2,456 | $0 |
| 3 | 98 | yes / yes | 265.428 s | 3,564 | $0 |
| Mean | 98.67 | 3/3 | 271.177 s | 2,708 | $0 |

Total reported incremental API cost is $0. This is subscription-backed use,
not a claim that the service or consumed subscription quota is free. The
runner does not measure remaining z.ai subscription quota.

The two 98-point trials did not explicitly load the secondary
`plc-code-engineering` skill. Both loaded the VCI skill and completed all
critical workflow assertions, including exact-format source validation.
Trial 2 loaded both skills and scored 100.

Trial 1 also had one recovered read-only `list-vci-workspaces` rejection:
the model overlapped TIA calls and the serial worker returned `Conflict` /
"Another TIA worker operation is active." It retried successfully, with no
mutation from the rejected call. Trials 2 and 3 had no tool errors. All three
had zero artifact-validator interventions. The existing skills already require
serial MCP calls, so this observation does not justify more task-specific prose.

## Independent verification

The retained baseline and distractor source hashes matched the gold fixture
in all trials. Each restored project-source workspace file also matched its
original `+10` source hash. The incoming target read back `+25`; interfaces and
other logic remained unchanged. Trial 2 additionally normalized insignificant
whitespace, which is not a behavioral difference.

After model execution, a separate repository MCP session attached to campaign
TIA PID 13188 and independently verified the exact final disposable project:

`C:\OTT\Bench\20260930T204317997Z-ott-31-t3-FEEA3D4C\restored\OTT_Benchmark\OTT_Benchmark.ap21`

Fresh mapping discovery returned exactly the two intended FC mappings, both
`SCL` and `Equal`. A fresh compile returned 0 errors and 0 warnings. Independent
native-source exports to
`E:\Documents\OpenTiaBench\Live\20260930-vci-final-audit-13188`
proved these implementations and hashes:

| Block | Constant | SHA-256 |
| --- | ---: | --- |
| `OTT31_ProjectSource` | 10 | `9DB2505B49C737242470904971E360C1D9CB6C292F1ED971E873982E3FDD7A46` |
| `OTT31_WorkspaceTarget` | 25 | `2EDB2BA9FCD8CD8E7C020D7F0BA84072CF5E96200C5ECE88F6F435DC8F4C8106` |
| `OTT31_Distractor` | 30 | `60794504D85A62942A14DC8F30CD937F6B7C105F569E8DB8A4A69D68749A3DA8` |

## Generic skill and evaluator hardening

The independently executed [gold audit](ott-31-gold-audit-20260930.md)
established a generic V21 ordering constraint: supported block formats are
enumerated through existing workspaces. An empty format list before any
workspace exists is not unsupported-block evidence. The canonical VCI skill,
all four plugin copies, and the explicitly selected installed Codex copy now
require workspace creation before format discovery. No fixture names, constants,
or benchmark solution were added to the skill.

Two earlier pilot cohorts were excluded from qualification. They exposed
evaluator false negatives for array-versus-paginated mapping output, equivalent
SCL format casing, redundant dedicated status reads when synchronization and
final mappings already prove Equal, and a shortened distractor name in the
summary. An overly broad sequence matcher also caused excessive scoring time
and was replaced with focused checks. The prompt's baseline step now uses an
independent native-source export, preserving the BOM through the supported
text-editing workflow. Original pilot traces/results and separate replays remain
preserved; none are pooled with this final frozen three-trial cohort.

This campaign is not a paired skill ablation and does not establish a causal
performance improvement from the prose change. It validates the corrected
contract and the final task under the shipped skill.

## Validation, artifacts, and cleanup

- Full `npm test`: passed; 24 cases, 54 campaigns, and 11 fixtures.
- VCI synthetic positive and negative controls: passed, including format-order,
  missing/wrong-path validation, wrong direction, bulk sync, distractor mapping,
  stale baseline/readback, and forbidden save.
- Canonical skill quick validation: passed.
- All packaged skills and the selected active VCI skill: zero drift.
- `git diff --check`: passed.
- No runtime C# or command surface changed; runtime rebuild/integration suites
  were not repeated for this benchmark/skill-only slice.

Authoritative ignored final checkpoint, original traces, per-trial results,
catalog snapshots, and merged qualification result:

`benchmarks/artifacts/20260930T203338545Z-batch-vci-sync-glm-5-3-flash-high-20260930`

Excluded pilot roots:

- `benchmarks/artifacts/20260930T173806658Z-batch-vci-sync-glm-5-3-flash-high-20260930`
- `benchmarks/artifacts/20260930T175042986Z-batch-vci-sync-glm-5-3-flash-high-20260930`

The final disposable project was explicitly closed without saving, and PID
13188 exited; fresh instance discovery found no TIA portals. No campaign-owned
OpenCode, CLI, Release worker, or testhost remained. The pre-existing repository
Debug MCP/worker were preserved. The immutable 5,595,516-byte archive remained
read-only and retained its declared SHA-256. A remote fetch confirmed the
feature branch was not behind `origin/master`. No merge was performed.
