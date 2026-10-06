# OTT-37 preparation: separate-account bootstrap

## Outcome and scope

**PASS for credential/identity/bootstrap readiness; OTT-37 is not yet qualified.**
Work is isolated on `codex/ott-37-multiuser-bootstrap`, based on merged commit
`b5afabe0bc08977f85070a61b3dac870521ceb0b`. No model inference, provider quota,
skill tuning, server publishing, local-session creation, project edits, check-in,
push, or merge was performed in this slice. The 29-case benchmark contract,
including OTT-18's unsupported-boundary honesty case, is unchanged.

The existing enabled test account resolved to `Agent`, not the spelling supplied
in conversation. Its password was entered through a masked local console prompt
and stored in the standard `multiuser-agent` DPAPI CurrentUser credential file
outside the repository. No password argument, hard-coded credential, benchmark prompt,
or credential payload was added to source or evidence. The file and secret
directory use a protected owner/SYSTEM-only ACL. DPAPI remains a local Windows
user/machine binding, not a vault boundary against the coordinating user itself.

## Implemented harness slice

- `Set-MultiuserAgentCredential.ps1`: masked provisioning or an in-memory
  `PSCredential`; explicit replacement; no user creation or password reset.
- `Start-MultiuserAgent.ps1`: loads that named secret in the coordinating user,
  starts a hidden profile-loaded Windows child under the expected agent SID,
  and records structured outcomes without putting credentials in child arguments.
- `Invoke-MultiuserAgentReadiness.ps1`: proves actual identity, token Openness
  membership, exact Release worker registration, and read-only CLI discovery.
  Optional visible portal launch checks owner SID and refuses duplicate agent
  portals. Explicit-PID discovery verifies owner before attachment and then
  checks open projects, server connections and optional server authorization.
- Readiness acceptance binds the SID, probe PID, worker path, read-only flag,
  membership, registration, portal ownership and requested server alias. An
  unavailable native exit code is retained as unavailable, not manufactured as
  zero; an explicit successful structured result is required. Pending children
  remain `Running`, not successful. Failed results retain diagnostic paths.

The development workflow drove fail-closed checks and evidence. The multiuser
workflow kept TIA and the worker under one identity, preserved the user's
separate identity, and kept UI check-in/certificate approvals outside the API.
No canonical, installed or packaged skills were changed.

## Live read-only evidence

Initially no TIA Portal process was running. The launcher started owned TIA PID
**21000**, with owner SID verified from an Agent-owned child. Under `Agent`, the
Release CLI/worker discovered that exact **V21 Update 2** instance, attached,
independently listed open projects (empty), listed the two existing server
connections, and listed four projects on the configured HTTPS `Agent Local
Server`. Existing server projects were not opened or modified. No certificate
dialog or TLS bypass was needed for these reads.

Under the normal coordinating account, discovery returned zero instances and an
explicit attempt to attach PID 21000 returned `SiemensRejected`, exit 1. A second
agent portal launch also failed closed before starting another portal. The sole
agent-owned portal remains intentionally open for the requested workflow.
All completed probe/Release CLI/worker processes exited. The pre-existing app
Debug MCP/worker processes were left untouched.

Final authorization/readiness capture:
`%LOCALAPPDATA%/OpenTiaToolkit/AgentRuns/097a3fb87c1f43458728cd2d139c967c/`.
Duplicate-launch rejection:
`%LOCALAPPDATA%/OpenTiaToolkit/AgentRuns/f15c057b92f44e8b817f491e8282046c/`.
Earlier captures remain unchanged. The first readiness probe actually passed;
its parent originally misreported failure because Windows did not expose a
native exit code. Structured readback corrected the launcher without changing
the captured result. Early connection-only captures do not prove server access;
the final explicit server-project query does.

The current Release worker was not rebuilt; its SHA-256 remains
`AC9D244988171BA338990FCD674CD826DC963BECD17B452DC196C26185896987`.
Both registration keys match its path, UTC timestamp and hash under the agent.

## Validation

The focused regression suite passes **47 checks** in both Windows PowerShell
5.1 and PowerShell 7: encrypted round-trip, private ACLs, explicit replacement,
missing/mismatched/malformed credentials, unsafe secret names, actual junction
rejection, WhatIf, wrong child identity, structured readiness positives and
adversarial variants. Only isolated fake test credentials are used and deleted.
The real encrypted secret remains outside source control.

Full benchmark `npm test` passes, including the new bootstrap gate and existing
29-case/72-campaign/15-fixture definitions and oracle suites. This Codex runtime
inherits PowerShell 7 module paths into Windows PowerShell child processes; the
test shell temporarily selected the native Windows PowerShell module directories
to avoid unrelated module-autoload errors. No global environment was changed.
The credential helper itself explicitly imports its own host's Security module.

Repository hygiene, credential-file ignore protection, 18-skill packaging drift
check and `git diff --check` pass. Release unit/architecture regression results
are recorded under
`C:/OpenTiaBench/Artifacts/ott37-bootstrap-20261005/`; these run with
`--no-build` because this slice changes only PowerShell harness/docs, not runtime
source or compiled hosts: **765 unit and 54 architecture tests pass**.

An existing clean-checkout issue surfaced in the packaging gate: system
`core.autocrlf=true` had converted the five bundled blueprint JSON copies to CRLF
while the source remained LF. Git considered the files clean, but the byte guard
correctly rejected them. A targeted `.gitattributes` rule now pins all six
`engineering-blueprint.schema.json` files to LF. Mechanical newline normalization
produced no schema/content diff; source and bundle bytes match again. The focused
resource test (canonical, four packages, installed-style positive, four negatives)
and packaging drift check pass. No skill instructions or acceptance contracts
were altered to resolve this checkout failure.

## Next qualification boundary

This is not yet an OpenCode MCP stdio bridge under another account, a two-actor
fixture, a model-scored task or proof of server persistence. Next: qualify the
stateful harness transport, freeze a uniquely named disposable server project
and exact object assertions, prove the real save/UI-check-in/fresh-session
readback deterministically, then run Flash High trials. Concurrent/stale/conflict
variants must preserve local work and never force a server lock or unsupported
check-in. UI automation remains optional harness capability; manual check-in
handoff is explicit and is not scored as fully automated persistence.
