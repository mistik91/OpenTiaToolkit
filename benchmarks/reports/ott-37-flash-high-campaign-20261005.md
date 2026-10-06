# OTT-37 GLM-5.3 Flash High formal campaign

## Outcome

PASS: all three unchanged model traces qualify at 100/100 with the corrected
evaluator. All three operator-assisted check-ins independently pass fresh native
server-derived artifact verification. The pinned batch is complete, with no
pending handoff. No skill, task prompt, acceptance vector, fixture or native
runtime change was needed.

The original scores are retained: 100, 15, 100. Trial 2's failure was an evaluator
false negative: the scope allow-list omitted the MCP host's read-only
`operation-status` diagnostic. Its trace and native state already proved the
complete requested local edit/save workflow. The corrected replay is separate
from the original results; it is not a new model campaign or a rewritten trace.

This campaign qualifies separate-account attachment, an exact narrow tag edit,
explicit local save, an honest pending UI handoff and subsequent server
persistence. It does not qualify concurrent peer editing, marking contention,
conflict resolution or stale-session reapplication. Those remain future cases.

## Pinned execution and per-task results

- Inference commit: `676c743e1ca5026403e09e128a818f4167ff64ea`, clean for all three
  trials on `codex/ott-37-multiuser-bootstrap`.
- OpenCode 1.18.10; `zai-coding-plan/glm-5.3-flash`, explicit High reasoning effort
  and `high` variant; provider-scoped z.ai subscription authentication.
- Three unique server projects, independent baseline sessions and native
  readbacks. The earlier pilot is excluded from this formal cohort.
- Agent TIA PID 22824 and coordinator TIA PID 11612 ran under separate Windows
  SIDs. Each model MCP/worker pair was independently observed under Agent's SID.

| Trial | Original score | Corrected replay | Model seconds | Reported cost/task | Native server verification |
| --- | ---: | ---: | ---: | ---: | --- |
| 1 | 100 | 100 | 338.667 | $0 | PASS, fresh session ID 2 |
| 2 | 15 | 100 | 337.620 | $0 | PASS, fresh session ID 2 |
| 3 | 100 | 100 | 336.834 | $0 | PASS, fresh session ID 3 |

Mean model duration is 337.707 seconds, approximately 5 minutes 38 seconds.
This excludes manual UI handoff and coordinator verification time. Reported $0
cost is subscription usage, not a claim that quota was unconsumed or that the
model is free. Original pass rate is 2/3; corrected replay pass rate is 3/3.

Reported input/output/reasoning tokens were respectively 110,035/3,936/1,676;
65,017/4,342/2,384; and 59,796/3,077/2,100. OpenCode's reported totals also include
cache-token accounting. No provider rate-limit failure occurred in the cohort.

## Native acceptance and identity evidence

Only PLC_Process/PumpEnable changed: `%M20.0` to `%M24.0`, with comment
`Process pump enable - commissioning revision A`. The exact selector,
before/after snapshots, successful preview, one confirmed apply, two clean PLC
compiles, explicit local save, current/conflict-free status and sole owned
PumpEnable marking passed in each trial. All other tags, both full block
inventories, both Main exports and both full hardware trees were preserved.

Observed model MCP/worker PIDs were 3000/35504, 36908/35684 and 26852/29436 for
trials 1, 2 and 3. The native probes independently verify the owning Windows SID,
selected TIA PID and exact session backing path. They do not infer identity from
the agent's final prose.

All three final model answers explicitly left check-in pending in the owning
TIA UI and did not claim server persistence. The user completed each check-in.
Trial 1 omitted the optional check-in comment; trials 2 and 3 included a comment.
The comment is not a frozen acceptance criterion. This is operator-assisted UI
check-in, not an automated model or Openness server commit.

Fresh coordinator verification sessions were:

- Trial 1: `OTT37_20261005142440_3a39c196_Verify_78336b09`, verified at
  `2026-10-05T14:49:47.7629511Z`.
- Trial 2: `OTT37_20261005145030_15a56b43_Verify_fea26fb5`, verified at
  `2026-10-05T15:56:41.822193Z`.
- Trial 3: `OTT37_20261005155746_6eee8a4f_Verify_07eee3d6`, verified at
  `2026-10-05T16:32:17.1501323Z`.

Each new session contained the exact requested change and passed full native
preservation checks. Both PLCs compiled with zero errors and warnings. Each
session was safely closed and ended with an empty project inventory. All three
hash-bound completions passed independent `Assert-MuHandoffCompletion` replay.

## Trial 3 post-check-in freshness and safe recovery

The first post-UI owner check returned successful native reads but
`IsUpToDate=false`, zero markings and zero conflicts. The expected freshness
comparison failed before any close command; the probe worker exited cleanly.
The stale state remained on a separate read-only recheck. A separate fresh
server-derived diagnostic already proved successful check-in and full artifact
preservation, but was not substituted for the owner cleanup guard.

