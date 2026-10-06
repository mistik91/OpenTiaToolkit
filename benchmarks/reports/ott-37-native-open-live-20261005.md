# OTT-37: fresh and reused Agent native-open qualification

## Outcome

**PASS: native opening is now qualified in both a reused and a fresh Agent TIA
instance. The earlier disposed LocalSession failure was not reproduced.**

The exact cause of that earlier rejection remains unproven. Do not attribute
this recovery to stderr draining, licensing, registration, or a TIA restart.
No retry policy, Siemens API workaround, skill correction, or model campaign
was added. The diagnostic/runtime implementation at `71b7196` is unchanged.

Work remains on `codex/ott-37-multiuser-bootstrap`. This closes the live gate
from [the diagnostic checkpoint](ott-37-native-open-diagnostics-20261005.md),
not the still-pending OTT-37 scored case/campaign implementation.

## Readiness and identity

After the user ran the registration helper, read-only checks found exactly one
matching entry in each of the general Siemens Openness allow-list and the
versioned V21 whitelist. Matching includes the absolute Release worker path,
UTC timestamp and Base64 SHA-256, not merely the executable name.

Worker SHA-256:
`BC32FFCF1882A2FBEB3C5F2F8EF28F14B673468DB1E31C1D92E47A4A67616C2E`.
UTC file timestamp: `2026/10/05 11:45:49.829`.

Agent MCP/worker and both selected TIA instances were independently checked
against the Agent Windows SID. Read-only discovery observed TIA V21 Update 2,
the expected HTTPS Project Server connection and the disposable
`OTT37_20261005_IdentitySave` server project. The other four server projects were
not opened or changed. Each worker doctor check passed package compatibility,
API-profile compatibility and disabled failure-injection assertions.

- Reused TIA: PID 22824, already manually initialized under Agent. It was empty
  before the experiment; no restart or licensing action was performed on it.
- Fresh TIA: PID 30852, launched under Agent using the existing DPAPI-protected
  credential, Agent profile/TEMP and a sanitized environment. Process ownership,
  creation time, parent PID and executable were retained. This is an external
  operator diagnostic launch, not automatic benchmark fixture provisioning.
- The fresh instance was selected by its explicit PID, even though discovery
  now reported two attachable Agent instances. The coordinator was never
  selected by these experiments.

No password was passed in process arguments, exported into the child
environment, or placed in model input. No TLS bypass or security-dialog
automation was used. The existing Project Server connection worked in both
instances without an interactive handoff during these runs.

## Native-open experiment

The reused-instance control first created a unique local session,
`OTT37_Agent_WarmDiag_001` (server session ID 6). Native `.als21` opening,
current/conflict-free status and clean close passed. No PLC reads, compilation
or engineering mutation occurred in that initial control.

Then each instance completed three serial cycles in one stateful MCP worker:

1. Require an empty project inventory.
2. Create a uniquely named Multiuser session from the qualified server project.
3. Open its `.als21` launcher natively; require the exact project name, backing
   `.amc21` path and selected TIA PID.
4. Require current, open Multiuser status with zero conflicts and markings.
5. Discover PLCs, export all tags and full block inventories, compile both PLCs,
   and export both complete Main OB XML documents.
6. Explicitly save the local session; require current state and zero conflicts
   and markings again.
7. Close without another save and require an empty inventory.

Reused sessions: `OTT37_Agent_WarmDiag_002` through `_004`.
Fresh sessions: `OTT37_Agent_FreshDiag_001` through `_003`.

All seven native opens passed. All six complete cycles passed without a retry,
and all 12 PLC compilations returned zero errors and warnings. This samples one
fresh instance and one reused instance; it is not a claim of universal or
stress-tested reliability.

## Independent artifact verification

An external deterministic verifier compared the exported files against the
previously qualified committed state, rather than accepting the open/save
messages as preservation evidence. Every complete cycle proved:

- Exactly the five expected tag rows, including every CSV field. PumpEnable
  retained committed address `%M24.0` and its commissioning-revision comment;
  the other four tags retained their exact addresses/types/comments.
- Both complete block inventories matched the baseline.
- Both full Main XML documents matched the original baseline after removing
  only `/Document/DocumentInfo/Created`, the export timestamp. No block-content
  subtree was excluded.
- Worker/TIA identity assertions and native MCP shutdown passed.

No tag/block/hardware mutation or server check-in was attempted. Local save was
exercised on unedited sessions, not presented as another server-persistence
test. The [earlier human check-in and fresh server readback](ott-37-stdio-fixture-checkpoint-20261005.md)
remain the persistence proof. Hardware canonical preservation was not separately
compared; preservation claims here are limited to tags and blocks.

## Evidence and cleanup

Private operator evidence root:
`C:/OpenTiaBench/Artifacts/ott37-native-open-live-20261005/`.

- `warm-readiness-001/`, `warm-native-001/` and `warm-cycles-001/` retain requests,
  structured responses, identity checks, full opted-in worker stderr and native
  shutdown. The cycle directory contains exports and
  `artifacts.verification.json`.
- `fresh-launch.json`, `fresh-readiness-001/` and `fresh-cycles-001/` retain the
  independent launch, explicit selection, exports and corresponding proof.
- Reviewed command JSON, `Start-FreshAgentDiagnostic.ps1` and
  `Verify-NativeOpenArtifacts.ps1` remain alongside the evidence. These are
  operator qualification scripts, not task examples shipped to models.

The fresh diagnostic instance was closed gracefully only after its empty
inventory, SID, executable, parent and creation time were verified. An initial
cleanup guard compared an ISO date string against PowerShell 7's parsed
DateTime and refused to act; inspection established the same exact timestamp,
and a typed UTC-tick comparison passed without weakening PID/ownership checks.
The main window close request then succeeded; PID 30852 and its helper PID
14584 both exited. No forced TIA termination was needed.

All Release qualification MCP/workers exited natively with code zero. The
pre-existing Debug MCP/worker and the four pre-existing TIA processes (including
both users' helpers) remain untouched. The local diagnostic session files are
retained for evidence; no uncommitted engineering edits exist from these runs.

The baseline archive SHA-256 is unchanged:
`8E6F2738A6332E6741599512745D859A6655AAA3CF4BAB27DDAADD99CD8A8CF2`.
The worker hash is also unchanged after registration and live validation.
No source or skill changed during this continuation, so the passing Release
unit/architecture/IPC, MCP protocol and full benchmark regressions recorded in
the diagnostic checkpoint remain applicable. Report-only validation includes
benchmark definition validation, repository hygiene and `git diff --check`.
No push, PR or merge was performed.

## Next step

Freeze the OTT-37 identity-bound task and exact preservation oracle, then add
identity-aware campaign provisioning. Gate each trial on actual instance,
project/session path, freshness and conflicts; retain the explicit TIA UI
check-in handoff and verify committed artifacts through a newly created
server-derived session. Do not add a mandatory per-trial restart or teach a
recovery workaround when the failure cannot currently be reproduced.

Only then run Flash High trials. These diagnostic cycles are not model trials
and must not contribute to model pass-rate, reasoning-effort or cost metrics.
