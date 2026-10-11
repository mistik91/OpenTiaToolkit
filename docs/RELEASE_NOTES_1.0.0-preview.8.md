# 1.0.0-preview.8 — unsigned preview

Prepared 8 October 2026; public-preview documentation updated 11 October 2026. The planned scoped engineering acceptance gates have passed. Preview.8 packages are unsigned. Preview.7 remains an unpublished historical draft; its artifacts and evidence retain their original identities. See the [preview.8 release](https://github.com/mistik91/OpenTiaToolkit/releases/tag/v1.0.0-preview.8) for downloads and the sanitized validation record.

## Unified provisioning correction

Preview.7 could successfully preview creation of a Unified panel, then fail the identical apply with Siemens `NonRecoverableException` and lose the project session. Independent local controls reproduced the sequence: fresh creation succeeded, reopening after preview restored successful creation, and a visible TIA instance still failed. The trigger is consistent with in-place rollback of Unified creation; the deeper Siemens cause is not established.

Preview.8 executes hardware patches containing `create-device` or `create-device-with-item` on an archived project copy in a separate owned TIA instance. Successful execution commits only on the copy, which is then closed and discarded. The selected project is never saved or closed by preview. The source must be a saved standalone project with no unsaved changes. Multiuser projects and archives that cannot be retrieved without authentication fail explicitly. Existing guards and destructive-confirmation requirements apply to the complete copied batch.

Creation preview reports `PreviewIsolated`, `PreviewDiscarded`, and `PreviewArchiveSha256`, with `Applied=false` and `RolledBack=false`. Other hardware previews retain rollback-only transactions. Apply still uses the selected project and requires confirmation. A copied snapshot does not prove that the source has remained unchanged: fresh reads and expected-value guards remain necessary.

Failed transactions preserve the underlying cause and report `RollbackAttempted`; they do not claim completed rollback. Fatal or disposed source sessions report `RecoveryRequired=true` and subsequent inspection fails explicitly instead of returning empty inventory. Reset and explicitly reconnect/reopen the intended project before further writes. No automatic save or reopen is attempted.

The worker registration helper also preserves existing entries for other builds/installation paths. Its retention and idempotence regression test runs against an owned HKCU test key without elevation.

## Validation and qualification

The exact binary source is `7e8c5820ea5df7beb9afc4225575bf064af30cce`. Its Release build completed with zero warnings/errors; 795 unit, 54 architecture, 7 worker IPC and 1 permanent licensed Unified regression tests passed. Packaged protocol discovery exposes 186 commands plus 3 infrastructure tools. Hosted vendor-free CI passed; its licensed job was skipped, with licensed validation collected separately.

Independent installed acceptance subsequently passed for first installation/client integration and a CPU creation/PLC compile/inverse round trip on V21 Update 3, repeated Unified provisioning and scoped screen/item editing with human HMI software compilation on Update 2 Hotfix 1, and a G120C PN Main telegram number/address round trip on the measured Startdrive V21 Update 1 binary. Fresh reads and independent saved reopen corroborate these results.

See [the qualification matrix](PREVIEW8_ACCEPTANCE.md) for exact targets, compiler evidence, unresolved historical process departures, uninstall observation limits and unqualified domains. Startdrive compiler transcripts contain zero errors and the same 14 existing warnings; project-wide IO coverage is incomplete. No physical download or runtime behavior is qualified.

Documentation revisions after acceptance do not change the candidate's binary source or hashes. Installer, source archive and plugin bundles remain the accepted preview.8 snapshots; current repository guidance and the release's `VALIDATION_PREVIEW8.md` supplement their embedded pre-acceptance documentation. A rebuild would be a new candidate requiring new identity and applicable retesting.
