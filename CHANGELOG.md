# Changelog

All notable changes to OpenTiaToolkit will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Added a portable Agent Plugins 1.0 package that exposes the shared OpenTiaToolkit skills and stdio MCP server to compatible clients while retaining the existing Codex, Claude Code, Claude Desktop, and OpenCode integrations.
- Added offline Agent Plugins contract, skill-layout, launcher-resolution, and installer-staging validation.

### Changed
- Made agent-plugin MCP launchers discover adjacent installed-product payloads and custom installation layouts without requiring inherited `OPEN_TIA_MCP_EXE` state.

## [1.0.0-preview.5] - 2026-08-05

### Added
- Added canonical WinCC Unified discrete-alarm discovery and exact reads with deterministic hashes, multilingual texts, parameter tags, and optional Siemens validation diagnostics.
- Added atomic versioned-JSON create/update dry-runs and applies plus hash-bound, cross-reference-aware guarded deletion for Unified discrete alarms.
- Added a dedicated discrete-alarm workflow skill synchronized across Codex, Claude Code, and OpenCode packages, together with a toolkit-owned disposable-project live integration campaign.
- Added the OpenCode hardware benchmark harness and expanded held-out, adversarial, cross-domain, and device-family campaign coverage.

### Changed
- Expanded the shared CLI/MCP command surface from 182 to 186 commands and advertised discrete-alarm write capability in the V21 compatibility profile.
- Tightened hardware-patch contract validation and guidance for generated-parent targets, IO byte ranges, topology/address bindings, GSD/GSDML fields, and atomic rollback evaluation.

### Fixed
- Prevented the disposable Unified discrete-alarm integration test from attaching to an arbitrary running TIA Portal instance; it now starts a hidden toolkit-owned portal and verifies the exact disposable project path before mutation.

## [1.0.0-preview.4] - 2026-07-30

### Added
- Added canonical `contained-type` support (with `faceplate-type` compatibility aliases) to direct and JSON-patch Unified HMI item creation.
- Added a documented MCP protocol compatibility matrix, exhaustive raw-stdio validation, and a live read-only MCP coverage harness.

### Fixed
- Bound a faceplate container's `ContainedType` before importing interface values, including real rollback-only initialization during JSON patch dry-runs, so bound faceplates can be created and duplicated without losing their interface schema.
- Made MCP reset wait for active worker operations to quiesce and fail closed when the grace period expires, while cancellation replaces the affected worker to restore a clean session.
- Preserved typed JSON-RPC errors for unknown or blank tool names and made request argument lookup consistently case-insensitive.

### Changed
- Migrated the MCP host from the preview C# SDK to stable `ModelContextProtocol` 2.0.0 and the MCP `2026-07-28` discovery and per-request metadata model, while retaining initialize-based compatibility with older clients.
- Derived the complete MCP catalog and strict JSON Schemas from the runtime command surface: 182 shared commands plus 3 infrastructure tools.
- Added structured content with text fallback, large-result spill artifacts, deterministic duplicate/collision checks, and exact CLI/MCP/worker parity validation for staged installer and MCPB payloads.
- Updated host dependencies to the stable .NET 10.0.10 extensions stack and removed preview transitive dependencies.

## [1.0.0-preview.3] - 2026-07-18

### Changed
- Made same-machine `Run As` collaboration the preferred Multiuser workflow, with separate Windows identities, TIA instances, workers, and private local-session directories.
- Defined the TIA UI check-in as an explicit harness boundary: users may complete it manually or through harness-provided computer-use automation, followed by fresh-session artifact verification.
- Updated the Multiuser agent-session harness and synchronized the canonical workflow skill across Codex, Claude Code, and OpenCode packages.

### Fixed
- Blocked `commit-multiuser-session` from misusing `LocalSession.CloseAndCommit()`, which can create a server revision without merging local-session changes in TIA Portal V21.
- Normalized Unicode and commonly misdecoded UTF-8 byte-order marks in CLI script/stdin mode.

## [1.0.0-preview.2] - 2026-07-14

