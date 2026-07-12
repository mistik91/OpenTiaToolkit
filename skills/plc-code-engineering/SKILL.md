---
name: plc-code-engineering
description: Route safe Siemens TIA Portal PLC code generation and modification through OpenTiaToolkit. Use for requests to create, rewrite, or patch LAD, SCL, STL/AWL, DB, FB, FC, OB, mixed-language blocks, S7DCL/S7RES, external sources, VCI files, or SimaticML while preserving existing project behavior.
---

# PLC Code Engineering

1. Establish the exact TIA project and PLC with `basic-tia-interaction`.
2. Inspect `get-software-tree`, `get-blocks`, `get-block-info`, and concrete cross-references before modifying an existing object.
3. Use `get-vci-block-formats` for the exact block/type when VCI or several representations are available.
4. Route general LAD or mixed LAD/SCL to `s7dcl-lad-authoring`; complex SCL to `scl-source-authoring`; global DBs to `s7dcl-data-authoring`; XML-only features to `simaticml-plc-fallback`.
5. Prefer a new uniquely named block for generated logic. Connect it to existing code only after it compiles.
6. For S7DCL, run `validate-plc-documents`, then bind an exact PLC/group/type/name target with `plan-block-document-import`; review classification, identity, hashes, action, and warnings before `apply-block-document-import --confirm=true`. Continue after rollback only when restored-hash equality is proven.
7. For native sources, dry-run and confirm both `import-source` and `generate-blocks-from-source`.
8. Verify the exact block and compile. Do not save on compile errors. Save only when the user wants the verified change persisted.

Do not assume every block supports every format. Do not hand-author general FlgNet XML when S7DCL can represent the LAD. Treat safety blocks separately; standard LAD in an F-CPU project does not imply F-block generation support.
