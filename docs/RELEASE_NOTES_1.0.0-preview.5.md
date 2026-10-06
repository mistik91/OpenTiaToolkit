# OpenTiaToolkit 1.0.0-preview.5

Release date: 5 August 2026

This unsigned prerelease adds safe WinCC Unified discrete-alarm engineering, expands the shared CLI/MCP surface to 186 commands, and packages a dedicated alarm workflow for supported agents.

## Highlights

- Added complete canonical `list-unified-discrete-alarms` and exact `get-unified-discrete-alarm` reads with deterministic content hashes, multilingual `EventText`/`InfoText` values, ordered parameter tags, and optional Siemens `Validate()` diagnostics.
- Added atomic `apply-unified-discrete-alarms` create/update dry-runs and applies. Versioned JSON patches preserve omitted properties, bind updates to fresh hashes, execute real Siemens setters and validation inside a transaction, and roll back the complete batch on failure.
- Added hash-bound `delete-unified-discrete-alarm` with exact-name resolution, cross-reference reporting, referenced-delete blocking by default, rolled-back deletion previews, and explicit confirmation for persistent deletion.
- Added `hmi-discrete-alarm-workflow` across the Codex, Claude Code, and OpenCode packages, including license, localization, trigger uniqueness, dry-run/apply, recovery, and disposable-project safety guidance.
- Hardened the opt-in live integration campaign so it starts a hidden toolkit-owned TIA Portal instance and verifies the exact disposable project path before any mutation.
- Expanded hardware configuration contract benchmarks and tightened validation/guidance for generated-parent targets, IO byte ranges, address binding, GSD/GSDML fields, and atomic rollback evaluation.

## Validation

- Release solution build completed with zero warnings and zero errors against the TIA Portal V21 PublicAPI.
- Passed 663 vendor-free fast tests, 54 architecture tests, and 5 worker IPC integration tests.
- Verified all 186 generated CLI commands and their MCP schemas plus the 3 MCP infrastructure tools.
- Passed generated-reference, canonical/packaged/active-skill drift, repository hygiene, staged installer, stable-SDK raw-stdio, worker package parity, and MCPB validation.
- Completed a disposable V21 Unified HMI live campaign covering canonical reads, rolled-back dry-run, persistent create/update, idempotence, multilingual text round-trip, multi-alarm apply, forced mid-batch rollback, hash/reference-guarded deletion, cleanup, save/reopen verification, and immutable baseline archive integrity.
- Confirmed the user's original open project remained unchanged and contained no campaign alarm names.

## Compatibility And Scope

- Supported: TIA Portal V21 with a WinCC Unified engineering license.
- Unsupported: TIA Portal V20 and earlier.
- Unvalidated and unsupported: TIA Portal V22 and later.
- Discrete-alarm engineering only: analog alarms, OPC UA alarm types, alarm-class CRUD, PLC alarms, logging archives, and runtime alarm generation or acknowledgment remain outside this workflow.
- Localized text cultures must already be configured in the Unified HMI text items; project-language presence alone may not create the required item.
- Alarm IDs and trigger tag/bit combinations must be unused. Siemens validation remains authoritative.

## Unsigned Preview Notice

This preview is intentionally unsigned. Windows may show an unknown-publisher or SmartScreen warning. Verify `INSTALLER-SHA256SUMS.txt`, `MCPB-SHA256SUMS.txt`, and the supply-chain checksums before running the installer or bundle.

Run `doctor` after installation and after every TIA Portal update or hotfix. Start mutation workflows on disposable project copies and keep independent backups.
