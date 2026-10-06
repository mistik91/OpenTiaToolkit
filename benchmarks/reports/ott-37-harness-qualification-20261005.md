# OTT-37 identity-aware harness qualification

## Outcome

PASS: a newly named disposable server project was restored from the immutable
archive, opened natively under Agent, edited and explicitly saved, checked in by
the operator through TIA, and verified through a fresh coordinator session.
This is deterministic harness qualification, not a model trial or an automated
Openness check-in. No skill changes, worker rebuild, TLS bypass, restart policy,
download or online operation were used.

The earlier disposed LocalSession error was not reproduced. Its cause remains
unproven; see [fresh/reused native-open qualification](ott-37-native-open-live-20261005.md).
Work remains on `codex/ott-37-multiuser-bootstrap`; no merge is implied.

## Qualified fixture and boundaries

- Immutable archive SHA-256:
  `8E6F2738A6332E6741599512745D859A6655AAA3CF4BAB27DDAADD99CD8A8CF2`.
- Exact registered Release worker SHA-256:
  `BC32FFCF1882A2FBEB3C5F2F8EF28F14B673468DB1E31C1D92E47A4A67616C2E`.
- Acceptance contract SHA-256:
  `43216c1284f0b73a772735eb70d28d3a6258a1c7bd0e26dc91a1d157d12b65c9`.
- Agent TIA PID 22824 and coordinator PID 11612 retained their distinct owners.
- New server project: `OTT37_20261005125228_05085a11`; Agent session ID 1.
- Fresh verification session: coordinator-owned, newly created from this server
  project, with a different session ID/path. Both sessions were safely closed.

Only PLC_Process/PumpEnable changed: `%M20.0` to `%M24.0`, and comment to
`Process pump enable - commissioning revision A`. Its name, Bool type and default
tag table remained unchanged. All five exact tag rows, both complete block
inventories, both full Main XML documents and both canonical hardware trees were
verified. XML normalization removes only DocumentInfo/Created. Baseline, edited
local and fresh server software each compiled with zero errors and warnings.

Native evidence is retained outside Git/model workspace under
`C:/OTT/Bench/20261005T125227724Z-ott-37-t1-FEEA3D4C/multiuser-evidence/`:
readiness, publish, baseline, golden-run, independent readback, post-ui-owner,
server-verification, prepared/handoff/completion and golden-validation files.
Proofs bind native journals, identities, clean worker shutdown and exported files
by SHA-256. Completion proof replay also passes. Operator UI check-in is recorded
separately from the model's scored local-save phase.

## Problems found before inference

1. The coordinator could not inspect Agent's private Documents directory. Setup
   stopped before any project mutation. Directory preparation now runs as Agent
   with a loaded profile, sanitized environment and the existing DPAPI secret;
   it does not broaden profile ACLs or pass a password in arguments.
2. A deliberately long external execution root exceeded Siemens' 143-character
   project-directory limit during retrieve. Native rejection was retained; no
   project was published. The harness now preflights length before provisioning
   and uses the short default `C:/OTT/Bench`.
3. Coordinator Documents is redirected outside its profile. Fresh verification
   uses an explicit profile-local Documents/OpenTiaSessions directory instead.
   A completed, proven owner-close phase can be resumed without reissuing a
   failed vendor operation or discarding pending work.

All attempts and successful setup/readback remain independently inspectable.
Existing server projects were not edited, overwritten or deleted.

## Scoring and campaign behavior

OTT-37 scores separate-account context verification, exact preview/apply, clean
compilation of both PLCs, explicit local save, a sole owned marking, preservation
and an honest pending UI handoff. It does not award server persistence merely
because the model saved locally. Native provisioning failure stops before
inference. Native readback outages are harness-invalid; successful native
responses showing wrong context/markings are model failures.

Each identity-bound trial pauses the campaign. Checkpoint resume requires the
same handoff's completed, hash-bound fresh-server proof. Human check-in and
native setup/readback time are excluded from model inference time and cost.
GLM-5.3 Flash High is the first configured subscription campaign (three trials),
with no paid API fallback authorized by that campaign definition.

## Regression coverage

- Local-save oracle: structured/text-envelope positives, 43 adversarial controls
  and full scorer pass/model-failure/harness-invalid checks.
- Setup: one valid input plan, 11 safety controls and three independent readback
  failure classifications, without credentials or TIA.
- Batch checkpoint planning preserves a pending UI handoff.
- Full benchmark regression suite, definition validation, PowerShell parsing,
  repository hygiene, skill-package drift and unchanged worker/archive checks
  accompany this implementation. The existing Release non-integration suite
  passes 774 tests and architecture passes 54; no C# rebuild was needed.

The bundled PowerShell environment initially gave npm's child Windows PowerShell
an incompatible module search path (Get-FileHash unavailable). The full npm test
run uses the native Windows PowerShell module directory; this is a validation
environment adjustment, not a model/provider failure or a benchmark skill change.

This qualifies one live fixture and human handoff on this workstation, not
arbitrary hosts, certificate/license states, collision races or Multiuser
conflict resolution. Those are separate future cases. No model performance
conclusion follows from deterministic qualification.
