# 1.0.0-preview.8 — private candidate

Prepared 8 October 2026. The repository remains private; publication is blocked pending acceptance. Preview.7 artifacts and evidence retain their original identities.

## Unified provisioning correction

Preview.7 could successfully preview creation of a Unified panel, then fail the identical apply with Siemens `NonRecoverableException` and lose the project session. Independent local controls reproduced the sequence: fresh creation succeeded, reopening after preview restored successful creation, and a visible TIA instance still failed. The trigger is consistent with in-place rollback of Unified creation; the deeper Siemens cause is not established.

Preview.8 executes hardware patches containing `create-device` or `create-device-with-item` on an archived project copy in a separate owned TIA instance. Successful execution commits only on the copy, which is then closed and discarded. The selected project is never saved or closed by preview. The source must be a saved standalone project with no unsaved changes. Multiuser projects and archives that cannot be retrieved without authentication fail explicitly. Existing guards and destructive-confirmation requirements apply to the complete copied batch.

Creation preview reports `PreviewIsolated`, `PreviewDiscarded`, and `PreviewArchiveSha256`, with `Applied=false` and `RolledBack=false`. Other hardware previews retain rollback-only transactions. Apply still uses the selected project and requires confirmation. A copied snapshot does not prove that the source has remained unchanged: fresh reads and expected-value guards remain necessary.

Failed transactions preserve the underlying cause and report `RollbackAttempted`; they do not claim completed rollback. Fatal or disposed source sessions report `RecoveryRequired=true` and subsequent inspection fails explicitly instead of returning empty inventory. Reset and explicitly reconnect/reopen the intended project before further writes. No automatic save or reopen is attempted.

## Validation and remaining gates

Release build, focused regression tests, architecture checks, packaged protocol tests, and a disposable live campaign are required for this changed worker. Live acceptance must cover the exact Unified preview/apply/read-back sequence, repeated previews, isolation after a mid-batch failure, unsaved-source refusal, confirmation denial, ordinary PLC/hardware regression, and independently verified cleanup. Register the exact rebuilt worker before those tests; preview.7 registration does not apply to preview.8.

Passing local checks does not close the outstanding genuine clean-machine acceptance, colleague-machine retest, relevant WinCC Unified editing/compile qualification, or affected live Startdrive workflow gate. HMI provisioning alone does not qualify HMI editing, and a compatible API/hash-warning doctor result does not qualify drive behavior. No physical equipment download is included. Candidate packages remain unsigned.
