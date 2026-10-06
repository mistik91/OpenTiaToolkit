# HMI trend tag-filter skill verification

Date: 2026-09-05

## Outcome

PASS. GLM-5.3 Flash High completed three valid `ott-29` trials at
100/100/100 after the generic `list-hmi-tags` scope clarification. The
specific behavior that motivated the change improved from three filter-only
safety rejections in the original cohort to zero in the verification cohort,
while all trend creation, association removal, archive retirement, and final
reference assertions remained green.

Every trial loaded `opentia-toolkit-workflow`, `basic-tia-interaction`, and
`hmi-trend-logging-workflow` exactly once. The captured trend-skill payload
contained the new rule that a text filter narrows returned matches but does
not scope traversal, so it must be paired with a concrete `root-path` or an
explicitly bounded full scan.

## Configuration

- Campaign:
  `hmi-trend-filter-skill-verification-glm-5-3-flash-high-20260905`
- Campaign source commit:
  `f50e9e01ab36668aa169d9f0f34c024f5176e8ba`
- OpenCode: `1.18.10`
- Model: `zai-coding-plan/glm-5.3-flash`
- Reasoning effort / variant: `high` / `high`
- Cost class: `subscription`
- Fixture: `tia-v21-unified-trends-9`
- TIA Portal: V21 Update 2
- Fixture archive SHA-256:
  `7078FA79558948F375229C94AB69FF00D171646CAE874D51DA9B8453D35C82EB`

## Verification trials

| Trial | Score | Passed | Valid | Duration | Reasoning tokens | Tag reads | Tag-read errors | Filter-only rejections |
| ---: | ---: | --- | --- | ---: | ---: | ---: | ---: | ---: |
| 1 | 100 | yes | yes | 211.876 s | 1,771 | 2 | 0 | 0 |
| 2 | 100 | yes | yes | 196.751 s | 2,192 | 2 | 0 | 0 |
| 3 | 100 | yes | yes | 228.531 s | 2,353 | 3 | 0 | 0 |
| Mean / total | 100 | 3/3 | 3/3 | 212.386 s | 2,105.33 mean | 7 | 0 | 0 |

The harness reported $0 total request cost through the Z.AI subscription
route. There were no runtime failures, critical failures, endpoint-invalid
trials, rate limits, validator interventions, or benchmark MCP policy errors.
Each trace contained three expected error-state results from guarded or
not-found `delete-hmi-data-logs` checks; none was a tag-discovery error.

## Before-and-after behavior

| Metric | Original cohort | Verification cohort |
| --- | ---: | ---: |
| Score under corrected rubric | 99/99/99 | 100/100/100 |
| Valid passes under corrected rubric | 0/3 | 3/3 |
| Filter-only calls rejected by the safety guard | 3 | 0 |
| `list-hmi-tags` error calls | 3 | 0 |
| Mean duration | 266.967 s | 212.386 s |
| Mean reasoning tokens | 2,205.33 | 2,105.33 |

The original result files remain immutable evidence of the old 100-point
adjudication. Replaying their event streams through the corrected case scorer
produced 99/100 and the critical assertion
`no-filter-only-safety-rejection` for each trial.

The new traces exercised two legitimate strategies rather than copying a
fixture-specific recipe:

- trials 1 and 2 used bounded full scans with `allow-full-scan=true`,
  `max-results=200`, and `max-depth=4`; and
- trial 3 combined bounded `Mixer` and `Batch` filters with
  `allow-full-scan=true`, `max-results=200`, and `max-depth=6`, then used a
  bounded unfiltered final verification scan.

The duration reduction is directional with only three trials. The reliable
result is the 3-to-0 reduction in the targeted error class while the complete
task stayed green.

## Attribution limit

The only model-visible repository change between cohorts was the generic
trend-skill clarification. The scorer correction changed adjudication but
could not change model behavior; runtime tool schemas and the fixture were
unchanged. This is therefore useful forward evidence for the skill change,
but it is not a randomized paired A/B and should not be treated as a precise
causal-effect estimate.

## Harness recovery, artifacts, and cleanup

Authoritative ignored artifacts:

- model-only preflight:
  `benchmarks/artifacts/20260905T010755926Z-campaign-hmi-trend-filter-skill-verification-glm-5-3-flash-high-20260905`;
- completed checkpoint, trial traces, and merged result:
  `benchmarks/artifacts/20260905T010812193Z-batch-hmi-trend-filter-skill-verification-glm-5-3-flash-high-20260905`.

The first checkpoint attempt stopped before trial 1 because its selected TIA
PID exited and fixture preflight timed out. A toolkit-launched replacement then
exposed that the default Debug worker lacked the effective Siemens allow-list
entry. The checkpoint was resumed without renumbering trials by explicitly
using the allow-listed Release CLI and MCP against visible TIA PID 13352. This
was a harness/environment recovery, not a model retry; each accepted trial ran
once.

The final disposable project was closed without saving and a Release-CLI read
returned an empty project list. TIA PID 13352 remains open with no project for
future work. Campaign-owned OpenCode, MCP, and worker processes exited. The
pre-existing repository MCP was preserved; after a final successful
Debug-path `list-instances`, `connect-instance`, and empty `list-projects`
proof, its isolated worker/session was reset. The effective Siemens allow-list
entry was returned to the exact Debug worker identity used by default harness
resolution. The immutable fixture archive remained read-only, 5,641,130 bytes,
and retained its declared SHA-256.
