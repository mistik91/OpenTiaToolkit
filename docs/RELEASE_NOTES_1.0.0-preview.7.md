# OpenTiaToolkit 1.0.0-preview.7

Status: unsigned private release candidate, prepared 7 October 2026. Publication remains on hold. This candidate addresses independent-machine preview.6 installation findings; it is not an accepted stable release.

## Changes since preview.6

- Installed CLI and MCP hosts discover the sibling `worker-v21` package without inherited worker environment variables, including custom installation paths. Explicit version-specific and legacy overrides retain precedence and manifest validation.
- `doctor` reports binary baseline drift as a visible warning when the installed public API remains compatible. Missing assemblies/profiles, wrong process architecture, and breaking API differences remain failures with specific messages. Exact baseline hashes are preserved; warnings do not establish live compatibility with a Siemens update.
- Hardware catalog search exposes `type-identifier` for the actual second V21 `Find` argument. The previously advertised `culture` parameter is explicitly rejected before calling Siemens, with corrective guidance; it is not silently ignored or treated as a type identifier.
- Module-type discovery exposes the original nested Siemens exception and fails explicitly when its provider/method is unavailable. The preview.6 module-query rejection still needs a live retest to establish its actual cause.
- Legacy Codex and Claude Code MCP configurations invoke the installed host through `OPEN_TIA_MCP_EXE` (process, user, then machine scope) or the default product location, without assuming plugin-root interpolation or a particular working directory. PowerShell profiles are suppressed. Installer staging now exercises real modern/legacy MCP discovery and execution through all three packaged configurations, including worker discovery without environment overrides.

## Acceptance findings and remaining gates

Installer compilation now receives the exact staged payload and output directory explicitly. A custom `Build-Installer -OutputRoot` no longer compiles stale files from the default staging directory; compiler failures are checked and the previous installer-version environment value is restored.

Independent-machine preview.6 testing verified the exact installer checksum, installation, worker registration, 189 MCP tools, direct desktop tool execution, and a disposable hardware preview/apply/read-back/reversal. That machine already had development processes, so it did not complete clean-machine or uninstall/reinstall acceptance. HMI engineering was not tested.

The reported portable Codex startup failure was not reproduced locally: the unchanged preview.6 launcher passed both the protocol harness and a cached portable-plugin probe through Codex 0.160.1. Do not treat this as a confirmed portable-plugin fix. Retest the actual cached preview.7 plugin on the reporting machine, and retain the direct MCP fallback while investigating any remaining startup error.

See the candidate validation asset for exact commit, test totals, installer/worker hashes, and observed skips. Before public release:

1. Register the exact candidate worker and repeat the failed `doctor` and hardware catalog/module queries on the reporting V21 installation.
2. Test the cached plugin through the actual agent client, including tool execution; compare it with direct MCP without duplicate active servers.
3. Complete clean-machine installation, uninstall/reinstall, and applicable disposable PLC/HMI/hardware acceptance.
4. Recheck repository protections and published/downloaded artifact integrity after explicit publication authorization.

TIA Portal V21 is the only supported major version. Optional domains require corresponding Siemens installations/licenses. The catalog remains 186 engineering commands plus three MCP infrastructure tools.

## Unsigned preview

The installer and toolkit binaries are unsigned. Verify release checksum files, SBOM, and provenance before installation. Keep engineering changes on disposable projects with independent backups. Stable publication requires trusted signing under [the supply-chain policy](SUPPLY_CHAIN.md).
