# Brownfield modernization: V4 endpoint, paid V4, and Sol ceiling

Date: 2026-08-03

## Outcome

`ott-25` is valid, solvable, and not saturated. The frozen skill and case
produced a sharp capability split:

| Model | Effort / variant | Valid trials | Scores | Qualification | Recorded cost | Mean cost/task | Mean duration | Mean reasoning tokens |
| --- | --- | ---: | --- | --- | ---: | ---: | ---: | ---: |
| DeepSeek V4 Flash Free (New) | `max` / `max` | 0/1 | endpoint-invalid (raw 0) | excluded | $0 | $0 | n/a | n/a |
| DeepSeek V4 Flash, OpenRouter | `xhigh` / `xhigh` | 3/3 | 60 / 60 / 80 | 0/3, critical | $0.26399478 | $0.08799826 | 353.881 s | 9,961.33 |
| GPT-5.6 Sol, OpenAI subscription | `max` / `max` | 3/3 | 100 / 100 / 100 | 3/3 qualified | $0 reported | $0 reported | 395.335 s | 6,468 |

OpenCode was pinned at `1.18.10`. Paid V4's refreshed catalog exposed `xhigh`
as its highest variant and prices of $0.14/M input and $0.28/M output. Sol's
refreshed catalog exposed `max`; its zero reported cost represents external
subscription usage, not a claim that the model is economically free.

The formal paid V4 trial costs were $0.088647832, $0.087632384, and
$0.087714564. `costPerPassedTrialUsd` is null because none qualified. One valid
$0.051441012 paid diagnostic trial was discarded after it exposed a campaign
aggregation defect described below. Including that diagnostic, actual paid
OpenRouter spend for this experiment was $0.315435792.

## Frozen boundary

- Case: `ott-25`
- Fixture: `tia-v21-brownfield-7`
- Fixture SHA-256:
  `256BED153DF00421DBA65B1F9FFB2625CE09E8715C430A5038CE074F46889073`
- Formal paid/Sol Git commit: `0ea3f2b`
- OpenCode version: `1.18.10`
- Skill changes during case design or model campaigns: none

The Siemens gold proof is in `ott-25-gold-audit-20260803.md`. It covers
strict-placement failure, corrected preview, positive apply, independent
readback, clean compile, exact inverse, and a real final-operation collision
whose rollback was read from a new CLI attachment to the same live TIA
instance.

## Free endpoint smoke

The refreshed OpenCode catalog reported `opencode/deepseek-v4-flash-free` as
active, tool-capable, released 2026-07-31, zero-priced, and advertising
`high`/`max` variants. The one-trial maximum-effort smoke nevertheless ended
after 225.166 seconds with HTTP 503 `The request queue is full`.

The new harness classification recorded:

- `validForModelComparison: false`;
- `executionFailure.kind: provider-error`;
- `executionFailure.statusCode: 503`;
- model status `endpoint-invalid`;
- zero valid trials and null comparison mean; and
- raw score 0 retained only for audit.

The remaining free trials were deliberately skipped. This is provider
availability evidence, not a model-quality result.

## Paid V4 trace audit

All three paid trials were runtime-valid. Every trial eventually applied the
requested final state, independently read tree/network/IO state, and compiled
with zero errors and warnings. They failed on reviewed transaction discipline,
not basic TIA reachability.

### Trial 1 - 60, $0.088647832

V4 made five patch calls: four dry-runs and one apply. Before its successful
preview it:

1. added an unrequested subnet-mask write that Siemens rejected;
2. obtained the required strict placement failure; and
3. used the duplicated generated-child path
   `ModernOutputs/ModernOutputs` instead of the plug-relative
   `ModernOutputs` path.

The final batch omitted all three expected-state guards on the pre-existing
PROFINET identity. It therefore failed the exact operation set and the exact
two-preview retry sequence.

### Trial 2 - 60, $0.087632384

V4 made nine patch calls: eight dry-runs and one apply. The generic validator
blocked its first call before TIA because operation 1 used `name` instead of
required `itemName`; the model corrected that issue on the immediately next
patch. Later calls omitted destructive confirmation, targeted the device root,
repeated a placement failure after premature displacement opt-in, used the
duplicated generated-child path, and retried the unsupported subnet-mask
write. Its final successful batch again omitted the three identity guards.

