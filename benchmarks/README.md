# OpenCode benchmark slice

This directory contains the executable OpenTiaToolkit agent benchmark slice. Twenty-seven cases cover discovery, guarded PLC and HMI mutation, constrained PLC code editing, native SCL multi-object authoring, structured S7DCL data authoring, brownfield LAD interlock and timer editing, bidirectional VCI synchronization, library reuse, hardware relocation and rollback behavior, computed multi-row planning, cross-domain remapping and transfer, catalog-driven Siemens and third-party GSDML topology provisioning, brownfield modernization, discrete-alarm and trend-logging reconciliation, guarded retirement, and an unsupported Multiuser boundary.

The benchmark is in this repository because the cases, skills, command contracts, and fixtures must evolve together. Large TIA project artifacts stay outside Git; only their identity and integrity hashes are versioned here.

The first formal 3-trial model campaign is summarized in
[`reports/initial-model-qualification-20260731.md`](reports/initial-model-qualification-20260731.md).

## What is measured

Each run records:

- the exact OpenCode CLI and model selection;
- the declared reasoning effort and exact OpenCode variant (`provider-default`
  with a null variant when no override is applied);
- the Git commit, branch, dirty flag, and deterministic worktree-state hash under test;
- newline-delimited OpenCode events, including MCP calls and outputs;
- elapsed time, token usage, cost, exit status, and timeout status;
- whether the trace is valid for model comparison, including a sanitized
  terminal provider/runtime failure classification when it is not;
- hardware-patch validator interventions: blocked call and issue counts,
  stable issue codes and operation indexes, pre-TIA rejection, and immediate
  next-patch correction;
- the one-based trial index for repeated model/task executions;
- deterministic trace and final-answer assertions.
- path-confined workspace-file assertions for authored artifacts, including
  strict UTF-8 decoding, optional BOM and size guards, required content
  patterns, and forbidden content patterns;

Paired skill-evaluation campaigns add:

- workspaces where the target skill directory is physically present or absent;
- explicit, implicit, contextual, and negative routing labels;
- target-skill attempt and successful-load telemetry;
- per-arm pass-rate Wilson intervals, paired score lift, paired pass outcomes,
  and an exact McNemar diagnostic.

NVIDIA SkillEvaluator 0.2.1 is also available as an advisory, provider-free
static audit. The wrapper pins the official Git revision, enables UTF-8 for its Windows
console output, skips model-backed deduplication, and applies the repository
policy in `skill-evaluation/nvidia-policy.yaml`. That policy downgrades only
NVIDIA public-catalog author metadata; security, PII, license, Unicode, lint,
and quality findings remain visible in the generated reports:

```powershell
.\scripts\Invoke-NvidiaSkillAudit.ps1
```

Every invocation also writes a suite summary with mean score, pass count,
duration, cost, and links to the individual result files. Campaign and merged
reports retain every trial and aggregate each model/task combination with score
range, pass rate, critical-failure rate, runtime-failure count, mean and median
duration, mean reasoning tokens, total and mean cost, and cost per passing
trial.

Terminal provider/runtime errors and traces that exit without a final stop are
retained with their raw score and cost but marked invalid for model comparison.
They do not contribute to score, pass-rate, duration, or reasoning-token means;
aggregates report valid, invalid, and provider-endpoint-invalid counts plus the
raw mean for audit. A versioned case timeout remains a valid negative model
outcome because completing within that budget is part of the evaluated task.
The implementation, compatibility rules, immutable V4 trace replay, and test
evidence are recorded in
`reports/harness-validity-and-validator-telemetry-20260803.md`.

Cases contain no hidden answers in the model workspace. The runner copies only the OpenCode plugin bundle and the selected prompt into an isolated directory outside the Git worktree. Evaluation happens after OpenCode exits.

For portable case definitions, the scorer normalizes hyphens in OpenCode tool names to underscores before matching `namePattern`.

The initial score is a transparent behavior score, not a claim of model intelligence. Critical safety failures fail a case even when its point total would otherwise pass.

## Prerequisites

- Windows with Siemens TIA Portal V21 and Openness access configured.
- A built `OpenTiaToolkit.CLI.exe` and `OpenTiaToolkit.MCP.exe`.
- Node.js and npm.
- The external fixture archive described by `fixtures/manifest.json`.
- One visible TIA Portal instance. The runner attaches to it, verifies that any open project is a declared benchmark fixture, closes that project without saving, and restores a fresh copy from the fixture archive. It refuses to close unrelated projects.

Install the pinned harness dependency:

```powershell
Set-Location benchmarks
npm ci
```

Validate definitions without TIA:

```powershell
npm test
```

Run a read-only case:

```powershell
.\scripts\Invoke-OpenCodeBenchmark.ps1 `
  -CaseId ott-01 `
  -Model opencode/big-pickle `
  -FixtureArchivePath E:\Documents\OpenTiaBench\Archives\OTT_Benchmark_V21_20260731.zap21
```

Run all single-user cases:

```powershell
.\scripts\Invoke-OpenCodeBenchmark.ps1 `
  -CaseId ott-01,ott-03,ott-08,ott-10,ott-14,ott-15,ott-16 `
  -Model opencode/big-pickle `
  -FixtureArchivePath E:\Documents\OpenTiaBench\Archives\OTT_Benchmark_V21_20260731.zap21
```

`ott-18` uses a pre-provisioned Multiuser local session and is deliberately excluded from the default single-user batch. Pass its `.als21` path with `-MultiuserSessionPath`.

## Model qualification campaign

`campaigns/initial-model-qualification.json` is the first versioned model cohort. It qualifies three inexpensive paid models and one free control on `ott-01`, `ott-03`, and `ott-14` before any full-suite comparison.

`campaigns/sol-ceiling-qualification.json` runs the subscription-backed
`openai/gpt-5.6-sol` ceiling baseline on the same cases at reasoning effort
`max`.

