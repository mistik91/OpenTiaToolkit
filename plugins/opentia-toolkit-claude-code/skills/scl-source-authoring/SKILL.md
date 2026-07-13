---
name: scl-source-authoring
description: Author and safely generate Siemens TIA Portal PLC blocks from native .scl external source files with OpenTiaToolkit. Use for complex SCL expressions, IF/CASE, FOR/WHILE/REPEAT loops, arrays, state machines, conversions, FB statics, timers, counters, or source-based FC/FB/OB generation.
---

# SCL Source Authoring

1. Read [patterns.md](references/patterns.md) and the shared [format contract](../plc-code-engineering/references/format-contract.md) before writing stateful instructions or interfaces.
   Use `assets/Codex_SCL_Probe.scl` as the minimal live-tested complex-control-flow source shape.
2. Inspect project blocks, types, DBs, tags, cross-references, and conventions.
3. Prefer a new unique block name and number for the first compile.
4. Write complete native Siemens SCL as UTF-8 with BOM.
5. Run `validate-plc-code-artifact --code-format=scl --input-path=<file>`. Stop on validation errors; review dependencies and every declared FC/FB/OB/DB identity. Route any `TYPE` declaration to VCI or guarded SimaticML; the SCL transaction rejects it because PLC-type rollback is not yet available.
6. For a real change, begin a PLC code transaction and add the source with `add-scl-source-transaction-operation --transaction-id=<id> --source-path=<file> --group-path=<path> --confirm=true`. Add callers and dependent documents to the same transaction in dependency order.
7. Plan once. Review all generated targets, existing/missing baselines, source hash, compile policy, and apply token. The transaction deliberately rejects an existing external source with the same name because that source cannot be losslessly backed up.
8. Apply once with `apply-plc-code-transaction --transaction-id=<id> --apply-token=<token> --confirm=true`. Completion requires exact generated identities, compile policy, and canonical exports for every target.
9. Accept a failed apply only when all existing blocks were restored, all new blocks and the imported external source were removed, restored hashes match, and the rollback compile is proven. Use explicit recovery for `RecoveryRequired`.

For a disposable language probe only, the lower-level sequence remains available: `import-source` dry-run/apply, `generate-blocks-from-source` dry-run/apply, exact verification/compile, then removal of generated blocks and `delete-external-source --confirm=true`.

Use S7DCL when LAD or mixed LAD/SCL is required. Do not force complex SCL through a limited JSON/token DSL.
