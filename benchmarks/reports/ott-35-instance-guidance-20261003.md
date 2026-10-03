# OTT-35 generic native instance-DB guidance — 2026-10-03

## Checkpoint: skill qualified; fresh model cohort pending

Branch `codex/ott-35-plcsim-behavior-benchmark`, following `49c9770`.
No push, PR, or merge. Historical campaigns and the append-only 15/100 timeout
replay remain unchanged. This is a new skill cohort, not a rewrite of those runs.

## Generic skill correction

Only `scl-source-authoring` changes. Its entrypoint links a neutral same-source
FB/instance-DB asset; its existing declaration reference explains:

- attributes/metadata (including VERSION) precede the bare FB binding;
- the instance interface comes from the FB, not a copied global DB layout;
- the toolkit needs one matching same-source FB/TYPE declaration to distinguish
  `InstanceDB` from a UDT-based `GlobalDB` offline;
- each target needs its actual subtype capability;
- native instance DB generation is create-only, not lossless replacement;
- static subtype validation is not Siemens grammar/type/compile proof.

The 23-line asset records the previous integer input. It contains no timers,
interlocks, reset/count logic, scheduler, contract DB, benchmark names, vector,
or acceptance thresholds. It is a declaration example, not the OTT-35 solution.
Canonical and all four packaged copies, plus the explicitly installed Codex
copy, were synchronized and checked. No invocation or UI metadata changed.

## Qualification before model execution

- Skill creator validation passed; packaged and installed drift zero.
- Focused format tests: 27 passed, including exact FB/InstanceDB classification,
  source BOM, zero external dependencies, and rejection when the FB declaration
  is removed from the same shipped asset.
- Full vendor-free suite: 733 unit and 54 architecture tests passed.
- Full benchmark regressions passed: 28 cases, 70 campaigns, 15 fixtures.
- Generated references current under Windows PowerShell 5.1, 186 commands.
- No runtime/Core code, harness oracle, case prompt, gold code, or frozen vector
  was changed by this slice.

The exact new asset was imported by `run-plc-code-probe` through a fresh owned
headless TIA and independent archive restore, not the user's original project:

- Verified evidence `8e095f3106974344b07b8df04dc1a944`.
- Exact generated objects: FB and InstanceDB; both canonical hashes captured.
- Baseline, probe, and cleanup compile all zero errors/zero warnings.
- Cleanup proven, block/source inventories restored, original Main hash unchanged.
- Project explicitly closed without saving; owned MCP/worker exited.
- Asset SHA-256: `FA01F9CAEFAA441449459D1AF3BDDE684D3B045213ED9C00AC31E82883DA89BC`.
- Qualification worker at `49c9770`:
  `F50F4522DAB6600C89CD47C395DBC4FE72363160D3D528CEA828CD5C814A91CF`.

Evidence root: `E:/Documents/OpenTiaBench/Artifacts/ott35-instance-guidance-20261003/`.
`asset-live/` retains the full request/response journal, probe, before/after Main
exports, and qualification summary. `tests/` contains focused/full TRX files.

The re-created owned simulator passed an independent known-good temporal health
probe, fresh evidence `6980bcee101847d7babd02586460d919`, with execution and both
cleanup proofs true, baseline inventories/Main hash restored and clean final
compile. Its full journal is under `gold-health/`; the gold package is not staged
to model workspaces. The simulator is an owned bounded lease, not real hardware.

## Campaign plan and isolation

Fresh three-trial Flash High definition:
`plc-behavior-instance-guidance-glm-5-3-flash-high-20261003`.
Full GLM High fallback is separately defined, not launched at this checkpoint.
Use single-trial batches with durable checkpoints and cleanup inspection between
trials. Provider throttling or harness-invalid evidence pauses the batch; neither
counts as a model failure. Stop for another demonstrated generic skill gap
rather than silently adding benchmark-specific instructions during a cohort.

Only the unchanged public vector is staged as an exact frozen input, SHA-256
`1D6EF01BFD7C8B8FF2BFACB7136AEAB3B80DE08CF2247A5C2BE48455ECE66724`.
Immutable behavior archive SHA-256 remains
`D2A66124EC3269132DAD9FF5149DA0E73E8C6EA2897E9A62DDA80F7E9F3017FE`.
The original preflight project remains open in protected TIA PID 15780; its
children and unrelated Debug MCP/worker remain out of scope. New model TIA PID
26952 was created for this campaign and independently confirmed empty. Simulator
lease PID 44424 owns `OTT35_Bench`; do not end it during a probe or recovery.
