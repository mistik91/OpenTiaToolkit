# Hardware structural validation: paid V4 evaluation

## Decision

Keep the generic structural validator. It did not make paid DeepSeek V4 Flash
qualify on `ott-24`, but it converted one malformed patch in every trial into
an indexed `ValidationFailed` response before Siemens Openness or a transaction
was entered. This is a safety and diagnostic improvement, not evidence that V4
has learned the required retry discipline.

Do not tune the skill from these traces. The hardware skill remained byte-for-
byte identical to the restored control (`SHA-256 1526365EA8077DF55C11F4ADB28528C068754A970895049643C091E12D5D10D2`),
and neither the validator nor its tests contain an `ott-24` identity, article
number, path, address, subnet, or IO-system answer.

## Implementation and pre-model validation

The runtime change was committed as `9726f1e` and the frozen campaign as
`de05d53`. Structural validation now runs at the command boundary and again at
the hardware workflow boundary. A rejected result sets
`ValidationBlocked=true`, `SafetyBlocked=true`, and `RolledBack=false`, returns
stable indexed `ValidationIssues`, and contains no operation results because no
Siemens transaction was opened.

The generic checks cover reusable-result producers, duplicate and forward or
unknown references, relative child paths for same-patch created devices,
explicit node/controller/connector indexes, generated IO-child binding through
the producing plug result, and unobservable expected-address guards.

Before V4 was allowed to run, the change passed:

- a Release solution build with 0 warnings and 0 errors;
- 637 non-integration tests;
- 54 architecture tests;
- 8 focused worker/MCP boundary tests;
- the 182-command generated-reference check and stable MCP 2.0 stdio smoke;
- a Release CLI boundary probe that aggregated three structural issues with
  outer error code `ValidationFailed` without TIA;
- a disposable TIA V21 dry-run and apply/readback campaign. The accepted batch
  created both modules, connected subnet and IO system, set the requested
  PROFINET identity and IO starts, and compiled `PLC_Main` with 0 errors and 0
  warnings.

The disposable project was closed without saving, the toolkit-owned TIA process
tree was stopped, and the original read-only fixture retained SHA-256
`33193A83BE565BBD63486D66FFDE39404C76D9F8F01178DF28D435DBE3CC5732`.

## Campaign

- Case: `ott-24`, pass score 90, all assertions critical
- Fixture: `tia-v21-topology-6`, freshly restored for every trial
- Model: `openrouter/deepseek/deepseek-v4-flash`
- Provider/auth: OpenRouter API key
- Reasoning effort and OpenCode variant: `xhigh`
- OpenCode: `1.18.10`
- Trace commit: `de05d538a6421158e1d28f1ca74f908aa9a4e65f`
- Trial count: 3
- Campaign cost ceiling: $1.00

| Trial | Score | Qualified | Duration | Reasoning tokens | Cost |
| ---: | ---: | :---: | ---: | ---: | ---: |
| 1 | 55 | no | 374.296 s | 10,064 | $0.10547726 |
| 2 | 55 | no | 310.235 s | 8,892 | $0.11095762 |
| 3 | 75 | no | 270.821 s | 8,774 | $0.08638361 |
| **Aggregate** | **61.67 mean** | **0/3** | **318.451 s mean** | **9,243.33 mean** | **$0.30281849** |

Cost per passed trial is undefined because no trial qualified.

The historical paid V4 control used the same case, fixture, model, `xhigh`
effort, OpenCode version, and unchanged skill, but the pre-validator runtime.
It scored 55/60/55 (56.67 mean), qualified 0/3, and cost $0.29080640. The new
run's five-point mean increase is not a qualification improvement and is too
small a sample to attribute causally. It also exactly matches the rejected
pre-call skill candidate's 61.67 mean. The reason to retain the runtime change
is its deterministic safety behavior, not this score delta.

## Trace audit

The new validator rejected exactly one malformed batch in each trial:

- Trial 1, call 2: two `missing-relative-item-path` issues on the two plugs.
- Trial 2, call 1: five `missing-relative-item-path` issues on both plugs and
  the three device-child network/identity operations.
- Trial 3, call 2: two `missing-relative-item-path` issues on the two plugs.

Every response used error code `ValidationFailed`, reported the exact operation
indexes, set `ValidationBlocked=true`, and kept `RolledBack=false`. V4 corrected
the root-binding defect after each response. This demonstrates that the
diagnostics are actionable.

It did not produce an economical or compliant retry sequence:

- Trials 1 and 2 each submitted three batches matching the final corrected
  operation set where exactly one clean preview and one identical apply were
  required. Trial 3 finally matched exactly twice, earning the extra 20 points.
- Trial 1 enabled displaced placement for both siblings after observing only
  the input module's displacement.
- Trial 2 separately observed both displacements, but then spent retries on a
  missing subnet name, a missing IO-system name, and an unrequested unsupported
  subnet mask before preview and apply.
- Trial 3 first previewed station creation alone, later previewed the six
  topology operations without either address assignment, and only then added
  the two address operations for a successful preview and apply. It also spent
  retries on missing network names and the unsupported subnet mask.

All three trials eventually applied the requested final state, independently
read it back, and compiled successfully. They remained disqualified because
the benchmark intentionally scores atomicity, exact placement learning, and
preview/apply discipline rather than final state alone.

## Boundary of this mechanism

This first validator is deliberately stateless and prompt-independent. It can
prove that a submitted operation graph is internally coherent; it cannot know
that a user's requested transaction is incomplete, that an optional field was
not requested, or that the current retry changed more than one observed fact.
Those are the remaining V4 failures.

A small generic follow-up is available: aggregate operation-required fields
such as `subnetName` and `ioSystemName` in the same pre-transaction validation
pass. A stronger future mechanism would issue a fingerprint/token for a
successful preview and require the apply to present the identical operation
graph. Neither change should encode benchmark-specific examples, and neither
should cause validation-blocked calls to receive task-success points.

Raw traces are retained under the ignored artifact directory
`benchmarks/artifacts/20260803T052920172Z-campaign-hardware-family-transfer-deepseek-v4-flash-paid-xhigh-structural-validation-20260803`.
