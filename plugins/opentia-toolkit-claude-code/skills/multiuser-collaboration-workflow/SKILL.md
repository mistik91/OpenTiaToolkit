---
name: multiuser-collaboration-workflow
description: Use for Siemens TIA Portal Project Server and Multiuser collaboration through OpenTiaToolkit, especially same-machine Run As collaboration with separate Windows accounts and TIA instances, server discovery, local-session creation, certificate readiness, identity isolation, markings, conflicts, explicit save, TIA UI check-in handoff, stale-session recreation, and stress testing.
---

# Multiuser Collaboration Workflow

## Overview

Prefer a dedicated Windows account, TIA process, and private local Multiuser session for the agent. A second machine is optional: on one workstation, start a second TIA instance with Run As under the agent account. Ordinary PLC/HMI commands work after the `.als<version>` session is open, but session creation, freshness/conflict checks, save, and check-in remain explicit.

Project Server authorization uses the Windows identity running TIA Portal and OpenTiaToolkit. A command password cannot impersonate another Windows account. UMAC environment credentials apply only to protected TIA projects.

TIA Portal V21 public Openness does not expose check-in of local-session changes. `LocalSession.CloseAndCommit()` commits changes made in an opened server-project view; it does not merge a local session. OpenTiaToolkit therefore blocks `commit-multiuser-session`. Save through Openness, hand off check-in to the TIA UI under the session owner, then verify through a fresh local session.

The UI handoff is an intentional product boundary. A user may check in manually or let the chosen agent harness control the Agent-owned TIA window with computer-use/UI automation. That automation is outside OpenTiaToolkit: do not present it as an Openness capability, require it as a toolkit dependency, or weaken fresh-session verification when it is used.

## Same-Machine Run As Mode

1. Keep the user's normal TIA instance under the user's Windows account.
2. Start a second TIA instance with Run As under the agent account.
3. Run the CLI/MCP worker under that same agent identity. A different account cannot discover or attach to the Agent-owned TIA process.
4. Store sessions under the owning profile, for example `%USERPROFILE%\Documents\OpenTiaSessions`. Do not assume another user's project/session directory is accessible.
5. Allow-list the exact `OpenTiaToolkit.Worker.V21.exe`; CLI and MCP hosts are vendor-free.
6. On first Project Server access after a fresh Run As launch, TIA may show a certificate confirmation in the Agent-owned window. The user must confirm it; do not bypass TLS or automate another user's security dialog.
7. Keep dependent commands in one stateful CLI/MCP worker. Exiting the worker releases its selected project/session context even when TIA stays open.

## Machine Readiness

1. Confirm the process runs as the intended agent Windows identity. Do not claim that account was tested from another user's session.
2. Confirm the account belongs to `Siemens TIA Openness` and has Project Server permissions.
3. Confirm the Project Server certificate is trusted and its hostname matches the configured HTTPS endpoint. Do not bypass TLS validation.
4. Establish TIA context with `basic-tia-interaction`.
5. Inspect connections and projects:
   - `list-multiuser-servers`
   - `list-multiuser-projects --server-alias <alias>`
6. Use `ensure-multiuser-server` only when creating or intentionally updating a non-default connection. Siemens does not allow editing the built-in default connection.

For a repeatable identity-bound save test, run `scripts/Test-MultiuserAgentSession.ps1` under the agent account. It refuses an unexpected identity, defaults to the current profile's Documents directory, and never claims an automated server check-in. `-ConfirmCommit` intentionally fails with the V21 API limitation.

## Session Provisioning

1. Publish a closed single-user project only when requested:
   `add-project-to-multiuser-server --server-alias <alias> --project-path <project.ap21> --confirm=true`
2. Inspect existing sessions:
   `list-multiuser-sessions --server-alias <alias> --project-name <project>`
3. Give the agent its own unique session name and directory.
4. Default to `mode=Multiuser`. Use `Exclusive` only when the target object is not markable in a normal session and the user accepts a server-project lock.
5. Create and open:
   - `create-multiuser-session ... --mode=Multiuser --confirm=true`
   - `open-project --project-path <session.als21>`
6. Immediately call `get-multiuser-session-status`. Do not edit when `isUpToDate` is false or conflicts already exist.

## Editing Workflow

1. Allocate a narrow object scope so the user and agent do not intentionally edit the same objects.
2. Before mutation, inspect relevant markings and domain state.
3. Apply the domain skill's normal preview/dry-run rules.
4. Call `save-multiuser-session --confirm=true` after a coherent edit batch.
5. Re-read `get-multiuser-session-status` after saving and before check-in.
6. Block check-in when stale or conflicted. Do not disable `require-up-to-date` or `require-no-conflicts` merely to force progress.

Automatic marking covers many supported objects, but not every object is markable. If an object is not markable, stop and decide explicitly between server-project editing and an Exclusive session.

## Check-In Handoff

1. Call `save-multiuser-session --confirm=true`.
2. Require `isUpToDate=true` and zero conflicts immediately before handoff.
3. Close with `close-project --save=true --confirm=true` if the automation worker must exit.
4. In the Agent-owned TIA UI, open the same local session and use Siemens Multiuser Check in with a clear comment. This may be done manually or by optional UI automation supplied by the chosen harness.
5. Create a new uniquely named local session from the server and verify the exact PLC/HMI/hardware artifacts. A revision number alone is insufficient evidence.

OpenTiaToolkit owns the API-based preparation, safety checks, save, and post-check-in verification. The harness owns any computer-use implementation, including dialog handling, window focus, localization, timeouts, and user confirmation of security prompts. Treat UI automation failure as an incomplete handoff, never as evidence that changes reached the server.

Do not call `commit-multiuser-session` expecting a merge; it fails safely. Plain `close-project` discards pending edits, while `close-project --save=true` preserves local edits but does not send them to the server.

## Stale Sessions And Recovery

V21 Openness exposes `IsUptoDate()` but no public refresh operation. When another user commits:

1. Save the agent's local work first; never destroy uncommitted work silently.
2. Close the old session.
3. Create a new uniquely named agent session from the latest server revision.
4. Open it and require a clean, current status before continuing.

If the server is unreachable, keep edits local with `save-multiuser-session`; do not claim they reached the server. If a CLI/MCP timeout hides an operation result, inspect operation status and verify server revision/session state before retrying.

If TIA reports a disposed `LocalSession` after repeated create/open cycles, stop the empty test instance, start a fresh TIA process under the same identity, confirm the Project Server certificate if prompted, and create a new session. Do not retry against a poisoned session object.

## Agent-Session Test

Run the smoke harness from a desktop session logged in as the agent account:

```powershell
.\scripts\Test-MultiuserAgentSession.ps1 `
  -ExpectedIdentity Agent `
  -ServerAlias "Agent Local Server" `
  -ProjectName OpenTiaAgentE2E `
  -InstanceId <agent-tia-pid>
```

The automated test proves identity, TIA ownership, server/project visibility, local-session create/open, current/conflict-free status, explicit save, and safe close. Complete persistence testing manually checks in from Agent-owned TIA, creates a fresh verification session, and reads the exact changed objects.

For stress validation on a disposable device-rich project, test: wrong-account attach rejection; separate PLC/HMI edits with both workers alive; marking state; compile under concurrent load; one user check-in making the other session stale; guarded stale handling; fresh-session reapplication; TIA restart; certificate confirmation; and exact artifact verification after UI check-in.
