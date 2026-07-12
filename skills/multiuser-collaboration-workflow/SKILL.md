---
name: multiuser-collaboration-workflow
description: Use for Siemens TIA Portal Project Server and Multiuser collaboration through OpenTiaToolkit, including server connection discovery, publishing projects, creating agent-owned local sessions, identity and certificate readiness, session freshness and marking checks, conflict-safe editing, explicit local save, server-project check-in, revision verification, stale-session recreation, and full agent-session smoke testing.
---

# Multiuser Collaboration Workflow

## Overview

Use a dedicated Windows account, TIA engineering station, and local Multiuser session for each agent. Ordinary PLC/HMI commands work after the `.als<version>` session is open, but session creation, freshness/conflict checks, save, and check-in are explicit operations.

Project Server authorization uses the Windows identity running TIA Portal and OpenTiaToolkit. A command password cannot impersonate another Windows account. UMAC environment credentials apply only to protected TIA projects.

## Machine Readiness

1. Confirm the process runs as the intended agent Windows identity. Do not claim that account was tested from another user's session.
2. Confirm the account belongs to `Siemens TIA Openness` and has Project Server permissions.
3. Confirm the Project Server certificate is trusted and its hostname matches the configured HTTPS endpoint. Do not bypass TLS validation.
4. Establish TIA context with `basic-tia-interaction`.
5. Inspect connections and projects:
   - `list-multiuser-servers`
   - `list-multiuser-projects --server-alias <alias>`
6. Use `ensure-multiuser-server` only when creating or intentionally updating a non-default connection. Siemens does not allow editing the built-in default connection.

For a repeatable identity-bound test, run `scripts/Test-MultiuserAgentSession.ps1` from this repository while logged in as the agent account. It refuses an unexpected identity and commits only with `-ConfirmCommit`.

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

## Check-In

Use:

```text
commit-multiuser-session \
  --comment <clear change summary> \
  --require-up-to-date=true \
  --require-no-conflicts=true \
  --confirm=true
```

The command saves and closes the engineering-session view, opens the associated server-project view, calls Siemens `CloseAndCommit`, and returns the created revision. Report that revision.

Do not replace this with `close-project`: Siemens `LocalSession.Close()` discards pending edits. `close-project --save=true` preserves local edits but does not check them into the server.

## Stale Sessions And Recovery

V21 Openness exposes `IsUptoDate()` but no public refresh operation. When another user commits:

1. Save or check in the agent's conflict-free work first; never destroy uncommitted work silently.
2. Close the old session.
3. Create a new uniquely named agent session from the latest server revision.
4. Open it and require a clean, current status before continuing.

If the server is unreachable, keep edits local with `save-multiuser-session`; do not claim they reached the server. If a CLI/MCP timeout hides an operation result, inspect operation status and verify server revision/session state before retrying.

## Full Agent-Session Test

Run the smoke harness from a desktop session logged in as the agent account:

```powershell
.\scripts\Test-MultiuserAgentSession.ps1 `
  -ExpectedIdentity Agent `
  -ServerAlias "Agent Local Server" `
  -ProjectName OpenTiaAgentE2E `
  -SessionDirectory "E:\Documents\Automation\Sessions" `
  -ConfirmCommit
```

A complete test must prove: expected Windows identity, TIA connection, server/project visibility, local-session creation/open, current and conflict-free status, explicit save, successful guarded check-in, and returned revision. Use a disposable server project for commit tests.
