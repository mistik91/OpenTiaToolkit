---
name: scl-source-authoring
description: Author and safely generate Siemens TIA Portal PLC blocks and PLC data types from native .scl external source files with OpenTiaToolkit. Use for complex SCL expressions, IF/CASE, FOR/WHILE/REPEAT loops, arrays, state machines, conversions, FB statics, timers, counters, UDT/TYPE declarations, or source-based FC/FB/OB/DB/type generation.
---

# SCL Source Authoring

1. Read [patterns.md](references/patterns.md), [declaration-catalog.md](references/declaration-catalog.md), and the shared [format contract](../plc-code-engineering/references/format-contract.md) before writing stateful instructions or interfaces.
   Use `assets/Codex_SCL_Probe.scl` for control flow, `assets/Codex_SCL_Stateful_Probe.scl` for FB statics, and `assets/Codex_SCL_Type_Probe.scl` for a multi-target TYPE plus FC source.
2. Run `discover-plc-code-capabilities` for every declared block/type target with SCL language and create/modify intent. Continue here only when it selects `DirectScl`; follow VCI when selected and stop on `Blocked`. Inspect project blocks, types, DBs, tags, cross-references, and conventions.
3. Prefer a new unique block name and number for the first compile.
4. Write complete native Siemens SCL as UTF-8 with BOM.
5. Run `validate-plc-code-artifact --code-format=scl --input-path=<file>`. Stop on validation errors; review dependencies and every declared FC/FB/OB/DB/TYPE identity. Static validation does not type-check Siemens expressions, library calls, or hardware identifiers. For unknown constructs, create one missing-target capability per uniquely named declared object and run `run-plc-code-probe`; require exact generated identities, compile, canonical hashes, removal of every block/type/external source, and cleanup compile proof. Query the registry again for the intended artifact/current capabilities; project-call evidence is project-bound.
6. Begin a PLC code transaction with all per-target capability ids, then add the source with `add-scl-source-transaction-operation --transaction-id=<id> --source-path=<file> --group-path=<block-group> --type-group-path=<type-group> --confirm=true`. Add every related source/document. Planning revalidates all fingerprints and infers cross-operation block calls; use `depends-on=<sequence,...>` for UDT/DB/library prerequisites it cannot infer.
7. Plan once. Review every generated block/type target, existing/missing baseline, source hash, interface/layout change kind, resolved or unknown dependency, live PLC consumer, impact fingerprint, execution order, and compile policy. Apply `Safe`; acknowledge the exact fingerprint for `ReviewRequired`; accept `Unknown` only after reviewing unresolved project/library or non-PLC consumer evidence; stop on `Blocked`. Cycles are rejected. The transaction rejects an existing external source with the same name because that source cannot be losslessly backed up.
8. Apply once with `apply-plc-code-transaction --transaction-id=<id> --apply-token=<token> --confirm=true`. Completion requires exact generated identities, compile policy, and canonical exports for every target.
9. Accept a failed apply only when all existing blocks/types were restored, all new blocks/types and imported external sources were removed, restored hashes match, and the rollback compile is proven. Delete a verified type later only with `add-delete-plc-type-transaction-operation`, never a raw unverified deletion.

Use the lower-level `import-source`/`generate-blocks-from-source` sequence only for manual diagnosis when the automated runner cannot represent the probe. It does not create promotable evidence, and every generated target plus external source still requires explicit cleanup proof.

Use S7DCL when LAD or mixed LAD/SCL is required. Do not force complex SCL through a limited JSON/token DSL.
