# Release Supply Chain

The .NET SDK is pinned by `global.json`; every project has a committed `packages.lock.json`, and CI restores in locked mode. Builds are deterministic and embed source metadata.

`scripts/Generate-ReleaseArtifacts.ps1` produces:

- `SHA256SUMS.txt` for the staged payload;
- `sbom.spdx.json` with direct and transitive NuGet packages;
- `provenance.intoto.json` using an in-toto statement and SLSA provenance predicate;
- Authenticode signatures for executable and library payloads when `OPEN_TIA_SIGNING_CERT_THUMBPRINT` is supplied.

`Build-Installer.ps1 -RequireSigning` rejects unsigned publication payloads and installers. Signing keys are never stored in the repository; CI or the release workstation supplies a certificate from the Windows certificate store.

The installer output also includes `INSTALLER-SHA256SUMS.txt`. The exact validated Siemens V21 assembly versions and SHA-256 hashes are tracked in `compatibility/tia-v21.json`; Siemens binaries are not redistributed.
