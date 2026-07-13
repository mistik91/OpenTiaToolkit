---
name: s7dcl-data-authoring
description: Author, modify, validate, and safely import Siemens global data blocks in SIMATIC SD .s7dcl format with OpenTiaToolkit. Use for DB structures, arrays, UDT members, optimized access, retain settings, start values, and data initialization while preserving PLC/HMI addressing constraints.
---

# S7DCL Data Authoring

1. Read [data-blocks.md](references/data-blocks.md), [layout-catalog.md](references/layout-catalog.md), and the shared [format contract](../plc-code-engineering/references/format-contract.md).
   Use `assets/Codex_Data_Probe.s7dcl` for a minimal DB and `assets/Codex_Data_Struct_Probe.s7dcl` for nested structures, strings, arrays, and start values.
2. Inspect the DB, UDT dependencies, cross-references, HMI bindings, and optimized-access requirement.
3. Export before modification. Preserve unrequested members, order, attributes, number, retain settings, and start values.
4. Write UTF-8-with-BOM `.s7dcl`. `.s7res` is optional unless `MLC_*` is referenced.
5. Run both `validate-plc-code-artifact --code-format=s7dcl` and `validate-plc-documents`. Stop for duplicate members or start assignments, invalid array/string bounds, unbalanced structures, invalid Bool values, instance-DB markers, or missing resource text. Resolve every project-type warning before apply; do not infer that a similarly named UDT exists in the target group.
6. Use one persistent PLC code transaction even for an isolated DB: begin, add the complete operation set, plan, review exact identities/actions/hashes, inferred and explicit dependencies, execution order, layout warnings, compile policy, and token, then apply once. For a DB plus UDT/caller change, use `depends-on` where the type relationship is not inferable from block calls.
7. Verify canonical read-back and compile. Recheck absolute-address, HMI, recipe, OPC UA, and communication consumers whenever layout, optimized access, retain behavior, or member order changes.

Do not recreate instance DBs as global DB documents. Generate PLC types from validated SCL, patch existing types with baseline-bound SimaticML, or use a validated VCI mapping; do not treat a type as a block document.