Validator telemetry was exact: one blocked call, one
`missing-required-field` issue at operation index 1, one pre-TIA rejection,
and one immediate correction.

### Trial 3 - 80, $0.087714564

V4 made four patch calls: three dry-runs and one apply. Its first call produced
the required strict placement failure. Its second used the duplicated
generated-child path. The third and fourth calls were the exact complete
corrected preview and unchanged apply, including all observed-state guards.

This trial passed the structured operation-set check but correctly failed the
one-strict-plus-one-corrected-preview sequence. The 80 score distinguishes a
nearly correct recovery from qualification without relaxing the invariant.

## Sol ceiling audit

All three Sol trials made exactly three hardware-patch calls:

1. one complete strict dry-run, rejected only for Siemens' reported
   `LegacyCell/ModernOutputs` displacement;
2. one complete corrected dry-run; and
3. one confirmed destructive apply.

Each trace had patch statuses `error / completed / completed`, exactly two
dry-runs and one apply, byte-identical corrected preview/apply operation JSON,
all existing-state guards, the correct plug result/relative-child binding, and
no validator intervention. Independent tree, network, and IO reads plus clean
compilation passed in every trial.

Per-trial durations were 432.720, 381.570, and 371.714 seconds. Reasoning-token
counts were 7,011, 6,382, and 6,011. Sol was somewhat slower on average than
paid V4, but it used fewer reasoning tokens and qualified reliably.

## Harness defect found during execution

The first paid campaign attempt produced one intact, valid 80-point model
result, then failed while building its suite summary. The runner's compact
suite record omitted `exitCode`, while the newly shared statistics helper read
that field under PowerShell strict mode.

Commit `0ea3f2b` fixed the issue generically by:

- preserving `exitCode` in every compact suite result;
- centralizing runtime-failure detection;
- treating absent `timedOut`/`exitCode` as the historical successful defaults
  for backward compatibility; and
- adding a regression test for legacy compact records.

The complete benchmark test suite passed after the fix. The formal paid and
Sol campaigns were restarted on the same clean commit; the diagnostic trial
was not mixed into either aggregate.

## Decision

Keep `ott-25`, its scorer, fixture, and skills unchanged.

The result does not justify a skill edit. The skill already states the generic
rules V4 violated: use the observed parent for a strict probe, preserve the
complete batch, bind generated children through the plug result, guard
pre-existing state, and apply the unchanged corrected preview. Sol's stable
3/3 ceiling proves those instructions are sufficient. Adding this station's
names, fields, or retry history would be case-shaped overfitting.

The mechanical validator is useful but correctly bounded. It prevented one
malformed V4 request from touching TIA and measured immediate correction. It
cannot statically infer an unrequested writable Siemens attribute, the one
acceptable retry count, or the runtime-generated child shape without turning
prompt policy and live Siemens behavior into a task-specific validator.

The next hardware case should expand coverage rather than tune this one: build
a two-network/two-controller brownfield fixture and require atomic disconnect,
reconnect, identity, and address migration of an existing remote station. That
would exercise the currently unbenchmarked destructive topology operations
while retaining the same anti-overfitting boundary.

## Artifact roots

- Free endpoint smoke:
  `benchmarks/artifacts/20260803T084259561Z-campaign-hardware-brownfield-modernization-deepseek-v4-flash-free-max-smoke-20260803`
- Formal paid V4:
  `benchmarks/artifacts/20260803T085349431Z-campaign-hardware-brownfield-modernization-deepseek-v4-flash-paid-xhigh-20260803`
- Sol ceiling:
  `benchmarks/artifacts/20260803T091418798Z-campaign-hardware-brownfield-modernization-gpt-5-6-sol-max-20260803`
- Discarded paid diagnostic:
  `benchmarks/artifacts/20260803T084736897Z-campaign-hardware-brownfield-modernization-deepseek-v4-flash-paid-xhigh-20260803`
