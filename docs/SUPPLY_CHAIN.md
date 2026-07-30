# Release Supply Chain

The .NET SDK is pinned by `global.json`; every project has a committed `packages.lock.json`, and CI restores in locked mode. Builds are deterministic and embed source metadata.

`scripts/Generate-ReleaseArtifacts.ps1` produces:

- `SHA256SUMS.txt` for the staged payload;
- `sbom.spdx.json` with direct and transitive NuGet packages;
- `provenance.intoto.json` using an in-toto statement and SLSA provenance predicate;
- Authenticode signatures for executable and library payloads when `OPEN_TIA_SIGNING_CERT_THUMBPRINT` is supplied.

`scripts/Pack-ClaudeDesktopMcpb.ps1` validates and packs the Claude Desktop extension and writes `MCPB-SHA256SUMS.txt` beside `opentia-toolkit.mcpb`.

`Build-Installer.ps1 -RequireSigning` rejects unsigned publication payloads and installers. Signing keys are never stored in the repository; CI or the release workstation supplies a certificate from the Windows certificate store.

Stable public releases must use `-RequireSigning`. An explicitly labeled unsigned preview may omit that switch only when the release name, release notes, README, and generated `UNSIGNED-NOTICE.txt` all state that the installer and payload are unsigned. The unsigned path still requires payload and installer checksums, the SPDX SBOM, provenance, and verification that Authenticode reports `NotSigned`.

The installer output also includes `INSTALLER-SHA256SUMS.txt` and, for an unsigned build, `UNSIGNED-NOTICE.txt`. The exact validated Siemens V21 assembly versions and SHA-256 hashes are tracked in `compatibility/tia-v21.json`; Siemens binaries are not redistributed.
