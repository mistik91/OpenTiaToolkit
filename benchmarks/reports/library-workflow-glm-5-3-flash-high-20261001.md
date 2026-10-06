# PASS — project-library reuse GLM-5.3 Flash High verification

Date: 2026-10-01

## Outcome

GLM-5.3 Flash High qualifies for `ott-32`: three fresh trials passed all
critical assertions. Original scores were 99/100/100; a one-point Markdown
summary false negative was corrected after the cohort finished, yielding
100/100/100 without changing model output or engineering evidence. Mean
duration was 355.368 seconds (5 minutes 55 seconds). The GLM-5.3 High
capability fallback was not needed or run.

This qualifies approved PLC master-copy reuse and inspection-only library
type-version reporting in this bounded case. It does not qualify library
upgrades, global-library editing, HMI master reuse, collision recovery, or the
whole library domain. The root-only PLC browse/source-export limitation
remains a toolkit issue, not a claimed runtime fix.

## Configuration

- Branch: `codex/library-workflow-benchmark`
- Inference commit: `7c67ebb4c6c955cd4d545b8bd33b649d2c1feeab`
- Campaign: `library-compile-readback-glm-5-3-flash-high-20261001`
- Case / fixture: `ott-32` / `tia-v21-library-11`
- Model: `zai-coding-plan/glm-5.3-flash`
- Reasoning effort / OpenCode variant: `high` / `high`
- OpenCode: pinned `1.18.10`
- Authentication: isolated provider-scoped reuse of the user's z.ai subscription
- TIA Portal: V21 Update 2; disposable `OTT_Benchmark` restores only
- Archive SHA-256:
  `B749366ECD7CFD0564FEC06881526373BF0DBE14F9BE9ECCD1043D0071166749`

All three runs recorded the same clean Git revision and immutable fixture
hash. Each used a fresh archive restore and separate filesystem workspace.
The checkpoint ran one trial first, then resumed for the remaining two; all
three completed without checkpoint retries, provider/harness failures, or
rate-limit pauses.

## Trials and cost

| Trial | Original / adjudicated score | Duration | Reasoning tokens | Reported cost |
| ---: | ---: | ---: | ---: | ---: |
| 1 | 99 / 100 | 350.500 s | 3,271 | $0 |
| 2 | 100 / 100 | 399.289 s | 3,197 | $0 |
| 3 | 100 / 100 | 316.315 s | 2,554 | $0 |
| Mean | 99.67 / 100 | 355.368 s | 3,007 | $0 |

Total reported incremental cost is $0. Subscription quota was consumed; this
is not a claim that the service is free. The runner does not measure remaining
z.ai subscription quota. There were zero MCP tool errors in every trial, and
exactly two master-copy calls per trial: one dry-run and one confirmed apply.
Each model compiled successfully before its first XML export, read the actual
grouped payload, and retained the outdated UDT instance. No duplicated copy,
global-library opening, unauthorized mutation, or save occurred.

## Excluded pilot and generic correction

The original campaign at `2064de2395cf54d802ff49aaab823d1f49deff9c`
ran only its first trial: score 91, critical XML-readback failure, duration
551.249 seconds, and $0 reported cost. Trials 2 and 3 were deliberately not
run. That incomplete pilot is not pooled with this qualification cohort.

The pilot copied the right master and ultimately compiled cleanly, but tried
XML export before compilation and never retried it afterwards. It recovered
the implementation through recursive SD document export, which did not meet
the required XML contract, then incorrectly attributed XML rejection to
grouped-block lookup. No post-trial diagnostic repaired its model workspace
or rescored that failure.

A [controlled fresh-state reproduction](library-compile-readback-preflight-20261001.md)
proved that the same known leaf XML export was rejected before compile and
succeeded afterwards. The generic library skill now distinguishes:

- Read-only global opening still changes the portal session and needs MCP
  confirmation.
- A successful master-copy dry-run proves path/target resolution, not source
  domain compatibility or permission to overwrite a collision.
- Grouped identity must be verified with the software tree; flat block reads
  and the native-source export precheck inspect root blocks only.
- Compile newly copied PLC software before recursive XML payload readback;
  stop on compile errors, use the known leaf name, and do not repeat the copy
  or infer unsupported grouped XML from a pre-compile rejection.

The canonical skill and all four packaged copies are synchronized. These
instructions contain no fixture names, constants, paths, versions, or task
solution. No production C# runtime or command surface was changed. The
fixture-only builder is not available to benchmarked models.

This is not a paired skill ablation. The evidence validates the generic
ordering correction and the final task; one excluded pilot cannot establish
a causal performance or latency improvement.

## Independent verification

The [gold audit](ott-32-gold-audit-20261001.md) independently established the
embedded library, same-leaf distractors, empty destination, dry-run and
policy-denied paths, exact approved copy, and baseline preservation.

For every completed trial, the five exported engineering XML nodes were
compared directly with the independent gold/baseline nodes: approved FC,
legacy FC, existing FC, `Main` OB, and `MyANALOG_LEVEL` type. All 15 comparisons
were identical, excluding only the export document metadata. The copied FC
retained its exact identity/interface and +10 implementation; the existing
FCs retained +20 and +30. Every before/after update report retained current
version 0.0.3 versus default 0.0.4 with `Out-of-date` status.

After trial 3, the separate repository MCP session reattached to the exact
campaign-owned project on PID 1768. Fresh hierarchical and library-update
reads independently reconfirmed its final state; fresh compilation returned
0 errors and 0 warnings. Four additional external XML exports matched the
gold nodes. These diagnostics were written outside the model workspace to
`C:\OpenTiaBench\Live\20261001-library-gold-03`.

The 5,701,716-byte library archive and its parent archive remained read-only
with their pinned hashes. Both source global-library data hashes remained
unchanged; the original library was not modified.

## Evaluator correction, validation, and artifacts

Trial 1 said the project was `**not** saved`. The original one-point final
summary matcher accepted only plain `not saved`, so it missed equivalent
CommonMark emphasis. Only that noncritical text matcher was relaxed; a
dedicated positive regression now covers the phrasing. All engineering,
safety, artifact, and critical checks remained unchanged. Original result
backups, traces, metadata, segment summaries, and the original merged summary
are preserved. Re-adjudication used the harness's Windows PowerShell entry
point and retained the original ISO UTC timestamps and inference revision.

- Full `npm test`: passed; 25 cases, 57 campaigns, 12 fixtures.
- Library scorer: 3 positive traces and 16 rejected negative controls.
- Canonical skill quick validation: passed; all packaged skills: zero drift.
- Repository hygiene and `git diff --check`: passed.
- Runtime rebuild/integration suites were not repeated: no production runtime
  change was made; fixture construction and changed skill behavior were live-tested.

Ignored qualification artifact root:

`benchmarks/artifacts/20261001T055240326Z-batch-library-compile-readback-glm-5-3-flash-high-20261001`

`checkpoint.json` records completion; `merged-campaign-result.json` retains
99/100/100. Each trial's `result.pre-summary-fix.json` retains its original
score, and `campaign-result.re-adjudicated.json` records 100/100/100.

Excluded pilot artifact root:

`benchmarks/artifacts/20261001T053844009Z-batch-library-reuse-glm-5-3-flash-high-20261001`

The final disposable project was closed without saving and campaign TIA
PID 1768 exited. Fresh instance discovery found no TIA portals; no
campaign-owned OpenCode/MCP/worker remained. The pre-existing repository
Debug MCP/worker and Siemens Project Server were preserved. No push, PR, or
merge was performed.
