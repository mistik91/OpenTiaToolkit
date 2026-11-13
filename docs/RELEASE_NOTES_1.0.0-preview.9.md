# 1.0.0-preview.9 — unsigned preview

13 November 2026. This release supports **TIA Portal V21 only**. The installer and product binaries are unsigned; download `INSTALLER-SHA256SUMS.txt` and `UNSIGNED-NOTICE.txt` with the installer and verify its hash before running it. Windows may show an unknown-publisher or SmartScreen warning.

## Changes since preview.8

- Bulk Unified HMI trends reject conflicting size or retention settings for a shared archive before any writes, in preview and apply. Every row using the same archive must agree on effective `logMaxSize`, `durationDays`, and `segmentMaxSize`. Omitted values still mean 2000, 365, and 500; acquisition mode and cycle remain independent per tag.
- Filtered HMI JSON exports discover canonical screen paths before reading selected screen metadata. Existing matching, ordering, nested paths, and missing-filter behavior are preserved.
- PLC transaction impact analysis discovers potential targets once per analysis. Planning, approval, and apply still use independent inventories and fresh consumer evidence; changes to callers invalidate stale plans.
- PLC type rollback restores all targets and dependencies, compiles, then verifies exact identities and canonical type hashes. TIA V21 can reject an imported type's export before compilation; this no longer causes a false recovery incident. A genuine hash mismatch or unproven compile still retains the recovery package.
- Unified discrete-alarm batches reuse reference lookups only within one preflight, and PLC block-name filters reject unmatched blocks before expensive metadata reads.

The shared surface remains 186 commands plus three MCP session tools. Command names and parameters are unchanged. See the [follow-up report](FOLLOWUP_VALIDATION.md) and [earlier optimization audit](BATCH_READ_OPTIMIZATIONS.md) for implementation evidence and local measurements.

## Validation and limits

The functional changes passed 835 non-integration tests, 10 worker/remote-registry tests, and 54 architecture tests, plus three independent disposable TIA V21 Update 2 campaigns through the production host and isolated worker. Those campaigns cover trend preview/apply/cleanup, filtered screen compatibility and inverse hash restoration, fresh PLC consumers at approval/apply, a real Siemens compile failure, proven rollback, explicit incident recovery, and fresh worker/TIA sessions. Original project and archive hashes were preserved.

The release's `VALIDATION_PREVIEW9.md` records the exact new binary source, packaged-worker identity, installer and bundle validation, checksums, signatures, compatibility checks, and applicable live retests. Use that asset for this build's qualification; the hashes in the earlier follow-up report identify its original development build.

The [preview.8 independent acceptance matrix](PREVIEW8_ACCEPTANCE.md) remains historical evidence for its exact earlier artifacts. Preview.9 does not claim a repeat of every independent-machine, client UI, HMI compiler, or Startdrive scenario from that matrix. Local offline qualification does not establish physical download, simulator runtime behavior, or compatibility with another TIA major version. Workflows beyond the recorded scope need validation on a disposable project with independent backups.

## Packages

The [preview.9 release](https://github.com/mistik91/OpenTiaToolkit/releases/tag/v1.0.0-preview.9) supplies the Windows installer, portable Agent Plugins bundle, Claude Desktop MCPB, source archive, validation record, checksums, SBOM, and provenance. Siemens assemblies and proprietary projects are excluded. Historical draft releases and accepted preview.8 artifacts retain their original identities.