`campaigns/deepseek-v4-flash-free-refresh-20260801.json` reruns the stable
OpenCode Zen `deepseek-v4-flash-free` alias after its provider-side refresh.
The public alias does not expose a dated backend revision, so the campaign ID
records the serving epoch rather than claiming equivalence to a separately
versioned provider model. It preserves the original free-model configuration:
three trials per case at reasoning effort and variant `max`.

`campaigns/hardware-skill-lift-glm-5-3-flash-high-20260830.json` is the first
same-commit paired skill-lift pilot. It evaluates
`hardware-configuration-workflow` on one explicit case (`ott-14`), one
implicit case (`ott-17`), one contextual cross-domain case (`ott-21`), and one
negative routing case (`ott-01`). Every case/trial pair runs first without the
target skill directory and then with it. All other case inputs, visible base
skills, model settings, fixture restoration, and mechanical scoring remain
fixed. The model is the subscription-backed `zai-coding-plan/glm-5.3-flash`
at declared reasoning effort and OpenCode variant `high`.

`campaigns/hardware-gsdml-ifm-glm-5-3-flash-high-20260901.json` freezes the
first campaign for held-out cross-vendor case `ott-27`. It runs three trials
with the same subscription-backed GLM-5.3 Flash model at declared reasoning
effort and OpenCode variant `high`. The hardware skills are unchanged from the
prior Promag and skill-lift work, so the campaign measures transfer rather than
case-specific skill tuning. It scored 60, 70, and 90, but all three trials
failed at least one critical transaction-discipline assertion despite reaching
the correct final hardware state. The trace audit, zero-metered-cost semantics,
and recovered aggregation are in
`reports/hardware-gsdml-ifm-glm-5-3-flash-high-20260901.md`.

`campaigns/hardware-gsdml-ifm-glm-5-3-high-20260902.json` holds that case,
fixture, three-trial count, skill payload, and `high` effort fixed while moving
from GLM-5.3 Flash to subscription-backed `zai-coding-plan/glm-5.3`. This is
the lower-cost capability escalation before an optional GPT-5.6 Sol `xhigh`
ceiling campaign. GLM-5.3 qualified 3/3 at 100/100/100 with the exact four-call
transaction sequence and zero validator interventions, so no Sol run was
needed on this case. Results and cleanup are in
`reports/hardware-gsdml-ifm-glm-5-3-high-20260902.md`.

The PR qualification baseline groups the remaining nineteen cases by fixture
so every checkpoint batch receives exactly one immutable archive or one
preprovisioned Multiuser session. All groups use
`zai-coding-plan/glm-5.3` at declared effort and variant `high`, with three
trials per case:

| Campaign prefix | Fixture | Cases | New trials |
| --- | --- | --- | ---: |
| `full-suite-baseline` | `tia-v21-baseline-1` | `ott-01`, `ott-03`, `ott-08`, `ott-10`, `ott-14`, `ott-15`, `ott-16` | 21 |
| `full-suite-io` | `tia-v21-io-2` | `ott-13`, `ott-17` | 6 |
| `full-suite-multi-address` | `tia-v21-multi-address-3` | `ott-12`, `ott-19` | 6 |
| `full-suite-cross-domain` | `tia-v21-cross-domain-4` | `ott-20` | 3 |
| `full-suite-cross-domain-transfer` | `tia-v21-cross-domain-transfer-5` | `ott-21` | 3 |
| `full-suite-topology` | `tia-v21-topology-6` | `ott-22`, `ott-23`, `ott-24`, `ott-26` | 12 |
| `full-suite-brownfield` | `tia-v21-brownfield-7` | `ott-25` | 3 |
| `full-suite-multiuser` | `tia-v21-multiuser-1` | `ott-18` | 3 |

The already completed, input-identical `ott-27` GLM-5.3 High qualification
supplies the other three trials, producing a 60-trial full-suite baseline
without spending subscription quota to repeat a 3/3 result. The batch driver
defaults to one case-sized three-trial checkpoint. It never mixes fixture
archives inside one campaign.

`campaigns/hardware-skill-io-address-candidate-20260801.json` is the candidate
half of a controlled skill A/B. It holds the refreshed free model, `ott-14`
prompt, fixture, three-trial count, and maximum reasoning configuration fixed;
the clean Git commit identifies the candidate hardware skill payload. Compare
it with the `ott-14` control recorded in
`reports/deepseek-v4-flash-free-refresh-20260801.md`. The completed comparison
is in `reports/hardware-skill-io-address-ab-20260801.md`.

`campaigns/hardware-skill-io-address-transfer-20260801.json` raises the skill
qualification bar with three trials each of the original collision case, a
successful guarded relocation with fresh readback, and a reversed collision
with a different target, owner, and Siemens next-free response. The candidate
skill remains unchanged for this campaign. Results are in
`reports/hardware-skill-io-address-transfer-20260801.md`.

`campaigns/hardware-skill-io-atomicity-20260801.json` raises it again without
changing the candidate skill: `ott-13` transfers guarded relocation to a real
output module, while `ott-17` requires one mixed input/output transaction and
proves that a later output collision rolls back an earlier valid input change.
These cases use the independently verified `tia-v21-io-2` fixture archive.
The Siemens gold audit and immutable archive identity are recorded in
`reports/ott-13-17-gold-audit-20260801.md`.
The scorer audit, two surgical skill iterations, and final 3/3 qualification
are reported in `reports/hardware-skill-io-atomicity-20260801.md`.

`campaigns/hardware-skill-io-address-index-20260801.json` removes the single-
address-object shortcut. `ott-12` uses a real mixed DI/DQ module whose input is
address index 0 and output is address index 1, then requires the output move to
byte 132 while proving that the input remains at byte 0. Its independently
restored fixture and Siemens gold audit are recorded in
`reports/ott-12-gold-audit-20260801.md`. The existing skill qualified 3/3 at
100 after an attribute-order-dependent scorer bug was corrected; the audit is
in `reports/hardware-skill-io-address-index-20260801.md`.

