# Post-merge native alarm verification — 16 November 2026

**PARTIAL: the complete reviewed batch validates, but real Apply still times out at 900 seconds.**

## Installed fix

Public [OpenTiaToolkit PR #4](https://github.com/mistik91/OpenTiaToolkit/pull/4) merged on 13 November 2026 as c1cbd75. The installed MCP and V21 worker report preview.9 at commit 98646dd, three commits after that merge. The installed worker matches its release-provenance SHA-256, and the Core assembly contains the merged preflight-cache helper. This campaign used the installed binaries, with no Toolkit rebuild, replacement, or code changes.

## Live results

Validation used TIA Portal V21 Update 3 and an independent copy of the saved engineering project. The input was the application's exact 1,936-alarm document, using its corrected backing prefix. The original project was read only.

| Check | Result |
| --- | --- |
| Two scoped reads of existing scalar backing tags | Both found as Bool, 0.422–1.500 seconds |
| Original discrete-alarm inventory | Empty before testing and in the final fresh read (0.657 seconds) |
| One-alarm dry-run | Passed in 9.610 seconds; confirmed rollback; fresh inventory empty |
| Full 1,936-row dry-run, 180-second limit | Timed out after 182.031 seconds; test session discarded |
| Full 1,936-row dry-run, configured 900-second limit | Passed in 765.734 seconds (12 min 46 s); 1,936 proposed creations, zero row errors or warnings, confirmed rollback |
| Fresh inventory after the successful full dry-run | Empty, confirming no persistent dry-run alarms |
| Review-to-document comparison | All 1,936 names, IDs, trigger tags/bits, classes, priorities, trigger modes, and English texts match |
| Real Apply of that exact reviewed document | Timed out after 902.453 seconds; the Toolkit replaced its worker; no write retry |

The timeout returned no transaction report. Whether the disposable Apply had committed before timing out is **unconfirmed**. It must not be described as either a successful import or a confirmed rollback. The disposable TIA process ended following worker termination, so its unsaved outcome was discarded. No changes were saved to the original or disposable project.

The batch contains 1,936 distinct trigger expressions across 721 roots: 640 scalar paths and 1,296 member paths. The merged PR's fast rejected-preflight benchmark on a small fixture does not establish performance for this successful-reference workload. The full reviewed batch now validates its tag references, but native bulk execution/read-back still needs investigation; the timed-out call does not identify its internal stopping phase.

## Recovery and preservation

One initial connection to the original project timed out at 180 seconds; a fresh worker attached successfully in 2.734 seconds. The final original-project attachment succeeded in 0.469 seconds. Treat this as a recorded transient connection issue, separate from the repeatable long batch operation.

Before mutations, the copy's exact path and MCP → worker → hidden TIA process ownership were independently verified. Saved-project payload hashes remained unchanged in the original project after testing, and the immutable baseline archive and local demo profile match their initial hashes. The Toolkit reported no active operation after the Apply timeout. Recorded campaign MCP, worker and disposable Portal PIDs were audited as ended, while the three original visible TIA processes remained responsive. Generated working copies were removed; the immutable baseline ZIP, documents, reports and ownership receipts remain in ignored local evidence.

Doctor reported that the installed WinCC Unified and Startdrive assembly hashes differ from its recorded binary baseline, while the public API profile remains compatible. The workflow results above were collected against those actual installed assemblies; they are not inferred from API compatibility alone.

## Scope and consequence

These observations cover the installed Toolkit backend with the client application's exact input document. Full Apply success, its commit outcome, and native bulk performance remain open. This evidence follow-up to #4 records the installed preview.9 behavior; it is not a new runtime validation of a build from the PR base branch.

The false missing-tag rejection is resolved for the complete reviewed batch. The native 1,936-row import cannot yet be marked resolved: Review remains slow and Apply exceeds the configured timeout. The client's Excel generation and manual TIA workbook import have earlier user acceptance; they were not rerun in this native-backend campaign.

## Reproduction scope and recorded requests

Use a disposable saved-project copy with a Unified HMI whose referenced tags, alarm class and text culture already exist. The measured input was a schemaVersion 1.0 document containing 1,936 new alarm names, unused IDs and expectedState absent. It shared one alarm class and one text culture, used 1,936 distinct trigger expressions over 721 roots, and began with an empty discrete-alarm inventory. The report and evidence omit the proprietary project and alarm/tag identities; the shape alone is not a portable fixture or a deterministic standalone reproducer.

Start a fresh installed MCP host with OPEN_TIA_MCP_TOOL_TIMEOUT_SECONDS=900 and a client response-wait budget above that limit (930 seconds in this campaign). Discover instances, connect, verify the exact disposable project path, select its Unified HMI and read its empty canonical inventory. Keep engineering calls serial.

The full dry-run used the following tools/call parameters, with the target and input path redacted:

```json
{
  "name": "apply-unified-discrete-alarms",
  "arguments": {
    "hmi-name": "<disposable-unified-hmi>",
    "input-path": "<absolute-path-to-alarm-document.json>",
    "dry-run": true,
    "fail-on-warning": true
  }
}
```

It returned success true with Transaction.Created true, Transaction.Committed false and Transaction.RolledBack true. All 1,936 row statuses were Created within the rolled-back transaction. Every row had zero errors/warnings, all requested fields/texts matched the proposed snapshots, and a separate fresh inventory remained empty.

Only after that review and fresh empty inventory, the exact same input bytes were submitted with:

```json
{
  "name": "apply-unified-discrete-alarms",
  "arguments": {
    "hmi-name": "<disposable-unified-hmi>",
    "input-path": "<absolute-path-to-alarm-document.json>",
    "dry-run": false,
    "apply": true,
    "confirm": true,
    "fail-on-warning": true
  }
}
```

The server returned error code TimedOut and this message after 902.453 seconds:

```text
Tool 'apply-unified-discrete-alarms' exceeded 900 seconds; its isolated worker was terminated and replaced.
```

No transaction result accompanied the error. There was no retry. A subsequent operation-status reported no active operation; the disposable Portal had ended. The original project's independently read inventory remained empty and its saved payload hashes were unchanged.

## Follow-up investigation

Collect separate timings for reference preflight, transaction/setter work, commit or rollback, and post-commit canonical snapshot/validation reads on a successful-reference bulk workload. The observed timeout does not locate the bottleneck or establish whether commit preceded it.

A follow-up fix should demonstrate full dry-run rollback and full Apply completion within the configured command deadline, compare all persisted requested fields/hashes with the reviewed document, and preserve concurrency, transaction, warning and no-automatic-write-retry behavior.

The [redacted machine-readable evidence](alarm-bulk-apply-2026-11-16.json) records the measured counts, requests, outcomes and preservation checks. Raw project files, full alarm documents, Siemens binaries and private transcripts stay outside this public PR. This documentation change adds the existing campaign evidence; it does not establish a successful full Apply.
