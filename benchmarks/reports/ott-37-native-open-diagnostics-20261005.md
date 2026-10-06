# OTT-37: native session-open diagnostics checkpoint

## Outcome

**At the non-live checkpoint: PASS for diagnostic implementation and non-live
validation; BLOCKED for the fresh Agent TIA experiment until the rebuilt Release
worker is registered.** The subsequent [live qualification](ott-37-native-open-live-20261005.md)
cleared that gate and passed fresh/reused Agent experiments. The historical
checkpoint below records the state before registration and live testing.
The original disposed LocalSession root cause remains unproven. No model trials,
skill changes, fresh TIA launches, project edits, license/security changes, push,
PR or merge were performed during this slice.

Work remains on `codex/ott-37-multiuser-bootstrap`, following persistence
checkpoint `a53c279`. The earlier human check-in and exact fresh server readback
remain qualified; this diagnostic checkpoint does not invalidate or extend that
proof. See [the qualification report](ott-37-stdio-fixture-checkpoint-20261005.md).

## Changes

- Local-session failures now identify the native `open-local-session` call,
  subsequent `read-local-session-project`, or state selection step. No automatic
  retry, ownership change, fallback authentication or session cleanup policy was
  added. Full exception objects still go to the worker logger.
- The host continuously drains redirected worker stderr. Previously it read
  stderr only after an already-exited worker; ordinary failures lost useful
  vendor diagnostics, and enough logging could fill the undrained pipe. A bounded
  tail retains at most 65,536 characters without forwarding normal logs.
- `OPEN_TIA_WORKER_FORWARD_STDERR=true` explicitly forwards the full diagnostic
  stream to host stderr, never protocol stdout. A failed optional sink does not
  stop draining. Stream-teardown error messages are not copied into the tail.
- The Agent relay preserves this explicitly allowed diagnostic setting without
  inheriting provider credentials or coordinator profile state. The deterministic
  probe enables it only with `-CaptureWorkerDiagnostics`; otherwise it explicitly
  disables forwarding. Diagnostic logs are private operator evidence, not model
  prompt input.

## Validation

- Focused Release tests: 16 passing, including large stderr, bounded tail, exact
  forwarding, draining before EOF, failed sink, failed reader, staged native-open
  rejection, staged session-project rejection and attached user ownership.
- Full Release non-integration tests: 774 passing.
- Release architecture tests: 54 passing; both Application target frameworks
  build successfully.
- Worker IPC tests: seven passing, including diagnostic forwarding on/off while
  a typed input rejection remains intact over IPC. These controls do not attach
  to TIA or modify a project.
- Release worker build: zero warnings/errors.
- MCP smoke with diagnostics off and explicitly on: passing modern/downlevel
  protocol, catalog parity, structured/text outcomes, denied/error paths,
  protocol/stderr separation and native shutdown. Observed 186 runtime commands
  and three infrastructure tools. No live project flag was used.
- Full benchmark `npm test`: passing; 29 cases, 72 campaigns and 15 fixtures
  remain unchanged.
- Credential/bootstrap controls: 47 passing; relay controls: six passing;
  exact probe assertions: eight passing. These use no real account logon or TIA.
- Generated reference: current, 186 commands. Skill packaging: zero drift.
  Repository hygiene and diff checks: passing.

TRX evidence:
`C:/OpenTiaBench/Artifacts/ott37-native-open-diagnostics-20261005/tests/`.

## Required live-test gate

The current shell is not elevated. Read-only registry comparison found no exact
matching entry in either the general allow-list or V21 versioned whitelist for:

`C:/src/OpenTiaToolkit/src/OpenTiaToolkit.Worker.V21/bin/Release/net48/OpenTiaToolkit.Worker.V21.exe`

SHA-256: `BC32FFCF1882A2FBEB3C5F2F8EF28F14B673468DB1E31C1D92E47A4A67616C2E`.
UTC file timestamp: `2026/10/05 11:45:49.829`.

The development workflow therefore blocks live attachment/opening with this
rebuilt worker until registration. No allow-list write was attempted from the
non-elevated shell, and no security dialog was used to work around that gate.
Run the existing `scripts/Register-OpennessAllowList.ps1` as Administrator for
the exact Release executable and `-PortalVersion 21.0`. Do not register CLI/MCP
hosts or rebuild again between registration and the experiment.

Then recheck registration and worker identity, use read-only discovery, and
launch a uniquely owned fresh Agent TIA instance with the existing stored
credential. Keep create/open/status/compile/export/save/close serial in one MCP
worker, using a new disposable local session from the qualified server project.
Capture full worker diagnostics and compare fresh versus reused instances.
Stop at a disposed session or certificate/license handoff; do not guess a
fresh-per-trial recovery policy before this experiment passes.

## Preservation and cleanup

The baseline archive SHA-256 remains
`8E6F2738A6332E6741599512745D859A6655AAA3CF4BAB27DDAADD99CD8A8CF2`.
The pre-existing app Debug MCP/worker and all four retained user-owned TIA
processes were preserved. No diagnostic-owned MCP/worker remains. Registration,
fresh/warm native-open comparison, root-cause correction and live validation
remain pending; this checkpoint is not merge-ready.
