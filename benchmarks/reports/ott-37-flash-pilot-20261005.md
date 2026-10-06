# OTT-37 GLM-5.3 Flash High pilot

## Outcome

The first model run completed the requested local edit/save workflow and passed
independent native tag/block/Main/hardware readback. An evaluator false negative
was found: the model proved its context through `list-projects`, but the initial
oracle demanded `get-project-info`. The original 15/100 result and raw trace are
preserved; replay of the unchanged trace after correcting that equivalence is
100/100. No prompt, acceptance vector, fixture, native operation, skill or model
output was changed to obtain this result.

Treat this run as a pilot, not a completed three-trial comparison campaign.
Formal qualification should start on the corrected, clean harness commit. The
pilot session has completed its operator handoff and fresh-server verification;
the Agent TIA instance is ready for another trial. No PR or merge is implied.

## Run evidence

- Source commit at inference: `18f5656800f99c6ab89a9094a0a489edfa5a2d5c`.
- OpenCode 1.18.10; `zai-coding-plan/glm-5.3-flash`, explicit `high` variant and
  High reasoning effort, provider-scoped z.ai subscription auth.
- Model duration: 368.225 seconds. Reported cost: $0; this is subscription usage,
  not a claim that subscription quota was unconsumed.
- Reported input 60,086, output 3,683, reasoning 2,412 and total 1,872,901 tokens;
  the total includes OpenCode's cache-token accounting.
- Agent TIA PID 22824. During inference, live MCP PID 10352 and worker PID 19592
  were independently observed under Agent's SID, not the coordinator's SID.
- Unique server project `OTT37_20261005131842_a3169749`; private Agent session
  `OTT37_20261005131842_a3169749_Agent`, server session ID 1.

Only PLC_Process/PumpEnable changed to `%M24.0` and the commissioning revision A
comment. The exact selector and before/after snapshots, one confirmed apply,
two clean PLC compiles, explicit local save, current/conflict-free status and
sole Agent-owned marking were verified. All other tags, both full block
inventories, both complete Main exports and both complete hardware trees matched
the frozen baseline. The final answer explicitly handed off pending UI check-in
and did not claim server persistence.

One parallel read was rejected with `Another TIA worker operation is active`.
The model retried that read serially and recovered. This was not a malformed
filter argument or an unsafe write; existing workflow guidance already requires
serialized TIA calls. No skill correction is indicated by this pilot.

Raw trace, metadata, native tool-output captures, original `result.json` and
separate `result.context-equivalence-corrected.json` are retained below:

`C:/OpenTiaBench/Artifacts/ott37-model-campaigns-20261005/20261005T131837231Z-batch-multiuser-identity-glm-5-3-flash-high-20261005/`

Identity-bound baseline/readback proofs and the saved handoff context are below:

`C:/OTT/Bench/20261005T131841752Z-ott-37-t1-FEEA3D4C/multiuser-evidence/`

The batch stopped at `paused-multiuser-handoff`; no next trial started. Temporary
provider auth was removed and model MCP/worker processes exited. Pre-existing
coordinator/debug hosts and both user-owned TIA instances were preserved.

## Generic evaluator correction

Both supported context getters now qualify when called after explicit Agent
attachment and before apply. Inventory evidence must contain exactly one project
with the exact PID and backing path. Only the observed, exact ` (Session)` display
decoration is accepted; arbitrary prefixes/suffixes, foreign paths, missing PIDs
and multiple projects fail. Ordinary read-only device/software-tree/block-search
discovery is permitted; all mutation and foreign-instance restrictions remain.

Regression checks pass in Windows PowerShell 5.1 and PowerShell 7: four positive
routes, 47 adversarial controls and the full scorer's pass/model-failure/
harness-invalid paths. The original deterministic native/fresh-server
qualification remains valid; see [the harness report](ott-37-harness-qualification-20261005.md).

## Operator handoff and server verification

The operator confirmed UI check-in. Fresh native server-derived verification
completed at `2026-10-05T14:23:14.2073980Z` using coordinator PID 11612 and a new
server session ID 2, distinct from the model's session ID 1. The verification
session was `OTT37_20261005131842_a3169749_Verify_afa3d60b` under the coordinator's
profile-local `Documents/OpenTiaSessions` directory.

The server contained the exact requested PumpEnable address and comment change;
all other tags, both complete block inventories, both Main exports and both
hardware trees matched the frozen baseline. Both PLC compiles reported zero
errors and zero warnings. The Agent session was proven current, conflict-free
and unmarked before closing; the fresh verification session also closed with an
empty project inventory. Both TIA processes and existing server projects were
preserved. The fixture archive and registered Release worker hashes were
unchanged.

The hash-bound `completion.json`, owner-close journal and fresh-server snapshot
are retained in the pilot's private evidence directory above. Independent
`Assert-MuHandoffCompletion` replay passed. This is operator-assisted check-in
plus independent server verification, not an automated model check-in. The
original local-save score and separately corrected replay remain unchanged.
