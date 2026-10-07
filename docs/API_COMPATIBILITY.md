# Versioned workers and API compatibility

Each Siemens TIA major version is isolated behind a worker package with its own executable, `worker-manifest.json`, binary compatibility profile, and public-API self-description baseline. Hosts remain vendor-free and resolve a worker by requested TIA version.

For V21, resolution order is `OPEN_TIA_WORKER_V21_EXE`, legacy `OPEN_TIA_WORKER_EXE`, an installed sibling `worker-v21` package, then a matching repository build. The host rejects a manifest with an incompatible IPC protocol or unsupported TIA version before launch. Adding V22 requires a separate worker and profiles; it must not add Siemens references to Contracts, Application, CLI, or MCP.

## Diagnostics and migration

- `version` reports the selected worker/runtime target.
- `capabilities` reports the command count and strategic feature flags.
- `doctor` checks process architecture, the presence of six Siemens V21 assemblies, binary baseline drift, and the API migration contract. Missing assemblies/profiles, a non-64-bit worker, and breaking API changes fail diagnostics with specific reasons. Different binary hashes with compatible public API surfaces produce visible warnings, while `compatibility.matched` remains false and the actual hashes remain available.
- `describe-api` computes exported public type/member counts and stable surface hashes, then probes prioritized official API areas.
- `diff-api-profile` compares the installed API with `compatibility/tia-v21.api.json`. A missing assembly, changed public surface, or lost required feature is breaking; a binary hash change with the same public surface is informational.

The V21 baseline includes Base, Step7, WinCC Unified, Startdrive, Safety, and SafetyValidation. Update it only after reviewing the Siemens update, exercising the compatibility suite, and recording support in release notes.

A healthy diagnostic with warnings does not qualify a new Siemens update or prove live Startdrive behavior. Keep the exact baseline intact, review the warning, and test affected workflows before claiming support. Do not replace baseline hashes merely to make a diagnostic pass.
