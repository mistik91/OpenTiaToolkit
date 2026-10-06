---
name: opentia-toolkit-workflow
description: Use as the general OpenTiaToolkit router skill for Siemens TIA Portal automation requests. Use when a task mentions OpenTiaToolkit, TIA Portal, Openness, PLCs, Unified HMI, discrete alarms, hardware configuration, VCI synchronization, project/device discovery, trend logging, or repo development and the correct specialized workflow is not already obvious.
---

# OpenTiaToolkit Workflow

## Overview

Use this skill as the entry point for OpenTiaToolkit work. Its job is to classify the request, establish a proven TIA Portal project context, choose the right specialized skill, and enforce shared safety rules before any mutation.

Keep this skill thin. After routing, follow the specialized skill or the MCP tool guidance instead of duplicating command details here.

## Routing

1. Establish the request domain:
   - TIA instance, project lifecycle, devices, PLC/HMI target discovery: use `basic-tia-interaction`.
   - Unified HMI screen JSON, screen item, faceplate, event handler, dynamization, or JSON import/export edits: use `hmi-unified-agent-editing`.
   - Generic catalog, module/submodule, PROFINET, IO address, Startdrive telegram, GSD/GSDML parameter, or `apply-hardware-patch` work: use `hardware-configuration-workflow`.
   - PLC blocks, types, source files, compile, import/export, tags, cross-references, watch tables, technology objects, alarm text lists, CSV tag updates, or find/replace rename work: use `plc-block-and-tag-workflow`.
   - PLC code generation or modification in LAD, SCL, S7DCL/S7RES, external source, or SimaticML: use `plc-code-engineering` after target discovery.
   - WinCC Unified discrete-alarm discovery, exact reads, create/update JSON applies, or guarded deletion: use `hmi-discrete-alarm-workflow`.
   - Unified HMI trend logging, logging tags, data logs, or bulk logging configs: use `hmi-trend-logging-workflow`.
   - Project/global library browsing, master copies, library types, UDT versions, update checks, or approved asset reuse: use `library-workflow`.
   - Version Control Interface workspace, mapping, status, export/connect/sync work: use `vci-sync-workflow`.
   - Project Server, Multiuser local sessions, markings, conflicts, save/check-in, or stale-session recreation: use `multiuser-collaboration-workflow`.
   - Project languages, internationalization, translation export/import, project text workbooks, `list-project-languages`, `export-texts`, or `import-texts`: use this skill plus `basic-tia-interaction`.
   - OpenTiaToolkit repo changes, tests, docs, plugin packaging, or skill packaging: use `opentia-dev-workflow`.
   - Declarative multi-step PLC/HMI/hardware outcomes, `validate-blueprint`, `plan-blueprint`, or `apply-blueprint`: use `engineering-blueprint-workflow` after target discovery.
2. If live TIA context is needed and not already proven in the current turn, use `basic-tia-interaction` before the domain workflow.
3. If several domains are involved, order them by dependency. For example: session selection, hardware discovery, PLC changes, HMI changes, compile/save verification.
4. Ask only for information that cannot be discovered safely: ambiguous project among multiple open instances, missing project path, unclear PLC/HMI target, or destructive confirmation.

## Shared MCP Rules

Keep OpenTiaToolkit MCP calls serial. If the server reports an active operation or a client timeout hides a result, call `operation-status` to inspect the active operation and latest progress, then wait for the in-flight operation to finish and verify the target state before retrying. Use `cancel-operation` for cooperative cancellation when a tool supports it, and reserve `reset-session` for recovery when the MCP gate or TIA attachment must be discarded. Reset waits for quiescence and fails closed if a vendor call remains alive; when that happens, terminate and restart the MCP process rather than retrying reset or another TIA command in the same process. PLC batch tag commands (`modify-tags`, `find-replace-tags`, `find-replace-db-members`, and `find-replace-udt-members`) report item progress and honor cancellation between items and before TIA write calls.

Prefer read-only discovery before mutation:

```text
list-instances -> connect-instance/connect-portal -> list-projects/get-project-info -> list devices/targets -> focused domain operation
```

Do not call `open-project` before connecting to TIA Portal. Do not conclude that no project is open from a connect response alone; verify with `list-projects` or `get-project-info`.

Use exact command argument names from the tool schema. For CLI-shaped examples, use kebab-case such as `plc-name`, `hmi-name`, `screen-path`, `dry-run`, and `confirm`.

