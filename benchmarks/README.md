# OpenCode benchmark slice

This directory contains the first executable OpenTiaToolkit agent benchmark slice. It is intentionally small: fifteen cases cover discovery, guarded PLC and HMI mutation, constrained PLC code editing, hardware relocation and rollback behavior, computed multi-row planning, cross-domain remapping and transfer, catalog-driven hardware topology provisioning, and an unsupported Multiuser boundary.

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
- the one-based trial index for repeated model/task executions;
- deterministic trace and final-answer assertions.

Every invocation also writes a suite summary with mean score, pass count,
duration, cost, and links to the individual result files. Campaign and merged
reports retain every trial and aggregate each model/task combination with score
range, pass rate, critical-failure rate, runtime-failure count, mean and median
duration, mean reasoning tokens, total and mean cost, and cost per passing
trial.

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

## Adding a case

Copy an existing `cases/ott-*` directory, keep the prompt separate from `case.json`, and run the definition test. Prefer assertions against tool names, structured inputs, and tool outputs over stylistic final-answer checks. Give irreversible or unsafe conditions a critical assertion.
