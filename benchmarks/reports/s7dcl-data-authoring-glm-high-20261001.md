# OTT-33 — structured S7DCL data authoring qualification

## Outcome

GLM-5.3 High qualifies in 3/3 fresh trials, with adjudicated scores
100/100/100 (original 100/99/100). GLM-5.3 Flash High scored 100/97/100 and
passes 2/3 strictly: trial 2 omitted explicit TIA instance discovery/selection.
Every trial nevertheless produced the exact requested engineering state and
preserved all baseline objects. The Flash failure remains a valid negative
workflow outcome, not a provider failure or a discarded trial.

No production toolkit or skill changes were made. The data-authoring evidence
does not justify tuning the skill: both models handled the initialization-only
replacement, nested new DB, existing UDT reference, and atomic transaction.
The basic skill already requires instance discovery when TIA is running; the
Flash deviation was instruction-following, not an uncovered data-skill rule.

This is one bounded case, not qualification of arbitrary data authoring or a
general ranking of the models. It covers consistently non-retained DBs,
preserving a non-optimized layout, nested structures, arrays, existing UDT reuse,
and scalar initialization. It does not qualify retention changes, mixed-retain
layouts, instance DBs, UDT replacement, resource/comment authoring, HMI address
migration, or runtime PLC behavior.

## Frozen inputs

- Branch: `codex/s7dcl-data-benchmark`, based on merged master
  `b018e35d63b549e668d5da083f258817c3623e46`.
- Inference revision for all six trials:
  `bd794aa3393c392647287a2a58de3f6a9eeffa51`; clean worktree.
- Case / fixture: `ott-33` / `tia-v21-s7dcl-data-12`.
- Archive SHA-256:
  `A550A21C719511E7F019E4FBEEC1413EB8238762E8E38D9547EECF3E0B67D02E`.
- Models: `zai-coding-plan/glm-5.3-flash` first, then
  `zai-coding-plan/glm-5.3` as the strict-workflow fallback.
- Declared reasoning effort / OpenCode variant: `high` / `high` for both.
- OpenCode: pinned `1.18.10`.
- Authentication: isolated provider-scoped reuse of the user's z.ai subscription.
- Three fresh archive restores and separate model workspaces per model;
  one trial per checkpoint. No checkpoint retries, provider failures, timeout,
  or rate-limit pauses occurred. Remaining subscription quota was not measured.

Both campaigns used the same prompt, fixture, skills, and inference revision.
No model output was repaired and no skill was changed between models.

## Per-model / per-task results and cost

| Model, High effort | Trial | Original / adjudicated score | Strict pass | Duration | Reasoning tokens | Reported cost |
| --- | ---: | ---: | --- | ---: | ---: | ---: |
| GLM-5.3 Flash | 1 | 100 / 100 | Yes | 483.135 s | 5,687 | $0 |
| GLM-5.3 Flash | 2 | 97 / 97 | No | 456.741 s | 6,112 | $0 |
| GLM-5.3 Flash | 3 | 100 / 100 | Yes | 651.015 s | 7,707 | $0 |
| GLM-5.3 | 1 | 100 / 100 | Yes | 344.660 s | 5,689 | $0 |
| GLM-5.3 | 2 | 99 / 100 | Yes | 324.526 s | 6,755 | $0 |
| GLM-5.3 | 3 | 100 / 100 | Yes | 372.135 s | 8,477 | $0 |

Flash: mean score 99, strict pass rate 2/3, mean duration 530.297 seconds
(8 minutes 50 seconds), mean reasoning tokens 6,502. Full GLM: adjudicated mean
score 100, strict pass rate 3/3, mean duration 347.107 seconds (5 minutes
47 seconds), mean reasoning tokens 6,973.67. These small-cohort observations are
not a causal latency or skill-lift claim.

Each model/task trial and both campaign totals report $0 incremental metered
cost. Subscription quota was consumed; the monthly subscription price is not
amortized into these results, and the models are not claimed to be free.
No OpenRouter credit or OpenAI model benchmark quota was used.

## Workflow and independent verification

