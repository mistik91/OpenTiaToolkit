# PLC code format coverage

Use this matrix to select a route before authoring. `Verified` means the repository has structural checks plus V21 round-trip evidence. `Probe` requires a disposable or rollback-safe transaction and a zero-error Siemens compile. `Unsupported` must stop before mutation.

| Route | Verified authoring surface | Probe-required surface | Unsupported surface |
|---|---|---|---|
| S7DCL LAD/mixed | Standard FC/FB interfaces; LAD networks and branches; registered contacts, coils, comparisons, moves, arithmetic, scaling, timers, edges; complete SCL networks; mixed-network static `IEC_COUNTER.CTU` | Project/library calls; unregistered instructions; graphical LAD CTU; CTD/CTUD; inferred typed calls; new instruction variants; target-specific DB/type operands | F-language blocks; export-only `PBox`/`NBox` and bare compare forms; decorated `Move`; FC-local stateful instances |
| S7DCL data | Global DBs with scalar members, arrays, strings, nested structures, block-level retain policy, and start values | Project UDT members; exported member attributes; unusual multidimensional/open arrays; hardware/library types; address-sensitive layout changes | Instance DB recreation; PLC type declarations; authored member-level `S7_Retain`; descending/implausible arrays; invalid or duplicate initialization |
| Native SCL | FC control flow; FB static timer/edge patterns; multi-declaration TYPE plus block generation; exact block/type group targeting | Project/library instructions; OB event semantics; indirect addressing; complex DB declarations; target-specific technology types | AWL/STL text through the SCL route; F-block generation; external-source overwrite without recoverable source backup |
| SimaticML | Baseline-bound narrow patches to an existing block or `SW.Types.PlcStruct`; explicit changed-leaf path allow list; unchanged identity and ID set; canonical read-back | Explicitly reviewed rewiring using the existing ID set; schema surfaces not covered by a live baseline | New object creation; identity or ID-set changes; unrelated XML leaves outside the path contract; hand-authored general FlgNet; DTD/entity input |
| VCI | Confirmed formats and mappings returned by the target workspace/PLC | A representation not previously synchronized in that exact workspace | Implicit mapping creation during workspace-to-project synchronization |

## Selection rules

1. Prefer the format that preserves the user's intended engineering representation, not merely one that can express equivalent logic.
2. Prefer S7DCL over SimaticML for general LAD. Prefer native SCL over SCL embedded in a document when source generation owns several blocks or PLC types.
3. Treat a project-specific call as a dependency and a probe boundary even when its syntax resembles a built-in.
4. Keep one user intent in one persistent transaction across formats. Use explicit `depends-on` when a UDT, DB, or library relationship is not inferred from calls.
5. Promote a probe-only construct to verified only after adding a reusable fixture, validator contract, regression test, and live V21 round-trip evidence.
