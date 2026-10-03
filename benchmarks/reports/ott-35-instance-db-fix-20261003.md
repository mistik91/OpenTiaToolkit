# OTT-35 generic instance-DB correction — 2026-10-03

## Outcome: BLOCKED at fresh live validation

The generic correction is implemented and local validation passes. No new live
probe or model campaign has run with the rebuilt worker. Its Siemens allow-list
identity changed, and the current Codex session is not elevated. The user has been
given the exact registration command; do not bypass that gate or report gold
qualification as complete.

This continues the independently recovered incident recorded in
[the preflight report](ott-35-behavior-preflight-20261003.md). No benchmark-specific
skill instructions, production project imports, or model quota were used.

## Correction and deliberately bounded support

- Native SCL validation distinguishes source-bound FB instance DBs from ordinary
  global DBs and source-bound UDT global DBs. Quoted names alone are not subtype
  evidence: the unique referenced FB or TYPE must be declared in the same source.
  Unresolved or ambiguous bindings are rejected before import and external binding
  names remain visible as dependencies.
- Commented-out declarations cannot establish that binding. Duplicate block names
  across subtypes are rejected; reported declaration lines point to the keyword.
- New native instance DBs are canonically exported through the exact Siemens XML
  object route, not an unsupported SIMATIC SD recreate route. Existing native
  instance-DB replacement is explicitly blocked before import because lossless
  restoration is not supported by this correction.
- Exact block resolution now treats a same-name, same-group, wrong-subtype object
  as a collision, even for `allowMissing`. Rollback must retain recovery evidence
  rather than claim absence or delete an object of an unproven type.
- Behavior cleanup includes instance DBs among disposable tag owners, while package
  validation still requires a separate global contract DB and scheduler OB.
- Validator catalog is now `plc-code-formats/1.5`; old qualification signatures are
  intentionally not reused under the corrected declaration contract.

No command names, arguments, or skill packages changed.

## Local validation: PASS

- Final Release solution build: zero errors, zero warnings.
- Final non-integration suite: **688 passed**, zero failed/skipped (14 added cases).
  TRX: `E:/Documents/OpenTiaBench/Artifacts/ott35-instance-db-fix/unit/ott35-instance-db-final-unit.trx`.
- Final architecture suite: **54 passed**, zero failed/skipped.
  TRX: `E:/Documents/OpenTiaBench/Artifacts/ott35-instance-db-fix/architecture/ott35-instance-db-final-architecture.trx`.
- Regression coverage includes quoted/unquoted FB bindings, keyword-bearing quoted
  names, UDT global DB distinction, ambiguous/external bindings, comment decoys,
  cross-subtype duplicates, XML canonical export, compile/export failure cleanup,
  instance-before-FB deletion, wrong-subtype recovery retention despite clean
  compilation, existing-instance replacement denial, and the global contract gate.
- CLI artifact validation now reports the unchanged candidate's `OTT35_Instance`
  as `InstanceDB`. Static `Classification=verified` is not a live behavior pass.
- Generated command-reference check: current, **186 commands**.
- MCP modern/legacy stdio smoke: passed, **189 tools** (186 catalog + 3 infrastructure),
  including startup/status, exposure parity, malformed requests and pre-write policy
  denials. No live-project option was used.
- Packaged skill drift check: zero drift; no skills were edited.
- `git diff --check`: passed. The prior complete benchmark scorer/batching regression
  pass belongs to scaffold commit `401c230`; harness/scorer code is unchanged here.

## Required environment step

Run this in an administrator PowerShell after the final build:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File "E:\Documents\Codex\OpenTiaToolkit\scripts\Register-OpennessAllowList.ps1" -ExecutablePath "E:\Documents\Codex\OpenTiaToolkit\src\OpenTiaToolkit.Worker.V21\bin\Release\net48\OpenTiaToolkit.Worker.V21.exe" -PortalVersion 21.0
```

Final observed worker identity:

- UTC modification time: `2026/10/03 07:20:01.589`.
- SHA-256 (base64): `oZfiecCQy72Pbo/qTcdLTY/H5ckk/vAruzEgzO9g0cA=`.
- Exact current V21 allow-list match: absent at the checkpoint audit.

Recheck the executable identity after registration; rebuilding again invalidates
this historical identity. The old bounded simulator lease has expired normally.
Start a fresh owned simulator only after confirming no other instance is registered.

## Remaining required gates: NOT RUN

After registration, perform current Release read-only discovery, verify the exact
original project without probe mutation, restore the read-only archive independently,
and run the unchanged stateful gold package/vector with forced fresh evidence.
Then force a real later-source/compile failure on another disposable restore and
prove rollback from fresh inventories, Main hashes, compilation and simulator state.
Reopen a disposable baseline in a fresh worker/TIA session for final health reads.

Gold behavioral qualification, semantic negative controls, and GLM model trials
remain pending. The development workflow's worker-registration and Tier 4 live
gates cause this pause; local test success is not a substitute.

Archive integrity was rechecked without mutation:

- Behavior archive SHA-256:
  `D2A66124EC3269132DAD9FF5149DA0E73E8C6EA2897E9A62DDA80F7E9F3017FE`.
- Parent archive SHA-256:
  `1C17F223F00F0A43EFD5E51A7C8E2DBE4DD424540AE0C9A799F102997205AB74`.

All short-lived local test/CLI/MCP sessions ended. The user's TIA instance and
the chat's managed MCP/Debug worker are not campaign-owned disposal targets.
