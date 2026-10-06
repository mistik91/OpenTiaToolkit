# Multiuser agent identity bootstrap (Windows)

The user supplies an existing dedicated Windows account, not a password in a
model prompt. TIA Portal and its CLI/MCP worker must run under that same account.
This bootstrap does not create users, reset passwords, grant server permissions,
change TLS trust, publish a project, create a local session, or check in changes.

## Local credential convention

Run this once from the Windows account that will coordinate the harness:

```powershell
.\scripts\Set-MultiuserAgentCredential.ps1 -UserName "$env:COMPUTERNAME\Agent"
```

Enter the password at the masked local prompt. The default secret name is
`multiuser-agent`; its private file is
`%LOCALAPPDATA%\OpenTiaToolkit\Secrets\multiuser-agent.credential.clixml`.
PowerShell encrypts the password using Windows DPAPI for the storing user and
machine. The file and secret directory grant access only to that user and SYSTEM.
The username is metadata, not encrypted. This is not a portable secret, a shared
vault, or protection from code already running as that Windows user. Never copy
it into the repository, benchmark workspace, traces, or model context.
[Microsoft documents the DPAPI binding](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.utility/export-clixml?view=powershell-5.1).

Use `-Force` only for an intentional replacement. Automation can pass a
`PSCredential` object through `-Credential`; there is no plaintext password
argument. The launcher decrypts in the coordinating process and passes the
credential to Windows `Start-Process`, not to the child command line or model.
Test-only weak passwords are unsuitable for production accounts.

## Read-only readiness, then optional visible TIA launch

```powershell
.\scripts\Start-MultiuserAgent.ps1 -ExpectedIdentity Agent -Action Readiness
$launch = .\scripts\Start-MultiuserAgent.ps1 -ExpectedIdentity Agent -Action Portal
if ($launch.Status -ne 'Succeeded') { throw 'Inspect pending launch evidence before continuing.' }
.\scripts\Start-MultiuserAgent.ps1 -ExpectedIdentity Agent -Action Discovery -InstanceId $launch.PortalPid -ServerAlias "Agent Local Server"
```

Release CLI and V21 worker paths are the defaults; neither is rebuilt. Override
`-CliPath`, `-WorkerPath`, or `-PortalPath` for another installation. Register the
exact worker executable first with `Register-OpennessAllowList.ps1`.

The hidden child loads the agent profile, proves its exact SID and Openness token
membership, verifies worker path/date/hash registration, and calls read-only
instance discovery. `Portal` additionally launches a visible TIA instance only
when the agent has no discoverable instances, and independently verifies the
new process owner SID. It does not claim the portal has finished initializing.
All launch modes refuse the coordinator's own identity.

`Discovery` requires an explicit TIA PID, independently verifies its owner, and
uses one stateful CLI worker to list instances, attach, list open projects, and
list configured server connections. Optional `-ServerAlias` additionally lists
that server's projects to test authorization. Connection discovery alone does
not claim network access or certificate readiness. These are read-only queries;
certificate approval may require the user in the Agent-owned TIA window.

Redacted launch/readiness evidence is written under
`%LOCALAPPDATA%\OpenTiaToolkit\AgentRuns\<unique-id>`. The launcher waits up to
`-WaitSeconds` (default 10, maximum 45); `Running` means inspection is still
pending, not success. Read the returned stdout/stderr paths and process status
before another launch. `Succeeded` requires the child's structured SID-bound
readiness proof; a native exit code can be unavailable after alternate-identity
launch and is never silently substituted with zero. A timed-out child is not
killed automatically. The portal remains open when the probe exits. No
pre-existing process is terminated.

The separate-identity launcher is harness plumbing, not a new Openness command.
Stateful MCP transport uses the separately qualified relay below; the
OTT-37 campaign harness has passed deterministic qualification and three Flash
High trials with separately verified server persistence; see the
[campaign report](../benchmarks/reports/ott-37-flash-high-campaign-20261005.md).
A successful launch alone does not prove Project Server authorization,
certificate readiness, local-session safety, or persistence. First-use
certificate approval remains a user action.
Check-in remains a TIA UI handoff; afterward verify exact artifacts through a
new server-derived local session, never a revision number alone.

## Separate-account MCP stdio relay

`scripts/Start-MultiuserAgentMcp.ps1` runs in the coordinating account. It reads
the same named DPAPI secret, launches MCP through Windows credentialed process
creation, and independently checks the actual process owner SID before forwarding
any client request. MCP starts its exact selected V21 worker under the same
identity. No password enters the command line, OpenCode configuration, model
prompt, or protocol stream.

An OpenCode local MCP command can use:

