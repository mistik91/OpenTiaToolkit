---
name: scl-source-authoring
description: Author and safely generate Siemens TIA Portal PLC blocks and PLC data types from native .scl external source files with OpenTiaToolkit. Use for complex SCL expressions, IF/CASE, FOR/WHILE/REPEAT loops, arrays, state machines, conversions, FB statics, timers, counters, UDT/TYPE declarations, or source-based FC/FB/OB/DB/type generation.
---

# SCL Source Authoring

1. Read [patterns.md](references/patterns.md) and the shared [format contract](../plc-code-engineering/references/format-contract.md) before writing stateful instructions or interfaces.
   Use `assets/Codex_SCL_Probe.scl` as the minimal live-tested complex-control-flow source shape.
2. Inspect project blocks, types, DBs, tags, cross-references, and conventions.
3. Prefer a new unique block name and number for the first compile.
4. Write complete native Siemens SCL as UTF-8 with BOM.
5. Run `validate-plc-code-artifact --code-format=scl --input-path=<file>`. Stop on validation errors; review dependencies and every declared FC/FB/OB/DB/TYPE identity.
6. Begin a PLC code transaction and add the source with `add-scl-source-transaction-operation --transaction-id=<id> --source-path=<file> --group-path=<block-group> --type-group-path=<type-group> --confirm=true`. Add every related source/document. Planning infers cross-operation block calls; use `depends-on=<sequence,...>` for UDT/DB/library prerequisites it cannot infer.
7. Plan once. Review every generated block/type target, existing/missing baseline, source hash, inferred/explicit dependencies, execution order, compile policy, and token. Cycles are rejected. The transaction rejects an existing external source with the same name because that source cannot be losslessly backed up.
8. Apply once with `apply-plc-code-transaction --transaction-id=<id> --apply-token=<token> --confirm=true`. Completion requires exact generated identities, compile policy, and canonical exports for every target.
9. Accept a failed apply only when all existing blocks/types were restored, all new blocks/types and imported external sources were removed, restored hashes match, and the rollback compile is proven. Delete a verified type later only with `add-delete-plc-type-transaction-operation`, never a raw unverified deletion.

For a disposable language probe only, the lower-level sequence remains available: `import-source` dry-run/apply, `generate-blocks-from-source` dry-run/apply, exact verification/compile, then removal of generated blocks and `delete-external-source --confirm=true`.

Use S7DCL when LAD or mixed LAD/SCL is required. Do not force complex SCL through a limited JSON/token DSL.