### Changed
- Made PLC code transaction planning perform mandatory semantic impact analysis: normalized interface/layout deltas, exact external dependency resolution, live PLC cross-reference consumers, deterministic evidence fingerprints, and apply-time stale revalidation now gate every direct-import mutation.
- Added a shared PLC code-format capability/validation contract and format-specific hardening for native SCL, S7DCL LAD/data, baseline-bound SimaticML, and confirmed directional VCI synchronization.
- Extended persistent PLC code transactions with dependency-ordered multi-target SCL generation, PLC `TYPE` generation/deletion, and existing block/type SimaticML patch adapters, including source/XML stale checks, canonical read-back, compile policy, reverse rollback, and restart-safe recovery.
- Grounded LAD validation in live V21 exports: `Normalize`, `Scale`, positive/negative edge contacts, retentive timers, inferred typed calls, exact `ATTR_DB` pins, and project calls now have instruction-specific templates, pin contracts, and edge-memory placement rules.
- Expanded the format-specific authoring skills with an evidence-tier coverage matrix, complete LAD instruction catalog, DB layout catalog, SCL declaration/state catalog, SimaticML patch recipes, aligned UI prompts, and live-tested branch/timer, structured DB, stateful FB, and TYPE-plus-FC assets.
- Hardened global DB validation for quoted members, duplicate start values, array/string bounds, structure balance, optimized-layout impact, and explicit instance-DB/type workflow boundaries.
- Corrected SCL dependency extraction for local FB instances and rejected the V21-proven invalid authored member-level `S7_Retain` form before mutation.
- Required `confirm=true` for VCI workspace-to-project synchronization and rejected implicit mapping creation in that direction.
- Added persistent multi-block PLC code transactions with TIA/project/PLC binding, whole-set stale preflight, baseline compile evidence, canonical result exports, reverse-order rollback, restart recovery, retained unverified recovery packages, and live V21 success/failure coverage.
- Hardened SIMATIC SD planning around exact PLC/group/type/name/number identities, ambiguity rejection, exact deletion, stale-target detection, startup cleanup of orphaned plans, and byte-hash proof after rollback.
- Added instruction-aware S7DCL validation with verified/known-unsupported/unknown classifications, pin/template checks, timer/counter/edge placement rules, branch/resource consistency, and standard-versus-safety boundaries.
- Extended live MCP PLC-code testing with deliberate stale-state rejection and forced compile-failure restoration verified by the original document hash.
- Extended the staged MCP live test to generate, verify, compile, and clean up twelve PLC code-authoring blocks plus a source-generated PLC type against an open V21 project.

### Added
- Added `get-plc-code-impact` and fingerprint-bound `approve-plc-code-impact`; safe plans receive tokens directly, review/unknown findings require explicit revalidated acknowledgement, and deletions with active PLC consumers fail closed.
- Added `describe-plc-code-formats`, `validate-plc-code-artifact`, `add-scl-source-transaction-operation`, `add-simaticml-transaction-operation`, and `add-delete-plc-type-transaction-operation`.
- Added offline SIMATIC SD validation and state-bound `plan-block-document-import` / `apply-block-document-import` workflows with input and target hashes, one-use tokens, read-back, compile verification, and automatic rollback.
- Added guarded PLC external-source deletion for disposable native SCL workflows.
- Added PLC code engineering, S7DCL LAD/data, native SCL source, and SimaticML fallback skills with live-tested FC, FB, mixed LAD/SCL, global DB, and SCL assets.

## [1.0.0] - 2026-07-11

