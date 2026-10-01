# OTT-34 LAD campaign audit — historical pre-fix results

Follow-up: the bulk SD export defect was corrected and a separate full GLM High
cohort passed 100/100/100. See the
[post-fix qualification report](lad-brownfield-bulk-export-fix-20261001.md).
The original results and findings below remain historical evidence.

FAIL — strict qualification is not clean. The benchmark implementation,
independent gold path, and real rollback proof passed. All six admitted model
runs produced the exact gold LAD state. Flash missed an encoding requirement once, and the
full model encountered a confirmed toolkit bulk/single export inconsistency.
Further model/skill work is paused for review of that toolkit defect. No
production C# or skill change is included; no PR or merge was performed.

## Frozen setup

- Branch: `codex/ott-34-lad-authoring-benchmark`, based on merged OTT-33
  `f8c6fc155c81a0e35c0b1714a3d1175a9b546417`.
- Flash cohort: `e4022435b58777fbbe75125020d1ee977df24d63`.
- Full-model cohort: `d1355abb28f225c77ebbd525233459bd6ca28ff3`.
- OpenCode 1.18.10; Windows PowerShell 5 harness.
- `zai-coding-plan/glm-5.3-flash` and `zai-coding-plan/glm-5.3`, both declared
  reasoning effort `high` and explicit OpenCode variant `high`.
- Existing user subscription authentication, provider-scoped to zai-coding-plan.
  No OpenRouter or OpenAI model was run.
- Three fresh restores per model, one admitted trial per checkpoint batch;
  TIA V21 Update 2 build 2100.0.121.1, PID 13008, PLC_Main.
- Read-only fixture `tia-v21-lad-brownfield-13`, SHA-256
  `952FC2C686BFA2C40D0D339340D8E6DDDEABF9B14E24367BF4BAAE796671EACC`.
- Prompt, allowed skills, engineering oracle, and point weights stayed unchanged
  across the model cohorts. The intervening safety guard covered every current
  out-of-scope Mutating/Destructive/Online command (66 commands).

See [the gold audit](ott-34-gold-audit-20261001.md) for fixture construction,
the successful Implementation-only/BodyOnly transaction, and forced compile
failure after two actually applied operations with exact rollback verification.

## Admitted results

Scores below use the final equivalent-single/bulk ordering assertion. Original
traces, metadata, and result files were not overwritten.

| Model / trial | Score | Strict pass | Seconds | Reasoning tokens | Reported cost | MCP rejections |
|---|---:|:---:|---:|---:|---:|---:|
| Flash High / 1 | 100 | yes | 462.975 | 7,513 | $0 | 0 |
| Flash High / 2 | 100 | yes | 433.550 | 6,950 | $0 | 1 |
| Flash High / 3 | 96 | no | 1084.373 | 20,332 | $0 | 1 |
| Full GLM High / 1 | 100 | yes | 389.406 | 7,830 | $0 | 1 |
| Full GLM High / 2 | 100 | yes | 323.303 | 6,753 | $0 | 0 |
| Full GLM High / 3 | 98 | no | 359.664 | 5,694 | $0 | 2 |

Flash: mean score 98.667, 2/3 strict passes, mean duration 660.299 seconds,
median 462.975 seconds, mean reasoning tokens 11,598.333.
Full GLM: corrected mean score 99.333, 2/3 raw strict passes, mean duration
357.458 seconds, median 359.664 seconds, mean reasoning tokens 6,759.
The latter is toolkit-confounded and must not be presented as a clean model
qualification or a LAD capability failure. Timings are descriptive, not a
controlled capability ranking.

All per-trial and per-model/task reported incremental costs are $0; cost per
strict passing trial is $0. These are subscription executions, not a claim that
the subscription itself is free or that it consumes no quota.

Flash trials 2/3 each recovered a rejected overlapping get-blocks read. Full
trial 1 recovered an overlapping get-software-tree read. Existing packaged
guidance already requires serial TIA calls. Full trial 3's two rejections were
the bulk baseline and readback exports described below, not concurrency errors.

## Flash encoding failure

Trial 3 authored the correct circuit but omitted the UTF-8 BOM from both
candidate files. It spent several attempts trying to insert invisible characters,
then declared BOM emission impossible and proceeded despite the explicit task
and existing authoring requirement. Both validators considered the ASCII document
valid/verified but warned about the absent BOM; this does not waive the task's
encoding contract. The critical verified-document-pair assertion correctly failed.

This was not a transport impossibility: the first two Flash writes began with
decoded U+FEFF, produced actual EF BB BF bytes, and validated with HasUtf8Bom true.
All three admitted full-model candidate pairs also had the BOM. No BOM recipe
or task-specific hint was added to any skill or fallback prompt.

## Confirmed toolkit inconsistency — pause point

Full-model trial 3 used export-blocks-as-docs for baseline and readback. Both
calls exported the FB and backup, but returned 2/3 with Main rejected:
`Skipped OB block 'Main': SIMATIC SD export not supported for Organization Blocks`.
The model reasonably used XML for Main after that response. It retained both
Main XML snapshots, but did not produce the task's required Main SD files.

