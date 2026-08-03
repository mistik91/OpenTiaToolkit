# Hardware operation-contract validation: V4 Free endpoint audit

## Decision

Keep the generic operation-contract validator, but exclude this campaign from
model-quality comparisons. All three DeepSeek V4 Flash Free trials ended with
the provider error `503 The request queue is full` before OpenCode produced a
final response. The harness's raw scores of 0, 10, and 10 describe incomplete
traces, not the capability of the refreshed model or the effect of the new
validator.

Do not make another skill change from this evidence. The hardware skill stayed
byte-identical at SHA-256
`1526365EA8077DF55C11F4ADB28528C068754A970895049643C091E12D5D10D2`, and
the runtime validator contains no benchmark station, catalog identifier,
target path, network name, or address value.

## Generic runtime change

Commit `8b5aa8b` extends the existing stateless hardware-patch validator with
operation contracts for all 27 registered patch operations. Before the
hardware service or Siemens Openness is entered, one malformed request can now
return every deterministic issue in the batch:

- unsupported operation names;
- missing operation targets;
- missing required text, value, and container fields;
- missing one-of choices for operations with alternative required inputs; and
- invalid positive lengths where zero cannot be meaningful.

The checks reuse stable indexed issue codes and leave handler validation in
place as defense in depth. They are independent of prompt wording and task
identity.

Before any model run, the implementation passed:

- a Release solution build with 0 warnings and 0 errors;
- 641 non-integration tests;
- 54 architecture tests;
- 8 focused worker/MCP boundary tests;
- the 182-command generated-reference check and MCP 2.0 stdio smoke;
- a Release CLI probe that aggregated five independent required-field issues
  from one invalid batch without connecting to TIA; and
- a disposable TIA V21 dry-run, apply, independent topology/network/address
  readback, and `PLC_Main` compile with 0 errors and 0 warnings.

The accepted live batch proved that valid operations still pass the new
contracts. The disposable project was closed without saving, its toolkit-owned
TIA process tree was stopped, and the read-only fixture retained SHA-256
`33193A83BE565BBD63486D66FFDE39404C76D9F8F01178DF28D435DBE3CC5732`.

## Free campaign identity

- Campaign commit: `8078185`
- Trace commit: `8078185057fa11fe569e48dda56242b9646fb0fb`
- Branch: `experiment/opencode-benchmark-slice`, clean in every trial
- Case: `ott-24`, pass score 90 with all assertions critical
- Fixture: `tia-v21-topology-6`, freshly restored for every trial
- Model: `opencode/deepseek-v4-flash-free`
- Catalog name: `DeepSeek V4 Flash Free (New)`
- Catalog status/release date: active, 2026-07-31
- Reasoning effort and exact OpenCode variant: `max`
- Advertised variants: `high`, `max`
- OpenCode: `1.18.10`
- Catalog context/output limits: 200,000 / 128,000 tokens
- Catalog input/output price: $0 / $0
- Authentication: none; isolated free-provider run

| Trial | Raw score | Qualified | Duration | Reasoning tokens | Cost | Terminal event |
| ---: | ---: | :---: | ---: | ---: | ---: | --- |
| 1 | 0 | no | 479.924 s | 347 | $0.00 | provider 503 |
| 2 | 10 | no | 652.085 s | 0 | $0.00 | provider 503 |
| 3 | 10 | no | 825.656 s | 1,601 | $0.00 | provider 503 |

The campaign aggregate is formally 0/3 with a raw mean of 6.67 and zero
recorded cost. It is intentionally not treated as a model result because every
trace lacks the required final stop for the same external failure.

## Trace audit

Trial 1 reached project verification plus initial hardware-tree and network
reads, then the provider queue failed before a hardware patch was submitted.

Trial 2 completed focused catalog discovery. It first previewed station
creation alone, then submitted the complete eight-operation candidate batch.
All operation-contract fields were present, so the new validator correctly let
the batch reach TIA. TIA reported the first strict displaced-placement result;
the next model turn failed with the provider 503.

Trial 3 also completed catalog discovery. Its first three-operation topology
batch omitted both relative plug paths. The earlier graph-structural validator
aggregated two `missing-relative-item-path` issues before TIA and the model
corrected them. Two subsequent dry-runs learned the two module placements
independently. Before it could build the complete network/address batch, the
next model turn failed with the same provider 503.

No trial produced a missing operation-contract field, so this campaign did not
exercise the incremental required-field aggregation. It does reconfirm that
the prior structural diagnostics are actionable, but it cannot measure whether
the new contracts improve V4's completed-task reliability.

## Artifacts and cleanup

Authoritative campaign directory:

- `benchmarks/artifacts/20260803T061802167Z-campaign-hardware-family-transfer-deepseek-v4-flash-free-max-contract-validation-20260803`

The three disposable workspaces were sent to the Windows Recycle Bin. The
final project was closed with `save=false`, and only the exact benchmark-owned
TIA process tree was stopped after its ownership and children were verified.
No campaign OpenCode or TIA process remains. Raw traces, catalog identity, and
result JSON remain in the ignored artifact directory.

## Paid follow-up

The free alias was reachable and correctly cataloged, but not reliable enough
for this 8-14 minute live task. The same frozen evaluation was subsequently
completed on paid DeepSeek V4 Flash `xhigh`. It scored 55/55/75 and qualified
0/3; none of the new required-field contracts fired. The completed comparison
is in `hardware-contract-validation-v4-paid-20260803.md`.
