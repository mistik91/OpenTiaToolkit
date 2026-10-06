# OTT-37: identity-bound transport and fixture checkpoint

## Outcome

**PASS for separate-account stdio, OpenCode connection, identity-bound local
edit/save, human TIA UI check-in and exact fresh server-derived persistence
verification. Automatic campaign provisioning and model trials remain pending.**
Work remains on
`codex/ott-37-multiuser-bootstrap`, following bootstrap commit `32047a2`. No push,
PR, merge, skill modification, physical PLC download or provider inference was
performed. The 29-case/72-campaign/15-fixture scored suite is unchanged; OTT-37
must not be counted as a passing or failing model campaign yet.

## Implemented transport

`Start-MultiuserAgentMcp.ps1` uses the existing private DPAPI `multiuser-agent`
credential only in the coordinator. Windows credentialed process creation runs
MCP as Agent with a loaded profile; the actual owner SID is independently checked
before forwarding requests. The registered Release worker path is pinned. The
child environment excludes coordinator model-auth/XDG state and uses Agent
profile paths plus an explicit per-run temp directory inside allowed MCP roots.
The bridge did not add the stored password to source, model prompts,
configuration, process arguments, protocol streams, or evidence.

`MultiuserStdioRelay.cs` transports raw stdin/stdout/stderr independently. A first
real initialize request exposed buffered small writes that the initial EOF-only
test missed. Per-chunk flush and an interactive-before-EOF regression corrected
that transport defect. It preserves binary/UTF-8 bytes and native child exits.
EOF permits bounded shutdown; exit 124 reports a still-running child, never
forced TIA termination or successful cleanup.

The deterministic MCP driver retains pre-send requests, responses, identities,
native shutdown and failures in new evidence directories. It checks doctor
health/path, worker/TIA owner SIDs and optional typed exact-result assertions.
Missing, wrong-type, stale, conflicted or non-exact proof stops the sequence.
Success is written only after protocol work and native relay shutdown pass.

Live Agent MCP/worker/TIA ownership passed with the exact Release worker. Initial
read-only discovery, attachment, empty-project inventory, server connections and
existing-server authorization passed. MCP exposed 189 tools: 186 runtime commands
plus three infrastructure tools. OpenCode's pinned CLI accepted the explicit
relay configuration and reported `opentia connected` with isolated XDG roots and
`--pure`; no model run or provider authentication was requested. Both host and
worker exited after that connection check. General benchmark-runner automatic
alternate-identity fixture provisioning is still pending, not silently enabled.

Transport evidence:

- `C:/OpenTiaBench/Artifacts/ott37-agent-mcp-qualified-20261005/`
- `C:/OpenTiaBench/Artifacts/ott37-opencode-mcp-20261005/`

## Disposable fixture

A new single-user project and server project named
`OTT37_20261005_IdentitySave` were created; the four existing server projects were
not opened or modified. Two independently owned TIA instances passed identity
and server-access checks. Agent and coordinator each created a unique local
Multiuser session under their respective private profile directories.

The [fixture contract](../fixtures/builders/ott-37/README.md) records two PLCs,
five exact baseline tags, preservation requirements and the candidate narrow
change. Both PLC software compilations returned zero errors and warnings.
Baseline CSV readback matched every address/type/comment. The single-user
baseline was saved, archived, closed and published through reviewed toolkit
commands. This is not evidence that a local-session edit was saved or checked in.

Archive: 424,908 bytes; SHA-256
`8E6F2738A6332E6741599512745D859A6655AAA3CF4BAB27DDAADD99CD8A8CF2`.
The final hash equals the original captured hash.

Qualification evidence and reviewed command scripts:
`C:/OpenTiaBench/Artifacts/ott37-qualification-20261005/`.

## Rejections and startup boundary

These failures remain preserved, not erased or reclassified as model errors:

- Module-type discovery rejected the supplied partial type identifier. Ordinary catalog
  discovery then succeeded without optional culture. Supplying `culture=en-US`
  failed with Siemens reporting it as an unknown TypeIdentifier; this separate
  existing command issue was recorded, not patched or taught around in skills.
- Initial tag apply lacked global MCP confirmation and was denied before writes.
  The reviewed previews were repeated with explicit `confirm=true`; exact
  baseline writes and readback then passed.
- Session creation initially rejected a nonexistent destination. The Agent
  created the private directory. A legacy direct credentialed smoke process
  then failed writing its command file to inherited coordinator TEMP; it did not
  create a session. The environment-isolated MCP relay subsequently created the
  Agent session, and the coordinator created its own separate session.
