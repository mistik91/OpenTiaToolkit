# CLI Testing Guide

Guide for running the CLI automation harness against the built executable and a real TIA Portal V21 project.

## Purpose

The CLI automation harness validates the shipped CLI surface end to end:

- help output and command discovery
- argument validation and negative paths
- real portal, project, PLC, and export flows
- mutation flows on a disposable project copy

The harness lives in [scripts/Test-CLI.ps1](../scripts/Test-CLI.ps1) and writes all generated files under `artifacts/cli-tests/`, which is git-ignored.

## Prerequisites

- TIA Portal V21 installed locally
- a real `.ap21` test project
- a PLC name that exists in that project
- a built CLI executable at `src/OpenTiaToolkit.CLI/bin/Debug/net48/OpenTiaToolkit.CLI.exe`

Recommended environment variables:

- `OPEN_TIA_TEST_PROJECT_PATH`
- `OPEN_TIA_TEST_PLC_NAME`
- `OPEN_TIA_TEST_HMI_PROJECT_PATH`
- `OPEN_TIA_TEST_UNIFIED_HMI_NAME`

The Unified HMI integration tests skip cleanly when `OPEN_TIA_TEST_HMI_PROJECT_PATH` does not point to an existing `.ap21` project.

If the current CLI build has not been authorized for Openness yet, pre-authorize it from an elevated PowerShell session:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Register-OpennessAllowList.ps1 `
  -ExecutablePath .\src\OpenTiaToolkit.CLI\bin\Debug\net48\OpenTiaToolkit.CLI.exe `
  -PortalVersion 21.0 `
  -SkipVersionedWhitelist
```

Run build and test serially in the same workspace on Windows. Parallel build/test runs can leave `testhost` holding copied binaries and cause temporary file-lock warnings.

## Artifact Layout

Each run creates a timestamped folder:

```text
artifacts/cli-tests/<timestamp>/
  logs/
  exports/
  generated/
  copies/
  summary.json
  summary.txt
```

- `logs/`: one transcript per scenario
- `exports/`: read-only exported files
- `generated/`: mutation inputs and intermediate artifacts
- `copies/`: disposable project copies used by mutation tests
- `summary.txt`: human-readable pass/fail/skip summary
- `summary.json`: machine-readable summary

## Suite Overview

| Suite | Purpose | Real TIA Required | Mutates Fixture |
|------|---------|-------------------|-----------------|
| `Help` | Validates `help`, `--help`, `-h`, and `help <command>` across the exposed CLI surface | No | No |
| `Negative` | Validates unknown commands and missing/invalid arguments | No | No |
| `ReadOnly` | Exercises portal/project/PLC flows and export commands in one stateful CLI process | Yes | No |
| `Mutation` | Exercises mutation commands against a disposable project copy | Yes | Only the disposable copy |
| `All` | Runs `Help`, `ReadOnly`, `Mutation`, and `Negative` in sequence | Mixed | Only the disposable copy |

## Recommended Commands

```powershell
# Build first
dotnet build OpenTiaToolkit.sln

# Help suite
powershell -ExecutionPolicy Bypass -File .\scripts\Test-CLI.ps1 -Suite Help

# Negative suite
powershell -ExecutionPolicy Bypass -File .\scripts\Test-CLI.ps1 -Suite Negative

# Read-only real-project suite
powershell -ExecutionPolicy Bypass -File .\scripts\Test-CLI.ps1 `
  -Suite ReadOnly `
  -ProjectPath $env:OPEN_TIA_TEST_PROJECT_PATH `
  -PlcName $env:OPEN_TIA_TEST_PLC_NAME

# Mutation suite on a disposable copy
powershell -ExecutionPolicy Bypass -File .\scripts\Test-CLI.ps1 `
  -Suite Mutation `
  -ProjectPath $env:OPEN_TIA_TEST_PROJECT_PATH `
  -PlcName $env:OPEN_TIA_TEST_PLC_NAME
```

Optional harness switches:

- `-BuildFirst`: builds `OpenTiaToolkit.sln` before the suite
- `-RegisterAllowList`: runs the Openness allowlist helper before the suite
- `-CliPath`: points the harness at a different built executable
- `-OutputRoot`: overrides the default `artifacts/cli-tests` root

## What The Harness Covers

### Help

- general help output
- `--help` and `-h`
- `help <command>` for every command currently registered by the CLI host

### Negative

- unknown command handling
- help for unknown commands
- missing required arguments
- invalid command values such as unsupported block types

### ReadOnly

The `ReadOnly` suite is designed to keep state alive inside one CLI process so the test matches real operator workflows and `--script`-style automation.

It covers:

- `list-instances`
- `open-tia-portal`
- `connect-instance` when an external TIA instance is already open
- `connect-portal`, `disconnect-portal`, and `portal-status`
- `open-project`, `get-project-info`, and project session handling
- `list-devices`, `list-plcs`, `list-hmis`, and project tree inspection
- `open-plc`, `get-software-tree`, `compile-software`, `compare-software`, guarded `download-software` dry-runs, `get-online-status`, `start-cpu`, `stop-cpu`, `get-blocks`, `get-block-info`, and `search-blocks`
- XML export commands such as `export-block`, `export-plc`, `export-type`, and `export-types`
- SIMATIC SD document export commands `export-block-as-docs` and `export-blocks-as-docs`

Applied PLC downloads are not part of the generic `ReadOnly` suite. Validate `download-software --dry-run=false --confirm=true` only against an explicitly configured PLCSIM Advanced target. For fail-safe projects, prefer `--only-changes=true`; Siemens Openness may reject full fail-safe software loads even when the simulator connection is valid.

### Mutation

The `Mutation` suite starts by creating a disposable copy with `save-as-project`, then runs all mutation checks against that copy.

It covers:

- `create-block`
- `modify-tags`
- `find-replace-tags`
- `find-replace-db-members`
- `find-replace-udt-members`
- `delete-blocks`
- XML block import/export roundtrips using an existing exportable block
- SIMATIC SD validation and guarded roundtrips using `validate-plc-documents`, `plan-block-document-import`, and `apply-block-document-import`, plus low-level export/import commands
- type XML roundtrips using `export-type`, `export-types`, `import-type`, and `import-types`

## Expected Skips

Some scenarios are intentionally treated as skips instead of failures when the environment does not support them:

- `connect-instance` is skipped if no external TIA Portal instance is already running
- export scenarios are skipped if the chosen fixture project does not contain an exportable probe block or type
- the created ProDiag block export/import probe is skipped when TIA V21 marks the block as know-how-protected

This lets the harness distinguish between:

- a real CLI regression
- a project-content limitation
- an environment precondition that was not present for that run

## Current V21 Notes

- The current validated fixture supports positive XML export/import and SIMATIC SD document export/import for blocks
- The current validated fixture supports positive XML export/import for PLC types
- SIMATIC SD testing is currently block-focused because the CLI surface does not expose document-format type commands
- Newly created ProDiag blocks can still be non-exportable in V21 because of know-how protection; the harness treats that as an expected skip, not a product failure

## Reading Results

Start with `summary.txt`, then open the scenario log under `logs/` for the failing or skipped case.

Typical triage order:

1. `summary.txt`
2. matching transcript in `logs/`
3. generated or exported artifacts in `generated/` or `exports/`
4. disposable project copy in `copies/` if the failure only happens after mutation
