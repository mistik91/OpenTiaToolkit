---
name: plc-block-and-tag-workflow
description: Use for OpenTiaToolkit PLC workflows in Siemens TIA Portal, including PLC discovery, opening PLC software, software tree inspection, block/type/source import or export, PLC tag export and CSV updates, cross-reference impact checks, watch tables, technology objects, alarm text lists, compile, guarded PLCSIM Advanced download, online status, online compare, and tag/DB/UDT member rename dry-run/apply workflows.
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
   - `export-tags` when a tag round-trip, audit, or CSV handoff is involved.
   - `get-cross-references` for a concrete tag/block/type/source before rename, delete, or broad impact analysis.
   - `list-external-sources`, `list-watch-tables`, `list-technology-objects`, or `list-plc-alarm-text-lists` when the request touches those PLC surfaces.
   For large PLC discovery reads, pass `output-path` to `get-software-tree`, `get-blocks`, `search-blocks`, `export-tags`, `get-cross-references`, or list commands, or use the returned artifact path when the command auto-spills. Keep software trees as JSON and flat block/tag/xref/surface lists as CSV.
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
- SIMATIC SD documents: `export-block-as-docs` or `export-blocks-as-docs` when Siemens document format round-tripping is needed.
- Plain-text PLC source: `export-block-as-source` for SCL/STL-capable, non-protected blocks.
- PLC tag tables: `export-tags`, preferably with `output-path` for reviewable CSV.

For imports, keep paths and groups explicit:

- Use `import-block` / `import-blocks` for XML.
- Use `import-type` / `import-types` for UDT XML.
- Use `import-block-from-docs` / `import-blocks-from-docs` for `.s7dcl` + `.s7res`.
- Use `import-source --dry-run=true` before adding `.scl` or `.awl` external source files. Apply only with `dry-run=false` and `confirm=true`.
- Use `generate-blocks-from-source --dry-run=true` before generating blocks from an external source. Apply only with `dry-run=false` and `confirm=true`, then verify with `get-block-info` or `get-blocks`.

After imports, run `get-blocks` or `get-block-info`, then compile when reasonable.

## PLC Surface Reads

Use the newer PLC surface commands instead of scraping broad tree output:

- `export-tags` writes all PLC tags across tag tables, including data type, logical address, and comment when available.
- `get-cross-references --name <tag-or-block>` answers impact questions such as "who uses this?" and should be run before concrete rename/delete operations when a named object is involved.
- `list-external-sources` shows imported PLC source files.
- `list-watch-tables` shows PLC watch and force tables.
- `list-technology-objects` shows motion/technology objects.
- `list-plc-alarm-text-lists` shows PLC alarm/text-list surface items.

Treat cross-reference output as impact-analysis evidence, not as permission to mutate by itself. If it returns many rows, prefer `output-path` and summarize the key blocks/tables before proceeding.

## Verification Loop

Use the online/simulator loop after intentional PLC software changes when the user wants behavior-oriented verification and a PLCSIM Advanced target is already configured:

1. Run `compile-software --plc-name <name>` and stop on compile errors.
2. Run `download-software --plc-name <name> --to-simulator=true` first as a dry-run.
3. Apply only with `download-software --plc-name <name> --to-simulator=true --dry-run=false --confirm=true`.
4. For fail-safe projects, prefer `--only-changes=true`; Siemens Openness may reject full fail-safe software loads even when safety access and PLCSIM Advanced are configured.
5. Run `get-online-status --plc-name <name>` to confirm the target is online.
6. Run `compare-software --plc-name <name>` or pass `output-path` for a durable JSON diff artifact.

Treat `start-cpu` and `stop-cpu` as optional guarded follow-ups. Some TIA Portal online providers expose connection state but no CPU RUN/STOP operation through Openness; report the structured not-supported result instead of trying an unsafe workaround.

## Tag Modification

Use `modify-tags` for explicit `create`, `update`, `delete`, and `replace` operations. Prefer inline JSON `operations` for precise agent-driven changes and `input-path` for reviewed JSON or CSV batches. Start with `dry-run=true`, require exact selectors that resolve to one tag, and review every planned before/after snapshot.

Use `replace` when the intent is to delete and recreate a tag so stale PLC references are exposed. Applying `delete` or `replace` requires `confirm=true`. Do not infer page-number, base-name, address-shift, table-selection, or auto-suffix behavior; inspect tags and encode the intended operations explicitly.

If safety program access is required, use the provided `safety-password` only when the user supplied it for this task. Do not ask for or persist passwords unless the operation blocks without one.

## Rename Workflows

Always preview broad renames:

- `find-replace-tags` with `dry-run=true`.
- `find-replace-db-members` with `dry-run=true`.
- `find-replace-udt-members` with `dry-run=true`.

Use `target-blocks` or `target-types` when the user intended a limited scope. Use `substring-match=true` only when the user explicitly wants substring replacement, and prefer specific patterns over short generic strings.

When a rename targets a concrete tag, block, DB member container, UDT, or source name, run `get-cross-references` first and include the impact summary in the preview.

For broad rename previews, prefer `output-path` so `find-replace-tags`, `find-replace-db-members`, and `find-replace-udt-members` write tabular CSV rows. If omitted, large rename results auto-spill to CSV artifacts once inline JSON would exceed the toolkit threshold.

After applying renames, verify candidate objects and compile the PLC. If the preview shows ambiguous matches, stop and ask for a narrower mapping or scope.

## Deletions

Use `delete-blocks` first as a preview by omitting `confirm` or using `confirm=false` when supported by the active tool. Apply with `confirm=true` only after the user has explicitly approved the matched block set.

After deletion, run `get-blocks` and compile.

## Failure Recovery

If a PLC command reports missing or ambiguous PLC names, list PLCs and choose only an exact or unambiguous match.

If compile fails after a mutation, report the compile diagnostics and do not save over the project unless the user explicitly asks to keep the current state.
