# PLC Code Format Contract

Every format workflow follows the same transaction boundary:

1. **Discover** the open project, exact PLC, target group, existing object identity, dependencies, references, and formats supported by that object.
2. **Export** every existing target before mutation. A missing target is an explicit `create` baseline, not an empty backup.
3. **Validate** the authored artifact with `validate-plc-code-artifact`. Stop on errors. Treat `unknown-requires-live-probe` as permission only for a disposable, guarded import/compile probe.
4. **Plan** the complete user intent once. Bind the plan to the TIA process, project, PLC, group, object type/name/number, input hashes, baseline hashes, compile policy, and all expected generated targets.
5. **Review** create/override actions, dependencies, warnings, and the one-use apply token. Replan when any input or baseline is stale.
6. **Apply** once with explicit confirmation. Do not split a multi-object intent into sequential applies.
7. **Verify** exact object identities, compile policy, and canonical TIA read-back. File-import success alone is not completion.
8. **Commit or roll back** as one unit. Restore existing objects and delete newly created objects in reverse dependency order. Success after failure requires restored-hash equality and a rollback compile.
9. **Retain recovery evidence** when automatic rollback cannot be proven. Treat `RecoveryRequired` as an incident; do not discard its package.

## Evidence classifications

- `verified`: statically supported and covered by a live round-trip or an equivalent repository fixture.
- `unknown-requires-live-probe`: structurally plausible but not yet supported by adequate Siemens round-trip evidence.
- `known-unsupported`: reject before mutation.

## Format routes

| Format | Preferred use | Mutation route | Transaction expectation |
|---|---|---|---|
| S7DCL LAD/mixed | LAD power flow and mixed LAD/SCL networks | Document import | Persistent transaction for multi-object work |
| S7DCL data | Global DBs | Document import | Persistent transaction for dependencies plus consumers |
| Native SCL | Complex FC/FB/OB source generation | External source generation | `add-scl-source-transaction-operation` |
| SimaticML | Narrow fallback for features text formats cannot preserve | Guarded XML patch/import | Baseline-bound plan and verified restore required |
| VCI | Source-controlled synchronization | Explicit directional sync | Validate workspace artifacts before workspace-to-project |

PLC data types currently require a validated VCI type mapping or guarded SimaticML export/patch/import. They are not silently treated as blocks. Safety/F blocks, protected blocks, and AWL/STL generation are outside the standard authoring contract unless a dedicated, live-proven workflow says otherwise.
