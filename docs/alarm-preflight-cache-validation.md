# Batch-local alarm preflight lookup caching

For large discrete-alarm documents, preflight repeated Siemens alarm-class and culture reads per row and re-enumerated IDs even after taking the complete canonical inventory. A batch of 1,936 rows sharing a class/culture can therefore pay for thousands of identical cross-process reads before creating a transaction.

The preflight now indexes IDs from that inventory and caches exact class, culture, and tag existence results within the current batch only. Every distinct identity is still validated; missing references still block the whole batch. No transaction, hash, rollback, warning policy, or setter validation is bypassed. The cache is recreated for each call.

## Automated coverage

The synthetic 1,936-row test confirms one alarm-class lookup, two culture lookups, 1,938 distinct tag lookups (1,936 triggers plus two shared parameters), zero extra ID enumerations, and an empty inventory after dry-run rollback. Focused regressions also cover:

- cached hits and misses refreshed independently for classes, tags, and cultures on each batch;
- exact cache keys, including case-different names;
- shared trigger, acknowledgment, control, and parameter tags, with per-reference error reporting preserved;
- existing-ID collisions, exact self-name exclusion, and a fresh ID inventory after commit and deletion;
- a later setter failure restoring an earlier update and unrelated existing alarms.

Using the committed SDK 9.0.102 policy, the Release build passes with zero warnings/errors. The expanded non-integration suite passes **821 tests**, architecture passes **54 tests**, and worker/remote-registry integration passes **10 tests**. Generated command-reference validation passes at **186 commands**; packaged skills have zero drift. No public command, schema, host, or worker protocol was changed. See the [broader batch-read review](BATCH_READ_OPTIMIZATIONS.md) for the additional PLC browse coverage.

This is a preflight read optimization. Synthetic access counts establish the eliminated work; they do not establish an end-to-end TIA speedup. Full canonical inventory reads, distinct tag resolution, setters, and transaction work still contribute to total time.

## Disposable worker qualification

`UnifiedDiscreteAlarmWorkerCacheIntegrationTests` is opt-in and uses the production `ToolkitSession`, mutation policy, IPC, and exact selected worker. It retrieves an independent archive copy into a new disposable directory, checks project ownership and path, and chooses two valid existing alarms sharing a class, trigger tag, and text culture. It verifies dry-run rollback, atomic apply, unrelated alarm preservation, stale-hash rejection, existing-ID collision rejection, inverse cleanup, and canonical inventory readback in a fresh worker/TIA session. It saves command outcomes and elapsed times as test evidence and verifies that the source archive hash is unchanged.

The test also submits 1,936 distinct missing alarm names with `expectedState=present`, unused IDs, and valid shared class, culture, trigger, and parameter tags. All reference checks execute, but every row must fail only for the absent expected target and the batch must be rejected before creating a transaction. A fresh canonical read verifies that this timed preflight leaves the fixture unchanged.

After building, register the exact worker using `scripts/Register-OpennessAllowList.ps1` from an elevated shell. Then set these variables for a reviewed fixture and run only the opt-in test:

```powershell
$env:OPEN_TIA_TEST_ALARM_CACHE_DISPOSABLE = 'true'
$env:OPEN_TIA_TEST_ALARM_CACHE_ARCHIVE_PATH = '<absolute path to an immutable .zap21 fixture>'
$env:OPEN_TIA_TEST_ALARM_CACHE_WORKER_PATH = (Resolve-Path '.\src\OpenTiaToolkit.Worker.V21\bin\Release\net48\OpenTiaToolkit.Worker.V21.exe').Path
$env:OPEN_TIA_TEST_ALARM_CACHE_HMI_NAME = '<exact Unified HMI name>'
dotnet test .\tests\OpenTiaToolkit.Tests\OpenTiaToolkit.Tests.csproj -c Release --no-build --filter 'FullyQualifiedName~UnifiedDiscreteAlarmWorkerCacheIntegrationTests'
```

The optional `OPEN_TIA_TEST_HMI_DISCRETE_ALARM_DISPOSABLE_ROOT` controls the parent of the uniquely created campaign directory. A fixture without two suitable alarms is explicitly inconclusive. The test changes only existing alarm origin and event text, then restores them; it does not save, compile, download, or alter the original project.

## Live results, 13 November 2026

The exact Release baseline worker (`master` at `3cec384`) and candidate worker both passed this campaign on independently retrieved copies of the same read-only five-alarm archive. TIA Portal reported V2100; the portal executable file version was `2100.0.121.1`. Both workers completed the dry-run, apply, stale-hash and ID-collision guards, inverse cleanup, unchanged inventory, and fresh-session checks. The archive SHA-256 was unchanged after both runs.

In one measured 1,936-row rejected preflight per build, sharing one class, one culture, and three tags, the baseline took **37,800 ms** and the candidate took **448 ms** (approximately **84 times faster**). These elapsed command times include inventory reads and result serialization. They characterize this preflight workload on a five-alarm fixture; they do not establish a full 1,936-alarm create/update speedup, compilation performance, or timing on a large existing inventory. No full bulk apply timing is claimed.

After adding the PLC browse optimization, both builds passed the expanded two-test worker campaign on fresh, independent PLC and HMI copies. The repeated alarm preflight took **35,234 ms** on the baseline and **502 ms** on the expanded candidate. These are individual observations rather than a statistical performance distribution.
