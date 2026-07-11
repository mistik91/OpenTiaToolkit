# Production Architecture

Phase 3 extends this boundary with independently manifest-driven worker packages. The vendor-free host selects a worker by TIA version and validates its IPC/version contract before launch. Inside the worker, blueprint planning binds a one-use token to the canonical desired state and current project fingerprint; API self-description verifies the exact Siemens public surface before automation proceeds.

## Process boundary

```text
CLI / MCP (vendor-free net48 hosts)
        |
        v
Application (policy, typed outcomes, audit, worker lifecycle)
        |
        v  versioned JSON over a private named pipe
Worker.V21 (.NET Framework 4.8, one request queue)
        |
        v
Core + Siemens Openness V21
```

`OpenTiaToolkit.Contracts` and `OpenTiaToolkit.Application` target `netstandard2.0` and contain no Siemens references. CLI and MCP reference only those projects. Architecture tests reject a Core or Siemens reference in any vendor-free project.

The worker is launched per host session. It accepts one pipe client and reads exactly one request at a time; the next request is not read until the current command completes. This preserves Siemens Openness' validated async/thread behavior while enforcing one logical command queue. Siemens portal, project, PLC, and HMI objects never cross IPC. IPC returns a protocol version, correlation ID, typed error code, timestamps, warnings, and JSON data.

## Mutation enforcement and auditing

The worker catalog classifies commands as read-only, mutating, destructive, or online. Application evaluates that descriptor before transport. Preview-capable commands remain allowed in dry-run mode; an apply requires explicit `apply` or `confirm`. Other mutations receive a synthetic `confirm` parameter and cannot reach the worker without it.

Every mutation decision is appended to `%LOCALAPPDATA%\OpenTiaToolkit\audit\mutations.jsonl` by default. Password, secret, and token arguments are redacted. Set `OPEN_TIA_AUDIT_JOURNAL` to change the journal path.

## Recovery

If a worker exits, IPC failures map to `WorkerUnavailable`. MCP timeout or cancellation terminates the isolated worker and creates a fresh worker/session. The host never releases a Siemens gate or disposes Siemens objects while a call is executing in the same process.

For deterministic failure tests, set `OPEN_TIA_WORKER_FAILURE_INJECTION` to `throw:before-execute`, `throw:after-execute`, `crash:before-execute`, `crash:after-execute`, or `delay:<milliseconds>`. Never enable failure injection in production.

## Compatibility and diagnostics

`version`, `doctor`, and `capabilities` run inside the worker. `doctor` hashes the installed Siemens API assemblies and compares them with `compatibility/tia-v21.json`. A different hash is reported as an unvalidated compatibility profile, not silently treated as supported.

List/search commands expose optional `page-size` and `continuation-token` parameters. Existing artifact outputs remain the streaming mechanism for large hierarchical and tabular results.