After the user refreshed the original local session in the TIA UI, a new
`post-ui-owner-refreshed` phase proved the exact owner/context and current,
unmarked, conflict-free state before closing. An evidence-local recovery script
was text-compared against the pinned completion script: only absolute module/case
paths and the new phase name differed. No guard was weakened. A second fresh
server verification then issued the final completion proof. The original failed,
recheck and diagnostic phases remain intact.

The precise UI choice that initially left the owner stale is not proven. The
successful user refresh is recovery evidence, not a reason to score away or
hide the earlier cleanup failure. The batch was finalized on clean commit
`676c743e` before modifying scorer source.

## Generic evaluator correction

Trial 2 attempted parallel block reads; one was rejected with
`Another TIA worker operation is active`. It called `opentia_operation-status`,
observed no active operation and retried the read serially. The existing router
and PLC guidance already recommends inspecting operation status after an active
operation rejection. This was valid read recovery, not an unsafe mutation.

`RemoteToolRegistry.StatusOutcome` only reads the active-operation snapshot under
a lock; the MCP host handles this diagnostic before worker dispatch. The oracle
now permits exactly this normalized tool name. Cancellation, worker/session
reset, server commit, session provisioning, foreign targets and unrelated writes
remain forbidden. Diagnostic output cannot replace project identity, initial
freshness, explicit save or any native state-preservation proof.

The change adds no task-specific examples to a skill. Existing serial-call
guidance is still appropriate; the recoverable parallel read rejection is
retained in trial 2's raw evidence, not presented as flawless execution.

## Reproducibility and preserved evidence

Formal batch root:

`C:/OpenTiaBench/Artifacts/ott37-model-campaigns-20261005/20261005T142435099Z-batch-multiuser-identity-glm-5-3-flash-high-20261005/`

This contains the completed checkpoint, unchanged original
`merged-campaign-result.json`, all original `result.json`, raw traces, metadata
and native tool-output captures. Each trial also has a separate
`result.operation-status-corrected.json`.

`operation-status-corrected-replay.json` binds SHA-256 values for every original
trace, metadata and result, the original completed checkpoint and merged result,
the corrected results, completion proofs and evaluator sources. Original file
hashes were checked unchanged after replay. Replay ran at
`2026-10-05T16:36:45.4063091Z`; inference metadata keeps its original commit.

Evaluator source hashes at replay (actual working-file bytes, not Git newline
normalization):

- `MultiuserCaseAssertions.ps1`:
  `348A16D5A6130D3056FAC6C16FC61BD90A4DA9EDCFCFF85EC2DA3B448D459B32`.
- `Test-MultiuserCaseAssertions.ps1`:
  `D68521FE3502E5DD1A43BDAC9CFA980B89D4B4E3F491267B6BC77AA688E4D168`.

Private baseline, local readback and server-completion evidence roots:

- `C:/OTT/Bench/20261005T142439981Z-ott-37-t1-FEEA3D4C/multiuser-evidence/`
- `C:/OTT/Bench/20261005T145030083Z-ott-37-t2-FEEA3D4C/multiuser-evidence/`
- `C:/OTT/Bench/20261005T155745995Z-ott-37-t3-FEEA3D4C/multiuser-evidence/`

Frozen acceptance SHA-256:
`43216c1284f0b73a772735eb70d28d3a6258a1c7bd0e26dc91a1d157d12b65c9`.
Fixture archive SHA-256:
`8E6F2738A6332E6741599512745D859A6655AAA3CF4BAB27DDAADD99CD8A8CF2`.
Exact registered Release worker SHA-256:
`BC32FFCF1882A2FBEB3C5F2F8EF28F14B673468DB1E31C1D92E47A4A67616C2E`.
Archive and worker hashes remained unchanged; no executable was rebuilt.

## Regression and cleanup

PASS: focused Multiuser oracle in Windows PowerShell 5.1 and PowerShell 7,
six positive routes and 53 adversarial controls in each. Coverage includes idle
and active/idle/retry diagnostics, JSON-text envelopes, full scorer pass,
model-state failure and harness-invalid classification. Newly added rejection
controls cover cancellation, reset, unknown diagnostic names and using the
diagnostic in place of identity, freshness or explicit save.

PASS: full `npm test`, including pretest, all domain scorers, batch/merge/recovery
tests and posttest Multiuser setup controls. Definitions validate 30 cases,
73 campaigns, 16 fixtures and all referenced skills. No benchmark regression
failed.

PASS: generated command reference current (186 commands), packaged skill drift
zero, repository hygiene and `git diff --check`. No canonical or installed skill
changed. No C# or native executable change required a build or new allow-list
registration; the exact registered worker was used for all live checks.

Final process audit preserved both user-owned TIA instances and helper children,
Project Server processes and the pre-existing Debug MCP/worker. No campaign-owned
Release MCP/worker remained. Both TIA project inventories ended empty. Server
projects, private sessions, fixture archive and all campaign evidence were
retained. No PR, push or merge is implied by this local qualification.
