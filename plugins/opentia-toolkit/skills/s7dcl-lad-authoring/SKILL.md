---
name: s7dcl-lad-authoring
description: Author, modify, validate, and safely import Siemens SIMATIC SD LAD or mixed LAD/SCL blocks in .s7dcl/.s7res format with OpenTiaToolkit. Use for contacts, coils, branches, comparisons, moves, arithmetic, timers, edges, FC/FB calls, or network-by-network LAD/SCL code generation.
---

# S7DCL LAD Authoring

1. Read [grammar.md](references/grammar.md), [instruction-catalog.md](references/instruction-catalog.md), and the shared [format contract](../plc-code-engineering/references/format-contract.md) before changing LAD instructions.
   Start new FC/FB/mixed blocks from the live-tested templates in `assets/`. Choose the smallest matching asset: basic Boolean/math, analog/edge, stateful FB, branch/timer, or mixed LAD/SCL.
2. Run `discover-plc-code-capabilities` for each exact target with the LAD/mixed language and create/modify intent. Continue here only when it selects `DirectDocuments`; use VCI when selected and stop when blocked. Retain every capability id for the transaction. Export nearby blocks for naming, numbering, operands, comments, and calls. An export is evidence, not proof that every emitted form re-imports.
3. Create UTF-8-with-BOM `.s7dcl`; create `.s7res` whenever `MLC_*` is referenced.
4. Keep interfaces explicit. Put IEC instances in FB static data or a global DB, never FC temporary data.
5. Use LAD for visible power flow and SCL networks for arithmetic or complex expressions. Preserve branch junction direction exactly: each named `wire#wN` must have a tap and a matching join.
6. Run `validate-plc-code-artifact --code-format=s7dcl` and `validate-plc-documents`; require `verified` for catalog instructions and reject `known-unsupported`. For `unknown-requires-live-probe`, give a uniquely named missing block its own `Create` capability and use `run-plc-code-probe`; require `Verified`, including canonical export and post-removal compile proof. Then use `query-plc-code-evidence` against the intended artifact and current target capability. A signature/fingerprint miss remains unknown. Missing typed templates are probe-only even when an untouched Siemens export round-trips. Never relabel a project call as a built-in.
7. Use a persistent PLC code transaction even for one block, binding its capability id at begin. Bind all per-target ids for a multi-block transaction. For creation, add the operation with `target-mode=create-only`. For modification, keep the exact untouched export in a separate directory and use `target-mode=baseline-bound --baseline-directory=<untouched> --allowed-changes=<narrow sections>`. Authorize only the sections actually requested: `Attributes`, `Interface`, `Implementation`, and/or `Resources`; identity changes are rejected. Never copy the candidate over the untouched baseline. Review the persisted baseline/candidate hashes, changed sections, and change-contract fingerprint along with capability and impact fingerprints, resolved dependencies, PLC consumers, inferred `dependsOn`, and `executionOrder`. Use explicit `depends-on` for DB/type/library prerequisites that calls do not expose. Apply `Safe`; explicitly acknowledge the exact fingerprint for `ReviewRequired`; accept `Unknown` only after reviewing the missing evidence; stop on `Blocked`, stale evidence, ambiguity, or dependency cycles.
8. Apply only with the persistent one-use token and `confirm=true`. Failed read-back or compile must report `RolledBackAndVerified`; use recovery for `RecoveryRequired`.
9. Re-read and compile after adding callers.

When one request changes multiple SIMATIC SD blocks, do not apply the documents one by one. Start a persistent PLC code transaction and add every operation; planning captures all baselines first and topologically orders known calls. Apply once. Accept failure only as `RolledBackAndVerified`; use explicit recovery for `RecoveryRequired` and retain its package.

Do not bypass the planned import with raw document-import commands for agent-authored files.