The restriction is local, not an observed Siemens failure:
`src/OpenTiaToolkit.Core/Services/PlcDocumentTransferService.cs:109` explicitly
skips OB and FOB before invoking the per-block export. In the exact same
live project/session, an independent bulk control repeated the rejection and
an immediate export-block-as-docs for Main succeeded. The resulting Main.s7dcl
was byte-identical to the original fixture/gold baseline. Full compile stayed 0/0.
Evidence is retained under the full-model batch's
`independent-audit-t3/bulk-single-control.json`.

The next surgical change should align bulk export with the authoritative
per-block export/capability path and retain accurate per-target failures.
Do not add a Main/OTT-34-specific skill workaround. Do not infer FOB support
or broaden OB authoring/import authorization from a successful read-only OB
export. After the toolkit correction and proportionate runtime validation,
rerun a fresh complete cohort before declaring qualification or merging.

## Independent live verification

After every admitted trial, the root auditor reattached to the exact restored
project and exported new SD and XML evidence outside the model workspace.
Every FB canonical result hash was the independently established gold:
`11024a5231428de1bf97306e2a419a766f729d175125e8bb6489ec582eee3a9e`.

All six root audits passed:

- All 64 Boolean input combinations through actual exported power-flow wiring,
  including shared interlocks on Auto/Jog and correct contact polarity.
- Existing nonretained RunDelay : TON_TIME, correct IN/Q connections, 750ms PT,
  unbound ET, and one ordinary DelayedRun coil.
- Exact interface/static layout, block identity, five-network ordering,
  unrelated fault/set/reset networks and resources.
- Original backup, Main and paired resources unchanged, plus fresh PLC compile
  at zero errors/zero warnings.
- Software inventory: exactly Main, OTT34_Conveyor and its backup; no types or
  external sources added. Candidate BOM bytes checked separately.

Original preservation hashes:

- Resource file: `6E406647DA1EFD6A15E4A937BF45701AFB3A1A9AE8FB378C7327621653B606BE`.
- Backup SD: `004A3C5349C2F3B61687B680354FCBF0173FD179B60234E07199CB9D5388ECDF`.
- Main SD: `6B6CAE0B8130241248E16ADA2328E1DB0586ED00679BF236ECCB03A9FA066518`.

This proves a narrow brownfield LAD engineering task, not general LAD mastery,
PLC scan execution, the runtime 750ms transition, or safety-controller suitability.
Main was intentionally preserved rather than given a new FB caller.

## Scorer replay and invalid attempt

The original order regex recognized only single-block SD export. The prompt
does not require that call granularity. It now accepts single or bulk export
ordering while the separate critical file assertions still require complete
preservation evidence. Positive bulk and missing-Main controls prove both sides.
Full trial 3 changes only from 96 to 98; it still fails for missing baseline/Main
and readback/Main SD files. Flash's results remain 100/100/96.

Final case SHA-256: `A3B25ADC0B02243F586E17AE0B8383CF64E1E2F84F1C9A77EF54E57B14AD045B`.
LAD oracle SHA-256: `6A6CAEB9269EB359FACDC60E8ED98C713D629BBBDAD48EFD4F5FBB3D71CC21D8`.
Separate contract-replays retain original and replayed scores plus immutable
trace/metadata hashes; raw campaign aggregates retain their original grades.

The first full-model trial-3 attempt ended after 10.328 seconds with OpenCode
`Unexpected server error. Check server logs for details.` It made no tool calls,
reported zero tokens/cost, and was automatically excluded as runtime-error
despite exit code 0. The exact trial remained pending and one bounded fresh-state
retry completed. The failure has no reported HTTP status and is not evidence
of a confirmed z.ai rate limit. Both attempts remain retained.

Evidence roots (ignored raw artifacts):

- Flash: `benchmarks/artifacts/20261001T165932144Z-batch-lad-brownfield-glm-5-3-flash-high-20261001`.
- Full: `benchmarks/artifacts/20261001T174054495Z-batch-lad-brownfield-glm-5-3-high-20261001`.
- Root audits: each batch's `independent-audit-t1`, `-t2`, `-t3`.
- Final replays: each batch's `contract-replays/replay-manifest.json`.

## Handoff and validation

The full benchmark regression suite, focused live-gold replay, definitions,
plugin synchronization, generated-command reference, repository hygiene and
diff checks were run. Final scorer controls include 6 equivalent XML positives,
35 circuit/XML negatives, 2 unsafe paths, 2 full-case synthetic positives and
33 negatives, plus live-gold replay and current catalog mutation coverage.
No compiled-runtime source changed, so no runtime rebuild was required for
this benchmark slice; the proposed bulk fix will require a separate validation
pass before use.

All disposable model and audit projects were closed without saving. The archive
remains read-only with its original SHA-256. The owned TIA process is retained
empty for subsequent work; no model campaign remains running. The feature
branch is committed separately from master and is not merged. The known bulk
export issue remains unfixed, and no clean qualification is claimed.