- Opening the Agent session returned the documented disposed LocalSession
  error, before any local engineering edit. The multiuser workflow required a
  fresh test TIA process. Windows control listed both windows but capture failed
  with `FrameArrived timed out`; activation recovery failed with `GetCursorPos`
  access denied. No successful UI automation or security-dialog action occurred.
- A fresh independent read proved the old harness-owned Agent TIA empty.
  SID, process name, ancestry and creation-time checks guarded stopping only
  owned Agent PIDs 21000/6460. The coordinator PID 11612 stayed intact. The
  credential launcher started new Agent PID 1632 with verified ownership.
- The new instance failed to complete attachment while starting. The retained
  request sequence stopped at `connect-instance`; create/open/edit/save requests
  were never sent. Client timeout and relay exit 124 correctly reported pending
  cleanup. Later the Agent TIA/MCP/worker exited; their exact reason is unproven.
  A guarded cleanup refused to act because its expected processes were already
  gone. It did not target another process or the coordinator.

The multiuser skill drove the safe restart and stop-at-handoff boundary; the
development, hardware and PLC workflows drove exact scope, confirmation,
preview/readback and compilation. Skills were not tuned to this fixture.

## Validation

- Six byte-exact relay scenarios pass under both Windows PowerShell 5.1 and
  PowerShell 7, compiling the relay in the selected host: EOF, UTF-8, 1 MiB binary,
  interactive-before-EOF, child exit 7, and bounded pending shutdown. The last
  fake child independently writes completion proof after its own timer; the
  relay did not kill it. No real credentials or TIA are used by these tests.
- Eight typed probe assertions pass in both hosts. Existing credential/bootstrap
  checks remain 47 passing cases. All three are included in benchmark pretest.
- Full `npm test` passes, including all existing definitions and oracle suites.
  The test shell temporarily selected native Windows PowerShell module paths;
  no global module setting was changed.
- Release regressions (`--no-build`): 765 non-integration unit tests and 54
  architecture tests pass. TRX files are under
  `C:/OpenTiaBench/Artifacts/ott37-stdio-validation-20261005/`.
- Generated reference check passes under its documented Windows PowerShell host:
  186 commands, current. PowerShell 7 gives an existing host-sensitive reference
  comparison failure; generated files were not rewritten to hide that variance.
  Skill packaging has zero drift across 18 canonical skills; repository hygiene
  and diff checks pass.

During the transport/persistence qualification, compiled toolkit hosts and
Siemens adapters were not changed or rebuilt; the
relay is compiled at launch by Add-Type and exercised in both PowerShell hosts.
The registered worker SHA-256 remains
`AC9D244988171BA338990FCD674CD826DC963BECD17B452DC196C26185896987`.

## State at the first checkpoint

Only the intentionally retained coordinator TIA and the pre-existing app Debug
MCP/worker remain. No qualification-owned MCP/worker or Agent TIA remains running.
The private local sessions, published disposable server project and unchanged
baseline archive are retained for continuation. No pending local edit exists
from this qualification.

Next, establish a fully initialized Agent-owned TIA instance with user help if
Windows UI access remains denied. Create/open the replacement session in one
stateful MCP worker, prove current/conflict-free state, perform the reviewed
single-tag change, compile, save and read it back. Complete explicit TIA UI
check-in under Agent, then compare exact artifacts in a new server-derived
session. Only after deterministic persistence passes should the task/oracle and
automatic alternate-identity provisioning be frozen and Flash High trials run.

## Continuation after manual Agent launch

The user manually initialized a new Agent-owned TIA instance, PID 22824. Both
TIA processes were responsive. The identity-bound Release MCP probe attached
successfully, proved matching Agent worker/TIA SIDs, found no open project, and
confirmed server access and the disposable project. This resolves the startup
block for that manually launched instance, not the local-session open failure.

An external operator-gated serial qualification driver retained one MCP worker
across reviewed command batches. It created a new Multiuser session,
`OTT37_Agent_ManualRecovery`, under the Agent profile. The immediately following
`open-project` returned the same disposed `Siemens.Engineering.Multiuser.LocalSession`
error in this fresh instance. The sequence stopped before context/status reads,
tag exports, preview, apply, compilation, or save. No local engineering edit was
made and no check-in or model trial ran. Native relay shutdown returned zero.

