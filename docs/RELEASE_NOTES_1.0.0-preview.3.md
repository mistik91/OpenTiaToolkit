# OpenTiaToolkit 1.0.0-preview.3

Release date: 18 July 2026

This unsigned prerelease makes same-machine TIA Portal Multiuser collaboration practical and fail-safe for automation agents.

## Highlights

- Added a validated `Run As` workflow in which the user and agent operate separate TIA Portal instances, workers, Windows identities, and private local-session directories on one workstation.
- Verified identity isolation, concurrent PLC/HMI editing, markings, conflicts, stale-session rejection, save/close behavior, certificate readiness, and fresh-session recovery against TIA Portal V21 Project Server.
- Corrected the Multiuser persistence boundary: V21 public Openness does not expose local-session check-in, so `commit-multiuser-session` now fails safely instead of creating an empty server revision.
- Documented TIA UI check-in as a harness boundary. Users may complete it manually or through optional computer-use automation supplied by their chosen harness; exact artifact verification through a fresh session remains required.
- Hardened CLI script/stdin handling for Unicode and commonly misdecoded UTF-8 byte-order marks.

## Compatibility

- Supported: TIA Portal V21.
- Unsupported: TIA Portal V20 and earlier.
- Unvalidated and unsupported: TIA Portal V22 and later.

Run `doctor` after installation and after every TIA Portal update or hotfix. Use disposable project copies for mutation workflows and verify the published SHA-256 checksum before running the unsigned installer.
