# Unified alarm root indexing and scalar writes

**PASS:** native baseline/candidate parity, production MCP end-to-end validation, the native Excel comparison and repository checks pass. All 1,936 canonical hashes match across the API runtimes and GUI import. The original private PR #9 input/project was not rerun.

This extends the [previous qualification](alarm-bulk-performance-validation.md) on the same immutable, shape-matched 1,936-alarm fixture. It does not establish the outcome of the historical private PR #9 Apply timeout or certify the original private project.

## Change

Preflight collects the explicitly requested trigger, acknowledgment and parameter references. For eight or more distinct normalized roots, it enumerates the shallow native tag collection once, retaining only requested handles and stopping when all are found. Smaller batches keep native exact lookup. Unmatched names still use native `Find`, including its case and error behavior. Handles and misses remain confined to one preflight.

Scalar writes use one ordered `IEngineeringObject.SetAttributes` call, preserving the previous setter order and conversions. Text writes, native validation, warning policy, confirmation, optimistic concurrency, transaction rollback and independent post-commit readback are unchanged. Additional text-reading experiments did not demonstrate an improvement and were excluded.

Siemens documents [the GUI XLSX import](https://docs.tia.siemens.cloud/r/en-us/v20/configuring-alarms-rt-unified/exporting-or-importing-alarms-rt-unified/importing-alarms-rt-unified) separately from [V21 Openness discrete-alarm operations](https://docs.tia.siemens.cloud/r/en-us/v21/functions-for-accessing-the-data-of-an-hmi-unified-device/hmisoftware/discretealarms/description-discretealarms). The GUI identifies existing alarms by ID. The documented Openness operations expose individual creation, properties, deletion, validation and cross references. Installed V21 invocation discovery found no callable HmiSoftware bulk alarm importer; alarm invocations were Delete and Validate. This establishes the exposed API difference, not Siemens' internal importer implementation. GUI import is not a replacement for the toolkit's reviewed name/hash/expected-state and transaction contract.

## Native comparison

The baseline is the previously qualified runtime at `360602f81bfffd49d93d1bc28df938a6af590d16`; the new runtime is `3687f09e3840038de14d00e8c85487b74b301d72`. Each retrieves an independent disposable copy of the same sealed TIA V21 archive. Input bytes are identical. These are individual service measurements on one machine, excluding MCP transport and response serialization.

| Operation | Previous runtime ms | New runtime ms | Elapsed reduction |
| --- | ---: | ---: | ---: |
| Full dry-run, including rollback verification | 389,844 | 144,458 | 62.9% |
| Full Apply, including fresh native validation/readback | 497,892 | 292,316 | 41.3% |

Dry-run reference resolution drops from 225,390 ms to 3,285 ms including root indexing. Apply reference resolution drops from 220,071 ms to 4,402 ms. Grouped scalar writes take 29,956 / 32,450 ms during dry-run / Apply, versus 53,337 / 54,506 ms previously. Native validation and fresh readback remain substantial costs.

All 1,936 proposed and persisted canonical hashes match across runtimes and phases, with zero alarm errors or warnings. Dry-run leaves an empty inventory; Apply commits; native teardown restores the empty inventory. Both runtimes pass real mid-transaction failure/rollback and fresh-scope tag creation/deletion checks. The new root lookup also matches direct native resolution at the one-, seven- and eight-root boundaries, for missing names, case differences and normalized aliases.

The bulk portion of the initial candidate runner completed and cleaned up successfully. Its later supplementary optional-field fixture failed because it used Bool acknowledgment tags where Siemens requires a suitable integer type. Subsequent fixture corrections also removed an invalid bit assignment after clearing its tag and restored the actual native default Area. These rejected fixture trials are retained separately and are not reported as passing runs.

The corrected optional-field campaign ran independently against both exact runtimes. Before/update/persisted/restored snapshot hashes match across builds; inverse hashes match the original snapshots exactly. It covers acknowledgment state/control tags, bit zero, null/empty clears, ordered parameter lists and clearing, Origin/Area/EventText, rollback and fresh-scope behavior. Its two alarms and four temporary DWord tags are removed and the inventory returns to zero.

The performance fixture starts empty. Preflight still reads complete existing-alarm snapshots for its name/ID inventory, so populated-project preflight can remain expensive even for a request that is ultimately rejected. A smaller identity inventory with targeted snapshots is a separate improvement requiring populated-project qualification; it must preserve whole-inventory rollback proof and collision checks. It is not part of this change.

The subsequent [populated Apply preflight qualification](alarm-preflight-inventory-validation-2026-11-17.md) implements that follow-up while preserving full dry-run rollback proof and post-commit validation. This report retains the original root-indexing measurements and binaries.

## Production MCP campaign

The exact Release worker opens an owned hidden Portal and retrieves another independent fixture copy. The project path and MCP → worker → Portal chain are verified independently before alarm writes. Calls are serial. The server deadline remains explicitly configured at 900 seconds, with a 930-second client wait; there is no write retry. Allowed file roots are restricted to the campaign directory. As in the previous campaign, the 282,708,860-byte fixture requires a 300 MiB input-file cap for archive retrieval.

| Operation | Elapsed ms | Verified result |
| --- | ---: | --- |
| Full dry-run | 150,990 | 1,936 proposals, zero errors/warnings, rollback, fresh empty inventory |
| Full Apply | 291,340 | Committed, 1,936 persisted snapshots, zero errors/warnings |
| Independent full inventory read | 111,140 | Every requested field and canonical hash matches |
| Expected-absent replay | 115,203 | Rejected before transaction creation |

Dry-run and Apply use identical input bytes, also identical to both native runs. Complete normal MCP result artifacts are read and compared; all 1,936 proposal/persisted/fresh hashes match the native and GUI results. An unconfirmed Apply is rejected before writes. A stale delete hash is rejected before transaction creation. Three representative alarms pass guarded delete dry-run and Apply, without cross references, provider limitations or an override.

A fresh read confirms 1,933 remaining unsaved alarms. Closing without saving discards them. A new MCP host, worker and hidden Portal reopen the same saved project and independently confirm its original empty inventory. Both hidden sessions close normally. No qualified Apply times out or is automatically retried.

## Repository checks

Release build: zero warnings/errors. Non-integration: 904 passed; architecture: 54 passed; focused alarm contracts: 43 passed; worker IPC integration: seven passed. No selected test is skipped. The separate real-worker existing-alarm regression passes, including updates, stale hashes, ID collisions, inverse cleanup, unrelated-alarm preservation and fresh worker/TIA readback.

Formatting, generated references, packaged skills and repository hygiene pass. The catalog remains 186 commands / 189 MCP tools. Modern and legacy stdio smoke tests pass, including expected negative protocol/tool cases. CLI doctor reports matching Siemens compatibility and API profiles.

## Native Excel comparison

The user operated TIA's GUI because desktop capture timed out and indexed clicks failed in this environment. An owned visible Portal retrieved another independent fixture copy. Two native-valid seed alarms supplied scalar/member examples for the GUI's exported XLSX template. The test workbook clones those native rows for all 1,936 requests, changing only ID, name, event text and trigger reference. All rows match the API input; native styles, workbook structure and custom properties are retained. Guarded native seed cleanup confirms an empty inventory before GUI import.

The user reports approximately **10 seconds** from the final Import click to completion. This is a user-observed estimate, not an instrumented measurement. TIA's XML/HTML log records successful import of 1,936 discrete alarms and zero analog alarms, with no warning/error entries. The log includes only a completion timestamp and cannot independently establish elapsed time. A separate full native snapshot read and Siemens validation confirm all 1,936 canonical hashes match both API runtimes, with zero diagnostics. This independent verification is outside the reported GUI import time.

The GUI estimate and the 292-second API Apply have different completion contracts: the API validates each alarm during mutation and again in its independent canonical readback, and preserves atomic transaction/concurrency checks. The installed exposed API does not provide the GUI importer as a callable bulk operation. The measurements support reducing native boundary calls; they do not establish which internal implementation makes the GUI faster or justify dropping validation to match its time.

After successful verification, a controlled close without saving did not return within three minutes. Only the previously identified GUI helper/Portal process tree was terminated, with executable paths, parentage and creation times rechecked before cleanup. A fresh production MCP host, worker and hidden Portal reopened that same saved disposable project and independently confirmed its original zero-alarm inventory. The GUI close is recorded as forced owned-process cleanup, not a successful graceful shutdown.

## Evidence scope

The [redacted machine-readable evidence](alarm-bulk-indexing-2026-11-17.json) records exact runtime revisions/build hashes, measured phases, test totals and preservation results. Raw fixture inputs, native snapshots, harnesses, TRX files, registration receipts and ownership records stay outside the repository under `C:\OTT\AlarmBulk\20261117`.

All five source/prepared archive hashes remain unchanged. All five original private Toolkit/shared-server actors retain their original identities. No campaign MCP, worker, helper or disposable Portal remains. The runtime under test is clean commit `3687f09e3840038de14d00e8c85487b74b301d72`; subsequent documentation changes do not rebuild or change those registered binaries.
