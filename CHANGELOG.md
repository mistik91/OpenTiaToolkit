# Changelog

All notable changes to OpenTiaToolkit will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed
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
- Made CI fail loudly when Siemens PublicAPI references are missing instead of reporting a green build with restore, build, and tests skipped.
- Centralized repository build metadata, pinned the C# language version, added an `.editorconfig` and CI format check, removed unused build packages, and added a cleanup script for generated artifacts and test deployment folders.

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
