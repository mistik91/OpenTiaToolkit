---
name: s7dcl-lad-authoring
description: Author, modify, validate, and safely import Siemens SIMATIC SD LAD or mixed LAD/SCL blocks in .s7dcl/.s7res format with OpenTiaToolkit. Use for contacts, coils, branches, comparisons, moves, arithmetic, timers, edges, FC/FB calls, or network-by-network LAD/SCL code generation.
---

# S7DCL LAD Authoring

1. Read [grammar.md](references/grammar.md) and the shared [format contract](../plc-code-engineering/references/format-contract.md) before changing LAD instructions.
   Start new FC/FB/mixed blocks from the live-tested templates in `assets/` when their instruction set fits. Use `Codex_LAD_Analog_Edge_Probe.s7dcl` for Normalize/Scale and retained P/N edge-contact memory shapes.
2. Export nearby blocks for naming, numbering, operands, comments, and calls. An export is evidence, not proof that every emitted form re-imports.
3. Create UTF-8-with-BOM `.s7dcl`; create `.s7res` whenever `MLC_*` is referenced.
4. Keep interfaces explicit. Put IEC instances in FB static data or a global DB, never FC temporary data.
5. Use LAD for visible power flow and SCL networks for arithmetic or complex expressions.
6. Run `validate-plc-code-artifact --code-format=s7dcl` and `validate-plc-documents`; require `verified` for cookbook instructions, reject `known-unsupported`, and treat `unknown-requires-live-probe` as a mandatory guarded Siemens import/compile probe. Missing typed templates are probe-only even when an untouched Siemens export round-trips.
7. Use a persistent PLC code transaction even for one block. Add the complete operation set, then plan once. Review inferred `dependsOn` sequences and `executionOrder`; use explicit `depends-on` for DB/type/library prerequisites that calls do not expose. Do not proceed on ambiguity or dependency cycles.
8. Apply only with the persistent one-use token and `confirm=true`. Failed read-back or compile must report `RolledBackAndVerified`; use recovery for `RecoveryRequired`.
9. Re-read and compile after adding callers.

When one request changes multiple SIMATIC SD blocks, do not apply the documents one by one. Start a persistent PLC code transaction and add every operation; planning captures all baselines first and topologically orders known calls. Apply once. Accept failure only as `RolledBackAndVerified`; use explicit recovery for `RecoveryRequired` and retain its package.

Do not bypass the planned import with raw document-import commands for agent-authored files.
