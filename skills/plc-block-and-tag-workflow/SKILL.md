---
name: plc-block-and-tag-workflow
description: Use for OpenTiaToolkit PLC workflows in Siemens TIA Portal, including PLC discovery, opening PLC software, software tree inspection, block and type browsing, block/type import or export, SIMATIC SD document sync, compile, PLC tag creation, CSV tag updates, and tag/DB/UDT member rename dry-run/apply workflows.
---

# PLC Block And Tag Workflow

## Overview

Use this skill for PLC software work through OpenTiaToolkit. Start with target discovery, preview any broad mutation, apply narrowly, then verify with reads and compile when behavior or consistency may be affected.

## Standard Workflow

1. Establish TIA context with `basic-tia-interaction` when the instance/project is not already proven.
2. Identify the PLC:
   - Use `list-plcs` when no exact PLC name was provided.
   - Use `open-plc` when a workflow needs PLC software access.
3. Inspect before changing:
   - `get-software-tree` for hierarchy.
   - `get-blocks` or `search-blocks` for candidate blocks.
   - `get-block-info` for a specific block.
   For large PLC discovery reads, pass `output-path` to `get-software-tree`, `get-blocks`, or `search-blocks`, or use the returned artifact path when the command auto-spills. Keep software trees as JSON and flat block lists/searches as CSV.
4. Choose the narrowest operation for the request.
5. Run dry-run or preview mode first when available.
6. Apply only after preview results match the user's intent.
7. Verify with focused reads and run `compile-software` after imports, deletes, or renames that may affect PLC consistency.
8. Save only when the user asked to persist or after an intentional successful mutation workflow.

## Import And Export

For backups or reviewable artifacts, prefer export before mutation:

- XML block export: `export-block` or `export-plc`.
- XML type export: `export-type` or `export-types`.
- SIMATIC SD documents: `export-block-as-docs` or `export-blocks-as-docs` when V20+ document format is needed.

For imports, keep paths and groups explicit:

- Use `import-block` / `import-blocks` for XML.
- Use `import-type` / `import-types` for UDT XML.
- Use `import-block-from-docs` / `import-blocks-from-docs` for `.s7dcl` + `.s7res`.

After imports, run `get-blocks` or `get-block-info`, then compile when reasonable.

## Tag Creation And CSV Updates

For individual tags, use `create-tag` with explicit `plc-name`, `tag-name`, `data-type`, optional `logical-address`, and optional `tag-table`.

For CSV-driven tag updates, use `update-tags-from-csv` with `dry-run=true` first. Review created, renamed, skipped, ambiguous, and failed entries before applying with `dry-run=false`.

If safety program access is required, use the provided `safety-password` only when the user supplied it for this task. Do not ask for or persist passwords unless the operation blocks without one.

## Rename Workflows

Always preview broad renames:

- `find-replace-tags` with `dry-run=true`.
- `find-replace-db-members` with `dry-run=true`.
- `find-replace-udt-members` with `dry-run=true`.

Use `target-blocks` or `target-types` when the user intended a limited scope. Use `substring-match=true` only when the user explicitly wants substring replacement, and prefer specific patterns over short generic strings.

After applying renames, verify candidate objects and compile the PLC. If the preview shows ambiguous matches, stop and ask for a narrower mapping or scope.

## Deletions

Use `delete-blocks` first as a preview by omitting `confirm` or using `confirm=false` when supported by the active tool. Apply with `confirm=true` only after the user has explicitly approved the matched block set.

After deletion, run `get-blocks` and compile.

## Failure Recovery

If a PLC command reports missing or ambiguous PLC names, list PLCs and choose only an exact or unambiguous match.

If compile fails after a mutation, report the compile diagnostics and do not save over the project unless the user explicitly asks to keep the current state.