Every trial had exactly one CleanCompile transaction, two document operations,
one reviewed safe plan, and one verified successful apply. Existing DB changes
were baseline-bound with Implementation-only authorization; the new DB was
create-only. Final transaction compile and fresh readback-following compile
returned 0 errors/0 warnings. No lower-level import bypass, source generation,
probe, save, unrelated mutation, or successful-transaction discard occurred.

Flash trial 2 used `connect-portal` then verified the project, but omitted
`list-instances` plus exact `connect-instance`; the critical connection assertion
therefore fails even though the project happened to be correct. No assertion
was weakened to accept that shortcut.

Three read-only MCP overlap conflicts were safely recovered: Flash trial 1
on software-tree discovery, Flash trial 3 on cross references, and full GLM
trial 3 on cross references. The remaining trials had zero MCP errors. These
are retained in the traces; passing the case does not establish perfect serial
call discipline. The shipped instructions already require serial MCP calls.
There were no offline validation failures or mutation rejections in the model
campaigns.

For each trial, four untouched baseline SD documents and five post-apply SD
documents were compared directly against independent gold, normalizing only
line endings and outer whitespace: all 54 comparisons matched exactly. Both
baseline and readback complete SW.Types.PlcStruct XML nodes were compared:
all 12 matched exactly, excluding document-level export timestamp metadata.
This proves the four requested changes, complete new nested layout and array
bounds, UDT resolution, and preservation of every unrelated member, attribute,
initialization, backup, consumer, Main, and UDT engineering payload in this case.

The [gold audit](ott-33-gold-audit-20261001.md) independently proved the fixture,
successful path, and a real second-operation import failure after the existing
DB had already changed. That failure restored the original DB exactly, left the
new DB absent, and recovered 0-error/0-warning compilation.

After the final model exited, the repository MCP session reattached to the exact
campaign process PID 18440 and verified its project path:
`C:\OTT\Bench\20261001T150540488Z-ott-33-t3-80EE5FEA\restored\OTT_Benchmark\OTT_Benchmark.ap21`.
Fresh compile and fresh exports of all five blocks plus the UDT matched gold.
The disposable project was closed without saving and no project remained open.
The read-only fixture archive retained its original hash and size.

## Scorer-only adjudication

Full GLM trial 2 explicitly stated `nothing saved/closed/deleted`. Its final
summary also contained the requested objects, DirectDocuments route, Succeeded
state, and preservation/compile evidence. The noncritical one-point summary
pattern recognized `not saved` and `unsaved` but missed `nothing saved`.

After both cohorts completed, that phrase was added to the noncritical summary
check with a positive regression trace. Final static review also strengthened
the unrelated-mutation guard to reject direct create and find/replace command
prefixes; no model trace contained them, and two negative controls were added.
Engineering assertions and the instance-selection requirement were unchanged.
The line-ending positive test explicitly generates LF and CRLF instead of
relying on source-file line endings.
Final scorer coverage: 3 positive traces and 28 negative controls; full `npm test`
and definition validation passed (26 cases, 59 campaigns, 13 fixtures).

All six immutable traces were replayed with their original metadata into separate
`adjudicated-result.json` files. Original `result.json` files and original merged
campaign summaries were preserved. Only full GLM trial 2 changed from 99 to 100;
all pass/fail outcomes, including Flash trial 2's failure, were unchanged. The
inference revision above remains the revision under test; this post-cohort
scorer amendment is not a new model run or a skill improvement.

## Evidence locations and remaining toolkit issue

Raw, original merged, and separately adjudicated evidence remains under:

- `benchmarks/artifacts/20261001T141902360Z-batch-s7dcl-data-authoring-glm-5-3-flash-high-20261001`
- `benchmarks/artifacts/20261001T145038766Z-batch-s7dcl-data-authoring-glm-5-3-high-20261001`

Fixture construction exposed an existing lower-level issue: the raw SD import
wrapper can report success for a non-null Siemens Failure result, and offline
validation does not detect the observed non-optimized retain inconsistency.
The persistent transaction caught the absent exact target and rolled back with
full proof. This issue is documented, not patched or attributed to the model.
A separate runtime hardening change would be preferable to adding a task recipe
to the skill.

All changes are local commits on the dedicated branch. No push, PR, or merge
was performed for this slice.