`campaigns/hardware-skill-dual-space-compaction-20260801.json` is the first
computed allocation case. `ott-19` supplies no destination bytes: the agent
must derive four guarded moves across independent Input and Output spaces from
the observed hardware order and bit lengths, keep both anchors fixed, handle
both indices of one mixed module, and execute the complete plan as exactly one
preview plus one atomic apply. Siemens gold evidence is in
`reports/ott-19-gold-audit-20260801.md`; its corrected 3/3 saturation result is
in `reports/hardware-skill-dual-space-compaction-20260801.md`.

`campaigns/hardware-plc-cross-domain-remap-20260801.json` crosses the skill
boundary. `ott-20` derives the same four-row hardware compaction, remaps four
PLC tags so they still reference the same physical channels, stages both
domain previews before either apply, independently verifies hardware and tag
state, and compiles. Its V4 fixture and live forward/inverse evidence are in
`reports/ott-20-gold-audit-20260801.md`. The strict control failed 0/3 on
silently weakened tag selectors; after one surgical skill change, the clean
candidate passed 3/3. The full comparison is in
`reports/hardware-plc-cross-domain-remap-20260801.md`.

`campaigns/hardware-plc-cross-domain-transfer-20260801.json` is the held-out
qualification for that unchanged skill. `ott-21` replaces every task identity,
uses different baseline and destination ranges, reverses the hardware ordering
rule, and preserves four nonzero tag bit offsets. Its case definition also uses
an order-independent structured operation-set assertion instead of encoding a
preferred JSON array/property order. The immutable fixture, forward/inverse
proof, failed-preview no-write check, and real mid-transaction rollback are in
`reports/ott-21-gold-audit-20260801.md`. The unchanged skill passed 3/3 after a
generic scorer correction normalized the MCP's direct and wrapped operation
array forms; the qualification audit is in
`reports/hardware-plc-cross-domain-transfer-20260801.md`.

`campaigns/hardware-topology-provisioning-20260801.json` moves into a distinct
hardware domain. `ott-22` must discover installed ET 200SP catalog versions,
create and populate a remote station, recover from TIA's reported displaced
module placement, chain newly created objects by result reference, wire the
station to an existing subnet and controller IO system, configure PROFINET
identity, and verify the new topology from fresh reads. Its immutable fixture
and Tier 4 forward/inverse/rollback proof are in
`reports/ott-22-gold-audit-20260801.md`. The skills remain unchanged for the
qualification campaign. The strict DeepSeek V4 Flash Free control failed 0/3
with a mean score of 26.67: one trace reached the correct final state through
a non-atomic 50-call exploration, one stalled after catalog discovery, and
one produced no runtime events before timeout. The qualification analysis is
in `reports/hardware-topology-provisioning-20260801.md`; the benchmark remains
unchanged for a surgical skill A/B. Three paid DeepSeek V4 Flash skill
candidates then improved mean score from 60.00 to 78.33 and 81.67, but all
remained 0/3 under the strict atomicity checks. The final skill is retained
because its exact field and target-binding guidance is generic; further
`ott-22` tuning stops at the anti-overfitting boundary. The full progression is
in `reports/hardware-topology-provisioning-skill-ab-20260801.md`.

A frozen maximum-effort escalation then held that final skill and all benchmark
inputs unchanged. DeepSeek V4 Flash at its catalog ceiling of `xhigh` remained
0/3 with an 85.00 mean because of critical retry-sequence failures. GPT-5.6
Luna at `max` timed out in all three trials. GPT-5.6 Sol at `max` passed 3/3
with three scores of 100, establishing that the task and generic skill are
solvable without case-shaped tuning. The comparison, exact effort identities,
costs, and raw artifact paths are in
`reports/hardware-topology-maximum-effort-escalation-20260802.md`.

`campaigns/hardware-topology-addressing-transfer-free-20260802.json` raises the
held-out topology bar without changing the skill. `ott-23` uses different
station and module identities, nonadjacent slots, new process-image ranges, and
an eight-operation transaction that must assign both IO starts through named
plug results plus their generated relative children. The unchanged read-only
topology fixture passed strict-placement, positive, exact inverse, real late
rollback, and fresh-process proof in
`reports/ott-23-gold-audit-20260802.md`.

DeepSeek V4 Flash Free at its advertised `max` variant then completed three
live `ott-23` trials at zero recorded cost. Every trial reached and verified
the correct final state and compiled cleanly, but the model remained 0/3. An
initial scorer correction made result-reference names alpha-equivalent while
preserving their binding graph. A later Sol trace audit also made the required
placement-probe count exact; under the current scorer the V4 trials are
65/65/65 because every trace contains extra hardware retries. The frozen-skill
V4 evidence is in
`reports/hardware-topology-addressing-transfer-free-20260802.md`.

Sol at `max` was then evaluated on the same held-out case. Its corrected frozen
control scored 90/65/90, still 0/3. One generic skill clarification stopped
agents from guessing expected address guards for IO children created inside
the same transaction; the clean candidate improved to a stable 90/90/90 but
remained 0/3 because every trial first probed the device root instead of the
discovered rack. That remaining rule was already explicit, so tuning stopped.
The scorer audit, controlled skill change, costs, and anti-overfitting decision
are in `reports/hardware-topology-addressing-sol-skill-ab-20260802.md`.

`ott-24` then moves the held-out transfer to the ET 200MP family without any
further skill change. It uses unrelated interface and HF I/O modules, a rail
topology, new identities and address ranges, and explicitly scores the absence
of guessed expected-state guards on address children created inside the same
transaction. The immutable fixture passed the exact two-stage placement retry,
positive, inverse, late-failure rollback, and fresh-process campaigns documented
in `reports/ott-24-gold-audit-20260802.md`. Maximum-effort V4 Free and Sol
campaign definitions are frozen for the first model comparison.

That frozen comparison produced a clean capability split. Sol qualified 3/3
with three scores of 100 and the exact four-call hardware sequence. V4 Free
reached and verified the requested project in every run but remained 0/3 at
60/60/75 because it added root-target or unsupported optional-field retries.
Those failure modes are already covered explicitly by the generic skill, so no
further skill edit is retained. The trace audit, effort identities, durations,
and zero-cost accounting are in
`reports/hardware-family-transfer-sol-v4-20260802.md`.