### Changed
- Added Phase 3 versioned worker manifests, API compatibility contracts, state-bound blueprints, V21 self-description/migration diffs, and prioritized official API insight commands.
- Added explicit Safety and SafetyValidation worker compatibility dependencies and documented the MCP-native companion UX decision.
- Completed the Phase 2 production architecture: vendor-free Contracts/Application/CLI/MCP projects, an isolated net48 V21 worker with a single request queue, versioned IPC, typed errors/outcomes, centralized mutation policy, correlation IDs, and redacted mutation auditing.
- Added capability boundaries, pagination and large-result artifact streaming, `version`/`doctor`/`capabilities`, disposable archive fixtures, deterministic failure injection, exact Siemens API compatibility profiles, locked SDK/dependencies, signed release support, SPDX SBOMs, checksums, and in-toto/SLSA provenance.
- Completed Phase 1 trustworthy-beta hardening: atomic session transitions, typed deduplicated PLC/HMI discovery, multilingual comments, strict versioned HMI JSON validation, fail-closed reflection writes, environment-referenced safety credentials, MCP file policy, and spreadsheet-safe CSV output.
- Split hosted vendor-free checks from licensed V21 validation, generated CLI/MCP references from the runtime catalog, corrected V21 support and runtime-location documentation, and added a tracked release checklist.
- Completed the Phase 0 stop-ship remediation: isolated CLI/MCP installer dependencies, added exact staged stdio smoke testing, and verified the staged CLI/MCP against an open V21 project.
- Made MCP session reset wait for operation quiescence and fail closed with process-restart guidance instead of releasing the gate and disposing a live Siemens session.
- Made Unified HMI JSON import dry-run by default and require explicit `dry-run=false`, `apply=true`, and `confirm=true`; blocked HMI operations now return command failures with structured data.
- Made PLC compile and zero/partial bulk import/export outcomes return command failures, preserving structured diagnostics for CLI and MCP callers.
- Made PLC tag replacement cancellation-safe across its delete/create window and report whether original-tag restoration failed.
- Pinned `Microsoft.Bcl.Memory` to patched version 9.0.14 and added an unconditional transitive vulnerability scan to CI.
- Added service-level progress reporting and cooperative cancellation checks for `modify-tags`, `find-replace-tags`, `find-replace-db-members`, and `find-replace-udt-members` when hosted through MCP.
- Replaced `create-tag` and the page-based `update-tags-from-csv` workflow with `modify-tags`, an explicit JSON/CSV batch surface for guarded create, update, delete, and replace operations.
- Prepared the repository for beta distribution and future open-source publication.
- Added TIA Portal V21 VCI workspace management, S7DCL block mapping, surgical synchronization, filtered bulk synchronization, and whole-PLC traversal through the shared CLI/MCP surface.
- Added compact bulk VCI results by default with `include-skipped=true` available for full audit output.
- Added generic hardware automation for catalog lookup, module/submodule placement checks, PROFINET network and process-image inspection, Startdrive telegram review, guarded hardware patch dry-runs/applies, GSD parameter reads, and PLC tag-to-hardware mapping audits.
- Added artifact-spill handling for large CLI/MCP responses, including compact manifests and domain-specific CSV or JSON output paths for PLC, HMI, hardware, and VCI review commands.
- Added PLC verification-loop commands for online compare, guarded PLCSIM Advanced software download, online status checks, and guarded CPU start/stop attempts where Siemens Openness exposes runtime control.
- Fixed attachment to a running TIA instance with one already-open project so downstream PLC and VCI operations share the selected project context.
- Added `list-hmi-tags --root-path` for fast enumeration of a known Unified HMI tag subtree.
- Raised supplied OpenCode MCP timeout defaults to 600 seconds for long-running TIA Portal reads.
- Added confirmed `remove-hmi-logging-tag` and `delete-hmi-data-log` tools, with reference checks that prevent deletion of an in-use Unified HMI data log.
- Added batch Unified HMI trend lifecycle tools for previewable creation, logging-tag removal, and safe unreferenced data-log deletion.
- Made CI report when Siemens-dependent build/format/tests are skipped while still running restore, dependency vulnerability, config, documentation, and skill-sync validation.
- Centralized repository build metadata, pinned the C# language version, added an `.editorconfig` and CI format check, removed unused build packages, and added a cleanup script for generated artifacts and test deployment folders.

### Release validation

- Validated the complete V21-only worker/CLI/MCP architecture, state-bound blueprint lifecycle, API compatibility diagnostics, release packaging, and representative live PLC, Unified HMI, hardware, safety, multiuser, and security reads.
- Confirmed successful independent-machine use without recording private machine, project, device, or user identifiers.
- Declared TIA Portal V21 as the only supported major version; future V22 support requires a separate worker package and compatibility validation.

## [0.1.0] - 2026-05-07

### Added
- Initial beta release of the OpenTiaToolkit CLI, Core library, and MCP server.
- Shared command architecture for portal, project, device, PLC, and Unified HMI workflows.
- Interactive CLI REPL mode plus single-shot command execution.
- MCP server exposing the same command surface for agent and harness integrations.
- TIA Portal V21 Openness integration as the current supported and validated target.
- PLC operations for browse, compile, import/export, tag management, block operations, and rename workflows.
- Unified HMI operations for screen/item browse and mutation, JSON export/diff/import, targeted JSON patches, capability inspection, faceplate interface probing, health checks, and label proximity auditing.
- Codex plugin package with the OpenTiaToolkit MCP configuration and HMI Unified editing skill.
- MCP client configuration snippets for Codex, Claude Desktop, Claude Code, Google Antigravity, and VS Code.
- Unit coverage for command parsing, routing, output formatting, composition, MCP tool bridging, JSON patching/import/diff behavior, and helper services.
- Opt-in live integration tests for real TIA Portal validation.

### Removed
- Siemens Openness PDF manuals from the repository; users should refer to Siemens documentation for official API manuals.
- Internal HMI workflow TODO notes and local machine validation artifacts.
- Placeholder unit test and hard-coded `test-find-replace` CLI command.
