# Hardware skill lift pilot — 2026-08-30

## Conclusion

The paired evaluator found a large, measurable benefit from making `hardware-configuration-workflow` available, and it also found one benchmark defect that materially understated that benefit. After correcting the scorer and replaying the saved traces, the skilled arm passed 11/12 trials versus 3/12 without the skill. Mean score lift was +52.36 points and pass-rate lift was +66.67 percentage points. The paired outcomes were eight skill-only wins, zero baseline-only wins, three both-pass pairs, and one neither-pass pair; the exact McNemar diagnostic was 0.007812.

This is strong pilot evidence, not a final generalization. It covers four routing buckets, one model/provider configuration, and three trials per arm. Two later held-out trials also exposed severe inference-latency variance in the fallback model.

Update, 2026-09-01: an unchanged three-trial GLM-5.3 Flash High retry passed 3/3. All trials reached TIA and scored 100, so the two earlier silent timeouts are best classified as transient inference/provider failures rather than representative workflow capability. See [GLM retry — 2026-09-01](#glm-retry--2026-09-01).

## Configuration

- Harness: OpenCode 1.18.10.
- Requested first choice: the OpenCode-hosted free DeepSeek V4 Flash endpoint.
- Actual model: `zai-coding-plan/glm-5.3-flash`, because the free V4 endpoint was absent from the OpenCode catalog at selection time.
- Variant and declared reasoning effort: `high` / `high`.
- Authentication: user OpenCode provider-scoped Z.AI subscription.
- Recorded cost: USD 0 for every trial.
- Target skill: `hardware-configuration-workflow`.
- Arms: physical target-skill absence versus presence.
- Routing buckets: explicit (`ott-14`), implicit (`ott-17`), contextual (`ott-21`), and negative (`ott-01`).
- Repetitions: three trials per case and arm, 24 paired campaign trials total.
- Corrected aggregate artifact (local, ignored by Git): `benchmarks/artifacts/20260830T120100000Z-corrected-hardware-skill-lift-glm-5-3-flash-high/campaign-result.json`.

## Corrected paired result

| Metric | With skill | Without skill | Lift |
| --- | ---: | ---: | ---: |
| Valid trials | 12 | 12 | — |
| Passed | 11 | 3 | +8 |
| Pass rate | 91.67% | 25.00% | +66.67 pp |
| Mean score | 98.68 | 46.32 | +52.36 |
| Mean duration | 196.950 s | 425.397 s | -228.447 s |
| Mean reasoning tokens | 1,939.67 | 6,310.92 | -4,371.25 |
| Total recorded cost | USD 0 | USD 0 | USD 0 |

Routing was correct in 12/12 skilled-arm trials: the target skill loaded in every positive bucket and did not load in the negative bucket.

| Case | Routing | With skill | Without skill |
| --- | --- | ---: | ---: |
| `ott-01` | Negative | 3/3, mean 100 | 3/3, mean 100 |
| `ott-14` | Explicit | 3/3, mean 100 | 0/3, mean 55 |
| `ott-17` | Implicit | 3/3, mean 100 | 0/3, mean 25 |
| `ott-21` | Contextual | 2/3, mean 94.74 | 0/3, mean 5.26 |

## Scorer defect found by trace replay

The original `ott-17` assertion used a trace-wide regular expression that accidentally required JSON object properties to be serialized in one order. The model had issued the required single guarded dry-run with both exact I/O operations, but OpenCode serialized `expectedStartAddress` before the other numeric properties. All three skilled traces therefore received 85 instead of 100.

The assertion now uses `toolOperationSet`, which compares the exact structured operation set without imposing object-property or array order. A regression test deliberately reverses both forms of ordering and also verifies that a partial one-operation patch is still rejected. Replaying the immutable traces changed all three skilled `ott-17` trials from 85/fail to 100/pass; all three skill-absent trials remained 25/fail.

The original results remain untouched. Regraded copies and a corrected merged artifact are stored separately so the correction is auditable.

## Generic skill changes

Two small instructions were added without benchmark identities, addresses, or worked task examples:

1. `hardware-configuration-workflow` now states that requested all-or-nothing changes belong in one complete guarded patch, that discovery must finish before the first preview, and that a successful preview must be followed by one apply of the exact same ordered operation set. For cross-domain work, every domain preview must finish before the first apply.
2. `plc-block-and-tag-workflow` now states that every `modify-tags` mutation—including create and update—must reuse the exact previewed operation set with `dry-run=false` and `confirm=true`. This closes an actual ambiguity: the prior text called out confirmation only for delete/replace.

The first post-hardware-change `ott-21` candidate executed exact hardware preview/apply calls but attempted the tag apply once without confirmation. The toolkit rejected it safely; the model retried with confirmation and scored 84.21 because the benchmark correctly counted three identical tag operation-set calls instead of two. That trace motivated the PLC skill correction.

## Post-change held-out validation

The fresh three-trial `ott-21` candidate at commit `c9025f1` produced:

| Trial | Score | Result | Duration | Reasoning tokens | Observation |
| ---: | ---: | --- | ---: | ---: | --- |
| 1 | 0 | Timeout | 720.317 s | 268 | Loaded four skills, never reached TIA |
| 2 | 100 | Pass | 361.229 s | 4,274 | Exact hardware and tag preview/apply pairs, confirmed mutations, compile and readback; zero validator interventions |
| 3 | 0 | Timeout | 720.248 s | 0 | Provider/model emitted no OpenCode event |

Aggregate pass rate was therefore 1/3, mean score 33.33, and mean duration 600.598 seconds. These are valid usability failures under the case's fixed 720-second budget. They do not disprove the instruction change: the only trial that executed the workflow followed the corrected contract and passed 100. They do prevent claiming a repeatable behavioral lift from this three-trial candidate.

## NVIDIA advisory audit

The pinned NVIDIA SkillEvaluator revision passed all 18 repository skills across schema, PII, license, Unicode, quality, and lint checks. After the changes:

- `hardware-configuration-workflow`: B, 87.2/100.
- `plc-block-and-tag-workflow`: B, 86.8/100.

Its remaining findings are advisory metadata and document-shape suggestions such as version/author/tags, shorter descriptions, and conventional headings. They did not identify the behavioral confirmation gap or the order-sensitive benchmark assertion; those required paired execution and trace replay. The static audit is useful as a complementary gate, not as a replacement for live skill evaluation.

## Recommendation

Keep the structured scorer correction and both generic skill clarifications. Do not add task-specific examples to force these cases through.

The next useful formal campaign is the full 24-trial paired slice at the same declared effort if a post-change qualification number is required. Otherwise, the corrected paired replay plus the stable GLM retry is sufficient to move on to another held-out capability case. Keep the earlier Z.AI timeouts in the evidence set as transient usability failures; do not replace or silently retry their trial identities.

## GLM retry — 2026-09-01

The same `ott-21` skilled arm was retried after the free V4 endpoint remained unavailable. No skill or harness behavior changed between the failed candidate and this retry. The retry used OpenCode 1.18.10, `zai-coding-plan/glm-5.3-flash`, variant `high`, declared reasoning effort `high`, the same 720-second case budget, and commit `7aa594c` whose only change after the skill candidate was this report. The worktree-state hash and immutable fixture hash remained unchanged.

| Trial | Score | Result | Duration | Reasoning tokens | Validator interventions |
| ---: | ---: | --- | ---: | ---: | ---: |
| 1 | 100 | Pass | 277.033 s | 4,694 | 0 |
| 2 | 100 | Pass | 256.420 s | 4,143 | 0 |
| 3 | 100 | Pass | 247.155 s | 3,460 | 0 |

Aggregate results:

- Pass rate: 3/3 (100%).
- Mean score: 100.
- Mean duration: 260.203 seconds.
- Mean reasoning tokens: 4,099.
- Target hardware skill loaded: 3/3.
- Total recorded cost: USD 0.
- Hardware-patch validator blocked calls: 0.

Every trial used exact hardware and PLC-tag preview/apply pairs, confirmed mutations, fresh readback, and compilation. Combined with the earlier post-confirmation candidate, all four post-change trials that reached the engineering workflow passed 100; the two failures occurred before any TIA work began. This supports the generic confirmation clarification while preserving the operational lesson that provider latency must remain part of model qualification.

Local artifact root, ignored by Git: `benchmarks/artifacts/20260901T000000000Z-retry-glm-5-3-flash-high-ott21`. The source archive SHA-256 was rechecked after cleanup and remained `B57573BBD33E185D7B843CDED87362F6BF08932944388508BFD934D575C35A56`.
