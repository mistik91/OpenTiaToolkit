# OTT-35 generic native instance-DB guidance — 2026-10-03

## PASS: skill qualified; Flash High 3/3 verified passes

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
- Release build passed with zero errors/warnings. Modern and legacy MCP startup
  smoke checks passed with 186 commands/189 tools using the qualified worker.
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

Evidence root: `C:/OpenTiaBench/Artifacts/ott35-instance-guidance-20261003/`.
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
Full GLM High fallback is separately defined, but was not needed or launched.
Use single-trial batches with durable checkpoints and cleanup inspection between
trials. Provider throttling or harness-invalid evidence pauses the batch; neither
counts as a model failure. Stop for another demonstrated generic skill gap
rather than silently adding benchmark-specific instructions during a cohort.

Only the unchanged public vector is staged as an exact frozen input, SHA-256
`1D6EF01BFD7C8B8FF2BFACB7136AEAB3B80DE08CF2247A5C2BE48455ECE66724`.
Immutable behavior archive SHA-256 remains
`D2A66124EC3269132DAD9FF5149DA0E73E8C6EA2897E9A62DDA80F7E9F3017FE`.
The original preflight project remains open in protected TIA PID 15780; its
children and unrelated Debug MCP/worker remain out of scope. Model TIA PID
26952 was created for this campaign and independently confirmed empty. Simulator
lease PID 44424 owned `OTT35_Bench`; both owned resources were removed only after
the final trial, explicit unsaved close, and independent health verification.

## Flash High trials and capture correction

Trials 1/2 used the clean skill commit `dab1768`, OpenCode 1.18.10 and
`zai-coding-plan/glm-5.3-flash`, with variant and reasoning effort explicitly
`high`. Skills tree: `3319b3ea35963f1f7ea985033fe4b0c1e3a8952a`.

| Trial | Qualified score | Duration | Reported reasoning tokens | Request USD | Fresh Verified evidence |
| --- | --- | --- | --- | --- | --- |
| 1 | 100/100, original result | 729.377 s | 14,030 | 0 | `fe883b3917674c7cb04b868746a5bb01` |
| 2 | 100/100, append-only capture replay | 864.937 s | 15,595 | 0 | `449c12d1becf48e6b3e052a02ca8cd8e` |
| 3 | 100/100, original result | 945.043 s | 17,038 | 0 | `ead845a6d1dc495093e8c1cd97acb8f0` |

All three loaded the new neutral asset; trials 2/3 also read the declaration catalog.
The FB/instance-DB declarations were valid in every guarded attempt. Trial 1
self-corrected a global-DB declaration error and then a compiler warning; trial
2 self-corrected a compiler warning. Trial 3 self-corrected a missing BOM and a
compiler warning. The final proofs independently verify all
15 vector steps/26 assertions, offline and simulator cleanup. No forbidden
standalone mutation/save occurred. These three observations are encouraging, not
a controlled statistical estimate of the skill's causal effect.

Trial 2's original result is **25/100, harness-invalid**, not a model failure.
Its native `grep` call produced the complete 100-match capped search response:
`matches=100`, `truncated=true`, no outputPath. The output-capture classifier
incorrectly demanded a full-output file for this semantic cap. The actual MCP
behavior proof and exact evidence read were genuinely byte-truncated and each
had its own native full-output file.

The surgical harness correction accepts only the completed built-in `grep`
protocol with exact call identity, integer 100-match metadata, nonempty path and
pattern, exact native header/footer, exactly 100 positive-numbered result rows,
and no outputPath or byte/full-file markers. It leaves the capped search text
unchanged. MCP, unknown tools, malformed grep protocols, and file-backed grep
still require authenticated full-output capture; runtime scoring is unchanged.
Focused tests passed in Windows PowerShell 5.1 and PowerShell 7, including 20
new negative grep controls, CRLF preservation, file-backed grep, and scorer
integration, alongside 22 existing adversarial captures, 10 glob and 17 read
controls.

