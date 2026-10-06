# FAIL - paid V4 contract-validation evaluation

## Decision

Paid DeepSeek V4 Flash at its advertised maximum effort did not qualify on
`ott-24`. It scored 55, 55, and 75, exactly reproducing the prior paid
structural-validator campaign's score distribution and 61.67 mean.

Keep the generic operation-contract validator because its deterministic tests,
CLI boundary proof, and disposable live-TIA proof establish a real diagnostic
and safety improvement. Do not extend it from these model traces: none of the
new required-field issue codes fired, so the campaign provides no evidence
that additional stateless field checks would improve V4's behavior.

The hardware skill remained byte-identical at SHA-256
`1526365EA8077DF55C11F4ADB28528C068754A970895049643C091E12D5D10D2`.
No skill, case, prompt, fixture, or scorer change was made between the frozen
campaign manifest and execution.

## Campaign identity

- Campaign commit: `a9b42be`
- Trace commit: `a9b42be9e4b322009a2f563a41db2aa47cca17c4`
- Branch: `experiment/opencode-benchmark-slice`, clean in every trial
- Case: `ott-24`, pass score 90 with all assertions critical
- Fixture: `tia-v21-topology-6`, freshly restored for every trial
- Fixture SHA-256:
  `33193A83BE565BBD63486D66FFDE39404C76D9F8F01178DF28D435DBE3CC5732`
- Model: `openrouter/deepseek/deepseek-v4-flash`
- Catalog name/status/release date: DeepSeek V4 Flash, active, 2026-04-24
- Reasoning effort and exact OpenCode variant: `xhigh`
- Advertised variants: `high`, `xhigh`
- OpenCode: `1.18.10`
- Authentication: isolated projection of the OpenRouter API credential
- Catalog input/output/cache-read prices per million tokens:
  $0.14 / $0.28 / $0.028
- Campaign cost ceiling: $1.00

| Trial | Score | Qualified | Duration | Reasoning tokens | Cost |
| ---: | ---: | :---: | ---: | ---: | ---: |
| 1 | 55 | no | 806.790 s | 10,258 | $0.138074916 |
| 2 | 55 | no | 493.674 s | 12,777 | $0.155224076 |
| 3 | 75 | no | 262.912 s | 9,410 | $0.071269632 |
| **Aggregate** | **61.67 mean** | **0/3** | **521.125 s mean** | **10,815 mean** | **$0.364568620** |

Cost per passed trial is undefined because no trial qualified. The campaign
completed normally without a provider error or timeout and stayed below its
declared cost ceiling.

## Comparison with the prior paid control

The prior paid V4 campaign used the same model, `xhigh` effort, case, fixture,
skill, OpenCode version, and first structural validator, but did not contain
the new all-operation required-field contracts.

| Runtime | Scores | Mean | Pass | Mean duration | Mean reasoning | Total cost |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| First structural validator (`de05d53`) | 55/55/75 | 61.67 | 0/3 | 318.451 s | 9,243.33 | $0.30281849 |
| Required-field contracts (`a9b42be`) | 55/55/75 | 61.67 | 0/3 | 521.125 s | 10,815.00 | $0.36456862 |

The equal scores are descriptive, not proof of strict causal equivalence in a
three-trial stochastic sample. The stronger conclusion comes from the trace
audit: the incremental validator rejected zero calls. Its required-field
behavior was therefore not on the execution path that determined any score.

## Trace audit

Every trial eventually applied the requested hardware, passed fresh tree,
network, and IO-address readback, and compiled `PLC_Main`. All three failed the
bounded preview/retry protocol rather than the final engineering outcome.

### Trial 1

V4 made nine hardware-patch calls. Its first complete eight-operation batch
was rejected before TIA with four existing graph issues: two
`missing-relative-item-path` and two `missing-plug-result-id`. A later batch
triggered two more `missing-relative-item-path` issues. V4 corrected those
diagnostics, but accumulated eight strict-placement/preview matches before the
one successful apply. No batch triggered a required-field contract issue.

### Trial 2

V4 made eleven hardware-patch calls. The first seven operated on only station
creation and the two plugs; the first was rejected with two existing
`missing-relative-item-path` issues. It then submitted four complete
eight-operation batches, including an unsupported subnet-mask retry, before
the successful preview and apply. The scorer found four corrected operation
sets where exactly two were allowed. No batch triggered a required-field
contract issue.

### Trial 3

V4 made ten hardware-patch calls. It repeatedly previewed a six-operation
topology without the two requested IO-address operations, then added the
addresses later. One complete retry was rejected with two existing
`generated-child-reference-mismatch` issues before V4 corrected the binding.
Its final preview/apply pair matched the exact corrected operation set, earning
the additional 20 points, but nine placement/preview attempts and the earlier
partial transaction still violated the strict trace. No batch triggered a
required-field contract issue.

## Mechanism boundary

The new validator can prove that submitted operations contain their
deterministic required fields. It cannot infer that a user's requested
transaction is incomplete when V4 submits only creation/plugs or omits address
assignment, nor can a stateless pass know that the model has already made too
many previews. Those are semantic completeness and transaction-history
properties.

A generic preview fingerprint or one-use apply token could prove that an apply
matches one successful preview. That would be a useful safety feature, but it
would not by itself stop the extra and partial previews seen here, so it should
not be implemented merely to raise this benchmark score. The appropriate
anti-overfitting decision is to stop contract tuning and move to a new case or
model comparison unless preview/apply identity is desired as a product safety
feature in its own right.

## Artifacts and cleanup

Authoritative live campaign directory:

- `benchmarks/artifacts/20260803T071048419Z-campaign-hardware-family-transfer-deepseek-v4-flash-paid-xhigh-contract-validation-20260803`

Catalog-only preflight directory:

- `benchmarks/artifacts/20260803T070944757Z-campaign-hardware-family-transfer-deepseek-v4-flash-paid-xhigh-contract-validation-20260803`

The final disposable project was closed with `save=false`. The exact
toolkit-started TIA PID and its verified helper children were then stopped. All
three campaign workspaces and temporary command scripts were sent to the
Windows Recycle Bin. No campaign OpenCode or TIA process remains, and the
read-only fixture retained its original hash.