A subsequent paid V4 `xhigh` A/B tested whether consolidating those existing
rules into a mandatory pre-call checklist changed behavior. The candidate mean
rose from 56.67 to 61.67, but both arms remained 0/3 and the candidate still
used root-bound plugs, premature sibling displacement, partial batches, or
broken result-reference chains. The rewrite was therefore reverted; the
controlled traces, per-trial costs, free-endpoint 503 exclusion, and
anti-overfitting decision are in
`reports/hardware-skill-precall-invariants-ab-20260803.md`.

The next experiment moved the repeated operation-graph invariants into a
generic pre-transaction runtime validator while leaving the skill unchanged.
Paid V4 `xhigh` remained 0/3 at 55/55/75, but each trial received an indexed
`ValidationFailed` response for a malformed root-bound batch before Siemens was
touched and then corrected that defect. The implementation gates, live TIA
proof, costs, historical-control comparison, trace audit, and boundary between
structural validation and prompt-dependent retry policy are in
`reports/hardware-structural-validation-v4-20260803.md`.

The validator was then extended generically to aggregate deterministic
required-field and target-contract failures across all 27 registered hardware
patch operations. Its implementation passed the full non-integration,
architecture, reference, MCP, CLI-boundary, and disposable live-TIA gates. A
maximum-effort V4 Free campaign was attempted on the unchanged `ott-24`, but
all three trials ended with the provider's `503 request queue is full` before a
final response. Its 0/10/10 raw scores are therefore endpoint-invalid and are
excluded from model-quality comparisons. The implementation proof, exact
catalog identity, trace audit, and cleanup record are in
`reports/hardware-contract-validation-v4-free-20260803.md`.

The paid V4 `xhigh` follow-up completed normally at 55/55/75, exactly matching
the first structural-validator campaign's scores and 61.67 mean. It cost
$0.36456862 and qualified 0/3. The incremental required-field validator fired
zero times: all four validation-blocked calls used the earlier graph checks,
while the remaining failures were extra or partial previews that a stateless
field contract cannot identify from one submitted batch. All trials still
applied, independently verified, and compiled the requested final state. The
controlled comparison and anti-overfitting decision are in
`reports/hardware-contract-validation-v4-paid-20260803.md`.

`ott-25` moves beyond greenfield topology work into an existing ET 200MP
station. It requires one destructive transaction to replace a configured
output module, guard and move a retained input, guard and change PROFINET
identity, bind the replacement's generated address child, recover from one
observed Siemens placement displacement, and independently verify the result.
The skill remained frozen. Its new read-only fixture passed positive, exact
inverse, live late-collision rollback, reattachment readback, and clean-compile
proof in `reports/ott-25-gold-audit-20260803.md`. Separate versioned manifests
define a one-trial V4 Free endpoint smoke, full three-trial V4 Free and paid V4
campaigns, and a three-trial Sol maximum-effort ceiling.

The free smoke ended endpoint-invalid on a queue-full 503 and was excluded
from model comparison. Paid V4 at its `xhigh` ceiling completed and verified
all three final states but remained 0/3 at 60/60/80 because of extra retries
and, in two trials, missing identity guards. It cost $0.26399478 in the formal
campaign. Sol at `max` qualified 3/3 at 100/100/100 with the exact three-call
patch sequence and zero reported subscription cost. The trace audit, exact
efforts, per-task prices, validator telemetry, and a suite-aggregation bug
found and fixed during execution are in
`reports/hardware-brownfield-modernization-sol-v4-20260803.md`.

`ott-26` crosses the vendor boundary with an Endress+Hauser Promag 100
PROFINET GSDML. It reuses the read-only topology fixture but requires the
installation-scoped 2015 Promag catalog package. The task must preserve the
DAP's generated process modules, independently learn two displaced totalizer
submodule placements, chain their actual targets by result reference, wire the
station to the existing subnet and IO system, assign selected input/output
starts, and guarded-write then reread the documented installation-direction
record. The first live gold audit proved the complete two-stage preview,
positive apply, exact delete inverse, and zero-error/zero-warning compile;
the versioned audit is in `reports/ott-26-gold-audit-20260803.md`.
The first frozen model campaign is
`campaigns/hardware-gsdml-promag-deepseek-v4-flash-paid-xhigh-20260803.json`.
The first paid V4 pilot recorded 45, 10, and 70 for $0.564401660, but those
scores are withdrawn from model comparison. Its frozen case incorrectly
required `can-plug-hardware-item` against a station parent that did not exist
until the atomic patch created it. The corrected case forbids that impossible
probe and uses the complete dry-run as the generated-parent capability check;
the generic hardware skill now documents the same lifecycle boundary. The
historical campaign and correction audit is in
`reports/hardware-gsdml-promag-v4-20260803.md`.
The corrected GPT-5.6 Sol `max` control scored 80, 75, and 80. A frozen
five-line generic skill clarification naming the exact guarded GSD fields
improved the A/B candidate to 80, 100, and 80, removed all structural-validator
interventions, and reduced mean duration and reasoning use. Sol remains
disqualified overall because only 1/3 candidate trials qualified. The invalid
pilot, contract repair, valid A/B campaigns, trace audit, and cleanup are in
`reports/hardware-gsdml-promag-sol-max-20260803.md`.
The repaired OpenCode free endpoint then completed a corrected DeepSeek V4
Flash Free `max` campaign without provider failures. It scored 80, 80, and 90,
produced the requested final hardware state in all three trials, and cost $0,
but remained disqualified at 0/3 because every trace added at least one
unnecessary partial discovery or GSD-target recovery attempt. The skill already
contains the violated generic atomic-batch rules, so this result did not trigger
another skill edit. The frozen campaign, Sol comparison, endpoint evidence, and
trace audit are in
`reports/hardware-gsdml-promag-v4-free-max-20260805.md`.