The full `npm test` benchmark regression suite also passed after this harness
correction (28 cases, 70 campaigns, 15 fixtures). Compiled hosts, worker, command
surface and skill payload were untouched; the previously qualified Release
worker remains SHA-256
`9C6C2BF37A4E49468232537D4336B16B5AE748B506E1B588E47F9AF1AED12C23`.

Append-only `flash-t2-capture-replay/` contains the identical raw trace and
frozen case, the two authentic call/path/prefix/hash-verified MCP full outputs,
and explicit source-file hashes/provenance. Its score is 100/100 and valid for
model comparison. Original trace, metadata, result and paused batch checkpoint
are preserved; the replay is not a new model execution.

Independent trial 2 health passed: Main-only block inventory, zero external
sources, zero compile errors/warnings, unchanged Main hash, zero online
differences, provider auth removed, and explicit unsaved close. Evidence is
under `flash-t2-health/`. Trial 1 had two health-wrapper failures (incorrect
response-field access, then compile while online); neither is reported as a
pass. The first wrapper retained clean reads and online comparison; subsequent
fresh owned headless reopen passed offline inventory/compile/Main checks under
`flash-t1-independent-health/`, and the next fixture explicitly closed trial 1
without saving.

An initial archive retrieval exceeded Siemens' 143-character path limit before
any model request. Its failed fixture evidence is retained, and executions used
the short owned root `C:/OTT/Bench/ott35-guidance/`. No provider throttling was
observed. Reported per-task request cost is $0 through the z.ai subscription;
this excludes subscription amortization and does not mean the provider is free.

The original batch remains paused after its capture error. Trial 3 ran as an
explicitly separate selected-trial segment at clean commit `cdb1057`, not by
rewriting that checkpoint. Its original score/capture is valid. Skill payload,
case, archive, vector and runtime/oracle remain identical; exact Git provenance
for the harness change stays visible. No fallback model or metered OpenRouter
endpoint was used, and no further skill correction was made during the cohort.

## Completed rollup and final health

`flash-qualified-3-trials.json` combines exactly trial 1's preserved result,
trial 2's verified capture replay and trial 3's original segment through the
existing campaign recovery/merge scripts. The intermediate two-trial report is
retained as incomplete; neither it nor the original paused checkpoint is
presented as a complete campaign. Accepted rollup: **qualified, 3/3 valid passes,
mean score 100, mean duration 846.452 s (14.1 min), median 864.937 s**. Reported
request cost remains $0 per trial, excluding the subscription.

`cohort-audit.json` independently confirms the accepted indexes are exactly
1/2/3; all have the same skills, runtime-source and frozen-case Git trees;
archive/vector/qualified worker hashes are unchanged; and all trial plus failed
fixture provider-auth copies are absent. It records each trace/result hash and
which result is a capture replay. The harness commit is intentionally not
hidden or described as an identical Git revision.

Trial 3 independent `flash-t3-health/` checks passed: reopened offline project,
Main-only inventory, zero sources, zero compile errors/warnings, exact original
Main hash, zero online differences, then explicit unsaved close and no open
owned projects. `cleanup-audit.json` proves `OTT35_Bench` was powered off and
unregistered and the exact owned TIA/children/lease/runtime-manager processes
exited. All five protected original TIA/helper and unrelated Debug MCP/worker
process identities are unchanged, and the original preflight project remains
open at its original path. Final cohort audit found no orphaned Release hosts
or workers. Archive, projects and evidence files were retained; no recursive
filesystem deletion or original-project save occurred.

Generated references were checked again after completion: 186 commands current.
All code and qualification reports are committed locally on the feature branch;
no push, pull request, merge, or default-branch modification was performed.

Conclusion: the generic declaration guidance is useful enough to retain. It
removes the observed FB/instance-DB authoring blocker without embedding the
benchmark's control logic. Compilation warnings still required model retries,
but all three executions eventually earned genuine runtime acceptance
under the unchanged clean-compile and restoration gates. This small unpaired
cohort does not prove general model superiority or benchmark saturation.
