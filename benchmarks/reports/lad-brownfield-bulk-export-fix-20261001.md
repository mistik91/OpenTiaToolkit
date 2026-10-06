# OTT-34 bulk SD export fix and post-fix qualification

PASS — the toolkit bulk/single export inconsistency is fixed and independently
verified in TIA. A separate three-trial full GLM-5.3 High cohort scored
100/100/100, with all three fresh live audits passing. No skills, task prompt,
grading rules, or authoring permissions changed. No PR or merge was performed.

## Generic production correction

`PlcDocumentTransferService.ExportBlocksAsync` previously rejected every OB/FOB
locally, before invoking Siemens. The historical OTT-34 trial showed that
single-block SD export of the same Main OB succeeded in the same project.

Bulk export now delegates every selected block to the existing single-block
export path. Actual API availability, returned state, or an export exception
determines each target's outcome. Existing failed-item aggregation, partial
failure reporting, filtering, and continuation remain intact. The V21 version
gate remains intact. Import, modification, safety-language, and capability
rules were not expanded.

Nine generic service tests cover OB/FOB delegation and single/bulk byte equality,
non-success state, missing export API, thrown export failure with later targets
still exported, case-insensitive filtered counts, no matches, invalid regex,
and the unsupported-version guard. Before the production edit, six failed and
three passed; afterward all nine passed. The FOB test proves delegation using
a fake API, not universal support for real FOB objects.

## Frozen cohort and runtime

- Branch: `codex/ott-34-lad-authoring-benchmark`.
- Fix and campaign commit: `a7f802460cb9c32e314ec72cc297d0e9d5b03c2b`.
- Campaign: `lad-brownfield-glm-5-3-high-bulk-fix-20261001`; three fresh restores,
  one trial per checkpoint batch, with independent audits between batches.
- OpenCode 1.18.10; Windows PowerShell 5 harness.
- Model `zai-coding-plan/glm-5.3`, reasoning effort `high`, explicit variant
  `high`, existing provider-scoped subscription authentication.
- TIA V21 Update 2, PID 13008, PLC_Main; immutable fixture
  `tia-v21-lad-brownfield-13` at
  `C:\OpenTiaBench\Archives\OTT_Benchmark_V21_LadBrownfield_20261001.zap21`.
- Fixture SHA-256:
  `952FC2C686BFA2C40D0D339340D8E6DDDEABF9B14E24367BF4BAAE796671EACC`;
  5,691,086 bytes, read-only, reverified after cleanup.
- Case SHA-256:
  `A3B25ADC0B02243F586E17AE0B8383CF64E1E2F84F1C9A77EF54E57B14AD045B`.
- LAD oracle SHA-256:
  `6A6CAEB9269EB359FACDC60E8ED98C713D629BBBDAD48EFD4F5FBB3D71CC21D8`.

All trial setup, model MCP sessions, and independent CLI audits explicitly used
the Release hosts and rebuilt Release worker. The worker was registered in both
Siemens allow-list locations before live verification. Runtime process snapshots
prove the model sessions launched the Release MCP and Release worker. The older
root-chat Debug MCP/worker were left untouched; they did not execute this cohort
or its export controls.

Paths below are relative to the repository root; each host was passed explicitly
to the harness, with `OPEN_TIA_WORKER_V21_EXE` set to the Release worker.

| Runtime file | SHA-256 |
|---|---|
| `src/OpenTiaToolkit.Worker.V21/bin/Release/net48/OpenTiaToolkit.Worker.V21.exe` | `2878C000DA5D70CAF07C07351AFC6580C5655FF902E010753E487B71811967DC` |
| `src/OpenTiaToolkit.Worker.V21/bin/Release/net48/OpenTiaToolkit.Core.dll` | `CC6F5909BCC57D0889C96DBBDB4187A4D25301A0703435A499DF95126461E1B5` |
| `src/OpenTiaToolkit.CLI/bin/Release/net48/OpenTiaToolkit.CLI.exe` | `500CC4063D55D6FDFBBBAF2CC82D496EEB9E8F4BE9915E51D1A8F458BE72ABB1` |
| `src/OpenTiaToolkit.MCP/bin/Release/net48/OpenTiaToolkit.MCP.exe` | `FAE2E255E4E47747FAE47B8323163874093FD05A7FE1171BE82767699E405A40` |

## Read-only live export control before model execution

Fresh disposable project:
`C:\OTT\Live\20261001-bulk-sd-fix\OTT_Benchmark\OTT_Benchmark.ap21`.

- Bulk SD export: 3/3 succeeded, zero failed: Main, OTT34_Conveyor,
  OTT34_Conveyor_Backup.
- Single Main SD export and bulk `filter-regex=^Main$`: both succeeded;
  all three Main files were byte-identical to the original baseline.
- Filtered export returned only Main, with total/succeeded counts 1/1.
- All four bulk document files matched original baseline SHA-256 hashes.
- Software inventory unchanged; compilation 0 errors/0 warnings.
- Project closed without saving; empty session independently verified.