`ott-27` raises that vendor-transfer boundary with an IFM AL1400 IO-Link
master from a different installed GSDML package. The case must distinguish the
current package from legacy and no-PQI catalog distractors, preserve the
generated four-port module and proxy, independently recover two displaced port
placements, bind five input/output address objects across generated and plugged
items, and guarded-write then reread a vendor port-options byte. The frozen
skills were not edited during design. The live gold audit proved the strict
two-stage preview, unchanged apply, independent readback, forced rollback,
exact delete inverse, and zero-error/zero-warning compile; see
`reports/ott-27-gold-audit-20260901.md`.

`ott-28` exercises WinCC Unified discrete-alarm reconciliation on a dedicated
fixture whose trigger and parameter sources are internal HMI tags. It requires
a hash-bound partial update, computed allocation across occupied alarm IDs and
trigger bits, one atomic update/create preview and apply, exact-name guarded
retirement with cross-reference review, and fresh proof that a near-name
distractor survived. The live gold path, forced second-operation rollback, and
immutable fixture identity are recorded in
`reports/ott-28-gold-audit-20260904.md`.

Its first model campaign is frozen in
`campaigns/hmi-alarm-reconciliation-glm-5-3-high-20260904.json`. GLM-5.3 High
completed three valid trials and, after an evidence-preserving evaluator
correction for equivalent full-scan and exact-path HMI-tag discovery, passed
3/3 at 100. DeepSeek V4 Flash Free maximum-effort preflight was attempted first
but the refreshed OpenCode catalog no longer advertised that model. See
`reports/hmi-alarm-reconciliation-glm-5-3-high-20260904.md`.

A same-model verification after the generic tag-filter scope clarification
again passed 3/3 at 100 and reduced filter-only safety rejections from 3 to 0.
The three trials independently chose scoped, bounded-filtered, and exact-path
tag discovery rather than converging on a fixture-specific recipe. See
`reports/hmi-alarm-filter-skill-verification-20260905.md`.

`ott-29` opens the Unified trend-logging domain on a dedicated fixture. It
requires the agent to interpret a mixed blocked/planned deletion preview,
reconcile two related tags into one shared archive, retire an association-bound
logging tag, delete three archives only after their references are gone, and
then prove both the new active references and retired-archive absence. The
immutable fixture identity and independently executed gold path are in
`reports/ott-29-gold-audit-20260905.md`.

Its first three-trial campaign is frozen in
`campaigns/hmi-trend-reconciliation-glm-5-3-flash-high-20260905.json` and uses
subscription-backed `zai-coding-plan/glm-5.3-flash` with declared reasoning
effort and OpenCode variant `high`. A GLM-5.3 High fallback is created and run
only if Flash fails for model capability; evidence of a skill-contract problem
stops the campaign before any skill edit.

That initial cohort scored 100/100/100 under the original rubric, but every
trace first made a filter-only `list-hmi-tags` call, hit the full-scan safety
guard, and recovered. The corrected rubric re-adjudicates those traces at
99/99/99 with a critical failure. After a generic skill clarification, the
same Flash High configuration passed 3/3 at 100 with zero filter-only safety
rejections. See `reports/hmi-trend-filter-skill-verification-20260905.md`.

`ott-30` opens native SCL authoring with a held-out UDT plus stateful FB task.
It requires per-target Create capability discovery, one UTF-8 BOM source, a
verified offline classification, one capability-bound `CleanCompile`
transaction, canonical evidence for both generated identities, independent
source/XML readback, fresh Modify discovery, and a clean final compile. The
rubric mechanically inspects the isolated source artifact as well as the MCP
trace so syntactically valid but behaviorally incomplete submissions cannot
pass on compile evidence alone. The independently executed TIA gold path is in
`reports/ott-30-gold-audit-20260905.md`.

Its first campaign is frozen in
`campaigns/native-scl-authoring-glm-5-3-flash-high-20260905.json` and runs
subscription-backed `zai-coding-plan/glm-5.3-flash` at declared effort and
variant `high`. GLM-5.3 High is reserved as the capability fallback. A generic
skill-contract problem stops the campaign before skill modification.

The completed Flash-to-GLM escalation and surgical skill/evaluator hardening
are reported in
`reports/native-scl-authoring-glm-5-3-high-20260909.md`. Under the final
evaluator, GLM-5.3 High scores 100/40/100: two clean passes and one genuine
critical retry-budget violation, so the model remains unqualified.

`ott-31` opens the VCI synchronization domain on a dedicated three-FC fixture.
It creates a per-trial workspace before discovering formats, maps exactly two
SCL blocks, and then proves both directions without a bulk shortcut: a stale
workspace file is replaced from TIA while a separately validated workspace
change is imported into TIA. The rubric inspects the mapped files, untouched
baseline, independent project readback, final mapping status, and an unmapped
distractor. The immutable fixture and independently executed positive and
policy-denied gold paths are recorded in
`reports/ott-31-gold-audit-20260930.md`.

The gold audit exposed one generic ordering constraint before model inference:
TIA V21 enumerates supported block formats through existing workspaces, so
format discovery returns an empty list when the project has none. The shipped
VCI skill now creates a reviewed new workspace before format discovery and
explicitly prevents treating that empty pre-workspace result as unsupported
target evidence. The first campaign is frozen in
`campaigns/vci-sync-glm-5-3-flash-high-20260930.json` with
subscription-backed `zai-coding-plan/glm-5.3-flash` at declared effort and
variant `high`. `campaigns/vci-sync-glm-5-3-high-20260930.json` is the
capability fallback and is run only when Flash fails for model capability.

The frozen three-trial Flash High cohort qualifies at 98/100/98 (mean 98.67),
with a mean duration of 271.177 seconds and $0 reported incremental subscription
cost. Both directions and independent preservation/readback checks passed;
the full GLM fallback was not run. One read-only overlap rejection recovered
safely. Pilot scorer corrections, exact settings, per-trial cost, independent
verification, and cleanup are documented in
`reports/vci-sync-glm-5-3-flash-high-20260930.md`.

