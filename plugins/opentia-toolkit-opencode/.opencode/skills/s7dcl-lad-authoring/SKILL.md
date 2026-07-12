---
name: s7dcl-lad-authoring
description: Author, modify, validate, and safely import Siemens SIMATIC SD LAD or mixed LAD/SCL blocks in .s7dcl/.s7res format with OpenTiaToolkit. Use for contacts, coils, branches, comparisons, moves, arithmetic, timers, edges, FC/FB calls, or network-by-network LAD/SCL code generation.
---

# S7DCL LAD Authoring

1. Read [grammar.md](references/grammar.md) before changing LAD instructions.
   Start new FC/FB/mixed blocks from the live-tested templates in `assets/` when their instruction set fits.
2. Export nearby blocks for naming, numbering, operands, comments, and calls. An export is evidence, not proof that every emitted form re-imports.
3. Create UTF-8-with-BOM `.s7dcl`; create `.s7res` whenever `MLC_*` is referenced.
4. Keep interfaces explicit. Put IEC instances in FB static data or a global DB, never FC temporary data.
5. Use LAD for visible power flow and SCL networks for arithmetic or complex expressions.
6. Run `validate-plc-documents`; require `verified` for cookbook instructions, reject `known-unsupported`, and treat `unknown-requires-live-probe` as a mandatory guarded Siemens import/compile probe.
7. Run `plan-block-document-import` with the exact block-group path; review the bound PLC/group/type/name/number identity, create/override action, hashes, and warnings. Do not proceed on ambiguity.
8. Apply only with the one-use token and `confirm=true`. Failed read-back or compile must report `rolledBack=true` and `restoredHashMatches=true` before continuing.
9. Re-read and compile after adding callers.

Do not bypass the planned import with raw document-import commands for agent-authored files.