```text
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File <absolute-path-to-Start-MultiuserAgentMcp.ps1> -ExpectedIdentity Agent -McpPath <absolute-Release-MCP.exe> -WorkerPath <absolute-registered-V21-worker.exe> -SharedTempDirectory <existing-per-run-temp-directory>
```

Configure `OPEN_TIA_MCP_ALLOWED_ROOTS` explicitly in that MCP entry: the model
workspace and the exact disposable session directory, not a whole profile,
repository, or secret root. Shared temp must exist beneath one of those allowed
roots; use a unique subdirectory of the run workspace so compact tool artifacts
are readable by the coordinator. Omitting shared temp uses the Agent profile's
temp directory. Credentials and model-provider authentication remain outside the
workspace. `-SecretName` selects credential metadata, never a password value.

The child receives a minimal OS environment with its own profile and temp paths,
the explicitly allowed toolkit settings, and pinned worker paths. It does not
inherit the coordinator's XDG state or model-provider environment credentials.
Loading a Windows profile alone does not correct inherited environment variables:
the qualification found a legacy credentialed smoke launch still using the
coordinator's inaccessible TEMP. Use the MCP relay for this transport, not a
bare `Start-Process -Credential` recipe.

The relay preserves raw bytes and flushes each chunk for interactive JSON-RPC.
Only startup metadata and diagnostics go to stderr. EOF closes the child input
and permits bounded graceful shutdown. Exit 124 means MCP remains running after
the grace period; its PID is printed to stderr and must be inspected before any
retry. The relay never forcibly terminates an active TIA operation or TIA process.
A passed handshake is not proof of TIA attachment or server persistence.

`benchmarks/scripts/Invoke-MultiuserMcpProbe.ps1` is a deterministic qualification
driver, not a model-scored campaign. It requires an explicit TIA PID and server
alias, verifies worker/TIA owner SIDs, retains requests and responses, and can
execute a reviewed JSON command array with typed exact-result assertions. A
failed freshness/conflict/context assertion stops the sequence before the next
command. Success also requires native relay shutdown success. Use a new evidence
directory for each probe; the driver refuses prior evidence overwrite.

For supervised native-session diagnosis, pass `-CaptureWorkerDiagnostics` to
the probe. It explicitly forwards the worker's full stderr to the retained
`stderr.log`, without putting logs on JSON-RPC stdout. The opt-in setting
`OPEN_TIA_WORKER_FORWARD_STDERR=true` is preserved by the identity-bound relay;
the probe disables it when the switch is absent. Treat diagnostic journals as
private operator evidence, not model prompt input. No credentials are added by
this switch.

The host always drains redirected worker stderr and retains at most 65,536
characters in memory, even when forwarding is off. This avoids log-pipe
backpressure and keeps a bounded exit diagnostic. Local-session opening errors
identify `open-local-session`, `read-local-session-project`, or
`select-local-session-project`; full exception chains/stacks remain in the
opt-in worker diagnostic stream. These stages are observations, not license,
authentication or SDK root-cause classifications. A disposed session still
blocks further engineering; do not add automatic open retries.

The general benchmark runner uses the coordinating identity for ordinary
fixtures. Identity-bound Multiuser fixtures such as OTT-37 instead require
explicit Agent/coordinator TIA PIDs, provision a fresh archive-derived server
project and private Agent session, and start model MCP through this relay.
Each trial pauses for owning-TIA UI check-in and independently verified fresh
server readback before another trial can start. See the
[campaign instructions](../benchmarks/README.md#identity-bound-multiuser-campaigns-ott-37).
Do not silently score a coordinator-owned run as an Agent-owned run. Concurrent
peer edits, marking contention and stale-work reapplication remain unqualified.

## Focused validation

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-MultiuserAgentCredential.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-MultiuserStdioRelay.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\benchmarks\scripts\Test-MultiuserMcpProbeAssertions.ps1
```

Tests use an isolated randomly named fake credential, check encrypted round-trip,
private ACLs, wrong identity/type, missing secret, replacement guard, unsafe names,
reparse-point rejection, WhatIf, fail-closed child identity, and structured
readiness proof positives/negatives (including unavailable/nonzero exit codes).
They never log on with the test credential or launch TIA. Live account
discovery/portal checks are separate evidence.
The relay tests exercise EOF, UTF-8, a 1 MiB binary payload, a small response
before EOF, nonzero child exit, and a bounded fake child proving that pending
shutdown does not kill it. Assertion tests reject stale/conflicted, missing,
wrong-type, and non-exact evidence. These tests require neither a real credential
nor TIA and run in Windows PowerShell 5.1 and PowerShell 7.