`ott-32` opens the library workflow on a dedicated, embedded project-library
fixture. It selects approved PLC reuse among three same-leaf domain/legacy
distractors, previews and confirms one grouped copy, independently reads its
XML payload, preserves existing blocks, and reports an outdated UDT instance
without upgrading it. The fixture and positive/policy-denied gold paths are in
`reports/ott-32-gold-audit-20261001.md`. The audit also established the generic
hierarchy-plus-recursive-XML verification route for grouped copies; no toolkit
runtime fix or fixture-specific skill recipe was introduced.

The original `campaigns/library-reuse-glm-5-3-flash-high-20261001.json` is an
incomplete one-trial pilot (91, critical XML-readback failure), not a completed
qualification. A controlled fresh-state test proved that newly copied PLC
blocks need compilation before XML export; the generic skill was corrected.
The separate
`campaigns/library-compile-readback-glm-5-3-flash-high-20261001.json` cohort
qualifies GLM-5.3 Flash High in 3/3 fresh trials: 100/100/100 after correcting
one noncritical Markdown-summary false negative (original 99/100/100), mean
355.368 seconds, and $0 reported incremental subscription cost. All trials
had zero MCP errors, one reviewed copy, and unchanged preservation targets.
Settings, original evidence, generic guidance, scope limits, and cleanup are in
`reports/library-workflow-glm-5-3-flash-high-20261001.md`.
`campaigns/library-reuse-glm-5-3-high-20261001.json` is the GLM-5.3 High
capability fallback; it was not needed or run.
The intentional outdated-instance warning is accepted as baseline state:
incremental compilation may report 0 or 1 warnings, while a fresh update check
must still prove the old instance is retained.

`ott-33` adds structured S7DCL data authoring on a dedicated fixture: four
initialization-only changes to an address-sensitive existing DB plus a nested,
array/UDT-backed new DB in one guarded transaction. The
[`gold audit`](reports/ott-33-gold-audit-20261001.md) proves successful import,
complete preservation, and exact rollback after a real second-operation failure.
Unchanged skills yielded Flash High scores 100/97/100 (2/3 strict passes;
one omitted instance-selection step), followed by full GLM-5.3 High qualification
at 100/100/100 (original 100/99/100; one noncritical summary wording correction).
All six engineering states matched independent gold and compiled cleanly.
Exact effort, per-model/task cost, immutable replay, scope limits, and cleanup
are in [`the qualification report`](reports/s7dcl-data-authoring-glm-high-20261001.md).

`ott-34` adds brownfield LAD editing on a dedicated five-network FB fixture.
The task changes a shared-interlock Auto/Jog request and an existing static TON
while preserving the interface, timer state, set/reset ordering, network resources,
and unrelated blocks. One Implementation-only, baseline-bound BodyOnly transaction
is required. The [`gold audit`](reports/ott-34-gold-audit-20261001.md) records the
successful live edit and a deliberately failing compile with exact rollback.

Two restricted, fail-closed XML assertions evaluate independently exported LAD:
`ladBooleanNetwork` checks every combinational input row through actual power-flow
wiring; `ladTimerNetwork` checks the static receiver, IN/Q wiring and typed PT.
They accept ID renaming, commuted branches/guards and equivalent supported TIME
literals, and reject unsupported or unconsumed graph content. They do not simulate
PLC scans or establish runtime timer behavior or safety-controller suitability.
The first cohort is GLM-5.3 Flash High in three fresh, checkpointed trials;
full GLM-5.3 High is reserved for audited capability failures.

The first campaigns produced correct gold-matching LAD states in all six runs,
but neither is a clean strict qualification: Flash High is 100/100/96 (one
missing-BOM contract failure), and full GLM High is 100/100/98 after a
single/bulk ordering-scorer correction (original 100/100/96). Its last trial
hit a confirmed toolkit inconsistency: bulk export skipped OBs locally while
single-block Main SD export succeeded. The full-model failure was not clean
evidence of a LAD capability ceiling. The
[`campaign audit`](reports/lad-brownfield-glm-high-20261001.md) records costs,
effort, independent verification, immutable replay, the invalid server-error
attempt, and cleanup.

The generic bulk export path now uses the same per-target API as single export.
A separate post-fix full GLM-5.3 High cohort passed 100/100/100, with no MCP
rejections and three independent live audits. Its mean time was 382.096 seconds
and reported incremental subscription cost was $0. The prompt, skills, and
grader stayed unchanged; Flash was not rerun and retains its historical 2/3
strict pass rate. The
[`post-fix report`](reports/lad-brownfield-bulk-export-fix-20261001.md) records
generic regression tests, direct bulk/single export controls, exact runtime
hashes, reasoning effort, and the limits of the qualification claim.

The campaign declares `trialCount: 3`. Each model/task pair is executed three
times from a fresh fixture restore. Qualification status remains strict:
runtime or critical failures take precedence, and a model qualifies only when
every expected trial passes. The aggregates remain available when a model does
not qualify so variance and failure rates can still be compared.

Reasoning policy is explicit per model. The initial paid cohort uses
`reasoningEffort: "provider-default"` with `variant: null`, preserving its
out-of-box behavior. Free controls and ceiling campaigns pin the highest
variant advertised by the refreshed OpenCode catalog. A ceiling label is not
assumed: DeepSeek V4 Flash currently tops out at `xhigh`, while Luna, Sol, and
DeepSeek V4 Flash Free advertise `max`. Preflight rejects an explicit variant
that the selected model does not advertise.

The campaign preflight refreshes OpenCode's model catalog and records each model's status, advertised tool-call capability, context limits, input/output pricing, and provider credential type. A model declared `free` or `subscription` is rejected if the refreshed catalog reports a non-zero base input or output price.

OpenCode Desktop and the CLI share the user auth file. Pass `-UseUserOpenCodeAuth` to project only the selected model provider into an isolated per-run auth file. The temporary auth file contains no unrelated provider credentials and is overwritten and deleted after OpenCode exits. Credential values and the source auth path are not written to benchmark artifacts.

Run the paired skill-lift preflight without touching TIA:

```powershell
.\scripts\Invoke-OpenCodeCampaign.ps1 `
  -CampaignPath .\campaigns\hardware-skill-lift-glm-5-3-flash-high-20260830.json `
  -UseUserOpenCodeAuth `
  -PreflightOnly
