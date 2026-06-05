---
name: opentia-toolkit-workflow
description: Use as the general OpenTiaToolkit router skill for Siemens TIA Portal automation requests. Use when a task mentions OpenTiaToolkit, TIA Portal, Openness, PLCs, Unified HMI, hardware configuration, VCI synchronization, project/device discovery, trend logging, or repo development and the correct specialized workflow is not already obvious.
---

# OpenTiaToolkit Workflow

## Overview

Use this skill as the entry point for OpenTiaToolkit work. Its job is to classify the request, establish a proven TIA Portal project context, choose the right specialized skill, and enforce shared safety rules before any mutation.

Keep this skill thin. After routing, follow the specialized skill or the MCP tool guidance instead of duplicating command details here.

## Routing

1. Establish the request domain:
   - TIA instance, project, devices, PLC/HMI target discovery: use `basic-tia-interaction`.
   - Unified HMI screen JSON, screen item, faceplate, event handler, dynamization, or JSON import/export edits: use `hmi-unified-agent-editing`.
   - Generic catalog, module/submodule, PROFINET, IO address, Startdrive telegram, GSD/GSDML parameter, or `apply-hardware-patch` work: use `hardware-configuration-workflow`.
   - PLC blocks, types, compile, import/export, tags, CSV tag updates, or find/replace rename work: use `plc-block-and-tag-workflow`.
   - Unified HMI trend logging, logging tags, data logs, or bulk logging configs: use `hmi-trend-logging-workflow`.
   - Version Control Interface workspace, mapping, status, export/connect/sync work: use `vci-sync-workflow`.
   - OpenTiaToolkit repo changes, tests, docs, plugin packaging, or skill packaging: use `opentia-dev-workflow`.
2. If live TIA context is needed and not already proven in the current turn, use `basic-tia-interaction` before the domain workflow.
3. If several domains are involved, order them by dependency. For example: session selection, hardware discovery, PLC changes, HMI changes, compile/save verification.
4. Ask only for information that cannot be discovered safely: ambiguous project among multiple open instances, missing project path, unclear PLC/HMI target, or destructive confirmation.

## Shared MCP Rules

Keep OpenTiaToolkit MCP calls serial. If the server reports an active operation or a client timeout hides a result, wait for the in-flight operation to finish and verify the target state before retrying.

Prefer read-only discovery before mutation:

```text
list-instances -> connect-instance/connect-portal -> list-projects/get-project-info -> list devices/targets -> focused domain operation
```

Do not call `open-project` before connecting to TIA Portal. Do not conclude that no project is open from a connect response alone; verify with `list-projects` or `get-project-info`.

Use exact command argument names from the tool schema. For CLI-shaped examples, use kebab-case such as `plc-name`, `hmi-name`, `screen-path`, `dry-run`, and `confirm`.

## Mutation Defaults

Use dry-runs, previews, or status reads before applying changes when the command supports them:

- `apply-hardware-patch`: start with `dry-run=true`.
- `apply-hmi-screen-json-patch` and `apply-hmi-screen-json-edit`: start with `dry-run=true` and keep `base-hash` filters stable.
- `import-hmi-json`: start with `dry-run=true`; use `require-clean-preflight=true` before applying broad imports.
- `create-hmi-trends`, `remove-hmi-logging-tags`, `delete-hmi-data-logs`: start with `dry-run=true`.
- `find-replace-tags`, `find-replace-db-members`, `find-replace-udt-members`, `update-tags-from-csv`: start with `dry-run=true`.
- Deletions require explicit confirmation from the user or an explicit `confirm=true` request.

Prefer backups for HMI applies and disposable project copies for broad hardware/PLC/HMI mutation workflows. Save the project only when the user asked to persist changes or the workflow clearly requires persistence after successful verification.

## Verification

After a read-only task, report the discovered target and relevant result.

After a mutation, verify through the narrowest available read:

- Project/session changes: `portal-status`, `list-projects`, or `get-project-info`.
- PLC changes: `get-blocks`, `get-block-info`, `get-software-tree`, relevant tag/rename result, and compile when appropriate.
- HMI screen changes: fresh screen snapshot, item properties, `VerifiedScreen`, or `hmi-status`.
- Hardware changes: `get-hardware-tree`, `get-hardware-io-addresses`, `get-hardware-networks`, `audit-hardware-tag-mappings`, or parameter reads.
- VCI changes: `get-vci-block-status` or `list-vci-mappings`.

Report any safety block exactly enough for the user to act, especially online-mode blocks, stale hashes, ambiguous targets, active references, or destructive-operation confirmation requirements.
