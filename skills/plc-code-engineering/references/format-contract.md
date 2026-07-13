# PLC Code Format Contract

Every format workflow follows the same transaction boundary:

1. **Discover** every exact target with `discover-plc-code-capabilities`. Record the TIA process/version, project, PLC family/type, block/type/group identity, current language and protection, safety context, VCI mapping/status/formats, observed direct export routes, route ranking, expiry, and fingerprint. A format description or file extension is not capability evidence.
2. **Export** every existing target before mutation. A missing target is an explicit `create` baseline, not an empty backup.
3. **Validate** the authored artifact with `validate-plc-code-artifact`. Stop on errors. Treat `unknown-requires-live-probe` as permission only for a disposable, guarded import/compile probe.
4. **Plan** the complete user intent once. Direct-import transactions require one capability id per exact target and re-observe every fingerprint at begin, plan, and apply. Bind the plan to those capabilities plus the TIA process, project, PLC, group, object type/name/number, input hashes, baseline hashes, compile policy, expected generated targets, normalized interface/layout signatures, resolved external dependencies, and live PLC consumer fingerprints.
5. **Review impact** before expecting an apply token. `Safe` receives a token. `ReviewRequired` and `Unknown` enter `ImpactReviewRequired`: inspect `get-plc-code-impact`, then acknowledge its exact fingerprint with `approve-plc-code-impact`; accepting `Unknown` requires `accept-unknown=true`. `Blocked` is terminal. Replan when any dependency, consumer, input, baseline, or capability is stale. Reject dependency cycles; combine mutually dependent blocks in one source when appropriate. Treat DB/type HMI, recipe, OPC UA, communication, and absolute-address consumers as explicit review scope because PLC cross-references do not prove their absence.
6. **Apply** once with explicit confirmation. Do not split a multi-object intent into sequential applies.
7. **Verify** exact object identities, compile policy, and canonical TIA read-back. File-import success alone is not completion.
8. **Commit or roll back** as one unit. Restore existing objects and delete newly created objects in reverse dependency order. Success after failure requires restored-hash equality and a rollback compile.
9. **Retain recovery evidence** when automatic rollback cannot be proven. Treat `RecoveryRequired` as an incident; do not discard its package.

## Evidence classifications

- `verified`: statically supported and covered by a live round-trip or an equivalent repository fixture.
- `unknown-requires-live-probe`: structurally plausible but not yet supported by adequate Siemens round-trip evidence.
- `known-unsupported`: reject before mutation.

## Format routes

Route precedence is evidence-based: an exact mapped VCI object is first only when source control is preferred; otherwise use the native text route compatible with the requested/current language, and reserve SimaticML for an observed existing-target fallback. VCI is intentionally outside the direct-import transaction engine. `Blocked` is terminal for standard authoring. Capability expiry or any change to environment, identity, language, protection, safety, mapping/status/formats, or export evidence requires rediscovery.

| Format | Preferred use | Mutation route | Transaction expectation |
|---|---|---|---|
| S7DCL LAD/mixed | LAD power flow and mixed LAD/SCL networks | Document import | Persistent transaction for multi-object work |
| S7DCL data | Global DBs | Document import | Persistent transaction for dependencies plus consumers |
| Native SCL | Complex FC/FB/OB/DB/type source generation | External source generation | `add-scl-source-transaction-operation` with exact block/type groups |
| SimaticML | Narrow fallback for existing block/type features text formats cannot preserve | Guarded XML patch/import | Baseline-bound plan and verified restore required |
| VCI | Source-controlled synchronization | Explicit directional sync | Validate workspace artifacts before workspace-to-project |

PLC data types are separate targets with type-group identities and canonical XML hashes. Generate them from validated SCL, patch existing ones from untouched SimaticML baselines, or use validated VCI mappings. Delete them only with `add-delete-plc-type-transaction-operation`; the transaction must snapshot the exact type, compile after deletion, verify absence, and restore XML on failure. Safety/F blocks, protected blocks, and AWL/STL generation remain outside the standard authoring contract unless a dedicated, live-proven workflow says otherwise.