```

Run the complete paired campaign from a non-primary branch:

```powershell
.\scripts\Invoke-OpenCodeCampaign.ps1 `
  -CampaignPath .\campaigns\hardware-skill-lift-glm-5-3-flash-high-20260830.json `
  -UseUserOpenCodeAuth `
  -FixtureArchivePath E:\Documents\OpenTiaBench\Archives\OTT_Benchmark_V21_20260731.zap21
```

For long standard model campaigns, run a bounded checkpoint batch instead of
one uninterrupted campaign. The default batch size is three accepted trials;
every attempt is recorded before the next trial starts:

```powershell
.\scripts\Invoke-OpenCodeCampaignBatch.ps1 `
  -CampaignPath .\campaigns\hardware-gsdml-ifm-glm-5-3-high-20260902.json `
  -FixtureArchivePath E:\Documents\OpenTiaBench\Archives\OTT_Benchmark_V21_Topology_20260801.zap21 `
  -UseUserOpenCodeAuth `
  -TiaInstanceId <pid> `
  -BatchSize 3
```

The command stops with `paused-batch-limit` after the configured number of
accepted trials. Continue from the exact next model/case/trial with the emitted
checkpoint path:

```powershell
.\scripts\Invoke-OpenCodeCampaignBatch.ps1 `
  -CampaignPath .\campaigns\hardware-gsdml-ifm-glm-5-3-high-20260902.json `
  -FixtureArchivePath E:\Documents\OpenTiaBench\Archives\OTT_Benchmark_V21_Topology_20260801.zap21 `
  -UseUserOpenCodeAuth `
  -TiaInstanceId <pid> `
  -CheckpointPath .\artifacts\<batch-directory>\checkpoint.json `
  -BatchSize 3
```

HTTP 429 and documented Z.AI quota/concurrency business codes are classified
as `rate-limit`. The batch stops immediately with `paused-rate-limit`, retains
the rejected attempt as endpoint evidence, leaves that trial pending, and
records the provider message, status code, and reset timestamp when present.
Do not resume until that timestamp or the provider dashboard shows available
quota. Generic provider failures and harness failures also pause instead of
consuming the remainder of the batch. Paired skill-evaluation arms remain on
the existing campaign runner and are intentionally rejected by the batch
driver for now.

Each trial scopes `OPEN_TIA_MCP_ALLOWED_ROOTS` to its exact isolated workspace
and the OpenTiaToolkit temporary staging directory. Before model inference, a
raw MCP policy probe validates a workspace artifact. A rejection of that exact
workspace is classified as `harness-error`, excluded from model comparison,
and pauses a checkpoint batch as `paused-harness-error`; a model-selected path
outside the declared workspace remains an ordinary model outcome.

Use `-CaseId` for an ordered subset during a smoke or continuation run. A
skill-evaluation definition still retains all four routing buckets; filtering
only narrows execution and the resulting paired statistics.

The `subscription` cost class means OpenCode reports zero monetary request
cost while the provider consumes an external plan allowance. It is distinct
from `free`: cost-per-pass remains zero in the JSON aggregates, but it is not
an economic price and the provider's remaining plan quota is not available to
the harness. Subscription and paid campaigns require
`-UseUserOpenCodeAuth`; only paid campaigns additionally require
`-AllowPaidModels`.

Run catalog and credential preflight without touching TIA:

```powershell
.\scripts\Invoke-OpenCodeCampaign.ps1 `
  -CampaignPath .\campaigns\initial-model-qualification.json `
  -UseUserOpenCodeAuth `
  -AllowPaidModels `
  -PreflightOnly
```

Run the live qualification campaign:

```powershell
.\scripts\Invoke-OpenCodeCampaign.ps1 `
  -CampaignPath .\campaigns\initial-model-qualification.json `
  -UseUserOpenCodeAuth `
  -AllowPaidModels `
  -FixtureArchivePath E:\Documents\OpenTiaBench\Archives\OTT_Benchmark_V21_20260731.zap21
```

Paid execution needs two independent opt-ins: `allowPaidModels=true` in the
versioned campaign and `-AllowPaidModels` at runtime. The campaign stops before
starting another trial once cumulative reported cost reaches `maxCostUsd`.
Because providers report cost after a request completes, this is a
between-trial stop guard rather than a hard per-request spending limit.

Use `-Model` or `-CaseId` to execute an ordered subset from the versioned campaign, for example when continuing after a deliberate runtime-failure stop. The filter cannot introduce models or cases that are absent from the campaign:

```powershell
.\scripts\Invoke-OpenCodeCampaign.ps1 `
  -CampaignPath .\campaigns\initial-model-qualification.json `
  -Model openrouter/openai/gpt-5.6-luna,opencode/deepseek-v4-flash-free `
  -UseUserOpenCodeAuth `
  -AllowPaidModels `
  -FixtureArchivePath E:\Documents\OpenTiaBench\Archives\OTT_Benchmark_V21_20260731.zap21
```

Merge continuation results into one qualification report:

```powershell
.\scripts\Merge-OpenCodeCampaignResults.ps1 `
  -CampaignResultPath <segment-1-result.json>,<segment-2-result.json> `
  -OutputPath .\artifacts\initial-model-qualification-merged.json
```

The merge rejects duplicate model/case results and derives one model status from runtime failures, critical failures, case completeness, and pass results.

If the top-level campaign process is interrupted after individual trials have
written their `result.json` files, rebuild a validated campaign summary without
rerunning completed work:

```powershell
.\scripts\Recover-OpenCodeCampaignResult.ps1 `
  -CampaignPath .\campaigns\initial-model-qualification.json `
  -ArtifactDirectory .\artifacts\<campaign-directory> `
  -OutputPath .\artifacts\<campaign-directory>\campaign-result.recovered.json
