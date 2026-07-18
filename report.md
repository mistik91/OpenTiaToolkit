# OpenTiaToolkit full repository audit

> **Private engineering document.** This file is intentionally excluded by `.gitignore` and must not be included in source, installer, plugin, SBOM, provenance, or publication artifacts.

## Current release assessment — 11 July 2026

OpenTiaToolkit 1.0 is considered ready for a stable **TIA Portal V21-only** release. All roadmap work in Phases 0–3 is complete, automated and packaged end-to-end validation passes, live V21 workflows have been exercised, and the toolkit has also been used successfully on a second machine without an observed product blocker. Private machine, user, project, and device identifiers are deliberately not recorded here.

The prioritized finding register and detailed findings below preserve the original point-in-time audit evidence. They are **not a list of currently open defects**. The P0/P1 safety, packaging, lifecycle, compatibility, documentation, supply-chain, and architecture-boundary findings were remediated through Phases 0–3. The release-owner has accepted the remaining large source files as maintainability debt for this release because Siemens execution is now isolated behind a disposable, serialized V21 worker and centralized mutation policy.

The remaining release actions are operational rather than product defects:

1. Build the final `1.0.0` artifacts with the release-owner's trusted signing certificate and `-RequireSigning`.
2. Publish the installer, checksums, SPDX SBOM, in-toto provenance, release notes, and intended plugin bundles.
3. Download the published artifact and repeat signature/checksum verification plus `version`, `doctor`, CLI, and MCP smoke tests.

Repository-side release preparation completed on 11 July 2026: version metadata, installer defaults, plugin metadata, stable positioning, V21-only compatibility guidance, changelog, release notes, and the release checklist were aligned to `1.0.0`. A forced clean Release rebuild produced `1.0.0` file/product versions for CLI, MCP, and the V21 worker. The unsigned installer candidate was created successfully; its staged payload contained 265 files, 87 dependency packages in the SPDX SBOM, 265 provenance subjects, 145 MCP tools, no Siemens binaries, and no copy of this report. Packaging now fails closed if a requested installer version differs from embedded executable product metadata. Production signing and post-publication download verification remain intentionally pending.

Compatibility is intentionally conservative: V21 is supported; V20 and earlier are unsupported; V22 and later remain unsupported until a dedicated worker, API profile, binary hashes, and live validation suite are delivered.

