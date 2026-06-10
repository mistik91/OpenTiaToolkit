---
name: vci-sync-workflow
description: Use for Siemens TIA Portal Version Control Interface workflows through OpenTiaToolkit, including VCI workspace creation/deletion, mapping inspection, block format discovery, connecting existing files, exporting and mapping PLC blocks, checking VCI status, and synchronizing one block, selected blocks, or supported PLC objects.
---

# VCI Sync Workflow

## Overview

Use this skill for TIA Version Control Interface work. Treat sync direction as the central safety decision: `project-to-workspace` exports from TIA, while `workspace-to-project` imports file contents back into TIA.

## Standard Workflow

1. Establish TIA context with `basic-tia-interaction` when the instance/project is not already proven.
2. Identify the PLC with `list-plcs` or the user-provided `plc-name`.
3. Inspect VCI state:
   - `list-vci-workspaces`
   - `get-vci-workspace`
   - `list-vci-mappings`
   For large mapping reviews, pass `output-path` to `list-vci-mappings` or use the returned CSV artifact path when it auto-spills.
4. For a block workflow, discover supported formats with `get-vci-block-formats`.
5. Before synchronization, run `get-vci-block-status` for mapped objects when possible.
6. Choose the operation:
   - `create-vci-workspace` for a new workspace root.
   - `export-vci-block` to export one PLC block and create its mapping.
   - `connect-vci-block` to map an existing workspace file to a PLC block.
   - `sync-vci-block` for one block.
   - `sync-vci-blocks` for a filtered block set.
   - `sync-vci-plc` for supported objects of a PLC.
7. Verify with `get-vci-block-status` or `list-vci-mappings`.

## Direction Rules

Use `project-to-workspace` when the user's intent is backup, export, refresh files from TIA, or create source-controlled files.

Use `workspace-to-project` only when the user clearly wants to import file changes into TIA. Before doing so, inspect mappings/status and prefer a narrow block filter or exact block name.

Do not guess direction from vague words like "sync this" if both directions are plausible. Ask for direction or state the chosen direction when the user's wording is clear.

## Mapping Rules

Use `create-mapping` or `create-mappings` only when the user wants unmapped objects exported from TIA into the workspace. For workspace-to-project, unmapped file imports should be explicit through `connect-vci-block` or a user-confirmed mapping step.

Keep `relative-directory`, `file-name`, `file-format`, `block-type`, and `group-path` explicit when supplied. Avoid changing established workspace layout unless requested.

For broad syncs, use `filter-regex`, `block-types`, `scope`, and `include-skipped` to make the result reviewable.

For broad sync review, pass `output-path` to `sync-vci-blocks` or `sync-vci-plc`, or use the returned JSON artifact path when the result auto-spills. Keep sync results as JSON so per-item failures, skipped objects, and counters stay structured.

## Destructive Actions

Deleting a VCI workspace definition requires explicit confirmation with `confirm=true`. Confirm that the operation deletes the workspace definition in the project; do not imply it deletes or preserves filesystem contents unless the command result proves it.

## Verification

After project-to-workspace sync, verify that mapped objects report clean or expected status and that skipped unmapped items are intentional.

After workspace-to-project sync, verify the affected block with `get-block-info` or `get-blocks`, then compile the PLC when imported code may affect consistency.
