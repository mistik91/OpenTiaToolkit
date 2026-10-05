# OTT-36 blueprint coordinator fix — 2026-10-04

## Outcome: local checks PASS; live requalification BLOCKED by registration

The user authorized the generic correction after the independent
[preflight](ott-36-blueprint-preflight-20261004.md) reproduced a transport-successful
HMI safety block being counted as an applied blueprint step. Work remains on
`codex/ott-36-blueprint-benchmark`, following checkpoint `ef1c779`. No skill change,
model trial, provider request, push, PR, or merge has been performed. OTT-36 remains
unregistered until independent live qualification and the scorer pass.

## Generic correction

`BlueprintCoordinator` still validates and consumes the one-use token and checks
the portal/project fingerprint before execution. A vendor-free
`BlueprintStepExecutor` now distinguishes transport success from completion:

- Root `SafetyBlocked`, `ValidationBlocked`, `RolledBack`, or `DryRun` set to true,
  or `Applied`, `Success`, `Verified`, or `StateMatched` set to false, makes the step
  incomplete. Known fields with non-boolean values fail closed. Matching is
  case-insensitive and duplicate properties cannot hide a blocking value.
- The additive `TransportSuccess` and `Data` fields preserve the standalone
  command result and its structured recovery guidance. Standalone HMI semantics
  are unchanged, and the original outcome object is not modified.
- An incomplete step stops the suffix unless `continueOnError=true`. Explicit
  continuation can run independent later steps, but cannot report the incomplete
  step or whole blueprint applied.
- Nested preflight/backup/cleanup records are not confused with root completion
  fields. Legacy outcomes without these fields retain transport semantics.

This is sequential orchestration, **not** automatic cross-command rollback.
Earlier successful mutations remain visible after a later failure; the failure
message and blueprint documentation now say so. Focused domain readback remains
required. No task-specific targets or examples were added to a skill.

## Completed vendor-free validation

| Gate | Result |
| --- | --- |
| Release solution build | PASS, 0 warnings / 0 errors |
| Focused executor regressions | PASS, 32 / 32 |
| All non-integration unit tests | PASS, 765 / 765, including those 32 |
| Architecture tests | PASS, 54 / 54 |
| Worker IPC integration subset without TIA | PASS, 5 / 5 |
| Stable MCP stdio modern/legacy smoke | PASS, 186 commands / 189 tools, SDK 2.0.0 |
| Generated command reference | PASS, 186 current |
| Canonical/plugin skill drift | PASS, zero |
| Qualification script parsing | PASS, Windows PowerShell 5.1 and PowerShell 7 |
| Qualification registration guard | PASS, stale identity rejected before process launch or fixture creation |
| Full benchmark `npm test` | PASS, 28 registered cases / 70 campaigns / 15 fixtures and synthetic/adversarial suites |
| Repository hygiene and diff whitespace | PASS |

TRX evidence is under
`E:/Documents/OpenTiaBench/Artifacts/ott36-fix-tests-20261004/`:
`ott36-blueprint-focused.trx`, `ott36-fix-unit.trx`,
`ott36-fix-architecture.trx`, and `ott36-fix-worker-ipc.trx`.
The IPC subset checks catalog/health, injected crash isolation, centralized
confirmation and blueprint replay, API compatibility, and structural hardware
validation before TIA. It is not live Siemens qualification.
The benchmark regression suite used a process-scoped native Windows PowerShell
module path, as at preflight; no global environment setting was changed.

## Required live gate

The current shell is not elevated. The existing Siemens V21 registration matches
the Release worker path but not its rebuilt timestamp or hash. Live mutation was
not attempted with that stale registration. The worker now has SHA-256
`3E3DCC214ADFE9C165673EB9C8AFB7F0E0C5251F416F408557D5A32C47C53C5A`,
last-write UTC `2026-10-04T17:59:19.2934572Z`.

Run in an Administrator PowerShell:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File "E:/Documents/Codex/OpenTiaToolkit/scripts/Register-OpennessAllowList.ps1" -ExecutablePath "E:/Documents/Codex/OpenTiaToolkit/src/OpenTiaToolkit.Worker.V21/bin/Release/net48/OpenTiaToolkit.Worker.V21.exe" -PortalVersion 21.0
```

Then recheck the exact worker identity and run the independent qualification on
new disposable archive restores:

1. Default path: stale context, stopped prefix with retained HMI safety evidence,
   failed-token replay, fresh recovery plan, exact PLC/HMI preservation, successful
   replay rejection, unchanged Main, and clean final compilation.
2. Separate `-CheckContinuation` control: explicit independent suffix, overall
   incomplete result, structured step evidence, exact tag/HMI/Main preservation,
   replay rejection, and clean final compilation.
3. Fresh worker/TIA `-BaselineOnly` health restore and final process/archive audit.

All three are **unrun on the fixed worker**, not passed. The existing successful
health restore belongs to the pre-fix checkpoint. Earlier campaign-owned processes
are closed; the desktop MCP and its Debug worker are not touched. The final audit
will distinguish those app-owned processes from any new campaign processes.

Only after this gate passes should the structured scorer and frozen case be
enabled, followed by three fresh GLM-5.3 Flash High trials. Escalate to GLM-5.3 High
only for genuine model capability failures. No model quota or paid balance has
been consumed by this correction.
