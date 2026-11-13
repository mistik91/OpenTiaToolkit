---
name: hmi-trend-logging-workflow
description: Use for OpenTiaToolkit Unified HMI trend logging workflows, including HMI tag discovery for logging, creating or updating trend logging infrastructure, bulk trend configuration dry-runs, removing logging tags, deleting unreferenced data log archives, and verifying logging tag/data log associations.
---

# HMI Trend Logging Workflow

## Overview

Use this skill for Unified HMI logging infrastructure, not for screen layout edits. The core safety model is plan first, preserve tag/data-log associations, remove logging tags before deleting data logs, and verify active references.

## Standard Workflow

1. Establish TIA context with `basic-tia-interaction` when the instance/project is not already proven.
2. Identify the Unified HMI:
   - Use `list-hmis` when no exact HMI name was provided.
   - Use `open-unified-hmi` when the HMI needs to be selected.
3. Resolve tag paths:
   - Prefer a known `root-path` with `list-hmi-tags`.
   - A text `filter` narrows returned matches but does not scope tree traversal. Pair it with a concrete `root-path`, or explicitly use `allow-full-scan=true` with appropriate `max-results` and `max-depth` bounds. A filter-only call is rejected by the full-scan safety guard.
   - Avoid full scans unless discovery is unavoidable; if needed, set `allow-full-scan=true`, `max-results`, and `max-depth`.
   - For broad discovery or large tag tables, provide `output-path` or use the returned artifact `outputPath` CSV instead of expecting all tags inline.
   - When the user gives several related tag-table names or naming prefixes, prefer one bounded scan with a shared filter and CSV output, then group the artifact rows locally by `TagTableName`. Use `max-depth` deliberately: shallow scans are faster for parent/direct-member coverage checks, while deeper scans are needed when logging may live on nested members.
4. For one tag, use `create-hmi-trend`.
5. For multiple tags, use `create-hmi-trends` with `dry-run=true` first, then apply the reviewed batch with `dry-run=false` and `confirm=true`.
6. For removal, use `remove-hmi-logging-tag` or `remove-hmi-logging-tags` before deleting data logs.
7. Delete data logs only with `delete-hmi-data-log` or `delete-hmi-data-logs` after active references are gone.
8. Verify by listing tags with logging counts or by reading command results that show logging tag/data log associations.

## Creation Rules

Use one data log for related values when that matches the user's intent, such as process value, setpoint, and current for the same motor. Do not create one archive per tag by default when a shared archive is more natural.

For bulk configs, include explicit `tagPath`, `dataLogName`, `cyclic`, and `cycle` when known. Keep sizing fields such as `duration-days`, `log-max-size`, and `segment-max-size` only when the user specified them or project standards require them.

Rows sharing a `dataLogName` (case-insensitive) must agree on the effective JSON sizing fields `logMaxSize`, `durationDays`, and `segmentMaxSize`. Omitted fields use defaults of 2000, 365, and 500; values below 1 clamp to 1. Conflicts block the entire batch in both dry-run and apply before any writes. When setting a shared archive's sizing explicitly, repeat the same values on every row that uses it. Acquisition mode and cycle may differ per tag.

`storage-folder` is compatibility-only for Unified HMI and should not be used to promise a writable storage folder. Report the configured folder if the command returns it.

## Removal Rules

Logging tag removal requires verifying the expected `dataLogName`. If a logging tag was not created with the toolkit-derived name, use the explicit `loggingTagName` from the user or discovery result.

Applied removal requires `dry-run=false` and confirmation when the tool exposes `confirm`. Do not delete data logs until logging tags that reference them have been removed.

If a data log deletion reports active references, stop and report those references. Do not force deletion or broaden the delete set.

## Verification

After creation, verify that each requested tag is logged and associated with the intended data log.

After removal, verify that the logging tag association is gone.

After data log deletion, verify the command reported it as deleted or not found as expected, and mention any blocked archives separately from successful deletions.