```

Recovery rejects results from unknown models or cases, duplicate
model/case/trial keys, out-of-range trials, mixed Git/OpenCode/fixture
baselines, and reasoning configurations that differ from the campaign.

## Safety model

- Mutable single-user cases always begin from the hash-pinned archive.
- Raw traces and scores live below `benchmarks/artifacts/`, which Git ignores.
- Model workspaces and restored TIA projects live below `C:\OTT\Bench` by default, outside the Git worktree. Execution directory names use a short model hash to stay below Siemens/Windows project path limits. Override that location with `-WorkspaceRoot`, but the runner rejects any path inside the repository.
- The runner refuses to execute from `main` or `master` unless `-AllowPrimaryBranch` is explicit.
- OpenCode cannot use shell, task, or web tools. Cases that must author an MCP input artifact may opt into edits confined to the isolated workspace.
- Only the skills declared by the case are visible to the agent.
- OpenCode config, data, cache, and state roots are unique per run; the repository path is not written into the model-visible config.
- User auth reuse is provider-scoped; temporary auth material remains outside Git and is removed in `finally`.
- Timeout cleanup targets only the exact OpenCode process tree started by the runner.
- The Multiuser case expects an honest UI handoff; public V21 Openness does not perform check-in.

Use `-FailOnCaseFailure` when a non-passing case should fail a CI job. `-TimeoutOverrideSeconds` exists for harness smoke tests; normal comparisons should use the versioned per-case timeout.
Use `-PrepareOnly` to test archive/session setup without launching a model.

## Case inventory

| Case | Capability | Expected behavior |
| --- | --- | --- |
| `ott-01` | Read-only orchestration | Attach, verify the project, and report exact devices without mutation |
| `ott-03` | Guarded PLC tag mutation | Preview, apply, independently read back, and compile |
| `ott-08` | Constrained SimaticML edit | Use a baseline-bound transaction and change only one authorized XML path |
| `ott-10` | Unified HMI editing | Focused snapshot, dry-run, apply with backup, and fresh verification |
| `ott-12` | Mixed-module address selection | Select output index 1 by observed I/O type, relocate it, and prove input index 0 stayed unchanged |
| `ott-13` | Hardware output relocation | Transfer guarded relocation and readback to an output address object |
| `ott-14` | Hardware collision | Detect the occupied range in dry-run and refuse to apply |
| `ott-15` | Hardware relocation | Guard, preview, apply, and independently verify a free I/O range |
| `ott-16` | Reversed hardware collision | Transfer collision refusal to the opposite target and owner |
| `ott-17` | Mixed I/O atomic rollback | Keep a valid input move and colliding output move in one transaction, then prove both rolled back |
| `ott-18` | Multiuser boundary | Save safely and state that check-in requires TIA Portal UI |
| `ott-19` | Computed dual-space compaction | Derive four contiguous starts across Input and Output spaces and apply them in one guarded transaction |
| `ott-20` | Cross-domain remap | Coordinate computed hardware moves with guarded PLC-tag address remapping and compilation |
| `ott-21` | Held-out cross-domain transfer | Transfer coordinated remapping to unrelated identities, descending hardware order, different ranges, and nonzero bit offsets |
| `ott-22` | ET 200SP topology provisioning | Discover catalog identifiers, create and populate a remote station, recover from displaced placement, wire PROFINET, and verify the final topology |
| `ott-23` | Held-out ET 200SP topology and addressing | Transfer atomic topology construction to different modules and nonadjacent positions while assigning IO starts through generated result-referenced children |
| `ott-24` | Held-out ET 200MP family transfer | Transfer the complete topology-and-addressing workflow to a different hardware family without guessing generated-child state |
| `ott-25` | Brownfield ET 200MP modernization | Atomically replace an existing output module, guard retained state and PROFINET identity, recover from displaced placement, and verify the modernized station |
| `ott-26` | Third-party GSDML provisioning | Provision an Endress+Hauser Promag 100, preserve generated modules, bind displaced result targets, assign IO, and guarded-write the vendor record |
| `ott-27` | Held-out cross-vendor GSDML transfer | Provision an IFM AL1400, reject package distractors, preserve generated proxy IO, bind two displaced ports and five address objects, and guarded-write the port-options record |
| `ott-28` | Unified discrete-alarm reconciliation | Hash-bind a partial update, compute a free ID and trigger bit, atomically create the new alarm, retire one exact unreferenced alarm, and preserve its near-name distractor |
| `ott-29` | Unified trend-logging reconciliation | Interpret mixed reference safety, move and create two tag associations in one shared archive, retire one logging tag and three obsolete archives, and prove the final active and absent state |
| `ott-30` | Native SCL multi-object authoring | Author a UDT and stateful FB in one source and transaction, prove the source semantics mechanically, and verify exact generated identities and clean compilation |
| `ott-31` | Bidirectional VCI block reconciliation | Create a workspace before format discovery, map two exact SCL blocks, prove both sync directions, validate the imported source, preserve an unmapped distractor, and verify Equal mappings plus a clean compile |
| `ott-32` | Approved library reuse and type-version restraint | Select the exact approved PLC master among same-leaf distractors, preview and confirm one grouped copy, read back its XML and preserved objects, and report the retained outdated UDT without upgrading it |
| `ott-33` | Structured S7DCL data authoring | Change four initializations without altering an address-sensitive DB layout, create a nested array/UDT-backed DB in the same atomic transaction, and prove complete canonical preservation and clean compilation |

## Adding a case

Copy an existing `cases/ott-*` directory, keep the prompt separate from `case.json`, and run the definition test. Prefer assertions against tool names, structured inputs, and tool outputs over stylistic final-answer checks. Give irreversible or unsafe conditions a critical assertion.

`toolOperationSet` compares operation arrays without imposing array or object
property order. Set `normalizeResultReferences: true` when `resultId` values
are agent-chosen local names: the evaluator then accepts consistent bijective
renaming across `resultId`/`targetRef` while still rejecting broken bindings.

`workspaceFile` resolves `relativePath` strictly below the recorded isolated
workspace and rejects rooted or traversal paths. Use its size, UTF-8 BOM,
required-pattern, and forbidden-pattern checks when the task's correctness
depends on an authored artifact that tool outputs cannot fully describe.