Large MCP results may return a compact artifact manifest instead of the full payload. When `data.outputPath`, `data.format`, and `inlineOutputSuppressed` are present, read the file at `outputPath` for the full result before continuing. Domain commands choose task-appropriate formats: CSV for tabular rows such as HMI tags, PLC block lists, hardware addresses, VCI mappings, and PLC rename/update previews; JSON for hierarchical snapshots such as HMI screens, hardware trees, and software trees.

## Mutation Defaults

Use dry-runs, previews, or status reads before applying changes when the command supports them:

- `apply-hardware-patch`: start with `dry-run=true`.
- `apply-hmi-screen-json-patch` and `apply-hmi-screen-json-edit`: start with `dry-run=true` and keep `base-hash` filters stable.
- `import-hmi-json`: dry-run, warning blocking, and clean preflight default to safe values. Apply only with `dry-run=false`, `apply=true`, and `confirm=true` after reviewing the dry-run result; backup also defaults to true on apply.
- `create-hmi-trends`, `remove-hmi-logging-tags`, `delete-hmi-data-logs`: start with `dry-run=true`.
- `apply-unified-discrete-alarms` and `delete-unified-discrete-alarm`: start with `dry-run=true`, retain exact names and fresh canonical hashes, and follow `hmi-discrete-alarm-workflow` before applying.
- `import-texts`: start with the default `dry-run=true`; apply project text workbook changes only with explicit user intent and `dry-run=false`.
- `copy-from-master-copy`: start with `dry-run=true`; apply only with `dry-run=false` and `confirm=true` after confirming the master copy matches the target domain.
- `import-source`, `generate-blocks-from-source`: start with `dry-run=true`; apply only with `dry-run=false` and `confirm=true`, then verify with `get-block-info`/`get-blocks` and `compile-software`.
- `modify-tags`, `find-replace-tags`, `find-replace-db-members`, `find-replace-udt-members`: start with `dry-run=true`.
- Deletions require explicit confirmation from the user or an explicit `confirm=true` request.

Prefer backups for HMI applies and disposable project copies for broad hardware/PLC/HMI mutation workflows. Save the project only when the user asked to persist changes or the workflow clearly requires persistence after successful verification.

For project-level backup/rollback, prefer `archive-project` before risky mutation sessions and `retrieve-project` when a disposable copy or rollback is needed. Run `save-project` before `archive-project` because TIA Portal refuses to archive projects with unsaved changes.

## Project Texts And Languages

For translation or internationalization workflows, establish the TIA instance and open project first, then discover the export scope:

```text
list-instances -> connect-instance/connect-portal -> list-projects/get-project-info -> list-project-languages -> list-devices/list-hmis -> export-texts --objects <device-or-device-item> -> import-texts
```

Use `list-project-languages` before export/import so source and target cultures are based on the actual project language settings. `export-texts` requires an explicit `objects` scope; Siemens Openness rejects empty scope and can reject broad mixed device sets that contain objects without project text support. Prefer a known text-bearing HMI or device item such as a Unified HMI target, and retry with a narrower object if Siemens reports the object does not support text export.

Project text export writes a Siemens `.xlsx` workbook artifact. Treat `import-texts` as a mutation command: keep the default `dry-run=true` for validation, and use `dry-run=false` only after the user confirms the workbook should be imported. Verify after import with `get-project-info`, another targeted export, or a domain read that reflects the changed text.

## Verification

After a read-only task, report the discovered target and relevant result.

After a mutation, verify through the narrowest available read:

- Project/session changes: `portal-status`, `list-projects`, or `get-project-info`.
- PLC changes: `get-blocks`, `get-block-info`, `get-software-tree`, `export-tags`, `get-cross-references`, relevant source/tag/rename result, and compile when appropriate.
- Library workflows: `list-master-copies`, `list-library-types`, `get-library-type-versions`, or `check-library-updates`.
- Project text workflows: `list-project-languages`, `get-project-info`, a targeted `export-texts`, or `import-texts` dry-run/summary.
- HMI screen changes: fresh screen snapshot, item properties, `VerifiedScreen`, or `hmi-status`.
- Unified discrete-alarm changes: fresh `get-unified-discrete-alarm` or `list-unified-discrete-alarms` canonical snapshots and hashes.
- Hardware changes: `get-hardware-tree`, `get-hardware-io-addresses`, `get-hardware-networks`, `audit-hardware-tag-mappings`, or parameter reads.
- VCI changes: `get-vci-block-status` or `list-vci-mappings`.

Report any safety block exactly enough for the user to act, especially online-mode blocks, stale hashes, ambiguous targets, active references, or destructive-operation confirmation requirements.
