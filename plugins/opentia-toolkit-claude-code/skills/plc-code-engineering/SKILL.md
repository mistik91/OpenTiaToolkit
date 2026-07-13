---
name: plc-code-engineering
description: Route safe Siemens TIA Portal PLC code generation and modification through OpenTiaToolkit. Use for requests to create, rewrite, or patch LAD, SCL, STL/AWL, DB, FB, FC, OB, mixed-language blocks, S7DCL/S7RES, external sources, VCI files, or SimaticML while preserving existing project behavior.
---

# PLC Code Engineering

1. Read [format-contract.md](references/format-contract.md) and [format-coverage.md](references/format-coverage.md). Establish the exact TIA project and PLC with `basic-tia-interaction`.
2. Inspect `get-software-tree`, `get-blocks`, `get-block-info`, and concrete cross-references before modifying an existing object.
3. Run `describe-plc-code-formats`. Use `get-vci-block-formats` for the exact block/type when VCI or several representations are available.
4. Route general LAD or mixed LAD/SCL to `s7dcl-lad-authoring`; complex SCL and source-generated PLC types to `scl-source-authoring`; global DBs to `s7dcl-data-authoring`; existing block/type XML patches to `simaticml-plc-fallback`; and source-controlled objects to validated VCI mappings. Do not cross a format's coverage boundary merely because its file parses.
5. Prefer a new uniquely named block for generated logic. Connect it to existing code only after it compiles.
6. For every agent-authored change, use the persistent transaction lifecycle: begin, add the complete operation set, plan, review, then apply once. Planning infers dependencies from project/block calls and topologically orders them. Use `depends-on=<sequence,...>` for UDT, DB, library, or other dependencies that cannot be inferred from calls; reject cycles instead of guessing.
7. Never replace a multi-object intent with sequential single-block applies. The older `plan-block-document-import` token is process-local and is suitable only when plan and apply are guaranteed to share one persistent host session.
8. Treat `Stale` as a mandatory replan. Treat `RecoveryRequired` as an incident: retain the package and use `recover-plc-code-transaction --confirm=true`; never discard it before all hashes and the rollback compile are proven.
9. For native SCL sources, run `validate-plc-code-artifact --code-format=scl`. Use `add-scl-source-transaction-operation` for atomic generated block/type changes and set `type-group-path` when TYPE declarations do not belong at the root. Delete a PLC type only through `add-delete-plc-type-transaction-operation`, which snapshots XML and verifies absence/rollback.
10. Verify the exact block and compile. Do not save on compile errors. Save only when the user wants the verified change persisted.

Do not assume every block supports every format. Do not hand-author general FlgNet XML when S7DCL can represent the LAD. Treat safety blocks separately; standard LAD in an F-CPU project does not imply F-block generation support.
