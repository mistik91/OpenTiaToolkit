---
name: simaticml-plc-fallback
description: Safely patch Siemens SimaticML PLC XML with OpenTiaToolkit when S7DCL or native source cannot represent or round-trip the required block/type feature. Use for exported PLC XML, FlgNet XML, exact metadata preservation, unsupported document formats, and narrow XML fallback imports.
---

# SimaticML PLC Fallback

1. Read [xml-rules.md](references/xml-rules.md) and the shared [format contract](../plc-code-engineering/references/format-contract.md).
2. Prove text paths are unsuitable through live format discovery or export failure.
3. Export the exact existing block and retain that untouched XML as the validation baseline. Re-export if TIA changes before planning.
4. Patch the smallest subtree. Preserve namespaces, IDs, language attributes, interfaces, and unrelated compile units.
5. Run `validate-plc-code-artifact --code-format=simaticml --input-path=<patch> --baseline-path=<untouched-export>`. Reject changed object identity, IDs, unsafe XML, or unreviewed FlgNet topology.
6. Begin a persistent PLC code transaction and use `add-simaticml-transaction-operation` with the candidate, untouched baseline, exact group, and `confirm=true`. The planner re-exports the current TIA block and compares a canonical XML state that excludes volatile export metadata.
7. Review the exact existing block identity, current baseline hash, candidate/baseline hashes, compile policy, and token. SimaticML is patch-only; this transaction does not create blocks or mutate PLC types.
8. Apply once. Completion requires exact read-back, compile acceptance, and canonical SIMATIC SD export. Failure restores the pre-import documents and proves their hash plus rollback compile.

Set `allow-flgnet-topology=true` only after explicit topology review and preferably after a disposable-project probe. It does not weaken exact target, baseline, compile, canonical read-back, or rollback checks.

Do not generate general LAD FlgNet XML from scratch. Prefer S7DCL.
