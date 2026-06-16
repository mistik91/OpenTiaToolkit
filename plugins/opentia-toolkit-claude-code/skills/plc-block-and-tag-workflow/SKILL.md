---
name: plc-block-and-tag-workflow
description: Use for OpenTiaToolkit PLC workflows in Siemens TIA Portal, including PLC discovery, opening PLC software, software tree inspection, block and type browsing, block/type import or export, SIMATIC SD document sync, compile, guarded PLCSIM Advanced download, online status, online compare, PLC tag creation, CSV tag updates, and tag/DB/UDT member rename dry-run/apply workflows.
---

# PLC Block And Tag Workflow

## Overview

Use this skill for PLC software work through OpenTiaToolkit. Start with target discovery, preview any broad mutation, apply narrowly, then verify with reads, compile, and the guarded online/simulator loop when behavior or consistency may be affected.

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
8. When a PLCSIM Advanced target is explicitly configured and the user wants runtime verification, close the loop with the guarded verification sequence below.
9. Save only when the user asked to persist or after an intentional successful mutation workflow.

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

## Verification Loop

Use the online/simulator loop after intentional PLC software changes when the user wants behavior-oriented verification and a PLCSIM Advanced target is already configured:

1. Run `compile-software --plc-name <name>` and stop on compile errors.
2. Run `download-software --plc-name <name> --to-simulator=true` first as a dry-run.
3. Apply only with `download-software --plc-name <name> --to-simulator=true --dry-run=false --confirm=true`.
4. For fail-safe projects, prefer `--only-changes=true`; Siemens Openness may reject full fail-safe software loads even when safety access and PLCSIM Advanced are configured.
5. Run `get-online-status --plc-name <name>` to confirm the target is online.
6. Run `compare-software --plc-name <name>` or pass `output-path` for a durable JSON diff artifact.

Treat `start-cpu` and `stop-cpu` as optional guarded follow-ups. Some TIA Portal online providers expose connection state but no CPU RUN/STOP operation through Openness; report the structured not-supported result instead of trying an unsafe workaround.

## Tag Creation And CSV Updates

For individual tags, use `create-tag` with explicit `plc-name`, `tag-name`, `data-type`, optional `logical-address`, and optional `tag-table`.

For CSV-driven tag updates, use `update-tags-from-csv` with `dry-run=true` first. Review created, renamed, skipped, ambiguous, and failed entries before applying with `dry-run=false`. Pass `output-path` when the preview should be reviewed as a CSV file, or read the returned artifact path when the command auto-spills a large result.

If safety program access is required, use the provided `safety-password` only when the user supplied it for this task. Do not ask for or persist passwords unless the operation blocks without one.

## Rename Workflows

Always preview broad renames:

- `find-replace-tags` with `dry-run=true`.
- `find-replace-db-members` with `dry-run=true`.
- `find-replace-udt-members` with `dry-run=true`.

Use `target-blocks` or `target-types` when the user intended a limited scope. Use `substring-match=true` only when the user explicitly wants substring replacement, and prefer specific patterns over short generic strings.

For broad rename previews, prefer `output-path` so `find-replace-tags`, `find-replace-db-members`, and `find-replace-udt-members` write tabular CSV rows. If omitted, large rename results auto-spill to CSV artifacts once inline JSON would exceed the toolkit threshold.

After applying renames, verify candidate objects and compile the PLC. If the preview shows ambiguous matches, stop and ask for a narrower mapping or scope.

## Deletions

Use `delete-blocks` first as a preview by omitting `confirm` or using `confirm=false` when supported by the active tool. Apply with `confirm=true` only after the user has explicitly approved the matched block set.

After deletion, run `get-blocks` and compile.

## Failure Recovery

If a PLC command reports missing or ambiguous PLC names, list PLCs and choose only an exact or unambiguous match.

If compile fails after a mutation, report the compile diagnostics and do not save over the project unless the user explicitly asks to keep the current state.