**Audit date:** 10 July 2026<br>
**Repository state reviewed:** commit [5f9052a](https://github.com/mistik91/OpenTiaToolkit/commit/5f9052af0d927e8a0a2dbf8f00832f92817b7c54), branch <code>master</code><br>
**TIA environment used:** TIA Portal V21 Update 2 with a currently open project; only read operations were used<br>
**Overall conclusion at audited commit:** strong and unusually broad beta, but not ready for a trustworthy binary release or unattended write automation.<br>
**Phase 0 remediation update (10 July 2026):** **completed in the current worktree and verified end to end.** The findings below remain the point-in-time evidence for commit 5f9052a; the remediation status table records what changed. P1 and later findings remain open, so Phase 0 completion is not a claim of full production readiness.

### Phase 0 completion evidence

| Phase 0 gate | Current worktree status | Verification |
|---|---|---|
| Isolated CLI/MCP packaging | **Completed** | Installer stages under separate <code>cli/</code> and <code>mcp/</code> dependency roots; exact staged MCP initialize/tools-list smoke returns 127 tools |
| Vulnerable BCL dependency | **Completed** | <code>Microsoft.Bcl.Memory 9.0.14</code> is pinned; solution transitive vulnerability scan reports no vulnerable packages; CI scan added |
| HMI JSON safe defaults | **Completed** | Dry-run/warning block/clean preflight default true; apply requires <code>dry-run=false</code>, <code>apply=true</code>, and <code>confirm=true</code>; backup defaults true on apply |
| Quiescent MCP reset | **Completed** | Reset cancels and waits; refuses disposal/gate release after the grace period; recovery message requires MCP process restart |
| Cancellation-safe PLC tag replace | **Completed** | No cancellation checkpoint exists between delete and create; create failure recreates the original tag or returns an explicit critical rollback failure |
| Truthful command outcomes | **Completed** | Compile failure, HMI safety block, and zero/partial PLC batch operations return command failure with structured data preserved |
| Regression coverage | **Completed** | 344/344 fast tests pass, including 10 new Phase 0 regressions and focused reset/tag tests |
| Exact release/live path | **Completed** | Installer compiled; staged MCP and CLI were registered by exact path/hash and exercised read-only against one open V21 Update 2 project (one project, 13 devices); MCP reset and CLI disconnect succeeded |

## 1. Executive summary

OpenTiaToolkit has a valuable core idea and already delivers substantial engineering coverage: one command implementation is shared by the CLI and MCP host; the project can attach to TIA, inspect PLC and Unified HMI content, perform guarded hardware work, edit HMI JSON, synchronize VCI content, and expose agent-oriented workflows. The fast automated suite is healthy at 334 passing tests, the packaged skills are kept in sync, and several mutation paths already use dry-run, explicit confirmation, transactions, backups, or read-back checks.

The review nevertheless found four classes of release-blocking risk:

1. **The staged installer payload is broken.** CLI dependencies overwrite different MCP dependency versions in one flat directory. The exact staged MCP executable fails during startup with an assembly-manifest mismatch.
2. **Session reset is unsafe.** MCP reset releases the execution gate and disposes Siemens engineering objects while the timed-out operation may still be using them.
3. **Some write paths can lose data or apply unexpectedly.** A cancelled PLC-tag replacement can leave the original tag deleted, while <code>import-hmi-json</code> applies by default and can label a safety-blocked result as transport success.
4. **The normal hosted CI run can be green without compiling or testing the product.** The proprietary Siemens assemblies are absent on <code>windows-latest</code>, and all meaningful .NET validation is conditionally skipped.

The release also carries a high-severity vulnerable transitive runtime package, has inconsistent success semantics, accepts safety passwords as normal command arguments, does not constrain general MCP file access, and has no signed/reproducible release pipeline.

Architecturally, the dominant problem is not merely file length. Siemens object ownership, session lifetime, serialization, command policy, version-specific reflection, and domain logic are concentrated in one in-process Core assembly. Eight files over 1,000 lines contain 32.5% of production C#, the Unified HMI adapter alone is about 4,469 physical lines, and the biggest files are active change hotspots. That makes API drift, rollback, cancellation, and testing harder than they need to be.

The most important architectural move is to put all Siemens objects behind a **versioned, disposable worker process with a single execution queue**. A modern host should own MCP/CLI protocol concerns, validation, policy, typed results, and documentation generation. If a Siemens call hangs, reset should terminate and recreate the worker rather than concurrently disposing objects inside the same process.

### Release recommendation

At the audited commit, the installer had to remain unpublished. That recommendation has been superseded: Phases 0–3 satisfy the stop-ship and production-architecture gates, independent-machine use found no product blocker, and the release owner accepts the remaining large-file maintainability debt. OpenTiaToolkit 1.0 is approved for a stable V21-only release after production signing and a smoke test of the downloaded publication artifact:

- CLI and MCP are staged in isolated dependency directories and the exact installed MCP payload completes a real stdio initialization smoke test.
- Reset waits for quiescence or kills an isolated worker; no Siemens object can be disposed while a call is active.
- PLC tag replacement is atomic or journaled and cannot delete the source on cancellation.
- Every mutating command defaults to preview/dry-run where practical and uses an explicit apply/confirm transition.
- Transport success, CLI exit code, and domain outcome agree.
- The vulnerable BCL dependency is patched and vulnerability scanning is mandatory.
- CI always compiles and runs Siemens-independent tests; a licensed runner validates the actual Siemens-bound build.

## 2. Audit scope and method

This was a point-in-time engineering audit of the repository, build and packaging scripts, command surface, documentation, test suite, installed Siemens V21 API assemblies, and a live open TIA project.

### Work performed

- Inventoried all tracked source, test, documentation, installer, plugin, and workflow files.
- Read the application composition, session lifecycle, CLI routing, MCP execution, PLC, HMI, hardware, VCI, output, package, and release paths.
- Counted physical source size, large methods, approximate complexity, duplication, and dependency concentration.
- Built and ran the Release fast test suite.
- Ran command/skill synchronization checks, formatting verification, package vulnerability inspection, and package staging.
- Started the exact staged binaries rather than testing only build-output binaries.
- Reflected the installed Siemens V21 modular PublicAPI assemblies directly.
- Attached to the currently running TIA Portal V21 Update 2 instance and exercised only read-only discovery/browse workflows.
- Compared the product with Siemens' official V21 API/documentation and selected open-source alternatives using their primary repositories.

### Deliberate limitations

- No write command was run against the user's open project.
- The destructive/live integration suite was not run against that project.
- Competing open-source servers were inspected from source and documentation; their self-reported capabilities were not independently executed.
- Static complexity/duplication figures are screening indicators, not formal maintainability scores.
- “Missing API” entries are roadmap gaps, not defects, unless the current documentation claims support.
- The project and device names observed during live testing are intentionally omitted.

## 3. Evidence snapshot

| Check | Result |
|---|---|
| Production C# | 176 files, 47,750 physical lines |
| Test C# | 62 files, 15,163 physical lines |
| Tracked repository files | 381 |
| Fast Release tests | **334 passed, 0 failed** |
| Test methods found | 406 total; 72 categorized as Integration |
| Plugin skill synchronization | Passed |
| Formatting gate | Failed: 15 whitespace findings |
| Vulnerability scan | High-severity <code>Microsoft.Bcl.Memory 9.0.5</code> advisory |
| Installer stage | Script succeeded, but staged MCP failed at runtime |
| Live TIA attach | Succeeded against V21 Update 2 |
| Live project discovery | 13 devices; PLC and HMI candidate-quality problems found |
| Confirmed PLC browse | Succeeded; 44 blocks returned |
| Unified HMI browse | Succeeded; 34 screens returned |
| Installed Siemens API reflection | 2,427 exported types and 14,675 declared public methods across reviewed modular DLLs |

The build and test success is real but does not offset the release failure: tests exercise separate CLI/MCP output folders, while the installer flattens them and creates a dependency graph that neither test host uses.

## 4. Health scorecard

| Area | Assessment | Why |
|---|---|---|
| Functional breadth | **Amber/Green** | Broad PLC, Unified HMI, hardware, VCI, project, library, and agent workflow coverage for a beta |
| Core read workflows | **Amber/Green** | Live attach, PLC block browse, and HMI screen browse worked |
| Mutation safety | **Red** | Unsafe reset, cancellation data-loss path, apply-by-default import, false-success outcomes |
| Architecture | **Amber/Red** | Good shared-command direction, but in-process session ownership and active god classes dominate |
| Automated tests | **Amber** | 334 fast tests pass; important services and failure modes remain untested |
| CI/release engineering | **Red** | Hosted CI skips build/tests, staged MCP is broken, no signed or reproducible release pipeline |
| Documentation | **Amber/Red** | Substantial material exists, but command/version/testing claims drift from reality |
| Security | **Red** | High vulnerable dependency, plaintext safety credentials, unrestricted model-controlled paths, CSV injection |
| Siemens API alignment | **Amber** | Good chosen-domain coverage, but V21 self-description and many modular domains remain untapped |
| Competitive position | **Amber** | Strong breadth and agent guidance; trails leaders in isolation, blueprint workflows, version support, and distribution |

## 5. Prioritized finding register

Severity means:

- **P0:** plausible data loss, unsafe concurrent Siemens access, unusable release artifact, or a security defect that blocks release.
- **P1:** high reliability, security, correctness, or maintainability risk that should be addressed before production use.
- **P2:** material quality, testing, documentation, or operability weakness.
- **P3:** polish or strategic roadmap improvement.

| ID | Priority | Finding | Primary evidence |
|---|---:|---|---|
| REL-001 | P0 | Installer flattens incompatible CLI/MCP dependency graphs; staged MCP cannot start | <code>scripts/Build-Installer.ps1:119-125</code> |
| SES-001 | P0 | MCP reset disposes session objects while the timed-out operation can still run | <code>ToolRegistry.cs:240-261</code> |
| MUT-001 | P0 | Cancelled PLC-tag replace can permanently delete the source tag | <code>PlcTagService.cs:769-791</code> |
| HMI-001 | P0 | HMI JSON import applies by default and reports a safety block as success | <code>ImportHmiJsonCommand.cs:26-39,75-105</code> |
| DEP-001 | P0 | MCP resolves a high-severity vulnerable runtime dependency | <code>OpenTiaToolkit.MCP.csproj:35</code> |
| CI-001 | P1 | Normal hosted CI can pass without restore, build, format, or tests | <code>.github/workflows/ci.yml:42-75</code> |
| RES-001 | P1 | Domain failure/partial/block can map to CLI exit 0 and MCP success | compile/import/export/HMI command paths |
| SES-002 | P1 | Connect/project transitions are non-atomic, can leak ownership, and retain stale HMI state | <code>PortalSessionService.cs</code> |
| HMI-002 | P1 | HMI JSON version/kind/action and unknown fields are not validated | document store/import service |
| API-001 | P1 | Reflection setters silently no-op and can report mutation success | <code>TiaReflectionHelper.cs:39-44</code> |
| DSC-001 | P1 | PLC discovery produces many false and duplicate candidates | live test plus <code>PortalInspectionService.cs:345-438</code> |
| DSC-002 | P1 | HMI discovery and HMI open use inconsistent capability detection | live test plus both V21 adapters |
| SEC-001 | P1 | Safety passwords are exposed via process arguments/tool transcripts | hardware/tag command schemas |
| SEC-002 | P1 | MCP-exposed file commands have no central allowed-root/size policy | command surface review |
| API-002 | P1 | Runtime is V21-hardcoded while public docs claim “V21 and forward” | resolver, config, README/version guide |
| PLC-001 | P1 | PLC block comments serialize as a type name and comment search is unreliable | live test plus <code>PlcBrowseService.cs</code> |
| TXN-001 | P1 | Multi-object mutations lack a universal transaction/journal/audit contract | PLC rename/update paths |
| ARC-001 | P1 | Core and key adapters are active architectural monoliths | size/complexity profile |
| ARC-002 | P1 | Facades manually construct subservices; abstractions leak Siemens types | service constructors/interfaces |
| CLI-001 | P1 | The end-to-end CLI harness skips commands and calls a removed command | <code>scripts/Test-CLI.ps1</code> |
| DOC-001 | P1 | “Full” CLI reference omits commands and version/build guidance is inaccurate | README and docs review |
| REL-002 | P1 | Installed docs are incomplete; release metadata/signing/governance are unfinished | stage/manifests/installer |
| QLT-001 | P1 | Configured format validation already fails, hidden by CI skip | <code>dotnet format --verify-no-changes</code> |
| SCH-001 | P2 | Command requests/results are stringly typed and nested MCP schemas are weak | <code>CommandArgs</code>, <code>CommandResult</code>, <code>ToolAdapter</code> |
| MCP-001 | P2 | Roughly 120 tools are exposed without domain profiles/capability filtering | registration/registry |
| PERF-001 | P2 | Large results are fully materialized before artifact spilling | <code>ToolAdapter.cs:250-305</code> |
| OUT-001 | P2 | CSV output permits spreadsheet-formula injection | <code>CommandArtifactSupport.cs:129-139</code> |
| TST-001 | P2 | Coverage, contract, staged-payload, and several live-domain suites are absent | test/CI review |
| ERR-001 | P2 | Broad catches, bool/null plus mutable LastError, and stack reset lose diagnostics | service/proxy review |
| DSC-003 | P2 | Device IP reporting is usually null/misleading | live test plus inspection code |
| ART-001 | P2 | HMI snapshots and generic temp artifacts have incomplete cleanup semantics | edit/artifact services |
| GOV-001 | P2 | No tracked security/contribution/release governance baseline | repository root and ignored checklist |
| UX-001 | P3 | No version command, schema catalog, shell completion, or dedicated MCP reference | CLI/docs review |

## 6. P0 release blockers

### REL-001 — The installer creates an unloadable MCP application

[Build-Installer.ps1](scripts/Build-Installer.ps1#L119-L125) stages the MCP output, then copies CLI output into the same root. The applications were built with different transitive versions, so the second copy silently overwrites the MCP files. Eleven same-named assemblies differed by hash/version, including:

- <code>Microsoft.Extensions.Logging.Abstractions</code>
- <code>Microsoft.Extensions.DependencyInjection.Abstractions</code>
- <code>System.Text.Json</code>
- <code>System.Memory</code>
- <code>System.Buffers</code>
- <code>System.Diagnostics.DiagnosticSource</code>
- <code>System.Runtime.CompilerServices.Unsafe</code>

For example, the CLI copy replaces the MCP-required Logging.Abstractions assembly version 8.0.0.3 with 8.0.0, and replaces System.Text.Json 8.0.6 with 8.0.5.

Reproduction on the audited commit:

    powershell -File scripts/Build-Installer.ps1 -Configuration Release -SkipBuild -StageOnly
    '' | artifacts/installer/stage/OpenTiaToolkit.MCP.exe

Staging reports success, but the MCP host fails with an assembly-manifest mismatch while loading <code>Microsoft.Extensions.Logging.Abstractions, Version=8.0.0.3</code>. A <code>--help</code> check is insufficient because it exits before host initialization.

**Required correction**

- Install under <code>cli/</code> and <code>mcp/</code> subdirectories, or intentionally produce a single tested dependency graph.
- Add package locking/central versions so drift is visible.
- Smoke-test the exact staged/installed executable through MCP initialize/list-tools, not merely <code>--help</code>.
- Fail staging on any conflicting same-name file unless an explicit allow-list proves byte identity.

### SES-001 — Reset races an active Siemens call

The MCP registry removes the active operation and releases its outer execution gate before the original task is known to have ended ([ToolRegistry.cs](src/OpenTiaToolkit.MCP/Server/ToolRegistry.cs#L240-L261)). The reset service then deliberately bypasses the gate ([EngineeringSessionResetService.cs](src/OpenTiaToolkit.Core/Services/EngineeringSessionResetService.cs#L16-L19)), and [EngineeringSessionContext.cs](src/OpenTiaToolkit.Core/Services/EngineeringSessionContext.cs#L88-L115) closes/disposes project, portal, PLC, and HMI objects.

The test at [McpToolBridgeTests.cs](tests/OpenTiaToolkit.Tests/Server/McpToolBridgeTests.cs#L431-L471) explicitly expects a second command to start before the first command ends. That test therefore enshrines the unsafe property.

Cancellation of a managed task does not guarantee an arbitrary synchronous Openness call has stopped. Disposing the portal concurrently can produce undefined vendor behavior, corrupt the session state, crash the process, or leave TIA locked.

**Required correction**

- Short term: reset must mark the session unhealthy, reject new work, and wait for actual quiescence before disposal. If quiescence cannot be obtained, tell the caller that a process restart is required.
- Target design: all Siemens work lives in a disposable worker process. Timeout/reset terminates that process and creates a clean worker.
- Replace the current test with an invariant: no subsequent command starts and no object is disposed until the prior worker is gone.

### MUT-001 — Cancellation can delete a PLC tag

In [PlcTagService.cs](src/OpenTiaToolkit.Core/Services/PlcTagService.cs#L769-L791), replace deletes the original tag, checks the cancellation token, and only then creates the replacement. <code>OperationCanceledException</code> is rethrown, so the generic restoration branch does not run. The source can remain permanently deleted.

The wider rename/update loops commit object-by-object. A timeout after earlier iterations creates partial state but may discard the useful per-item result summary. Even the non-cancellation restoration path is best-effort and swallows restoration failures.

**Required correction**

- Prefer a Siemens project transaction/exclusive-access transaction for the whole operation.
- If transaction support is unavailable, use a durable mutation journal containing the full source definition and a verified recovery plan.
- Never observe cancellation between an irreversible delete and its compensating create.
- Return <code>Partial</code> plus applied/rolled-back/unknown items when rollback is incomplete; block further writes until the session is revalidated.

### HMI-001 — Import mutates unless the caller remembers dry-run

[ImportHmiJsonCommand.cs](src/OpenTiaToolkit.Core/Commands/Hmi/ImportHmiJsonCommand.cs#L26-L105) defaults <code>dry-run</code>, backup, clean preflight, and warning blocking to false. There is no separate confirmation parameter. An omitted flag therefore means apply. The command then wraps a safety-blocked result in <code>CommandResult.CreateSuccess</code>.

This is especially dangerous on MCP because omitted optional values are routine and the transport reports success to the model.

**Required correction**

- Default to dry-run.
- Require <code>apply=true</code> and a separate <code>confirm=true</code> for destructive/create-delete changes.
- Default to a clean preflight and backup for apply.
- Make <code>SafetyBlocked</code> a non-success transport/domain outcome.
- Require expected project/HMI identity and input document hash on apply to prevent stale-target execution.

### DEP-001 — High-severity vulnerable transitive dependency

[OpenTiaToolkit.MCP.csproj](src/OpenTiaToolkit.MCP/OpenTiaToolkit.MCP.csproj#L35) references the old preview <code>ModelContextProtocol 0.3.0-preview.4</code>. Its graph resolves <code>Microsoft.Bcl.Memory 9.0.5</code>, which <code>dotnet list package --vulnerable --include-transitive</code> reports under [GHSA-73j8-2gch-69rq / CVE-2026-26127](https://github.com/advisories/GHSA-73j8-2gch-69rq). The audited advisory identifies 9.0.14 as patched.

The local stdio deployment reduces remote exposure, but malformed model/client-controlled data can still become an availability issue. The dependency graph also mixes 8.x releases, 9.x components, and preview components, contributing directly to the packaging collision.

**Required correction**

- Immediately pin a patched <code>Microsoft.Bcl.Memory</code> or migrate to a tested stable MCP SDK release.
- Review the API/behavior changes rather than blindly changing the version.
- Add restore-lock validation and a mandatory vulnerability scan to CI/release.

## 7. Correctness, lifecycle, and safety findings

### RES-001 — “Success” is not a trustworthy contract

The command envelope conflates “the command handler ran” with “the requested engineering outcome succeeded.” Concrete examples:

- [CompileSoftwareCommand.cs](src/OpenTiaToolkit.Core/Commands/Plc/CompileSoftwareCommand.cs#L76-L93) returns <code>CreateSuccess</code> even when compilation failed.
- Blocked non-dry-run HMI edit/patch results are returned as success in [ApplyHmiScreenJsonEditCommand.cs](src/OpenTiaToolkit.Core/Commands/Hmi/ApplyHmiScreenJsonEditCommand.cs#L107-L114) and [ApplyHmiScreenJsonPatchCommand.cs](src/OpenTiaToolkit.Core/Commands/Hmi/ApplyHmiScreenJsonPatchCommand.cs#L109-L116).
- PLC import/export commands can return success when no item succeeded or some items failed.
- A blocked HMI JSON import is returned as success.

[Program.cs](src/OpenTiaToolkit.CLI/Program.cs#L97-L104) derives the process exit code from this envelope, and MCP uses the same flag for <code>isError</code>. Automation therefore cannot reliably distinguish successful engineering work from validation failure, safety block, partial completion, or compile failure.

Replace the boolean with a closed outcome model:

| Outcome | CLI | MCP | Meaning |
|---|---:|---|---|
| Succeeded | 0 | <code>isError=false</code> | Requested state verified |
| Preview | 0 | false | No mutation; valid plan returned |
| Partial | non-zero | true | Some work applied; explicit item journal |
| Blocked | non-zero | true | Safety/policy prevented apply |
| ValidationFailed | non-zero | true | Input/precondition invalid |
| Cancelled | non-zero | true | No mutation, or explicit partial state |
| Failed | non-zero | true | Operation failed with structured error |

Every mutation result should include target identity, precondition hash, transaction/rollback state, verification state, warnings, and a stable error code.

### SES-002 — Session transitions are not atomic

[PortalSessionService.cs](src/OpenTiaToolkit.Core/Services/PortalSessionService.cs#L69-L100) and its explicit-instance path clear the current selection before a replacement attach succeeds. They do not safely swap/dispose the previous owned portal. A failed connection can destroy a valid state while an internally created hidden portal is forgotten.

Project selection is also inconsistent. Switching to another already-open project clears PLC selection but leaves the former HMI selection, then overwrites ownership/session fields. [EngineeringSessionContext.cs](src/OpenTiaToolkit.Core/Services/EngineeringSessionContext.cs#L55-L77) shows that a proper project clear should clear both.

Use an explicit state machine:

    Disconnected
      -> PortalAttached(candidate)
      -> ProjectSelected(candidate)
      -> PlcSelected / HmiSelected
      -> Unhealthy
      -> Disposing

Transitions should prepare and validate a candidate, atomically commit the new immutable state, then dispose the prior owned handle. On failure, preserve the old state. Ownership must travel with each handle rather than exist as mutable flags beside it.

### HMI-002 — HMI import documents are fail-open

[HmiJsonModels.cs](src/OpenTiaToolkit.Core/Models/HmiJsonModels.cs#L22-L40) advertises <code>SchemaVersion</code> and <code>DocumentKind</code>, but neither is validated. [HmiJsonDocumentStore.cs](src/OpenTiaToolkit.Core/Services/HmiJsonDocumentStore.cs#L87-L93) simply deserializes with normal unknown-member skipping. Import starts iterating screens immediately. Missing action defaults to Update, and arbitrary strings other than Delete also fall into update/create behavior.

This means a typo, newer incompatible producer, or unexpected property can silently alter the meaning of an apply.

Add a versioned JSON Schema and a semantic validator that runs before any portal transaction:

- Reject unsupported schema version/document kind.
- Reject unknown properties by default, with an explicit forward-compatible extension area if required.
- Use enums for action and reject missing/unknown action.
- Validate required identifiers, uniqueness, paths, property types, and cross-references.
- Canonicalize and hash the validated input; bind apply to the reviewed hash.
- Fuzz malformed, truncated, duplicate, oversized, and deeply nested documents.

### API-001 — Reflection mutation silently succeeds under API drift

[TiaReflectionHelper.cs](src/OpenTiaToolkit.Core/Services/TiaReflectionHelper.cs#L39-L44) returns without error when a requested property does not exist. V21 adapter mutation methods call that helper and assume the write occurred; <code>TrySetTagComment</code> can return true after such a no-op.

Reflection is understandable at a volatile Siemens boundary, but mutation must be fail-closed:

- Distinguish unsupported member, non-writable member, conversion failure, vendor exception, and verified success.
- Cache typed capability descriptors rather than repeating string lookup.
- Read the value back and compare after every mutation where Siemens supports it.
- Include actual API assembly/version and failed member name in diagnostics.

### DSC-001 — PLC discovery is unsafe for automation

The live project contained 13 devices. <code>list-plcs</code> returned 27 candidates, but only one had <code>isConfirmedPlc=true</code>. The other 26 included duplicated fail-safe/ordinary I/O modules and proxy/interface items.

The cause is visible in [PortalInspectionService.cs](src/OpenTiaToolkit.Core/Services/PortalInspectionService.cs#L345-L438): if the parent name contains “ET 200” or “Station,” most child items not matching a short exclusion list become PLC candidates. Recursive traversal adds duplicates and no stable deduplication key is applied.

An agent can easily select the wrong target from this list. Treat heuristics only as fallback suggestions, never actionable identity:

- Prefer typed <code>SoftwareContainer</code>/<code>PlcSoftware</code> capability discovery.
- Return stable device/item path and unique engineering identity.
- Deduplicate by identity/path.
- Separate <code>confirmed</code>, <code>heuristic</code>, and <code>unsupported</code> collections.
- Require a confirmed target or an exact expected identity for writes.

### DSC-002 — HMI discovery disagrees with HMI opening

Live <code>list-hmis</code> returned five candidates, all unconfirmed/name-pattern matches. Nevertheless, opening the actual Unified HMI candidate succeeded and 34 screens were listed.

[V21OpennessAdapter.cs](src/OpenTiaToolkit.Core/Services/V21OpennessAdapter.cs#L282-L330) tries string-named services and name heuristics, while [V21UnifiedHmiAdapter.cs](src/OpenTiaToolkit.Core/Services/V21UnifiedHmiAdapter.cs#L819-L843) uses the typed <code>SoftwareContainer</code> path that actually works. Discovery and execution must use the same capability resolver; otherwise the user sees a low-confidence target that later becomes fully usable.

### PLC-001 — Multilingual block comments are not decoded

The confirmed live PLC returned 44 blocks, but every comment serialized as <code>Siemens.Engineering.MultilingualText</code> rather than human text. [PlcBrowseService.cs](src/OpenTiaToolkit.Core/Services/PlcBrowseService.cs#L86-L91) tries a simple <code>Text</code> property and falls back to <code>ToString()</code>. V21 comments are multilingual objects.

This also makes <code>search-blocks --search-in comment</code> unreliable. Resolve the requested project editing/reference culture through the multilingual composition, return all requested cultures when appropriate, and test comments with missing/default/multiple languages.

### TXN-001 — Safety policy is per-command rather than systemic

Hardware patching is a good counterexample: it uses exclusive access and a Siemens transaction, rolls a dry-run back, and requires destructive confirmation. HMI import also uses a project transaction for portions of its work. PLC tag/member workflows, however, mutate sequentially and use ad hoc rollback. Command defaults and result semantics vary.

Introduce one non-bypassable mutation interceptor around all writes. A command descriptor should declare:

- read versus mutation;
- destructive and safety-sensitive flags;
- required project/device identity;
- transaction capability;
- dry-run support and default;
- confirmation requirements;
- secret references;
- rollback and read-back verification strategy;
- audit fields and artifact policy.

The interceptor should acquire the session executor, validate preconditions, open the transaction/journal, run, verify, commit/rollback, and produce a uniform audit record.

### SEC-001 — Safety passwords are normal arguments

<code>apply-hardware-patch</code> and PLC tag modification accept <code>safety-password</code> as a CLI/MCP string. This exposes it to shell history, process inspection, command logging, model conversations, and tool traces. The project lifecycle path already demonstrates a better UMAC pattern using an environment-variable indirection and <code>SecureString</code>.

Remove raw secrets from schemas and examples. Accept a credential reference, environment-variable name, Windows Credential Manager identifier, or interactive secure prompt. Redact secret-shaped values centrally, and never include them in audit/result serialization.

### SEC-002 — General MCP file access lacks a boundary

Many MCP-exposed commands accept arbitrary input/output/project paths. No central allow-root, canonical-path, symlink/reparse-point, or maximum-file-size policy was found. This is not inherently wrong for a local CLI, but MCP makes the caller a model/client operating with the user's full filesystem permissions.

Add host-level policy:

- configurable allowed read/write roots;
- canonicalization after resolving reparse points;
- separate project, import, export, and temporary roots;
- input-size/depth limits;
- overwrite policy and atomic output writes;
- clear denial errors that do not leak unrelated filesystem content.

VCI's existing check that mappings remain under the workspace root is a useful pattern.

### OUT-001 — CSV exports permit formula injection

[CommandArtifactSupport.cs](src/OpenTiaToolkit.Core/Commands/CommandArtifactSupport.cs#L129-L139) performs CSV quoting but does not neutralize cells starting with <code>=</code>, <code>+</code>, <code>-</code>, or <code>@</code>. PLC/HMI names and comments can become formulas when opened in spreadsheet software.

Provide a spreadsheet-safe export mode, ideally the default for user-facing CSV. Prefix dangerous cells or export a typed workbook format; clearly document a raw-data mode for machine consumers.

### ART-001 — Temporary engineering artifacts have incomplete lifecycle control

[HmiScreenJsonEditService.cs](src/OpenTiaToolkit.Core/Services/HmiScreenJsonEditService.cs#L420-L430) creates working snapshots under the user's temporary directory without a corresponding cleanup path. Generic command artifacts default to seven-day retention, and cleanup only runs when a later artifact is created. Configuring retention to zero returns without pruning rather than deleting immediately.

Engineering exports can contain proprietary logic, names, comments, topology, or HMI content. Use one ACL-hardened per-user artifact store, document exactly what is persisted, make zero mean immediate cleanup, prune on host startup/shutdown as well as creation, and allow a caller to request explicit ephemeral or retained output.

## 8. Architecture assessment

### What is structurally good

- CLI and MCP share the same command implementations, avoiding two divergent business layers.
- Dependency direction from hosts into Core is simple.
- Nullable reference types are enabled.
- Automatic command registration has a composition test.
- <code>SessionSerializedServiceProxy</code> and the MCP execution gate show that the project understands Siemens session serialization.
- Recent extraction into PLC/HMI subservices points in the right direction.
- Hardware transactions, HMI project transactions, VCI path constraints, artifact spilling, and UMAC secret indirection are reusable foundations.

### Why the present boundaries are insufficient

Core contains 44,600 lines, 93.4% of production code. It combines:

- Siemens assembly discovery and binding;
- live object ownership and reset;
- V21 reflection/adapter code;
- project, PLC, HMI, hardware, library, VCI, text, and online use cases;
- command schemas and result envelopes;
- safety policy and artifact generation.

The public abstractions are not vendor-free: [IPortalService.cs](src/OpenTiaToolkit.Core/Abstractions/IPortalService.cs#L12-L32) exposes <code>Siemens.Engineering.ProjectBase</code>. CLI and MCP consequently carry Siemens references despite not using Siemens types directly. <code>PlcService</code>, <code>HmiService</code>, and <code>PortalService</code> construct concrete V21 adapters/subservices themselves instead of receiving narrow interfaces. That makes alternate versions, stubs, failure injection, and partial installations difficult.

The facades are also broad: <code>IPlcService</code> exposes about 40 methods and <code>IHmiService</code> about 25. <code>PlcService</code> and <code>HmiService</code> each manually assemble roughly eight subordinate services. Register those narrow capabilities in DI and inject them; keep a facade only for compatibility.

There are also 109 <code>Task.Run</code> uses in Core but only four service methods exposing a cancellation token. Wrapping a synchronous vendor call in a task does not make it safely cancellable. CLI routing uses only the context-free command method, and REPL Ctrl+C does not propagate cancellation into active work.

### Recommended target architecture

~~~text
CLI / MCP / future UI
        |
        v
Modern host process (.NET current LTS)
  - typed command catalog
  - validation and capability profiles
  - mutation policy / authorization / audit
  - result and error mapping
  - docs/schema generation
        |
        | versioned IPC contract (NDJSON, named pipe, or gRPC)
        v
TIA V21 worker process (.NET Framework 4.8)
  - one serialized execution queue
  - explicit session state machine
  - all Siemens objects stay here
  - typed V21 capability adapters
  - transactions and read-back
        |
        v
Siemens modular PublicAPI assemblies / TIA Portal
~~~

This split provides three immediate benefits:

1. A hung/corrupt session can be reset by terminating the worker.
2. Hosted CI can compile/test contracts, host, policy, schemas, docs, and fake-worker behavior without proprietary DLLs.
3. V22 or another supported Siemens version becomes a separate worker/adapter package selected by capability, not a web of conditionals inside Core.

The worker should own a single immutable session-state value and a dedicated queue. No caller should resolve raw services that bypass serialization. IPC requests should carry command ID, typed payload, expected target identity, deadline, correlation ID, and policy proof; responses should carry the typed outcome and audit journal.

### Practical module decomposition

Do not split merely by file size. Split by reason to change:

| Proposed module | Responsibility |
|---|---|
| <code>OpenTiaToolkit.Contracts</code> | Vendor-free requests, DTOs, outcomes, error codes, capabilities, IPC |
| <code>OpenTiaToolkit.Application</code> | Use cases, validation, command catalog, mutation policy, audit |
| <code>OpenTiaToolkit.Siemens.V21.Worker</code> | Process host, resolver, session state machine, serialized executor |
| <code>...V21.Plc</code> | Block/tag/source/online/compile adapters |
| <code>...V21.HmiUnified</code> | Unified HMI discovery and capability adapters |
| <code>...V21.Hardware</code> | Hardware topology/catalog/network/drive/transaction adapters |
| <code>...V21.Vci</code> | Version-control workspace and mapping adapters |
| <code>OpenTiaToolkit.CLI</code> | Parsing, interactive/script UX, host client |
| <code>OpenTiaToolkit.MCP</code> | MCP transport, profiles, progress/cancellation, host client |

Keep compatibility facades temporarily, mark them as delegating shims, and move one vertical capability at a time.

## 9. Monolith, complexity, duplication, and style

### Quantified concentration

| Metric | Observation |
|---|---:|
| Production files / physical lines | 176 / 47,750 |
| Types / executable declarations | 406 / 1,861 |
| Files over 500 lines | 20 files containing 50.4% of production code |
| Files over 1,000 lines | 8 files containing 32.5% of production code |
| Methods over 50 / 100 / 200 lines | 167 / 29 / 2 |
| Screening complexity over 10 / 20 / 40 | 98 / 14 / 1 |
| Representative production clone clusters | 12 |

The figures should be used to choose refactoring targets, not as arbitrary gates. The more important fact is that the largest types have heavy recent churn, so their concentration actively increases change risk.

### Primary hotspots

| File/type | Physical file lines | Responsibility/concern |
|---|---:|---|
| [V21UnifiedHmiAdapter.cs](src/OpenTiaToolkit.Core/Services/V21UnifiedHmiAdapter.cs) | 4,469 | About 171 methods across discovery, screens, items, tags, trends, events, dynamizations, faceplates, conversion, copy, and catalogs |
| [PlcTagService.cs](src/OpenTiaToolkit.Core/Services/PlcTagService.cs) | 2,164 | Query, CSV, rename, replace, safety, rollback, validation |
| [HardwareService.cs](src/OpenTiaToolkit.Core/Services/HardwareService.cs) | 1,811 | About 95 methods across read, topology, mutation dispatch, transactions, drives, audits |
| [HmiScreenJsonPatchApplier.cs](src/OpenTiaToolkit.Core/Services/HmiScreenJsonPatchApplier.cs) | 1,690 | Parsing, touched-property analysis, validation, apply, result assembly |
| [V21OpennessAdapter.cs](src/OpenTiaToolkit.Core/Services/V21OpennessAdapter.cs) | 1,671 | Cross-domain project/PLC/HMI reflection boundary |
| [HmiJsonImportService.cs](src/OpenTiaToolkit.Core/Services/HmiJsonImportService.cs) | 1,502 | Import orchestration, validation, transaction, item/property semantics |
| <code>TableOutputFormatter.cs</code> (legacy excluded source, removed in roadmap Chapter 1) | 1,184 | Historical central type-dispatch formatter; main dispatch screening complexity 48 |
| [PortalSessionService.cs](src/OpenTiaToolkit.Core/Services/PortalSessionService.cs) | 1,048 | Connection, ownership, project selection, open/create/close/save/archive |

Worst method examples include:

- <code>PlcTagService.FindAndReplaceTagsAsync</code>: about 259 lines, screening complexity 29.
- <code>PlcTagService.UpdateTagsFromCsvAsync</code>: about 258 lines, complexity 30.
- <code>HmiJsonDiffService.BuildScreenPatch</code>: about 140 lines, complexity 31.
- <code>HmiScreenJsonPatchApplier.GetTouchedProperties</code>: about 116 lines, complexity 32.
- <code>HardwareService.ExecuteOperation</code>: about 105 lines, complexity 29.
- <code>PortalInspectionService.SearchDeviceItemsRecursive</code>: about 95 lines, complexity 35; this is also where the live PLC discovery defect concentrates.

### Recommended splits

- **Unified HMI:** discovery/capabilities, screen tree, item CRUD, property codec, event bindings, dynamizations, tags/trends/logging, faceplate/composites, copy/clone, and catalog metadata.
- **PLC tags:** pure validation/planning, typed Siemens repository, CSV codec, mutation transaction/journal, reference-impact analysis, and result mapping.
- **Hardware:** query/topology, operation parsers, one handler per operation family, transaction coordinator, PROFINET networking, IO addresses, drive telegrams, parameter codec, and audits.
- **HMI JSON:** schema/semantic validation, diff, plan, policy, transaction executor, property appliers, and verification.
- **Portal lifecycle:** resolver/instance discovery, candidate connection, immutable session state, project lifecycle, ownership/disposal.
- **CLI output:** registered <code>IResultFormatter&lt;T&gt;</code> implementations per domain, with one generic fallback.

### Duplication and weak abstractions

Approximately 105 command classes repeat the same <code>ICommand</code> to <code>IContextualCommand</code> bridge. The three roughly 212-line find/replace command classes are around 89–90% aligned; import block/type commands are also heavily aligned. Output-path handling, HMI normalization/property logic, and parallel command/service result DTOs create further duplication.

This is not a call for a clever inheritance hierarchy. Prefer:

- one generic typed command base/adapter for context and cancellation;
- composable validators and mutation policies;
- a shared import/export pipeline parameterized by repository and codec;
- a shared rename planner with domain-specific target adapters;
- one typed result family used by service, command, CLI, and MCP.

### Style and diagnostics

The repository has an <code>.editorconfig</code>, nullable annotations, and generally clear naming, but the configured formatting gate currently reports 15 whitespace differences, concentrated in [IPlcService.cs](src/OpenTiaToolkit.Core/Abstractions/IPlcService.cs#L180) and [IPortalService.cs](src/OpenTiaToolkit.Core/Abstractions/IPortalService.cs#L17). Hosted CI hides the failure.

Additional style/quality issues:

- Broad catches often return empty collections, false, or null, making “nothing found” indistinguishable from “inspection failed.”
- <code>SessionSerializedServiceProxy</code> rethrows the inner exception in a way that resets the original stack; use <code>ExceptionDispatchInfo</code>.
- <code>CommandResult.Data</code> is <code>object</code>, and many commands return anonymous objects. This forces reflection and type switches downstream.
- Integer/boolean parsing can silently fall back to defaults for malformed values.
- Concrete facades and serialized proxies are both registered, so direct resolution/construction can bypass the intended gate.
- The legacy <code>TableOutputFormatter</code> had to be modified whenever a result type was added, violating open/closed design; the already-excluded source was removed in roadmap Chapter 1.
- XML docs are generated while missing-member warnings are suppressed, so the setting does not guarantee public API documentation.

Adopt Roslyn/.NET analyzers, warning policy, formatting, and a debt baseline. Initially fail only newly introduced issues so the existing cleanup can proceed deliberately. Add a lightweight complexity/duplication trend report rather than enforcing simplistic hard limits.

## 10. Command surface and usability

### Shared surface is a genuine strength

Automatic registration in [ServiceCollectionExtensions.cs](src/OpenTiaToolkit.Core/ServiceCollectionExtensions.cs#L96-L114) and its composition tests mean CLI and MCP normally execute the same command code. Stateful CLI script mode, JSON output, artifact spilling, progress/status/cancellation support in MCP, and packaged skills are meaningful differentiators.

### Schemas remain too weak for autonomous use

[CommandSchema.cs](src/OpenTiaToolkit.Core/Commands/CommandSchema.cs#L89-L104) supports coarse scalar/array/object categories. Nested MCP objects are largely open-ended, range/pattern/enum constraints are inconsistent, and output schemas are absent. At roughly 120 tools, all tools are registered whether the installed TIA modules/current project can support them.

Consequences:

- clients spend substantial context on irrelevant tools;
- ambiguous parameter shapes reach handlers;
- defaults can silently change behavior;
- models cannot validate output contracts;
- documentation drifts because schema, CLI help, MCP descriptions, and Markdown are separate projections.

Create strongly typed request/response DTOs and a single command descriptor generator. It should emit:

- CLI parser/help/completion;
- MCP input and output JSON Schema;
- command/MCP reference documentation;
- capability/domain profile manifests;
- safety classification and default-policy tests.

Offer profiles such as <code>read-only</code>, <code>project</code>, <code>plc</code>, <code>hmi-unified</code>, <code>hardware</code>, <code>vci</code>, and <code>full</code>, further filtered by current capabilities.

### Large result handling is late

[ToolAdapter.cs](src/OpenTiaToolkit.MCP/Server/ToolAdapter.cs#L250-L305) builds the complete JSON node and formatted string before measuring and spilling. Large projects therefore incur multiple full-memory copies. Add pagination/cursors and artifact-first streaming for block trees, tag tables, hardware topology, cross-references, and HMI exports.

### CLI cancellation is incomplete

CLI routing invokes only the context-free execution path, and interactive Ctrl+C sets a loop flag rather than cancelling the active command. A unified execution context containing cancellation, deadline, correlation ID, target identity, and policy should be mandatory in both hosts.

## 11. Test and validation assessment

### What passes

The audited Release fast command passed all 334 selected tests. There are 406 test methods in total, with 72 categorized as Integration. The suite is quick, command auto-registration is tested, MCP execution behavior has focused tests, packaged skill synchronization passes, and most integration classes are non-parallel with caller-supplied project paths.

### Material gaps

- A static reference screen found 60 of 121 command implementation types without a direct fast-test reference.
- Twenty-five of 46 service files have no fast-test mention of their primary type, including the 4,469-line Unified adapter, session service, PLC import/export, PLC surface, library, VCI, trend, text, browse, compile, and verification services.
- No code-coverage collection or threshold exists.
- No exact staged/installed binary initialization test exists.
- No dedicated live suites cover hardware, VCI, libraries, project texts, MCP transport, or several newer PLC source/cross-reference/watch/technology/alarm surfaces.
- Some live tests return/log when prerequisites are absent instead of becoming Inconclusive, making a green run ambiguous.
- The 1,869-line Unified HMI integration fixture has too many responsibilities.
- The reset test currently asserts the unsafe concurrency behavior.
- There is no failure-injection test for cancellation immediately after delete, rollback failure, worker timeout, API-member drift, assembly collision, or filesystem policy.

The Debug fast-test workflow can also collide with running MCP processes because the test project references both executable hosts and rebuilds their copied dependencies. Separate vendor-free contract/application tests from host binary tests, and keep host output directories isolated.

### Recommended test pyramid

1. **Always-on hosted tests:** vendor-free contracts, schemas, command policy, parsers/formatters, JSON/CSV codecs, diff/planning, docs generation, fake worker, IPC, host transports.
2. **V21 contract tests:** compile against licensed Siemens references; reflect expected members/capabilities; use generated stubs for hosted build shape.
3. **Disposable live project tests:** restore a known archive/copy for each run, apply mutations, verify read-back, close without risking a user's project, and retain artifacts on failure.
4. **Failure-injection tests:** worker kill/hang, cancellation at each mutation boundary, transaction/rollback failure, stale hash, unsupported member, partial import, disk-full/path denial.
5. **Release tests:** clean restore, locked dependencies, vulnerability scan, stage, install, stdio initialization, list/call a read-only tool, uninstall, checksum/signature verification.

Coverage should be used by risk: require high branch coverage for planners, validators, result mapping, and rollback logic; do not chase arbitrary coverage inside thin reflection wrappers.

## 12. CI, packaging, and release engineering

### CI-001 — Hosted CI is structurally false-green

[ci.yml](.github/workflows/ci.yml#L42-L75) detects whether Siemens PublicAPI DLLs exist. On the standard hosted image they do not, and restore, build, formatting, and tests are conditional on their presence. The job still succeeds after JSON/file/synchronization checks. [CHANGELOG.md](CHANGELOG.md#L24) claims the absence fails loudly, which is the opposite of current behavior.

Split validation:

- Hosted Windows CI must always restore/build/test vendor-free projects, run formatting/analyzers, validate docs/schemas/plugins, scan dependencies/secrets, and build the host against stubs/contracts.
- A licensed self-hosted Windows runner must build the real V21 worker, run contract/live tests against a disposable project, and stage/smoke the installer.
- Branch protection must require both appropriate statuses for a release.

### CLI harness regressions

[Test-CLI.ps1](scripts/Test-CLI.ps1#L323) recognizes help entries only when at least two spaces follow the command. Because names are padded to a fixed width, 17 long commands are skipped; only 107 of 124 are discovered. The PLC suite calls nonexistent <code>list-db-members</code> at line 617, and the negative suite expects obsolete error text at line 1021.

[CLI_TESTING.md](docs/CLI_TESTING.md#L112) therefore overstates coverage. Replace help-text scraping with a machine-readable generated command catalog and make the test assert the exact catalog count/IDs.

### Release hygiene gaps

- Staged documentation contains 25 broken relative links because source/examples/harness/marketplace targets are not copied.
- The Claude Desktop manifest points to a nonexistent <code>main</code> branch; the repository uses <code>master</code>.
- Publication manifests contain <code>maintainers@opentiatoolkit.local</code>.
- The canonical <code>basic-tia-interaction</code> skill alone lacks <code>agents/openai.yaml</code>, and the omission propagates to package copies.
- <code>docs/RELEASE_CHECKLIST.md</code> is ignored and therefore absent from a clean clone.
- CLI, MCP, and installer outputs are unsigned.
- There is no release workflow, SBOM/provenance, published checksum, dependency update bot, package lock, central package management, or SDK pin.
- No tracked <code>SECURITY.md</code>, <code>CONTRIBUTING.md</code>, code of conduct, issue templates, or PR template exists.

The official [Siemens TIA Portal Openness code snippets](https://github.com/siemens/tia-portal-openness-code-snippets) repository provides a useful governance baseline with security, contribution, ownership, and conduct material even though it is an examples repository rather than a competing toolkit.

## 13. Documentation audit

The repository has a substantial README, CLI guide, developer guide, version guide, test guide, plugin skills, and changelog. The problem is drift and overstatement, not absence of prose.

### Concrete inaccuracies and omissions

- Runtime help exposes 124 commands. [CLI_USAGE.md](docs/CLI_USAGE.md) has 97 command-level headings, 29 commands without dedicated headings, and nine commands absent anywhere:
  - <code>export-block-as-source</code>
  - <code>export-tags</code>
  - <code>generate-blocks-from-source</code>
  - <code>get-cross-references</code>
  - <code>import-source</code>
  - <code>list-external-sources</code>
  - <code>list-plc-alarm-text-lists</code>
  - <code>list-technology-objects</code>
  - <code>list-watch-tables</code>
- [README.md](README.md#L172) nevertheless labels the document the “Full CLI command reference.”
- [DEV_GUIDE.md](docs/DEV_GUIDE.md#L193) describes manual command registration, while registration is reflection-based.
- The developer guide presents <code>V21OpennessAdapter</code> as the version boundary but omits the larger Unified HMI and hardware adapters.
- It says Visual Studio 2019+, while C# 12 requires a current Visual Studio 2022/.NET SDK; there is no <code>global.json</code>.
- [TIA_PORTAL_VERSION_SUPPORT.md](docs/TIA_PORTAL_VERSION_SUPPORT.md#L5) and README say “V21 and forward,” while only V21 is validated and the runtime resolver/config are V21-specific.
- The version guide lists Base, Step7, WinCC, and WinCC.Extension as dependencies, while the Core project actually references Base, Step7, WinCCUnified, and Startdrive.
- Documentation tells users about <code>TiaPortalLocation</code>, but the runtime resolver does not read that environment variable.
- Library and project-text commands appear under “Other” in CLI help because categories are missing.
- There is no separate MCP tool reference, compatibility test matrix, recovery/partial-state guide, threat model, architecture decision log, or generated JSON schema catalog.

### Documentation system recommendation

Treat the typed command catalog as source of truth and generate command tables, parameter/default descriptions, input/output schemas, examples skeletons, profiles, and safety labels. Hand-written guides should explain workflows, judgment, recovery, and Siemens constraints rather than duplicate a 124-command catalog.

State compatibility conservatively:

> Supported and tested: TIA Portal V21 Update 2 (document exact tested updates). Other V21 updates require the contract test; future major versions are unsupported until a matching worker is released.

Add a mutation-safety matrix showing which commands are read-only, previewable, transactional, destructive, safety-sensitive, credentialed, read-back verified, and recoverable.

## 14. Direct Siemens V21 API assessment

The installed V21 PublicAPI was inspected directly rather than inferring the API only from toolkit code. Siemens V21 uses modular assemblies, which is important for both architecture and compatibility.

### Reflected assembly inventory

| Assembly family | Exported types | Declared public methods |
|---|---:|---:|
| Siemens.Engineering.Base | 1,382 | 4,578 |
| Siemens.Engineering.Step7 | 228 | 2,304 |
| Siemens.Engineering.WinCCUnified | 536 | 5,355 |
| Siemens.Engineering.Startdrive | 64 | 671 |
| Siemens.Engineering.WinCC classic | 65 | 669 |
| Siemens.Engineering.WinCC.Extension | 4 | 22 |
| Siemens.Engineering.Safety | 18 | 208 |
| Siemens.Engineering.SafetyValidation | 19 | 225 |
| Siemens.Engineering.TeamcenterGateway | 20 | 190 |
| AddIn Base/Permissions/Safety/Step7/Utilities combined | 91 | 453 |
| **Total inspected** | **2,427** | **14,675** |

These are reflection counts, not equally useful “features.” They show the size and modularity of the boundary. Core currently references Base, Step7, WinCCUnified, and Startdrive; it does not reference the Safety, SafetyValidation, WinCC classic, or TeamcenterGateway modules.

Official Siemens sources used for interpretation:

- [TIA Portal Openness V21 API documentation](https://docs.tia.siemens.cloud/r/en-us/v21/tia-portal-openness-api-for-automation-of-engineering-workflows), publication 03/2026.
- [What is new in TIA Portal Openness V21](https://docs.tia.siemens.cloud/r/en-us/v21/tia-portal-openness-api-for-automation-of-engineering-workflows/what-s-new-in-tia-portal-openness).
- [Siemens TIA Portal Openness code snippets](https://github.com/siemens/tia-portal-openness-code-snippets).
- [TIA Portal Openness Migration Assistant](https://cache.industry.siemens.com/dl/files/073/109998073/att_1352884/v2/109998073_TiaPortalOpennessMigrationAssistant_DOC_V1_0_0_en.pdf).
- [TIA Portal Openness Explorer support entry](https://support.industry.siemens.com/cs/ww/en/view/109760816).

### Coverage matrix

| Siemens domain | Toolkit position | Important gaps/opportunities |
|---|---|---|
| Portal/project lifecycle | Attach/open/create/save/close/archive, languages, texts, libraries | Atomic ownership/session transitions; multiuser server/local sessions; upload; snapshots; richer project security |
| Hardware/PROFINET | Strong generic tree/catalog/plug/wiring/address/patch support | Typed identity discovery; V21 communication connections, CiR, shared-device, secure online/IP/RH workflows |
| PLC software | Blocks/types/sources/tags, compile, guarded download, compare/status, VCI, browsing | Full watch/force management, technology object configuration, alarm CRUD, OPC UA, CAx, units; robust multilingual metadata |
| Version control | Useful VCI workspace/mapping/synchronization surface | Broader supported-object sync, conflict UX, blueprint/repository orchestration |
| Unified HMI | Broad screens/items/tags/trends/logging/JSON patch/import/capability work | Alarm system, audit, connections, runtime settings, scripts, text/graphic lists, strict schema/read-back |
| Startdrive | Telegram and some hardware integration | Parameter read/upload, commissioning, safety acceptance, encryption, technology extensions |
| Safety/SafetyValidation | Password hooks in selected workflows | Dedicated safety administration, signatures, runtime groups, printouts, validation tests/reports/traces |
| WinCC classic | Not a current target | Explicitly document unsupported rather than implying all HMI |
| Multiuser/UMAC/Security | Project UMAC credential path exists | Users/roles/rights/password policy, certificates, syslog, multiuser locking/marking |
| Teamcenter | Not referenced | Explicit non-goal or future integration |

The report does **not** recommend implementing every reflected API. The correct next step is a capability registry and a documented product strategy. High-value gaps should be chosen based on real workflows, safety, and competitive differentiation.

### V21 self-description is underused

V21 exposes richer self-description through <code>IEngineeringObject</code> composition, attribute, creation, and invocation information, plus service-provider information. The toolkit instead has many scattered reflection probes and string-named service checks.

A generic capability/introspection layer could:

- enumerate supported compositions, attributes, creations, invocations, and services;
- identify installed module/version support;
- generate diagnostic “why unsupported” output;
- replace name heuristics with stable capability evidence;
- drive MCP tool profiles and schema;
- make future version migration a contract-diff exercise.

This should not expose a dangerous unrestricted “invoke anything” tool by default. Separate read-only introspection from allow-listed typed mutations.

### Version claim is not defensible

[TiaPortalAssemblyResolver.cs](src/OpenTiaToolkit.Core/TiaPortalAssemblyResolver.cs#L72-L160), project references, and application configuration embed V21 paths. The documented runtime environment override is not implemented. Siemens' own V21 material describes a modular redesign and migration work for older applications, demonstrating why “and forward” cannot be assumed.

Advertise V21 only until a V22 worker passes a defined compatibility suite. Implement installed-version discovery and explicit worker selection; never silently bind an unvalidated major version.

## 15. Live V21 project findings

The Release CLI was rebuilt, its exact executable was registered in the Siemens Openness allow-list as required after the hash changed, and it attached to one running TIA Portal V21 Update 2 instance. All commands below were read-only. Project/device names and paths are omitted.

| Workflow | Observation | Assessment |
|---|---|---|
| List TIA instances | One V21 Update 2 instance found | Works |
| Connect/select open project | Open project found and selected | Works |
| List devices | 13 devices returned | Core traversal works |
| List PLCs | 27 candidates; only 1 confirmed; 26 false/duplicate candidates | **Unsafe discovery quality** |
| Open confirmed PLC | Succeeded | Works |
| List PLC blocks | 44 blocks returned | Works |
| Block comments | All displayed the multilingual object type name | **Functional defect** |
| Search comments | Based on the same bad extraction | **Likely false negatives** |
| List HMIs | Five heuristic candidates, none confirmed | **Discovery defect** |
| Open actual Unified HMI | Succeeded | Works despite discovery |
| List HMI screens | 34 screens returned | Works |
| Device IP addresses | Null across returned devices | **Misleading/incomplete discovery** |

The IP issue comes from reading direct device-item address objects/attributes rather than traversing the network-interface nodes and IP-suite attributes. Return “not inspected/unsupported” separately from “no address configured.”

This live pass confirms an important nuance: the core product is not vaporware. Real PLC and Unified HMI browse workflows work. The highest functional risk is **target confidence and result correctness**, not inability to attach.

## 16. Functional assessment by workflow

### Project and session

Breadth is good: instance attach, hidden portal launch, project open/create/save/close/archive, languages, and text import/export. The main weakness is lifecycle correctness—ownership, stale selection, atomic switching, and reset—not missing commands.

### PLC

The toolkit has a notably broad PLC surface for a beta: software tree, blocks/types/sources, tags/CSV, compile, compare, guarded PLCSIM Advanced download, online status, cross-references, watch/technology/alarm listing, VCI, and rename workflows. Priority should be:

1. make target discovery typed and unique;
2. make outcomes/transactions reliable;
3. correct multilingual metadata;
4. then deepen high-value official API areas.

### Hardware and drives

Generic catalog lookup, module/submodule operations, topology/networking, IO addressing, drive telegrams, GSD/GSDML parameters, dry-run, transactions, expected values, and destructive confirmations are among the strongest parts of the project. This domain should supply the reference mutation contract for the rest of the toolkit.

Remaining concerns are the size of <code>HardwareService</code>, string operation dispatch, credentials, typed discovery, and gaps in newly exposed V21 online/connection features.

### Unified HMI

The product has unusually deep open-source Unified HMI coverage: screen/item manipulation, tags/trends/logging, JSON export/diff/patch/import, events, property events, dynamization, faceplate probes, and backups/preflights in selected flows.

That breadth has accumulated in a 4,469-line adapter and several 1,500–1,700-line services. Before adding more features, stabilize strict document schema, typed capabilities, policy defaults, result truthfulness, property read-back, and modular boundaries.

### VCI, libraries, and texts

VCI/S7DCL mapping and synchronization, project/global library queries, master-copy operations, and translation workbook flows provide valuable breadth. They are less covered by live integration tests and documentation than headline PLC/HMI paths. Add disposable fixtures before expanding them.

### Operational/tooling UX

Script mode, JSON output, artifact spill, MCP progress/cancel/status, and workflow skills are useful. Missing operational basics include:

- <code>--version</code> with toolkit, worker, API assembly, and TIA versions;
- <code>doctor</code> for installations, allow-list hashes, dependency collisions, project state, and capabilities;
- machine-readable command/capability catalog;
- shell completion;
- read-only/default MCP profiles;
- a clear audit-log location and retention control.

## 17. Competitor comparison

Competitor capabilities below are based on their own repositories/README material as observed on the audit date. They are useful strategic signals, not independently certified benchmarks.

### Comparison matrix

| Project/reference | Positioning | Relative strengths | Relative weaknesses versus OpenTiaToolkit |
|---|---|---|---|
| [TIA_Portal_Openness_MCP](https://github.com/bulaofen0036-coder/TIA_Portal_Openness_MCP) | Direct CLI/MCP competitor; self-reports V20/V21 and about 180 tools | Declarative YAML/JSON generation and patching, blueprints/templates, scaffold/orchestration, doctor/config, prewarm/headless, dual transports, binding verification | Claims need independent validation; OpenTiaToolkit has strong workflow skills and some deeper guarded hardware/HMI paths |
| [Czarnak/tia-portal-mcp](https://github.com/Czarnak/tia-portal-mcp) | Narrow safety-focused MCP server | Modern .NET MCP host plus net48 Openness worker, single-use preview/apply token bound to tool/project/target/input/state, JSONL audit, compile-time Siemens stubs | Only about 16 grouped/batch tools; much narrower domain breadth |
| [chewcw/tia-portal-openness-mcpserver](https://github.com/chewcw/tia-portal-openness-mcpserver) | V20 MCP server with installer/updater | Allowed filesystem roots and maximum size, installer CLI, device-item copy/move, sampling tools, tests/releases | Older TIA target and narrower Unified HMI/hardware/VCI breadth |
| [vscode-tiaportal-mcp](https://github.com/heilingbrunner/vscode-tiaportal-mcp) | VS Code extension distribution | Low-friction one-click editor integration | More basic operation set and older target |
| [TiaUtilities](https://github.com/Parozzz/TiaUtilities) | In-TIA add-in rather than MCP server | SimaticML/LAD generation/parsing, alias/alarm generation, editable grids, JS customization/localization | Different automation model; less suited to headless MCP, while OpenTiaToolkit lacks its visual/in-TIA workflow |
| [tia-openness-api-client](https://github.com/Repsay/tia-openness-api-client) | Python facade | Low barrier for Python scripts; project/hardware/block/library abstractions | Maintainer notes limited ability to test; OpenTiaToolkit has broader validation/CLI/MCP/safety structure |
| [Siemens code snippets](https://github.com/siemens/tia-portal-openness-code-snippets) | Official examples/reference | Authoritative patterns, modular V20 solutions, governance | Not an end-user automation product |
| [Siemens Openness Explorer](https://support.industry.siemens.com/cs/ww/en/view/109760816) | Official inspection/code-generation utility | Object hierarchy/attribute exploration and snippet generation | Not a unified CLI/MCP workflow engine |

### Where OpenTiaToolkit is ahead

- Broad combination of PLC, Unified HMI, generic hardware, Startdrive telegram, VCI, project/library/text, CLI, MCP, and agent skills.
- Particularly useful HMI JSON diff/patch workflow and hardware dry-run/transaction model.
- Stateful script mode and one shared CLI/MCP command implementation.
- Guarded PLCSIM Advanced download/CPU operations.
- More explicit agent workflow packaging than most smaller servers.

### Where it is behind

1. **Isolation and safety:** the two-process worker and state-bound preview token in Czarnak's project are materially safer than the current reset and per-command confirmation model.
2. **Declarative orchestration:** the direct competitor's blueprints/scaffolds/templates address complete engineering outcomes rather than individual commands.
3. **Versioning:** self-reported V20/V21 support and explicit dual binaries are clearer than “V21 and forward.”
4. **Operational diagnosis:** doctor, prewarm, headless configuration, and binding verification reduce setup ambiguity.
5. **Filesystem policy:** allowed roots/size controls are already present in a competitor.
6. **Distribution:** VS Code extension and installer/updater experiences are easier to adopt.
7. **In-TIA visual productivity:** TiaUtilities provides grids, localization, and LAD/SimaticML generation that a headless toolkit does not.

### Recommended positioning

Do not compete on raw tool count. Position OpenTiaToolkit as:

> A safety-first, version-explicit TIA engineering automation platform with typed preview/apply workflows, disposable Siemens workers, deep Unified HMI/hardware support, and the same verified operations across CLI, MCP, and reusable blueprints.

That positioning requires finishing the safety/release architecture first. Tool count without trustworthy targeting and outcomes is a liability for agents.

## 18. Remediation roadmap

### Phase 0 — Stop-ship fixes (completed 10 July 2026)

1. [x] Installer publication remained frozen during remediation.
2. [x] CLI/MCP staging directories are split and real MCP initialization is automatically smoked from the exact stage.
3. [x] The vulnerable BCL dependency is pinned to 9.0.14 and CI runs a transitive vulnerability scan.
4. [x] HMI JSON import is dry-run by default, requires explicit apply/confirm, defaults clean preflight/backup safely, and returns blocked as failure.
5. [x] Reset no longer releases/disposes before quiescence; a non-quiescent call produces a fail-closed restart instruction.
6. [x] Tag replacement cannot observe cancellation between delete/create and explicitly reports restoration failure.
7. [x] Compile, blocked HMI, zero-result, and partial batch failures map to non-zero CLI/MCP outcomes with structured data.
8. [x] Focused regressions, the 344-test fast suite, installer build, staged stdio smoke, and live staged CLI/MCP workflows pass.

### Phase 1 — Trustworthy beta (1–2 weeks)

1. [x] Implement atomic session/project state transitions and clear both PLC/HMI selections consistently.
2. [x] Unify PLC/HMI discovery around typed capabilities; add stable identity/deduplication.
3. [x] Decode multilingual comments and test comment search.
4. [x] Publish/validate HMI JSON Schema; reject unknown version/kind/action.
5. [x] Make reflection setters fail closed and verify read-back.
6. [x] Replace raw safety passwords with credential references.
7. [x] Add MCP allowed-root/size/overwrite policy and spreadsheet-safe CSV.
8. [x] Fix formatting, CLI harness, docs gaps, branch URLs, metadata, and tracked release checklist.
9. [x] Split hosted vendor-free CI from licensed V21 validation CI.
10. [x] Generate command/MCP reference from the catalog.

Phase 1 completion evidence (11 July 2026): Release build completed with zero warnings/errors; `dotnet format --verify-no-changes` passed; all 365 vendor-free tests passed; NuGet reported no vulnerable direct or transitive packages; generated documentation reported 124 commands with no drift; packaged skills reported no drift; the CLI harness passed all 124 command help pages and all six negative cases; the staged payload initialized 127 MCP tools; and read-only live validation against the open V21 project found one instance, one project, 13 devices, one confirmed PLC, 44 blocks, two confirmed Unified HMIs, and 34 screens before successfully resetting the session. Raw project and target names were intentionally excluded from the report.

### Phase 2 — Production architecture (3–8 weeks)

1. [x] Create vendor-free Contracts/Application projects.
2. [x] Move Siemens session ownership into a net48 V21 worker process with a single queue.
3. [x] Implement typed outcomes/errors/audit journal across IPC, CLI, and MCP.
4. [x] Move mutation policy into one non-bypassable interceptor.
5. Unified HMI, hardware, tag, JSON, and formatter hotspots by capability:
   - [x] Introduce vendor-free facade and capability boundaries in the host architecture.
   - [ ] Decompose the remaining internal god classes behind those boundaries in later roadmap chapters.
6. [x] Add disposable archived-project integration fixtures and failure injection.
7. [x] Add capability profiles, pagination/streaming, doctor/version commands.
8. [x] Lock dependencies/SDK, sign binaries/installer, publish SBOM/checksums/provenance.

Phase 2 completion evidence (11 July 2026): Contracts, Application, CLI, and MCP have no Siemens/Core dependency; the isolated net48 V21 worker owns a serialized Siemens session and exposes typed IPC outcomes, centralized mutation policy, redacted JSONL audit, crash/delay failure injection, capabilities, paging, version, and hash-based doctor diagnostics. Release validation passed 7 architecture tests, 324 fast tests, sequential worker/MCP/archive integration suites, all 127 CLI help pages, six negative CLI cases, locked restore, formatting, generated-reference and skill-sync checks, and dependency vulnerability scanning. The signed staged payload exposed 130 MCP tools and completed a disposable hidden-TIA lifecycle through the packaged MCP: open TIA, create and enumerate one project, archive it, run diagnostics, and reset the worker. The release payload includes pinned SDK/dependencies, Authenticode-signed toolkit binaries and installer, SHA-256 manifests, SPDX SBOM, in-toto provenance, compatibility hashes, and security/recovery documentation.

### Phase 3 — Strategic differentiation (2–6 months)

1. [x] Build version-specific worker packages and a compatibility contract suite.
2. [x] Add declarative blueprints for common PLC/HMI/hardware outcomes with preview plans and state-bound apply tokens.
3. [x] Use V21 self-description for diagnostics, capability discovery, and migration diffs.
4. [x] Prioritize official API expansion from user demand: Unified alarms/runtime settings/text lists, richer Startdrive parameters, safety validation, multiuser/security, and deeper PLC watch/technology/alarm workflows.
5. [x] Consider VS Code/in-TIA companion UX only after core safety and release trust are established.

Phase 3 completion evidence (11 July 2026): the vendor-free host now resolves manifest-driven workers by requested TIA version and rejects incompatible protocol/version packages; the V21 package carries binary and public-surface contracts for Base, Step7, WinCC Unified, Startdrive, Safety, and SafetyValidation. A 142-command catalog adds blueprint validation/planning/apply with random ten-minute one-use tokens bound to canonical desired state and the live portal/project fingerprint, plus three PLC/HMI/hardware examples and JSON Schema. `doctor`, `capabilities`, `describe-api`, and `diff-api-profile` verified all ten prioritized V21 feature probes and a zero-difference migration baseline. Official-API reads were live-tested on the open project for F-safety state, watch/force tables, 432 bounded technology-object rows, alarm text lists, real SINAMICS parameters, 630 Unified alarm objects, runtime settings, 33 text lists, multiuser state, and project security services. A separate hidden-TIA blueprint created, archived, closed, rejected token replay, and cleaned a disposable project. Final validation passed 10 architecture tests, 328 fast tests, 4 worker integration tests, 3 MCP integration tests, all 142 staged CLI help pages, six negative cases, locked restore, formatting, generated-doc/skill drift, and the dependency vulnerability scan. The signed staged MCP exposed 145 tools and repeated the state-bound disposable blueprint/archive/reset lifecycle. The companion UX review selected the existing MCP-native VS Code experience and explicitly rejected a duplicate mutation path for this release.

## 19. Definition of done for the next release

| Gate | Required proof |
|---|---|
| Package | Clean-machine install; CLI and MCP isolated; MCP initialize/list-tools/read call succeeds |
| Safety | All writes classified; preview/apply policy tested; no apply-by-default destructive path |
| Lifecycle | Timeout/reset cannot overlap Siemens disposal; atomic session transition tests pass |
| Results | Failed compile, blocked safety check, partial batch, cancellation, and rollback failure map correctly |
| Security | No known High/Critical dependency; secrets indirect/redacted; filesystem policy enabled |
| Compatibility | Exact TIA V21 updates/API hashes listed and contract-tested |
| Tests | Hosted suite always runs; licensed live suite uses a disposable project; release smoke mandatory |
| Docs | Generated catalog has zero drift; compatibility/safety/recovery docs match behavior |
| Supply chain | Locked dependencies, signed artifacts, checksums, SBOM, provenance, security policy |
| Observability | Correlation IDs, structured errors, mutation audit journal, cleanup/retention documented |

## 20. Recommended issue breakdown

Create focused issues rather than one large “refactor”:

1. REL-001 isolate staged dependency graphs and add installed MCP smoke.
2. SES-001 make reset quiescent; then design worker-process isolation.
3. MUT-001 make tag replace cancellation-safe with explicit partial-state reporting.
4. HMI-001 change import defaults/confirmation/outcome.
5. DEP-001 migrate/pin MCP graph and add vulnerability gate.
6. RES-001 introduce typed outcome/error codes and host mappings.
7. SES-002 implement immutable session state and atomic swaps.
8. DSC-001/002 create one typed device software capability resolver.
9. HMI-002 publish strict schema and semantic validator.
10. API-001 make reflection mutation fail-closed/read-back verified.
11. CI-001 split hosted and licensed pipelines.
12. DOC-001 generate command/MCP docs and fix version claims.
13. SEC-001/002 create unified secret/file/artifact policy.
14. ARC-001 extract the V21 worker and vertical capability modules.
15. TST-001 introduce disposable live fixtures and failure injection.

## 21. Conclusion

OpenTiaToolkit is now a meaningful, release-ready V21 engineering product rather than a wrapper demo. The remediation retained its command breadth, Unified HMI work, hardware transaction patterns, VCI integration, shared CLI/MCP surface, and agent skills while adding the trust boundary the original audit found missing.

The former release blockers are closed: Siemens ownership is isolated in a serialized disposable worker; reset no longer races in-process vendor objects; mutations share centralized policy and typed outcomes; staged payloads are exercised; compatibility is explicit and diagnosable; and hosted plus licensed validation have distinct mandatory roles. Blueprints add repeatable, inspectable, state-bound engineering outcomes on top of that foundation.

Large Siemens-facing source files remain accepted maintainability debt. They should be decomposed before sustained rapid expansion, but they are not a 1.0 release blocker because their execution and failure boundary is contained. The only remaining release steps are production signing, publication, and verification of the downloaded artifacts. Support is deliberately limited to TIA Portal V21 until a future major version receives its own worker and complete compatibility validation.

## Appendix A — Reproducible checks used

Representative commands:

    dotnet test tests/OpenTiaToolkit.Tests/OpenTiaToolkit.Tests.csproj --filter "TestCategory!=Integration" -c Release
    dotnet format --verify-no-changes
    dotnet list OpenTiaToolkit.sln package --vulnerable --include-transitive
    powershell -File scripts/Sync-PluginSkills.ps1 -CheckOnly
    powershell -File scripts/Build-Installer.ps1 -Configuration Release -SkipBuild -StageOnly

The staged MCP check used stdin so the real host initialized. Live commands were limited to instance/project/device/PLC/HMI listing and browsing.

## Appendix B — Largest production files

| File | Physical lines |
|---|---:|
| <code>V21UnifiedHmiAdapter.cs</code> | 4,469 |
| <code>PlcTagService.cs</code> | 2,164 |
| <code>HardwareService.cs</code> | 1,811 |
| <code>HmiScreenJsonPatchApplier.cs</code> | 1,690 |
| <code>V21OpennessAdapter.cs</code> | 1,671 |
| <code>HmiJsonImportService.cs</code> | 1,502 |
| <code>TableOutputFormatter.cs</code> | 1,184 |
| <code>PortalSessionService.cs</code> | 1,048 |
| <code>HardwareCommands.cs</code> | 952 |
| <code>PlcImportExportService.cs</code> | 941 |
| <code>PlcSurfaceService.cs</code> | 872 |
| <code>V21HardwareAdapter.cs</code> | 855 |

## Appendix C — Positive controls to preserve

- Hardware patch transaction/exclusive access, dry-run rollback, expected-value checks, and destructive confirmation.
- HMI project transaction/rollback in supported import paths.
- Shared CLI/MCP command implementation and auto-registration composition test.
- Fast 334-test Release suite.
- MCP execution status/progress/cancellation/timeouts, after fixing reset semantics.
- VCI workspace path containment.
- UMAC environment-variable credential indirection and <code>SecureString</code>.
- Stateful CLI script mode, JSON output, and artifact spill.
- Canonical/plugin skill synchronization.
- Read-only live PLC/HMI workflows that successfully attached and returned real engineering objects.
