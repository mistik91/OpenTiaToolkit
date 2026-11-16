# Unified alarm bulk performance qualification

**PASS:** a shape-matched 1,936-alarm workload completes dry-run and Apply through the public MCP/worker path within the configured 900-second deadline. All requested fields and canonical hashes match. The original private PR #9 input still needs a reporting-machine rerun.

## Cause and change

The [installed preview.9 report](alarm-bulk-apply-validation-2026-11-16.md) demonstrated a valid 1,936-alarm dry-run but an Apply timeout at 900 seconds with no returned transaction outcome. That historical Apply outcome remains unknown.

The earlier exact-expression cache cannot share native root searches between distinct members of the same structured tag. Native profiling on a separately prepared workload spent 574 seconds resolving 1,936 distinct trigger expressions. Those expressions share only 721 roots.

This change caches resolved roots, member prefixes, and misses only during one alarm preflight. Each subsequent batch creates a fresh lookup. Other tag-lookup callers retain their existing uncached behavior. It also reads the 16 scalar alarm snapshot attributes with Siemens `IEngineeringObject.GetAttributes` in one call. Text compositions and native `Validate()` calls remain unchanged, including validation on the fresh post-commit inventory. No timeout, transaction, warning, concurrency, or retry policy changes.

## Fixture and measurement scope

The sealed pre-change baseline binaries embed revision `519f08f`; the alarm service, native access and tag-resolution sources are unchanged between that revision and PR base `72ca8b6` (the intervening runtime changes only add HMI JSON write guards). The baseline Core SHA-256 is `18150410BEADC8536EFD738F8FABB9686148A2786F514FC398587FE834242482`. The candidate runtime was built from clean commit `360602f81bfffd49d93d1bc28df938a6af590d16`. Each run retrieves an independent copy of one immutable TIA V21 archive. The observed Portal executable version is `2100.0.121.1`.

The input uses synthetic alarm names and text, unused IDs, `expectedState: absent`, one class, `en-US`, and an initially empty alarm inventory. It contains 640 scalar Bool paths and 1,296 Bool member paths across 81 structured roots, matching the reported 1,936-expression / 721-root shape. The selected trigger roots have system scope. Every reference was assigned to a control alarm and passed native validation in the disposable fixture before the archive was sealed.

This is a shape-matched qualification workload, **not the original private PR #9 document or project**. Private tag identities, raw inputs, projects, and Siemens binaries remain outside the repository. Performance on other project sizes, existing inventories, tag structures, and machines is not established by these individual observations.

The first fixture attempt contained a session-local trigger, which Siemens rejected at row 345; the transaction rolled back. That fixture attempt is excluded from successful performance comparisons. Its evidence is retained separately.

The sealed qualified fixture SHA-256 is `625202C214C625E4AD1DAE370656EA61734F4089E6DE3091AF04DD9CF3D622FC`.

## Native profiling

A test-only access decorator times reference reads, native create/set/map-and-validate, transaction completion, and inventory reads. It introduces no production instrumentation. Profiling runs directly against the native service without a host timeout, so a slow baseline can finish and identify its stopping phase. These service timings exclude MCP transport and final response serialization.

| Service operation | Baseline ms | Candidate ms | Elapsed reduction |
| --- | ---: | ---: | ---: |
| Full dry-run, including rollback verification | 759,030 | 392,024 | 48.4% |
| Full Apply, including fresh validation/readback | 864,722 | 491,463 | 43.2% |

Reference lookup took 573,823 / 575,672 ms for baseline dry-run / Apply, versus 225,688 / 223,285 ms for the candidate. Baseline Apply also spent 171,176 ms in the transaction and 117,734 ms on fresh validation/readback; the candidate spent 160,610 and 107,428 ms respectively. Shared-root lookup accounts for most of the improvement.

Both builds completed all 1,936 rows with zero errors/warnings, confirmed dry-run rollback, atomic Apply and full native teardown of only the created alarms. Native input bytes match across builds. All 1,936 canonical hashes match across both builds' dry-run proposals, Apply proposals and persisted snapshots. Fresh inventories were empty after rollback and teardown; the immutable archive hash was unchanged.

The candidate also passed a live missing → created → deleted tag lookup check with a fresh scope each time. A test-only decorator threw on the second alarm creation after the first real native write; the real transaction rolled back and a fresh inventory was empty.

## Public MCP and isolated-worker campaign

The production Release MCP host selects the exact rebuilt V21 worker, opens an owned hidden Portal, retrieves another independent fixture copy, and verifies its project path and MCP → worker → Portal process chain before alarm writes. Calls remain serial.

The server deadline is explicitly configured with `OPEN_TIA_MCP_TOOL_TIMEOUT_SECONDS=900`; the client waits up to 930 seconds. `OPEN_TIA_MCP_ALLOWED_ROOTS` is limited to the campaign directory. The 282,708,860-byte fixture archive requires `OPEN_TIA_MCP_MAX_INPUT_FILE_BYTES=314572800` (300 MiB). An initial retrieval was correctly rejected at the default 16 MiB cap before any alarm mutation; that empty owned session was closed. This file-size setting only prepares the fixture and does not change the command deadline or the alarm JSON format.

| Public command | Elapsed ms | Verified result |
| --- | ---: | --- |
| Full dry-run | 389,529 | 1,936 proposals, zero row errors/warnings, rollback, fresh empty inventory |
| Full Apply | 498,158 | Committed; fresh independent read matches all 1,936 requested fields and reviewed hashes |

Dry-run and Apply use the exact same input bytes. Both return large results through the normal MCP artifact mechanism, and the client compares the complete JSON artifacts. The MCP proposals also match all native baseline/candidate canonical hashes. An unconfirmed Apply is denied before writes.

An existing-target replay and a stale delete hash are rejected before transaction creation. Three representative alarms (a scalar trigger and the first/last structured-member sections) pass guarded delete dry-run and Apply with no cross references or provider limitations. The remaining 1,933 unsaved alarms are discarded by closing without saving. A new MCP host, worker and hidden Portal reopen that saved disposable copy and independently confirm its original empty alarm inventory. Both sessions close successfully.

## Repository checks

Release builds with zero warnings/errors. All 903 non-integration tests, 54 architecture tests, 42 focused discrete-alarm tests and 7 worker IPC integration tests pass with no skips in these selected suites. The separate real-worker existing-alarm regression also passes: updates, stale-hash and ID-collision rejection, inverse cleanup, unrelated-alarm preservation and fresh worker/Portal readback. Formatting, repository hygiene, generated command references and packaged-skill drift checks pass. The observed catalog has 186 commands and MCP exposes 189 tools; modern and legacy stdio protocol smoke tests pass. CLI doctor reports compatible API profiles, and the final worker passes MCP startup/status/instance discovery.

The scalar mapping tests cover every field, ordered parameter tags, optional defaults and unexpected native result lengths. Service tests verify a fresh lookup scope for each batch and retain reference, hash, warning and rollback coverage.

## Evidence

The [redacted machine-readable evidence](alarm-bulk-performance-2026-11-16.json) records exact build hashes, measured phases, test counts and preservation results. Raw requests/results, timing harnesses, TRX files, registration receipts and ownership snapshots remain under `C:\OTT\AlarmBulk\20261116`.

All four source/prepared archive hashes match their recorded baselines. No campaign MCP, worker, profiling helper or disposable Portal remains. The four original private Toolkit processes and shared Portal server are still present; other user processes were left alone. No Apply timed out or was automatically retried in the qualified campaign.

This establishes the runtime change on the described workload. It does not revise the historical unknown transaction outcome or certify the original private project's full Apply without rerunning that input.