This is direct evidence for the bulk fix. The new model trials chose single-block
exports, so their passing scores alone do not prove that choosing bulk became
more reliable. Each independent post-trial audit additionally used bulk export
and verified all three targets successfully.

## Fresh model results

| Trial | Score | Strict pass | Seconds | Reasoning tokens | Reported cost | MCP calls / rejections |
|---|---:|:---:|---:|---:|---:|---:|
| 1 | 100 | yes | 357.306 | 6,775 | $0 | 21 / 0 |
| 2 | 100 | yes | 407.997 | 5,646 | $0 | 22 / 0 |
| 3 | 100 | yes | 380.985 | 7,416 | $0 | 21 / 0 |

Three valid trials, zero invalid attempts, retries, timeouts, provider failures,
critical failures, or rate-limit pauses. Mean score 100; strict pass rate 3/3;
mean duration 382.096 seconds, median 380.985; mean reasoning tokens 6,612.33.
Total, mean, and per-passing-trial reported incremental cost are $0. This is
subscription telemetry, not a claim that the subscription or quota is free.

Every transaction selected DirectDocuments, used one baseline-bound operation
with only Implementation changes, planned a Safe/BodyOnly edit, applied once,
and ended Succeeded/Verified with baseline/final compilation 0/0.

| Trial | Transaction ID |
|---|---|
| 1 | `fcb6186d8c294252a731485f198ce55f` |
| 2 | `07bfbed4445244feaf242a797a4931c0` |
| 3 | `b33c0478adb34666a08dcd6a6fabf8d4` |

All three canonical FB result hashes equal the independently established gold:
`11024a5231428de1bf97306e2a419a766f729d175125e8bb6489ec582eee3a9e`.

## Independent verification and historical limits

Fresh workers attached to each still-open trial project after OpenCode exited,
verified its exact project path, and independently exported all SD documents and
the FB XML outside the model workspace. Each audit passed the 64 combinational
rows, existing nonretentive TON receiver and IN/PT/Q/open-ET wiring, five-network
inventory, interface/unrelated-network preservation, original resources, and
original backup/Main byte hashes. Each fresh compile was 0/0. Every disposable
project was then closed without saving and an empty session was verified.

Unique replay outputs reproduced 100/100/100 under the unchanged grader; raw
traces, metadata, and original result files were not rewritten. Replay manifests
record source hashes. The [historical report](lad-brownfield-glm-high-20261001.md)
continues to retain Flash 100/100/96 and full GLM 100/100/98, including the original
96-point grade before the documented ordering-scorer correction. Flash was not
rerun and still has only a 2/3 strict historical pass rate.

The new cohort removes the confirmed toolkit confound; it is not evidence of a
skill improvement, a universal model reliability guarantee, or faster execution.
The previous full-model mean was 357.458 seconds. With three trials and different
model-selected workflows, no speedup or causal performance claim is warranted.
The [gold and rollback audit](ott-34-gold-audit-20261001.md) remains the existing
forced mid-transaction failure proof; it was not rerun for this export-only fix.
No PLC runtime, scan order under execution, timer timing, online/download,
simulation, or safety-controller qualification is claimed.

## Validation and evidence

- Release worker build: zero warnings/errors.
- Focused service regression tests: 9 passed; non-integration tests: 674 passed;
  architecture tests: 54 passed. Post-campaign non-integration soak: 674 passed.
- Full benchmark `npm test` passed; final definitions validate 27 cases,
  62 campaigns, and 14 fixtures.
- Generated command reference current: 186 commands; MCP stdio parity/startup
  passed with 189 tools (186 commands plus 3 infrastructure tools), stable
  MCP SDK 2.0.0, modern and legacy protocol checks.
- Skill packaging CheckOnly: zero drift. Repository hygiene and diff checks
  clean. No skills changed.
- Final process audit: no OpenCode, campaign Release MCP/worker, or testhost
  remained. Existing root MCP 21224, worker 15608, and empty TIA 13008 retained.

Local ignored evidence roots:

- `benchmarks/artifacts/ott34-bulk-fix-validation/`: red/green and soak TRX,
  architecture TRX, export control documents/logs, software tree, MCP stdio log,
  final empty-session read.
- `benchmarks/artifacts/20261001T200542822Z-batch-lad-brownfield-glm-5-3-high-bulk-fix-20261001/`:
  completed checkpoint, immutable segment results/events/metadata,
  merged-campaign-result.json, trial runtime process snapshots,
  `independent-audit-t1` through `independent-audit-t3` with fresh reads/compile
  and cleanup logs, and unique contract replay files/hash manifest.

Decision: keep the generic toolkit fix and the unchanged skills. Full GLM High
is strictly qualified for this OTT-34 cohort; do not relabel the historical Flash
cohort as qualified or claim general LAD saturation from this one case.
