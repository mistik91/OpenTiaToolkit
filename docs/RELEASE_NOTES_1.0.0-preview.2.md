# OpenTiaToolkit 1.0.0-preview.2

Release date: 14 July 2026

This unsigned prerelease adds guarded, transaction-style PLC code engineering for TIA Portal V21.

## Highlights

- Added format-specific authoring workflows and agent skills for native SCL, S7DCL LAD/data, and baseline-bound SimaticML.
- Added capability discovery and compatibility gates that select supported import, export, and VCI routes from observed TIA and PLC capabilities.
- Added persistent multi-block PLC code transactions with semantic impact analysis, stale-state rejection, compile verification, canonical read-back, rollback, and restart recovery.
- Added guarded PLCSIM Advanced behavioral verification with evidence bound to production transaction approval.
- Added a repeatable end-to-end fixture covering create, modify, compile, behavior verification, and cleanup against a disposable V21 project.

## Compatibility

- Supported: TIA Portal V21.
- Unsupported: TIA Portal V20 and earlier.
- Unvalidated and unsupported: TIA Portal V22 and later.

Run `doctor` after installation and after every TIA Portal update or hotfix. Use disposable project copies for mutation workflows and verify the published SHA-256 checksum before running the unsigned installer.
