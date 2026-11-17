# Populated Unified alarm Apply preflight

**PASS:** populated-project preflight, complete native result parity, rollback/inverse safety, production MCP and fresh shipping-worker verification pass. Replay falls from 93.6 seconds to 1.57 seconds on the qualified fixture.

This follows [PR #12's qualification](alarm-bulk-indexing-validation-2026-11-17.md). A one-alarm Apply replay on 1,936 existing alarms previously read every localized text and scalar snapshot before rejecting the request. Only the requested alarm needs a complete canonical snapshot for planning; collision checks need every existing name and ID.

## Change and preserved checks

Apply reads the complete native name/ID inventory in one enumeration, using one two-attribute read per alarm. It maps complete snapshots only for exact requested names. A small internal inventory type keeps the complete identity index separate from requested snapshots; partial snapshots are never substituted for canonical records. If a requested alarm changes name or ID between its identity and snapshot reads, preflight fails before writes.

The service continues to check unrelated ID collisions, exact names, requested canonical hashes, omitted properties, reference availability and warnings. Each Apply builds a new inventory; no engineering handles or misses are cached across calls. Command schemas, hosts and IPC payloads are unchanged.

Dry-run deliberately retains its original complete before/after canonical inventories, including unrelated alarms, to prove rollback. Post-commit Apply still reads and natively validates the full inventory. These costs remain; the change does not replace full rollback proof with target-only checks.

## Fixture and comparison scope

The native baseline uses the previously qualified Core SHA-256 `A7717CA369D16436A206D23B46993701FC5AED3078F65DE251250CEF13E39740`, sealed before rebuilding. Its embedded runtime revision is `3687f09`; the affected alarm sources are unchanged in merged base `5d7119728434202b2bf3b1fadd1ade9629b688b5`. The candidate is built from clean commit `17cae4aa387f05d40ee1ca85a3de28f14587dafd`.

The timing comparison and full 1,936-alarm MCP campaign use that preflight commit. Shipping revision `64657d21e1c59dd2565edcfb24b47d56fb0192c4` additionally repairs transaction-outcome reporting when verification reads fail. Its native identity/snapshot algorithm is unchanged. The exact shipping binaries pass separate real-TIA readback-failure probes, the existing-alarm production-worker regression and fresh populated-project MCP verification described below. Build hashes for both revisions are recorded in the machine-readable evidence.

An owned hidden Portal retrieves the prior immutable empty fixture and creates the same 1,936 synthetic alarms. Every alarm passes native validation with no errors/warnings and matches the previous full canonical hashes. Only that disposable fixture is saved and archived. The populated archive is made read-only; its SHA-256 is `626D9F4EB6AD6A0FD44921C0FC1AB5DA6D2CE846835F6C4E40A379ACA5B9AC1C`.

Each measured run retrieves an independent copy of that populated archive, proves Portal parentage and the exact project path, and verifies all 1,936 initial hashes before mutation. These are individual observations on one machine, not a rerun of the original private PR #9 project/input.

| Service operation | Merged baseline ms | Candidate ms |
| --- | ---: | ---: |
| One-alarm expected-absent replay | 93,636 | 1,574 |
| One-alarm successful update, including full post-validation/readback | 240,475 | 108,839 |

Baseline replay spends 92,975 ms reading complete snapshots. Baseline update spends 109,683 ms on that preflight read and 129,363 ms on full post-commit validation/readback. The baseline update changes Origin, EventText and InfoText; all unrelated alarm hashes remain unchanged.

Candidate replay reads all 1,936 identities and only the requested alarm's full snapshot. It rejects the request before transaction creation, with byte-identical input and an identical complete result to the baseline. The elapsed reduction is 98.3%. Unrelated ID-collision and stale-hash requests also reject before transaction creation, in 1,193 ms and 932 ms respectively.

Candidate update also uses byte-identical input and returns an identical complete result. The independently captured full post-commit inventory matches the baseline's 1,936 hashes; all 1,935 unrelated alarms are preserved. Its elapsed reduction is 54.7%, while full native post-validation/readback remains enabled.

## Repository checks

Shipping Release build: zero warnings/errors. All 911 non-integration tests, 54 architecture tests, 50 focused alarm contract tests and seven worker IPC tests pass with no selected skips. Modern/legacy MCP stdio, CLI doctor, generated references, packaged skills, formatting and repository hygiene pass. The catalog remains 186 commands / 189 MCP tools.

New service tests cover a populated replay reading one requested snapshot, complete requested Before values and omitted properties, unrelated ID collisions in both modes, identity-index freshness after alarm changes, and detection of unrelated corruption after a dry-run rollback.

## Native safety and worker regression

The candidate passes replay, unrelated ID collisions and stale hashes before transaction creation. Successful update returns the same complete Before, proposed and persisted results as the baseline. The inverse restores the entire original 1,936-hash inventory exactly.

The real dry-run takes 228,078 ms and retains two full unvalidated inventory reads, with no targeted-preflight shortcut. Every original hash matches afterward. A test-only wrapper then throws on the second update after the first native write has succeeded. The real transaction rolls back; a separate fresh full native read verifies all 1,936 original hashes. That fresh verification is outside the timed failure response.

The existing-alarm regression also passes through the exact production worker: dry-run, update, stale-hash and ID-collision rejection, inverse, unrelated-alarm preservation and fresh worker/Portal readback. No selected test is skipped.

## Verification-read failures

An injected readback failure reproduced an existing bug: a completed transaction lost its outcome flags in the outer exception handler. The shipping service retains the actual transaction when verification fails. An Apply readback failure returns `apply-readback-failed`, `committed=true`, `rolledBack=false`, `projectHasUnsavedChanges=true` and `success=false`; it does not claim the already-committed write was blocked. The message instructs the caller to inspect current state before retrying. Dry-run verification-read failures retain the completed rollback state and still report failure.

The exact shipping Core is tested on an independent five-alarm archive with real native writes. A test-only wrapper throws during post-rollback verification and then during post-commit readback. Fresh native reads prove the former restored every original hash and the latter actually committed the requested change while preserving unrelated alarms. An explicit inverse restores all five original hashes. Both transaction outcomes, native state and inverse are checked; no write is automatically retried.

## Production MCP and shipping-worker verification

The serial production MCP campaign retrieves an independent empty fixture and checks its path, ownership and worker/Portal chain before mutation. Its reviewed input bytes match the fixture-preparation input exactly. All 1,936 dry-run proposals, Apply proposals/persisted snapshots and independently read hashes match the prepared populated fixture.

| Production MCP operation | Elapsed ms | Result |
| --- | ---: | --- |
| Full 1,936-alarm dry-run | 149,766 | Rolled back; fresh empty inventory |
| Full 1,936-alarm Apply | 288,486 | Committed; zero row errors/warnings |
| Independent full inventory read | 111,318 | Every requested field and canonical hash matches |
| One-alarm replay on the populated inventory | 1,748 | Rejected before transaction creation |

Unconfirmed Apply and stale delete hashes are rejected before writes. Three representative alarms pass guarded delete dry-run and Apply with no cross references, provider limitations or override. The remaining 1,933 unsaved alarms are discarded by closing without saving. A new MCP host, worker and hidden Portal reopen the same saved project and confirm zero saved alarms. The 900-second server deadline and 930-second client wait are unchanged; no qualified Apply times out or is retried. The 300 MiB input-file cap only accommodates the 282,708,860-byte fixture archive.

A further fresh MCP host using the exact shipping worker reopens the populated candidate project. It independently verifies all 1,936 original hashes and zero native diagnostics, then rejects the same replay before transaction creation in 1,687 ms. All hidden sessions close normally.

## Evidence and preservation

The [redacted evidence](alarm-preflight-inventory-2026-11-17.json) records exact revisions, build hashes, phase timings, test totals and safety checks. Raw projects, references, snapshots, harnesses, registration receipts and TRX files remain outside the repository under `C:\OTT\AlarmInventory\20261117`.

All seven source/prepared archive hashes remain unchanged. All five original private Toolkit/shared-server actors retain their original identities. No campaign helper, worker, MCP host or new Portal remains. The shipping worktree is committed before the final binary build; subsequent documentation changes do not rebuild those registered binaries.
