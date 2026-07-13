---
name: s7dcl-data-authoring
description: Author, modify, validate, and safely import Siemens global data blocks in SIMATIC SD .s7dcl format with OpenTiaToolkit. Use for DB structures, arrays, UDT members, optimized access, retain settings, start values, and data initialization while preserving PLC/HMI addressing constraints.
---

# S7DCL Data Authoring

1. Read [data-blocks.md](references/data-blocks.md) and the shared [format contract](../plc-code-engineering/references/format-contract.md).
   Use `assets/Codex_Data_Probe.s7dcl` as the minimal live-tested global DB shape.
2. Inspect the DB, UDT dependencies, cross-references, HMI bindings, and optimized-access requirement.
3. Export before modification. Preserve unrequested members, order, attributes, number, retain settings, and start values.
4. Write UTF-8-with-BOM `.s7dcl`. `.s7res` is optional unless `MLC_*` is referenced.
5. Run both `validate-plc-code-artifact --code-format=s7dcl` and `validate-plc-documents`. Stop for duplicate members or start assignments, invalid array/string bounds, unbalanced structures, invalid Bool values, instance-DB markers, or missing resource text. Resolve every project-type warning before apply.
6. Use one persistent PLC code transaction even for an isolated DB: begin, add the document operation, plan, review exact PLC/group/name/number, create/override action, hashes, layout warnings, compile policy, and token, then apply once with `confirm=true`. For a DB plus UDT/caller changes, order dependencies before consumers in the same transaction.
7. Verify canonical read-back and compile. Recheck absolute-address, HMI, recipe, OPC UA, and communication consumers whenever layout, optimized access, retain behavior, or member order changes.

Do not recreate instance DBs as global DB documents. S7DCL PLC data-type import is not currently available through the block-document transaction; use a validated VCI type mapping or guarded SimaticML type workflow.
