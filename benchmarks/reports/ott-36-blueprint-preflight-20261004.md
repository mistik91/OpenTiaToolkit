# OTT-36 engineering-blueprint preflight — 2026-10-04

## Outcome: BLOCKED by a toolkit completion-reporting defect

Preparation is isolated on `codex/ott-36-blueprint-benchmark`, based on clean,
remote-aligned `dcbc09a25643af9d0417cfc45ab0be7586d7c06f` (`master`, PR #40).
No skills or compiled runtime code were changed. No model campaign, credential
access, push, PR, or merge was performed. OTT-36 is not yet registered as an
executable benchmark: qualification and the future scorer must pass first.

The intended coverage is a related PLC metadata/HMI operator-label change through
validated, reviewed blueprints, with stale-context rejection, one-use tokens,
stop-on-failure, fresh recovery planning, independent readback and preservation.
Blueprint orchestration is deliberately **not** treated as an atomic cross-domain
transaction. No runtime PLC/HMI tag connection is claimed by these metadata edits.

## Independently reproduced defect

The disposable three-step control is:

1. Change `OccupiedBefore.Comment` to `Pending operator label`.
2. Attempt a focused `Overview/StartButton.Text=Run` HMI patch with a deliberately
   non-matching base hash. This must be a non-mutating safety block.
3. Change `OccupiedAfter.Comment` to `Unexpected later step`, a sentinel that must
   not run with `continueOnError=false`.

The HMI command intentionally returns transport `success=true` with structured
`SafetyBlocked=true`, `Applied=false`, and recovery guidance. This existing
standalone contract is correct: an expected safety rejection is not a protocol
failure.

`BlueprintCoordinator.ApplyAsync` only reads `CommandOutcome.Success`; it drops
the structured step data when building `BlueprintStepOutcome`. Consequently it
marks the blocked HMI step successful, executes the sentinel, and returns
`Applied=true` / `Blueprint applied.` despite the missing HMI mutation.

| Independent observation | Actual result |
| --- | --- |
| Closing the project between plan and apply | Old token correctly rejected as stale; fresh tag/HMI reads unchanged |
| Standalone stale-hash HMI dry-run | Transport success, `SafetyBlocked=true`, `Applied=false`, `re-read-screen` guidance |
| Blueprint failure control | All three steps reported successful; `Applied=true` |
| Fresh tag export after blueprint | Both prefix and sentinel comments changed |
| Fresh focused HMI export | Label remained `Start`; geometry and complete focused screen unchanged |
| Independent fresh-worker health restore | Original two tags, HMI baseline and 0-error/0-warning compilation verified |

This is a toolkit defect, not a model or skill failure. We did not substitute an
ordinary command error for the safety block just to make the control pass.
Recovery and replay assertions after the failed gate remain **unrun**, not passed.

## Live evidence and environment

Each run used its own Release MCP/worker, owned headless TIA instance and new
archive restore. No user TIA instance was running at initial discovery.

- `ott36-gold-20261004-a`: initial reproduction, stopped at the incorrect blueprint
  completion assertion; disposed project without saving and closed the owned TIA.
- `ott36-control-20261004-b`: independent repeat with direct guard proof and native
  post-failure tag/screen exports. Plan ID `63b230346af24f23abeab05d06c368fb`.
- `ott36-health-20261004-c`: new worker/TIA and independent archive restore;
  `PASS_HEALTH_ONLY`, not blueprint qualification.

Evidence roots are `E:/Documents/OpenTiaBench/Artifacts/<run-name>/`; disposable
projects are under `C:/OTT/Live/<run-name>/`. The repeat retains `mcp.jsonl`,
`guard-preview.json`, `stop-on-failure-result.json`, phase-specific native exports,
`failure.json` and `cleanup.json`. Journals replace bearer tokens with stable
SHA-256 markers; no raw apply tokens are persisted by the qualification script.

Fixture: `tia-v21-baseline-1`,
`OTT_Benchmark_V21_20260731.zap21`, 5,582,162 bytes, SHA-256
`1C17F223F00F0A43EFD5E51A7C8E2DBE4DD424540AE0C9A799F102997205AB74`.
The source archive hash remained unchanged after every run. The focused HMI hash
before, after stale rejection, after the incorrectly continued blueprint, and in
the independent health restore was
`oVX3KE1qt9mS5oY0SjNxwNXVyybxzp9MvhKWNrKZSyM=`.
Independent Main baseline exports also match after removing only document-level
export metadata: SHA-256
`66337a2def7a9405f0f35458b7a13d05985fa7097b696ec6f394d4823bcf31bb`.

The current Release worker was already Siemens-registered, SHA-256
`9C6C2BF37A4E49468232537D4336B16B5AE748B506E1B588E47F9AF1AED12C23`.
No rebuild or privileged registry write was needed. The desktop MCP initially
launched an older, unregistered Debug worker and stalled on startup. Its pending
operation was cancelled and the exact empty campaign-owned TIA process was
stopped. The app's MCP configuration was not edited; qualification then used the
registered Release worker explicitly.

## Validation and next gate

- The qualification script ran under Windows PowerShell 5.1; PowerShell 7 parsing
  also passed. Both live defect runs failed at the intended completion gate.
- Independent health restore compiled at 0 errors / 0 warnings.
- Current Release non-integration tests: 733 passed; architecture tests: 54 passed.
  These were `--no-build` reruns; no new compiled build is claimed.
- Canonical/plugin skill drift: zero. Generated command reference: 186, current.
- The final script's exact worker-registration guard passed, and its XML hash
  helper matched all three independent Main baseline exports.
- Repository hygiene: clean. Full `npm test` passed: 28 registered cases,
  70 campaigns, 15 fixtures, and all existing synthetic/adversarial suites. OTT-36
  was deliberately not counted as a registered case. The initial launcher attempt
  failed because inherited PowerShell 7 module paths hid Windows PowerShell's
  `Get-FileHash`; a process-scoped native Windows PowerShell module path fixed the
  environment for the successful rerun. No global setting was changed.
- Fresh-state checks after the stop-on-failure gate and final Main/HMI/tag
  preservation checks are prepared but cannot qualify until the coordinator is
  corrected. No full positive blueprint path or OTT-36 scorer pass is claimed.
- Model trials: zero; provider spend/quota: zero.

The next authorized decision is a surgical **generic toolkit** correction:
blueprint step completion must honor structured mutation/safety outcomes instead
of equating transport success with an applied engineering change. Keep standalone
HMI safety-block semantics intact, stop the suffix when `continueOnError=false`,
and never claim a blocked overall blueprint applied even when continuation is
explicitly enabled. Add vendor-free regression controls, repeat this independent
live control and recovery path, then finish the scorer and Flash High campaign.
There is currently no evidence requiring a skill change.
