# OTT-36 engineering-blueprint qualification

This builder independently qualifies the registered OTT-36 case; qualification
is not a model result. The first live qualification exposed a toolkit-level
completion-reporting defect; see
[`../../../reports/ott-36-blueprint-preflight-20261004.md`](../../../reports/ott-36-blueprint-preflight-20261004.md).
After the generic fix and exact worker registration, independent recovery,
continuation and fresh-worker health controls passed; see
[`../../../reports/ott-36-blueprint-live-qualification-20261004.md`](../../../reports/ott-36-blueprint-live-qualification-20261004.md).

## Intended coverage

Reuse the immutable `tia-v21-baseline-1` archive. Coordinate a narrowly scoped PLC
tag comment and Unified HMI operator-label change using declarative blueprints.
Preserve tag identities, addresses, types, the second tag, HMI geometry, and Main.
These are metadata/text changes: no new PLC/HMI connection or runtime binding is
claimed.

The independent qualification exercises:

1. Exact discovery, untouched exports, clean baseline compilation, and standalone
   command dry-runs.
2. Validation and planning, followed by a project-context change. The old token
   must reject before any write; reopening the exact disposable project must show
   the original tags and HMI hash.
3. A three-step blueprint: a valid PLC comment update, a deliberately stale HMI
   base hash, and a later PLC comment sentinel. With `continueOnError=false`, the
   first write must remain visible, the HMI step must be reported incomplete, and
   the sentinel must not run. This is **not** an atomic cross-domain transaction.
4. Rejection of the consumed failed token, fresh domain previews and a reviewed
   recovery blueprint, independent readback, clean compilation, and rejection of
   the successfully consumed token.
5. Closing the disposable project without saving, closing the owned headless
   portal/MCP, checking archive integrity, and a fresh-worker baseline health run.

The script never imports into an existing user project, persists apply tokens, or
uses an unregistered worker. Inputs and evidence live outside Git. Each invocation
requires new absolute restore/evidence roots and the exact baseline archive hash.
If an operation times out, its MCP session is retained; inspect it rather than
retrying an unknown write or forcibly closing an unrelated process.

## Run

First verify the exact Release worker has a matching Siemens allow-list entry.
The CLI/MCP hosts do not require registration. From the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File benchmarks/fixtures/builders/ott-36/Invoke-BlueprintQualification.ps1 `
  -ArchivePath E:/Documents/OpenTiaBench/Archives/OTT_Benchmark_V21_20260731.zap21 `
  -RestoreRoot C:/OTT/Live/ott36-unique-run `
  -OutputRoot E:/Documents/OpenTiaBench/Artifacts/ott36-unique-run `
  -McpPath E:/Documents/Codex/OpenTiaToolkit/src/OpenTiaToolkit.MCP/bin/Release/net48/OpenTiaToolkit.MCP.exe `
  -WorkerPath E:/Documents/Codex/OpenTiaToolkit/src/OpenTiaToolkit.Worker.V21/bin/Release/net48/OpenTiaToolkit.Worker.V21.exe
```

Add `-BaselineOnly` with new roots to perform only independent fixture restoration,
tag/HMI verification, baseline export and clean compilation. Its
`PASS_HEALTH_ONLY` result is **not** blueprint or model qualification.

Add `-CheckContinuation` in a separate fresh restore to qualify explicit
`continueOnError=true`: a blocked HMI step must retain its structured evidence,
allow the independent suffix, and still leave the overall blueprint incomplete.
The control verifies both exact tag rows, the unchanged HMI hash and Main, clean
final compilation, and consumed-token rejection. Its
`PASS_CONTINUATION_CONTROL` result does not substitute for the default recovery
qualification. These switches are mutually exclusive.

`mcp.jsonl` preserves actual command envelopes and redacts apply tokens to stable
SHA-256 markers, so planning/apply relationships remain auditable without retaining
bearer tokens. Native screen/tag exports and stopped-blueprint results are retained
even when the completion assertion fails. `cleanup.json` records confirmed cleanup.

The case and structured scorer are independently qualified with synthetic
positive/negative controls and live-gold replay. Freeze their revision before
running three GLM-5.3 Flash High trials with fresh fixture restores. Escalate
to GLM-5.3 High only for genuine model-capability failures; do not tune skills to
this task or treat toolkit/capture faults as model scores.
