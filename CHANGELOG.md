# Changelog

All notable changes to OpenTiaToolkit will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed
- Added persistent multi-block PLC code transactions with TIA/project/PLC binding, whole-set stale preflight, baseline compile evidence, canonical result exports, reverse-order rollback, restart recovery, retained unverified recovery packages, and live V21 success/failure coverage.
- Hardened SIMATIC SD planning around exact PLC/group/type/name/number identities, ambiguity rejection, exact deletion, stale-target detection, startup cleanup of orphaned plans, and byte-hash proof after rollback.
- Added instruction-aware S7DCL validation with verified/known-unsupported/unknown classifications, pin/template checks, timer/counter/edge placement rules, branch/resource consistency, and standard-versus-safety boundaries.
- Extended live MCP PLC-code testing with deliberate stale-state rejection and forced compile-failure restoration verified by the original document hash.
- Extended the staged MCP live test to generate, verify, compile, and clean up the complete PLC code-authoring corpus against an open V21 project.

### Added
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
