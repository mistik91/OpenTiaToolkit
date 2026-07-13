---
name: s7dcl-lad-authoring
description: Author, modify, validate, and safely import Siemens SIMATIC SD LAD or mixed LAD/SCL blocks in .s7dcl/.s7res format with OpenTiaToolkit. Use for contacts, coils, branches, comparisons, moves, arithmetic, timers, edges, FC/FB calls, or network-by-network LAD/SCL code generation.
---

# S7DCL LAD Authoring

1. Read [grammar.md](references/grammar.md) and the shared [format contract](../plc-code-engineering/references/format-contract.md) before changing LAD instructions.
   Start new FC/FB/mixed blocks from the live-tested templates in `assets/` when their instruction set fits.
2. Export nearby blocks for naming, numbering, operands, comments, and calls. An export is evidence, not proof that every emitted form re-imports.
3. Create UTF-8-with-BOM `.s7dcl`; create `.s7res` whenever `MLC_*` is referenced.
4. Keep interfaces explicit. Put IEC instances in FB static data or a global DB, never FC temporary data.
5. Use LAD for visible power flow and SCL networks for arithmetic or complex expressions.
6. Run `validate-plc-code-artifact --code-format=s7dcl` and `validate-plc-documents`; require `verified` for cookbook instructions, reject `known-unsupported`, and treat `unknown-requires-live-probe` as a mandatory guarded Siemens import/compile probe. Missing typed templates are probe-only even when an untouched Siemens export round-trips.
7. Use a persistent PLC code transaction even for one block. Add the exact block-group operation, plan once, and review the bound PLC/group/type/name/number identity, create/override action, hashes, compile policy, and warnings. Do not proceed on ambiguity.
8. Apply only with the persistent one-use token and `confirm=true`. Failed read-back or compile must report `RolledBackAndVerified`; use recovery for `RecoveryRequired`.
9. Re-read and compile after adding callers.

When one request changes multiple SIMATIC SD blocks, do not apply the documents one by one. Start a persistent PLC code transaction, add all operations in dependency order, plan once so every baseline is captured before mutation, and apply once. Accept failure only as `RolledBackAndVerified`; use explicit recovery for `RecoveryRequired` and retain its package.

Do not bypass the planned import with raw document-import commands for agent-authored files.