Windows computer-use screenshot capture again timed out. A refreshed
accessibility-only observation succeeded and showed the exact new session in
TIA's recent-project list with no project open. Attempting to open that observed
entry failed with `GetCursorPos` access denied. No successful UI input or security
prompt automation occurred. A separate read-only MCP probe afterward again
proved healthy attachment, server access and an empty project inventory.

Evidence is retained under `ott37-qualification-20261005/` in
`agent-manual-readiness/`, `agent-manual-preview/`, and
`agent-after-manual-open-rejection/`; the external stateful driver and reviewed
command scripts are retained alongside them. The baseline archive hash remains
unchanged. All qualification MCP/worker processes exited. The manually launched
Agent TIA and coordinator TIA are intentionally left open; the pre-existing app
Debug MCP/worker are untouched.

The user performed the manual diagnostic and reported that no Multiuser license
is available, then explicitly requested suspension. **At that checkpoint:
SUSPENDED at the user's request, with a missing Multiuser license reported as the
environment blocker.** No further session opening, edits, campaigns or UI actions
were attempted until the user resumed. The exact relationship between the
Siemens disposed-object response and licensing is not independently proven by
the MCP response; the license finding comes from the user's manual diagnostic.
No skill/runtime correction is justified from this incomplete qualification.

Any additional TIA instance started during the user's manual diagnostic is
user-owned and remains untouched. Resume only after Multiuser licensing is
available, re-establish exact identity/project/session context, and complete
local save plus fresh server-derived post-check-in verification before scoring
OTT-37.

## Resumed with the local session open

The user resumed qualification and opened `OTT37_Agent_ManualRecovery` in Agent
TIA PID 22824. Fresh discovery found exactly one attachable Agent instance and
one open Multiuser project. The user's coordinator TIA and the Agent-owned
secondary process were not selected or modified.

The selected project is backed by
`OTT37_Agent_ManualRecovery/LS/OTT37_20261005_IdentitySave.amc21`, not the launcher
`.als21` path. `list-projects` appends ` (Session)` to its display name;
`get-project-info` returns the undecorated project name. An initial deterministic
context assertion incorrectly copied the decorated name into the exact
`get-project-info` expectation. It stopped before engineering reads or edits and
shut down cleanly. The observed name/path/PID contract was then bound correctly
in a new evidence run; the rejected run remains retained.

One Agent-owned MCP worker performed the reviewed discovery, preview, apply,
compilation and local save in serial batches. Before mutation, the session was
current with zero conflicts and zero markings; all five tag rows exactly matched
the baseline. The preview contained one update to PumpEnable, with the intended
before/after snapshots and no failed operations.

The update applied once. Both PLCs compiled with zero errors and warnings.
`save-multiuser-session` succeeded and reported exactly one marking:
`PumpEnable`, type `Siemens.Engineering.SW.Tags.PlcTag`, state
`IsUptoDate, IsMarkedByMe`. The post-save status remained current and conflict-free.
Independent exported CSV comparison passed the intended address/comment change
and preservation of every field of all four other tags. Both block inventories
were exact, and both Main OB XML exports were unchanged except for the export's
`DocumentInfo/Created` timestamp. No block content was excluded from comparison.

Durable evidence is under `agent-license-qualified/`, including the serial
journal, identity proof, reviewed batches, before/after CSV/XML files and
`local-saved.verification.json`. The external
`Verify-QualificationState.ps1` repeats the exact artifact comparisons; it is a
deterministic qualification check, not a model-scored case or skill example.

**Handoff at the local-save checkpoint:** the user was asked to check in the
single marked tag in the Agent-owned TIA UI with comment
`OTT-37 identity-bound save qualification`.
No check-in success or server persistence was claimed then. The saved local
edit remained intact. After explicit UI completion, create a uniquely named
server-derived session and verify the exact intended tag rows and preserved
blocks; do not accept the edited local session or a revision counter as proof.

The qualification driver has now ended via its non-engineering finish marker;
native relay shutdown returned zero. It did not close the user-opened TIA session
or interrupt its UI handoff. No qualification MCP/worker remains running. The
local save proof and marked object remain in the Agent session; both user-owned
TIA instances and the Agent secondary process are retained. The immutable
single-user baseline archive hash still matches the original value.

## UI check-in and fresh server readback

The user supplied screenshots of the Agent-owned check-in editor showing only
PumpEnable pending and TIA's successful check-in dialog. A subsequent Agent
probe proved the same exact session current, conflict-free and with zero
markings. This is a human UI handoff, not automated Openness check-in or a model
trial. The check-in comment was not independently read back.

