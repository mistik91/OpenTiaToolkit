---
name: simaticml-plc-fallback
description: Safely patch Siemens SimaticML PLC XML with OpenTiaToolkit when S7DCL or native source cannot represent or round-trip the required block/type feature. Use for exported PLC XML, FlgNet XML, exact metadata preservation, unsupported document formats, and narrow XML fallback imports.
---

# SimaticML PLC Fallback

1. Read [xml-rules.md](references/xml-rules.md), [patch-recipes.md](references/patch-recipes.md), and the shared [format contract](../plc-code-engineering/references/format-contract.md).
2. Run `discover-plc-code-capabilities` for the exact existing target. Continue only when its ranked result selects `SimaticML`, proving text paths are unsuitable and exact XML export is observed. Do not choose XML merely because it is available as a lower-ranked fallback.
3. Export the exact existing block or PLC type and retain that untouched XML as the validation baseline. Inspect the exact block/type group; type names alone are insufficient when groups are present. Re-export if TIA changes before planning.
4. Patch the smallest subtree. Preserve namespaces, IDs, language attributes, interfaces, and unrelated compile units. Produce a semantic diff summary before validation; raw line diffs are insufficient for exported XML.
5. Run `validate-plc-code-artifact --code-format=simaticml --input-path=<patch> --baseline-path=<untouched-export>`. Reject changed object identity, IDs, unsafe XML, or unreviewed FlgNet topology.
6. Begin a persistent PLC code transaction bound to the capability id and use `add-simaticml-transaction-operation` with the candidate, untouched baseline, exact block/type group, and `confirm=true`. The planner revalidates the capability fingerprint, re-exports the current object, and compares canonical XML excluding volatile export metadata.
7. Review the exact existing block/type identity, current baseline hash, candidate/baseline hashes, interface/layout impact, live PLC consumers, unresolved non-PLC consumer scope, dependencies, compile policy, and impact fingerprint. Apply `Safe`; explicitly acknowledge `ReviewRequired`; accept `Unknown` only after manual evidence review; stop on `Blocked`. SimaticML remains patch-only: it does not create a missing object.
8. Apply once. Block completion requires canonical SIMATIC SD read-back; PLC type completion requires canonical XML read-back. Both require compile acceptance. Failure restores the exact block/type baseline and proves its hash plus rollback compile.

Set `allow-flgnet-topology=true` only after explicit topology review and preferably after a disposable-project probe. It does not weaken exact target, baseline, compile, canonical read-back, or rollback checks.

Delete a PLC type through `add-delete-plc-type-transaction-operation`, which snapshots the exact XML and verifies absence. Do not generate general LAD FlgNet XML from scratch. Prefer S7DCL.
