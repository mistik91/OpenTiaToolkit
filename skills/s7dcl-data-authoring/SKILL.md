---
name: s7dcl-data-authoring
description: Author, modify, validate, and safely import Siemens global data blocks in SIMATIC SD .s7dcl format with OpenTiaToolkit. Use for DB structures, arrays, UDT members, optimized access, retain settings, start values, and data initialization while preserving PLC/HMI addressing constraints.
---

# S7DCL Data Authoring

1. Read [data-blocks.md](references/data-blocks.md).
   Use `assets/Codex_Data_Probe.s7dcl` as the minimal live-tested global DB shape.
2. Inspect the DB, UDT dependencies, cross-references, HMI bindings, and optimized-access requirement.
3. Export before modification. Preserve unrequested members, order, attributes, number, retain settings, and start values.
4. Write UTF-8-with-BOM `.s7dcl`. `.s7res` is optional unless `MLC_*` is referenced.
5. Run `validate-plc-documents`, `plan-block-document-import`, review the plan, and apply with its token and `confirm=true`.
6. Verify and compile. Recheck absolute-address consumers when layout changes.

Do not recreate instance DBs as global DB documents.
