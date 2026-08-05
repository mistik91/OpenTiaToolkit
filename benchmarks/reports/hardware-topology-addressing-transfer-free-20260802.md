# NOT QUALIFIED - V4 Free reaches the state but not the protocol

## Outcome

DeepSeek V4 Flash Free at its advertised maximum reasoning effort did not
qualify on the held-out `ott-23` topology-and-addressing transfer:

| Trial | Score | Pass | Duration | Reasoning tokens | Recorded cost |
| ---: | ---: | --- | ---: | ---: | ---: |
| 1 | 75 | no | 189.581 s | 9,149 | $0.00000000 |
| 2 | 75 | no | 232.638 s | 12,060 | $0.00000000 |
| 3 | 75 | no | 274.101 s | 15,529 | $0.00000000 |
| **Aggregate** | **75.00 mean** | **0/3** | **232.107 s mean** | **12,246 mean** | **$0.00000000** |

All three executions completed without a runtime timeout, produced the
requested station, assigned both process-image starts, connected the station,
independently read the state back, and compiled `PLC_Main` with zero errors and
zero warnings. All three nevertheless failed critical transaction-shape and
retry-sequence assertions, so the model is disqualified rather than accepted
on final state alone.

## Frozen live surface

- Trace-generation commit: `e89256b399521f9e1288c874c24e847eca436ef1`
- Branch: `experiment/opencode-benchmark-slice`
- Git state during all three trials: clean
- Git-state SHA-256:
  `B9D712A26B4F49A6CCFE3DAEE6AD8242A25B586A405BDB37BC99111315E56DFC`
- OpenCode: pinned `1.18.10`
- Model: `opencode/deepseek-v4-flash-free`
- Catalog name/status: `DeepSeek V4 Flash Free (New)`, active
- Reasoning effort and variant: `max`
- Advertised variants: `high`, `max`
- Case: `ott-23`, pass score 90, all assertions critical
- Fixture: `tia-v21-topology-6`
- Fixture SHA-256:
  `33193A83BE565BBD63486D66FFDE39404C76D9F8F01178DF28D435DBE3CC5732`
- Final hardware skill SHA-256:
  `9A6826F13940189D83E79E740C2C8BE71A6A31760E50BE96A77722EA155E4F10`

The catalog reported a 200,000-token context limit, 128,000-token output
limit, tool-call support, and zero input/output price. The campaign used the
isolated provider endpoint without user credentials. A direct isolated
`READY` smoke test succeeded before the live campaign, and the three benchmark
traces confirm that the endpoint was healthy rather than quota-blocked.

The canonical, Codex, OpenCode, and Claude hardware skill copies remained
byte-identical. No skill content was changed for this case or campaign.

## What the model did correctly

Every trial passed 9 of 11 assertions, worth 75 points:

- focused exact-article catalog discovery;
- baseline and independent final hardware-tree reads;
- baseline and independent final network reads;
- baseline and independent final IO-address reads;
- both displaced-placement discoveries with rollback evidence;
- a successful complete preview and rollback;
- exactly one confirmed apply;
- the requested final hardware, addressing, and PROFINET state; and
- a clean compile.

This makes `ott-23` a useful distinction between functional outcome and a
bounded, auditable transaction. The model was capable of operating the live
toolkit, but was not reliable enough to follow the complete-batch recovery
protocol.

## Consistent failure mode

All three trials lost the 20-point exact corrected-operation-set assertion and
the 5-point strict sequence assertion.

Trial 1 kept all eight operations together after its first attempt, but first
guessed the device root instead of the discovered `Rack_0` parent. After the
two required displaced-placement failures, it added an unsupported subnet-mask
write, retried after that rejection, then previewed and applied successfully.
Consequently the semantic corrected operation set appeared three times rather
than exactly twice.

Trial 2 used three-operation discovery batches instead of retaining the full
eight-operation candidate. Its final candidate also named the primary item
`IM 155-6 PN ST` rather than the explicitly requested `PackagingBay`. It then
made the same unsupported subnet-mask attempt before the successful preview
and apply.

Trial 3 likewise reduced the first three probes to creation plus two plugs. Its
first full candidate doubled the generated child paths to
`Photoeyes/Photoeyes` and `Pneumatics/Pneumatics`; the next candidate repaired
those bindings but included the unsupported subnet mask. Only the following
preview and apply were clean. The semantic corrected operation set therefore
again appeared three times rather than exactly twice.

These are generic workflow failures already covered by the frozen skill:
discover the rack before the strict probe, keep the complete candidate intact,
combine a plug result with only the relative generated child, omit unrequested
optional setters, and apply exactly the successful reviewed batch.

## Scorer correction and anti-overfitting check

The first evaluation exposed an evaluator defect: the expected operation set
used the internal result ID `packaging-station`, even though result IDs are
local variable names and the prompt does not prescribe their spelling. The
operation-set evaluator now has an opt-in `normalizeResultReferences` mode. It
accepts any bijective, internally consistent renaming of `resultId` and
`targetRef`, still compares operations without array-order preference, and
rejects broken reference chains.

Synthetic coverage proves both accepted renaming and rejection of a mismatched
chain. The `ott-23` regression suite also proves that all result IDs may be
renamed consistently while wrong generated children, wrong addresses, an
apply-before-preview trace, and an extra corrected preview remain failures.

Rescoring the immutable raw traces did not improve any score or qualification
result. Trials 1 and 3 changed from zero apparent operation-set matches to
three semantic matches, still violating the required count of exactly two;
trial 2 remained at zero because its requested item name was wrong. This
correction removes a benchmark naming preference without giving the model an
example or weakening the transaction protocol.

## Artifacts

Authoritative campaign directory:

- `benchmarks/artifacts/20260802T041904791Z-campaign-hardware-topology-addressing-transfer-free-20260802`

The runner's `result.json` and `campaign-result.json` files retain the live
evaluation. Each trial directory also contains `result.rescored.json` from the
reference-normalized evaluator. Numerical scores and campaign status are
unchanged.

## Cleanup and integrity

The final disposable project was closed with `save=false`, and TIA Portal was
closed before its two exact lingering process IDs were stopped. The three
campaign roots, two gold-audit project roots, and the isolated endpoint-smoke
root were sent to the Windows Recycle Bin. Raw traces and score artifacts
remain under the ignored campaign artifact directory.

No OpenCode or TIA Portal trial process remains. The MCP process and its idle
worker that serve the active development session were intentionally retained;
they no longer own a TIA process. The read-only fixture retained its original
size and SHA-256. All four distributed hardware-skill copies still have the
same frozen SHA-256 recorded above.

## Conclusion

Keep V4 Free as the inexpensive control. It is clearly usable for interacting
with the toolkit and reached the complete requested state in all three trials,
but it is not qualified for strict autonomous topology provisioning on
`ott-23`. No further skill tuning is justified from this result: the failures
repeat generic guidance already present in the frozen skill. The next useful
comparison is a stronger model on this same held-out case, not another
case-shaped skill edit.

## Later scorer audit

The subsequent Sol comparison found that the placement assertion did not cap
the total number of hardware dry-runs. It now requires exactly the three calls
specified by the prompt. Rescoring these immutable V4 traces under the current
assertion produces 65/65/65 rather than 75/75/75; qualification remains 0/3.
See `hardware-topology-addressing-sol-skill-ab-20260802.md` for the audit and
the controlled Sol skill comparison.