The coordinator worker independently proved matching coordinator/TIA owner SIDs,
attached to PID 11612 and found no open project. It created a new session from
the server after the check-in, `OTT37_Peer_ServerReadback_001`, session ID 4,
under the coordinator's private profile. Native creation/opening and exact
project name, backing `.amc21` path and TIA PID assertions passed. The fresh
session was current with zero markings and conflicts. Exact five-row tag exports
and both block inventories matched the intended committed result.

The first Main XML export failed with a generic Siemens rejection. Compiling
both PLCs succeeded with zero errors/warnings but did not resolve export; an
export to a private profile path also failed. An external diagnostic launched
the unchanged, hash-pinned Release worker through its existing IPC protocol,
independently checked worker/TIA ownership, enforced live catalog `ReadOnly`
commands and captured worker stderr. Siemens reported:
`Necessary license 'STEP 7 Professional' is missing.` This is an environment
finding for the coordinator instance; it does not justify skill tuning.

The Agent instance still exported Main successfully. A fallback saved and
closed its now-unmarked checked-in session, then created a new Agent verification
session, `OTT37_Agent_ServerReadback_001`, ID 5. Native opening returned the
disposed LocalSession error again. The sequence stopped before any engineering
edit, export or save in that new session. No retry against the poisoned native
session, forced TIA termination or unsaved-work discard was attempted. This
unresolved Agent native-open boundary remains separate from the passing
fresh-session persistence proof and must be accounted for in campaign readiness.

After the user requested another retry, independent Agent discovery found no
open project. The coordinator retry adopted the same exact fresh server-derived
session, compiled both PLCs with zero errors/warnings, and successfully exported
both Main blocks. No toolkit, skill, security or license configuration change
was made by this qualification. It closed the read-only verification session
without saving and proved an empty project inventory afterward.

`Verify-QualificationState.ps1 -Stage server-readback` passed all comparisons:

- Exactly one intended tag row changed: PumpEnable address `%M24.0` and comment
  `Process pump enable - commissioning revision A`.
- Every field of the other four tag rows, and all tag names/types/table names,
  remained exact.
- Both complete block inventories remained exact.
- Both full Main XML exports remained exact after removing only
  `/Document/DocumentInfo/Created`, the export timestamp.

No hardware mutator ran. Hardware canonical preservation has not been separately
compared; the verified preservation claim here is limited to tags and blocks.
The immutable baseline archive SHA-256 still matches its original value.

Evidence remains under `ott37-qualification-20261005/`:

- `peer-post-checkin-discovery/`, `peer-server-readback/` and
  `peer-server-block-verification-retry-001/` retain independent ownership,
  fresh session creation/context, exports, compilation and clean close evidence.
- `agent-license-qualified/server-readback.verification.json` records the exact
  artifact comparison; fresh exported files have `server-readback` in their names.
- `peer-server-block-verification/`, `peer-server-export-path/` and
  `peer-export-diagnostic/` retain the rejected exports and explicit license
  diagnostic. The direct worker diagnostic is not MCP qualification evidence.
- `agent-post-checkin-readiness/`, `agent-server-readback/` and
  `agent-readback-resume-discovery/` retain the successful Agent export, safe
  fallback boundary and empty-session resume discovery.

All MCP/workers started during this post-check-in verification, and the direct
diagnostic worker, exited natively with code zero, including rejected engineering
runs; no orphaned qualification host/worker remains. The pre-existing app Debug
MCP/worker and user-owned TIA
instances/helpers were preserved. Both selected TIA instances are now empty;
their local session files and the disposable server project remain available.

This closes deterministic edit/save/check-in/persistence qualification. It does
not score OTT-37 or prove unattended Agent native session opening. Next, freeze
the case/oracle and add identity-aware campaign provisioning with an explicit
native-open readiness gate and human UI handoff checkpoint. Run Flash High
trials only once that readiness path is qualified. The prose-only checkpoint
update passes `git diff --check` and benchmark definition validation (29 cases,
72 campaigns, 15 fixtures). No compiled source or skill changed, so binaries were
not rebuilt and the existing Release regression evidence above remains the
applicable baseline. No push, PR or merge occurred.

The subsequent [native-open diagnostic checkpoint](ott-37-native-open-diagnostics-20261005.md)
adds staged errors and bounded/opt-in worker stderr capture. Its non-live checks
pass, but fresh Agent experiments require registration of the newly rebuilt
Release worker. The disposed LocalSession root cause is not yet established.
