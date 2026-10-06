# OTT-37 identity-bound Multiuser fixture

The scored case and identity-aware provisioning harness are now implemented.
Native setup, deterministic local-save/oracle qualification, manual UI check-in
and full fresh-server tag/block/Main/hardware verification pass. See
[the harness qualification report](../../../reports/ott-37-harness-qualification-20261005.md)
and [campaign instructions](../../../README.md#identity-bound-multiuser-campaigns-ott-37).
No model result should be inferred from these deterministic checks.

The subsequent [formal Flash High campaign](../../../reports/ott-37-flash-high-campaign-20261005.md)
qualified three unchanged traces and independently verified all three
operator-assisted check-ins. Historical setup checkpoints below remain evidence
of encountered failures, not the current campaign status.

The first real identity-bound Multiuser fixture is a uniquely named disposable
server project, `OTT37_20261005_IdentitySave`, on the existing `Agent Local Server`.
Existing server projects must not be used or overwritten. Each campaign must
publish a new unique project per trial from the immutable qualified baseline.

The baseline is external to the repository:

`C:/OpenTiaBench/Fixtures/OTT37_20261005_IdentitySave/OTT37_20261005_IdentitySave_Baseline.zap21`

SHA-256: `8E6F2738A6332E6741599512745D859A6655AAA3CF4BAB27DDAADD99CD8A8CF2`.

Both PLCs use the live-discovered `CPU 1511-1 PN`,
`OrderNumber:6ES7 511-1AK02-0AB0/V2.9`. Each has one default tag table and an
unchanged empty Main OB. Baseline PLC software compiles with zero errors and
warnings. These are software/collaboration tests; no physical download is allowed.

| PLC | Tag | Type | Address | Baseline comment |
| --- | --- | --- | --- | --- |
| PLC_Process | PumpEnable | Bool | %M20.0 | Process pump enable - baseline |
| PLC_Process | PumpFeedback | Bool | %M20.1 | Preserve process feedback |
| PLC_Process | MaintenancePermit | Bool | %M20.2 | Preserve maintenance permit |
| PLC_Auxiliary | PeerHeartbeat | Bool | %M28.0 | Peer station heartbeat - baseline |
| PLC_Auxiliary | AuxiliaryReady | Bool | %M28.1 | Preserve auxiliary ready |

The two Windows identities own different private local sessions:

- Agent: `C:/Users/Agent/Documents/OpenTiaSessions/OTT37_20261005_IdentitySave/OTT37_Agent_Qualification/OTT37_Agent_Qualification.als21`
- Coordinator/peer: `C:/Users/Coordinator/Documents/OpenTiaSessions/OTT37_20261005_IdentitySave/OTT37_Peer_Qualification/OTT37_Peer_Qualification.als21`

Creation succeeded for both sessions. Initially, opening the Agent session hit a
disposed LocalSession error; a new empty Agent TIA process subsequently failed
to finish attachment. These attempts made no local edit or check-in.
Do not mistake the saved single-user baseline for a saved session modification.

After a manual Agent launch, identity-bound attachment and server discovery
passed. A fresh `OTT37_Agent_ManualRecovery` session was created in the same
private parent directory, but immediate Openness opening again returned the
disposed LocalSession error. Its exact `.als21` file is retained for a manual
open diagnostic. This attempt made no engineering edit.
The user's manual diagnostic found a missing Multiuser license. OTT-37 was
suspended at the user's request at that checkpoint.

The user later resumed and opened `OTT37_Agent_ManualRecovery`. Identity-bound
attachment, exact baseline readback, the narrow tag preview/apply, zero-error
compilation and explicit local save now pass. At the local-save checkpoint, the
session contained one Agent-owned marking on PumpEnable and was current and
conflict-free. Exact tag and
block preservation checks pass. The user completed TIA UI check-in, and exact
fresh server-derived tag/block readback now passes through a new coordinator
session. No model trial had run at that historical checkpoint.

The coordinator's first Main exports reported a missing STEP 7 Professional
license in the retained worker diagnostic. After the user's retry request,
exports and zero-error/warning compilation passed without toolkit/skill changes.
At that earlier checkpoint, Agent native create/open still returned a disposed
LocalSession error when creating a fallback verification session. Readiness
remained unresolved then; that historical checkpoint alone did not qualify
unattended Agent provisioning from manual session adoption and coordinator
readback.

The candidate deterministic Agent change is only PumpEnable's address to %M24.0
and comment to `Process pump enable - commissioning revision A`. The other four
rows, all tag names/types/table locations, both Main OBs and hardware must stay
unchanged. Before editing, prove exact instance/session/server association and
current/conflict-free state. Preview the exact tag operation, confirm apply,
compile, explicitly save the local session and verify markings/status/readback.

Then hand off check-in to the owning TIA UI. Create a new unique server-derived
verification session and compare exact exported rows against the intended change
and preserved baseline. Reading the edited local session or observing a revision
alone cannot pass persistence. Do not weaken checks, call unsupported commit,
use Exclusive mode, or abandon saved work to force progress.

Qualified server readback came from the uniquely created coordinator session
`OTT37_Peer_ServerReadback_001`, not either pre-check-in local copy. All five tag
rows, both complete block inventories and both Main XML exports match; only the
export timestamp was normalized. Hardware canonical comparison was pending then.
The verification session was safely closed, qualification hosts/workers exited
and the immutable baseline archive hash was rechecked unchanged. Before model
campaigns, the next step was to freeze the case/oracle and qualify identity-aware
provisioning with an explicit native-open gate and UI check-in checkpoint.

Qualification requests, responses, baseline CSVs, rejected attempts, and command
scripts are retained under
`C:/OpenTiaBench/Artifacts/ott37-qualification-20261005/`.
See [the historical checkpoint report](../../../reports/ott-37-stdio-fixture-checkpoint-20261005.md).
The later [fresh/reused native-open qualification](../../../reports/ott-37-native-open-live-20261005.md)
did not reproduce that disposed error; its cause remains unproven. The current
campaign harness subsequently passed native provisioning, exact local save,
operator check-in and full fresh-server verification including hardware. Each
model trial uses a newly named project, not this historical qualification copy.
