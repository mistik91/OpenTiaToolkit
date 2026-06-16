---
name: library-workflow
description: Use for OpenTiaToolkit Siemens TIA Portal library workflows, including opening/listing/closing global libraries, browsing project or global library master copies, listing library types and UDT versions, running library update checks, and safely reusing approved library assets before creating PLC blocks or types from scratch.
---

# Library Workflow

## Overview

Use this skill when the user wants to inspect or reuse TIA Portal project libraries or global libraries through OpenTiaToolkit. Prefer approved library assets before creating new PLC blocks, UDTs, HMI faceplates, graphics, or scripts from scratch.

Keep this workflow read-first. Library commands can reveal company standards; mutation commands must stay gated by dry-run and confirmation.

## Standard Workflow

1. Establish TIA context with `basic-tia-interaction` when the instance/project is not already proven.
2. Identify the library scope:
   - Use `library-scope=project` for the current project library.
   - Use `library-scope=global` for an open global library.
3. For global libraries:
   - Run `list-global-libraries` first when the available library name/path is unknown.
   - Open user or default Siemens libraries with `open-global-library --path "<path>" --read-only=true`.
   - Close user global libraries with `close-global-library` when finished.
4. Browse before selecting:
   - `list-master-copies` for reusable objects.
   - `list-library-types` for UDTs, faceplates, scripts, graphics, and other typed library assets.
   - `get-library-type-versions` for a selected type path, including nested paths such as `UDT/MyTIMER/T_MyVALVE`.
5. Run `check-library-updates` before any update or harmonization workflow. Report outdated instances before mutating.
6. Use `copy-from-master-copy` only after confirming the master copy is compatible with the target. Start with `dry-run=true`; apply only with `dry-run=false` and `confirm=true` after user approval.
7. Verify with the narrowest read after any apply, and compile PLC software if PLC blocks or UDTs were imported or copied.

## Library Selection

Project library is the default when the user says "the project library" or asks for project-standard UDTs/types.

User global libraries are usually `.al21` files, for example `E:\Documents\Automation\TestLibrary\TestLibrary.al21`. Open them read-only unless the user explicitly asks to edit the library itself.

Siemens System/Corporate global libraries may be returned by `list-global-libraries`. They can be opened by matching `GlobalLibraryInfo` name/path, but Siemens owns their lifecycle; closing them may be a host-managed no-op. Some System libraries expose master copies but no `TypeFolder`, so an empty type list is valid.

## Master Copy Safety

Do not assume every master copy is PLC-compatible.

- PLC block/type master copies can be candidates for `copy-from-master-copy` into PLC block groups.
- HMI graphics, controls, faceplates, and screen assets should not be applied to PLC block groups.
- If a master copy looks HMI-oriented and the only available apply target is PLC software, stop after read-only discovery or dry-run and explain the mismatch.

When copying to PLC software:

```text
copy-from-master-copy --library-scope=global --global-library="<name>" --master-copy-path="<folder/item>" --plc-name="<plc>" --dry-run=true
```

Apply only after explicit approval:

```text
copy-from-master-copy --library-scope=global --global-library="<name>" --master-copy-path="<folder/item>" --plc-name="<plc>" --dry-run=false --confirm=true
```

## UDT And Type Workflows

Use library types for UDT discovery and version selection:

```text
list-library-types --library-scope=global --global-library="TestLibrary" --folder-path="UDT" --output-path="<types.json>"
get-library-type-versions --library-scope=global --global-library="TestLibrary" --type-path="UDT/MyVALVE"
```

For nested UDT folders, pass the full path exactly as returned by `list-library-types`.

Before changing a project that already contains library-managed instances, run:

```text
check-library-updates --library-scope=global --global-library="<name>" --include-up-to-date=false
```

## Artifacts

Use `output-path` for broad library lists. `list-library-types` and `list-master-copies` write JSON artifacts. If the command auto-spills a large result, read `data.outputPath` before choosing an asset.

## Failure Recovery

If a global command reports the library is not open, run `list-global-libraries`, then `open-global-library` with the exact name or path.

If a type path returns no versions, rerun `list-library-types` and use the exact `path` value.

If update checks return zero messages, report that no outdated instances were found; do not treat it as failure.

If a library command fails because no project is open, use `basic-tia-interaction` to reconnect and verify `list-projects` or `get-project-info` before retrying.
