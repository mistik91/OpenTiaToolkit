# HMI alarm tag-filter skill verification

Date: 2026-09-05

## Outcome

PASS. GLM-5.3 High completed three valid `ott-28` trials at 100/100/100
after the generic `list-hmi-tags` scope clarification. The specific behavior
that motivated the change improved from three filter-only safety rejections in
the original cohort to zero in the verification cohort, while all alarm
mutation, readback, retirement, and preservation assertions remained green.

Every trial loaded `hmi-discrete-alarm-workflow` exactly once, and the captured
skill payload contained the new rule that a text filter must be paired with a
concrete `root-path` or an explicitly bounded full scan.

## Configuration

- Campaign:
  `hmi-alarm-reconciliation-glm-5-3-high-skill-verification-20260905`
- Campaign source commit:
  `1a393d0d66fc70c4d2c5fb7072db9ed66b41e2d4`
- Skill/schema change: `6c80198`
- OpenCode: `1.18.10`
- Model: `zai-coding-plan/glm-5.3`
- Reasoning effort / variant: `high` / `high`
- Cost class: `subscription`
- Fixture: `tia-v21-unified-alarms-8`
- TIA Portal: V21 Update 2
- Fixture archive SHA-256:
  `316B538B73F1DF41FBC07DBD0DA5963F039E552568EB1984604FE854DBE9BD52`

## Verification trials

| Trial | Score | Passed | Valid | Duration | Reasoning tokens | Tag reads | Tag-read errors | Filter-only calls |
| ---: | ---: | --- | --- | ---: | ---: | ---: | ---: | ---: |
| 1 | 100 | yes | yes | 216.049 s | 2,275 | 2 | 0 | 0 |
| 2 | 100 | yes | yes | 232.663 s | 3,636 | 2 | 0 | 0 |
| 3 | 100 | yes | yes | 198.279 s | 2,576 | 3 | 0 | 0 |
| Mean / total | 100 | 3/3 | 3/3 | 215.664 s | 2,829 mean | 7 | 0 | 0 |

The harness reported $0 total request cost through the Z.AI subscription
route. There were no runtime failures, critical failures, endpoint-invalid
trials, rate limits, validator interventions, or benchmark MCP policy errors.

## Before-and-after behavior

| Metric | Original cohort | Verification cohort |
| --- | ---: | ---: |
| Corrected task score | 100/100/100 | 100/100/100 |
| Valid passes | 3/3 | 3/3 |
| Filter-only calls rejected by the safety guard | 3 | 0 |
| `list-hmi-tags` error calls | 3 | 0 |
| Mean duration | 415.316 s | 215.664 s |
| Mean reasoning tokens | 2,858.67 | 2,829 |

The new traces exercised three legitimate strategies rather than copying one
benchmark-specific recipe:

- trial 1 tried a scoped path, then used an explicit bounded full scan;
- trial 2 combined text filters with `allow-full-scan=true`, `max-results`, and
  `max-depth`; and
- trial 3 read the three known tag paths individually.

This diversity is evidence that the clarification preserved agent choice
while removing the semantic ambiguity. The large duration reduction is useful
but should be treated as directional with only three trials; the reliable
conclusion is the 3-to-0 reduction in the targeted error class.

## Attribution limit

Commit `6c80198` changed both the model-visible workflow skill and the runtime
tool-schema/error guidance. This campaign therefore verifies the combined
intervention. Because all three traces loaded the updated skill and followed
its allowed alternatives, the result supports the skill change operationally,
but it does not isolate how much of the improvement came from the skill versus
the schema. A strict causal skill A/B would keep the new schema fixed and run
paired old-skill/new-skill arms.

## Artifacts and cleanup

Authoritative ignored artifacts:

- preflight:
  `benchmarks/artifacts/20260904T230054924Z-campaign-hmi-alarm-reconciliation-glm-5-3-high-skill-verification-20260905`;
- completed batch and merged result:
  `benchmarks/artifacts/20260904T230114081Z-batch-hmi-alarm-reconciliation-glm-5-3-high-skill-verification-20260905`.

The final project was closed without saving model mutations, and TIA PID 13856
reported no open project. The isolated OpenCode, MCP, and worker processes
exited. TIA's compiler and search background children were identified by
parent PID and preserved. The trial-1 and trial-2 restore roots were moved to
the Windows Recycle Bin. The closed trial-3 restore remains at
`C:\OTT\Bench\20260904T230911893Z-ott-28-t3-80EE5FEA` because Windows rejected
Recycle Bin handling for that path. The immutable archive remained read-only
with its original size and SHA-256.
