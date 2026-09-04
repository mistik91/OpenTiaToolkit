---
name: hmi-discrete-alarm-workflow
description: Use for OpenTiaToolkit WinCC Unified HMI discrete-alarm engineering, including complete alarm discovery, exact reads, versioned JSON create or update dry-runs and applies, canonical-hash concurrency, multilingual alarm texts, parameter tags, cross-reference-aware guarded deletion, and disposable-project live verification.
---

# HMI Discrete Alarm Workflow

## Overview

Use this skill to read, create, patch, and delete WinCC Unified discrete alarms safely. Keep mutations state-bound with exact names and canonical hashes, execute a real rolled-back dry-run first, and verify every apply through a fresh read.

This workflow covers engineering configuration only. Do not use it for analog alarms, OPC UA alarm types, alarm-class CRUD, PLC alarms, logging archives, or runtime alarm generation and acknowledgment.

## Establish Context

1. Use `basic-tia-interaction` when the TIA instance, open project, or Unified HMI target is not already proven.
2. Discover the target with `list-instances`, `connect-instance` or `connect-portal`, `list-projects` or `get-project-info`, and `list-hmis` as needed.
3. Select an exact HMI with `open-unified-hmi` or let the alarm command open the requested `hmi-name`.
4. Confirm the project and HMI before mutation. Do not save the project unless the user explicitly asks to persist the verified changes.

## Read Alarms

- Use `list-unified-discrete-alarms --hmi-name <name>` for complete canonical snapshots and content hashes. Add `include-validation=true` when Siemens `Validate()` diagnostics are needed.
- Use `get-unified-discrete-alarm --hmi-name <name> --alarm-name <exact-name>` before updating or deleting one alarm. Names are exact and case-sensitive.
- Treat `list-unified-alarms` as a legacy shallow, read-only insight across multiple alarm families. Do not use it as the source for a discrete-alarm mutation.
- If a large result is returned as an artifact manifest, read the file at `data.outputPath` before planning the change.

## Resolve Alarm Tags

- Use `list-hmi-tags` to prove that every trigger, acknowledgment, control, and parameter tag required by the change already exists and has the expected data type.
- A text `filter` narrows returned matches but does not scope tree traversal. Pair it with a concrete `root-path`, or explicitly use `allow-full-scan=true` with appropriate `max-results` and `max-depth` bounds. A filter-only call is rejected by the full-scan safety guard.
- For several known tags in unrelated paths, separate exact `root-path` reads are equivalent to one bounded full scan. Judge discovery by the returned tag identities and metadata, not by one preferred call shape.

## Prepare a Versioned Change Document

Create a JSON document with `schemaVersion` `1.0` and one or more alarm requests:

```json
{
  "schemaVersion": "1.0",
  "alarms": [
    {
      "name": "Motor_Overload",
      "expectedState": "absent",
      "id": 1001,
      "alarmClass": "Error",
      "priority": 10,
      "raisedStateTag": "Motor1_Status",
      "raisedStateTagBitNumber": 3,
      "triggerMode": "OnRisingEdge",
      "alarmParameterTags": ["Motor1_Speed"],
      "texts": {
        "en-US": {
          "eventText": "Motor overload",
          "infoText": "Inspect motor and drive"
        }
      }
    }
  ]
}
```

Follow these document rules:

- For creation, use `expectedState: "absent"` and provide `id`, an existing `alarmClass`, an existing `raisedStateTag`, `raisedStateTagBitNumber`, and `triggerMode` (`OnRisingEdge` or `OnFallingEdge`). Use an unused ID and an unused tag/bit combination.
- For update, use `expectedState: "present"` and copy `expectedHash` from a fresh list/get result. Omitted properties are preserved. Changing the ID of an existing alarm is unsupported.
- Use only cultures that exist in the Unified HMI multilingual text items. A project language alone does not guarantee that an alarm text item exists for that culture.
- Use at most 10 `alarmParameterTags`, and ensure every alarm, acknowledgment, control, and parameter tag already exists.
- Clear optional string properties with `null` or an empty string, clear parameter tags with `null` or `[]`, and clear an individual localized text field with `null` or an empty string. Do not set the complete `texts` property to `null`.
- Keep `fail-on-warning=true` unless the user has reviewed and accepted a specific warning.

## Dry-Run and Apply

1. Call `apply-unified-discrete-alarms` with `hmi-name`, `input-path`, and the default `dry-run=true`.
2. Verify that the transaction reports `rolledBack=true`. Review each alarm's status, differences, errors, warnings, proposed snapshot, and Siemens validation diagnostics.
3. Correct every validation or concurrency failure and run a new dry-run. Do not apply an unreviewed document.
4. Apply the same reviewed document only with `dry-run=false`, `apply=true`, and `confirm=true`.
5. Fresh-read every affected alarm with `get-unified-discrete-alarm`; compare the persisted values and new `contentHash` with the reviewed intent.

The apply is atomic across the input batch. A failed setter or Siemens validation rolls back the transaction. A successful apply modifies only the open engineering project; it does not implicitly save, compile, download, or deploy.

## Delete One Alarm

1. Fresh-read the exact alarm and capture its `contentHash`.
2. Call `delete-unified-discrete-alarm` with `alarm-name`, `expected-hash`, and the default `dry-run=true`.
3. Confirm that the transaction rolled back, then review `crossReferences` and `crossReferenceLimitation`.
4. Stop when references exist. Use `allow-referenced-delete=true` only after the user explicitly reviews and authorizes that impact.
5. Apply deletion with `dry-run=false` and `confirm=true`, preserving the reviewed hash and reference policy.
6. Fresh-read the exact name and verify that it returns `alarm-not-found`.

## Recover Safely

- On an `expectedHash` mismatch, re-read the alarm, rebuild the change from the new snapshot, and dry-run again. Do not bypass concurrency protection.
- On a validation or setter failure, rely on the reported rollback, then fresh-read the alarm inventory before retrying.
- On `unified-license-required`, stop and report that the WinCC Unified engineering license must be installed or activated.
- On an ambiguous project or HMI, return to `basic-tia-interaction` and prove the target rather than guessing.

## Live Verification

Use an independently retrieved disposable project for live mutation tests. Start a toolkit-owned hidden TIA Portal instance, verify the exact disposable project path and HMI, preserve a baseline inventory or archive hash, and clean every test alarm before closing without saving unintended changes. Never attach a mutating alarm campaign to the user's already-open production project.
