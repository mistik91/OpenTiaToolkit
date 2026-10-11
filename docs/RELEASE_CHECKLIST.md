# Public Release Checklist

Use this checklist before publishing a public preview or stable release. Stable releases must be signed. An unsigned preview is allowed only when it is explicitly labeled as unsigned throughout the repository and publication artifacts.

## Release Positioning

- Confirm the support statement is still accurate: OpenTiaToolkit supports TIA Portal V21 only.
- Keep the README status section aligned with the exact public release version and signing state.
- For preview.8, use the [qualification matrix](PREVIEW8_ACCEPTANCE.md) and release `VALIDATION_PREVIEW8.md`; the planned scoped acceptance gates are complete. Review their explicit limits without claiming exhaustive feature qualification.
- Preserve the accepted installer, worker, source archive and plugin bundle bytes. Documentation follow-up commits do not retarget the accepted binary source. A rebuilt/repacked candidate needs new checksums and applicable acceptance.
- Do not imply forward compatibility; each future TIA major version requires its own worker and validation profile.
- Clearly state that users should start mutation workflows on disposable project copies and keep backups.
- For an unsigned preview, include `unsigned` in the release name and release notes and publish `UNSIGNED-NOTICE.txt`.

## Documentation

- Verify README, setup, CLI usage, MCP client configs, examples, and changelog all describe the same install path, executable names, and support level.
- Regenerate or refresh `docs/CLI_USAGE.md` when the shared command surface changes.
- Check docs for local machine paths, private project names, TODO notes, and placeholder credentials before publishing.
- Keep installed-product guidance separate from development-checkout guidance.
- Ensure the MCP client snippets point either to the installed product path or to a local build path users are expected to edit.

## Packaging

- Build the installer with `.\scripts\Build-Installer.ps1`.
- Validate staged payload contents with `.\scripts\Build-Installer.ps1 -StageOnly`.
- Validate the portable Agent Plugins 1.0 manifest, MCP configuration, skill layout, product version, and launcher with `.\scripts\Test-AgentPlugin.ps1 -TestLauncher`.
- Confirm the installed layout includes isolated CLI, MCP, and `worker-v21` payloads, docs, compatibility profile, release metadata, plugins, skills, and registration script.
- Confirm `OPEN_TIA_MCP_EXE` and `OPEN_TIA_WORKER_EXE` point to their installed subdirectory executables.
- Validate the Claude Desktop MCPB and its `MCPB-SHA256SUMS.txt` when publishing that bundle.
- Check packaged skills against canonical skill sources with:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Sync-PluginSkills.ps1 -CheckOnly
```

## Validation

- Run fast tests:

```powershell
dotnet test .\tests\OpenTiaToolkit.Tests\OpenTiaToolkit.Tests.csproj --filter "TestCategory!=Integration"
```

- Run live integration tests against a disposable or dedicated V21 test project before a public build.
- Run vendor-free architecture tests and confirm clean CLI/MCP payloads contain no Core or Siemens assemblies.
- Run worker IPC, failure-injection, disposable archive retrieve, and compatibility doctor tests.
- Register only the exact V21 worker with Siemens Openness after each rebuild, signing, or installer staging change.
- Verify every versioned worker manifest, binary/API compatibility profile, `describe-api`, and `diff-api-profile`.
- Validate all blueprint examples and run the disposable state-bound blueprint lifecycle, including replay rejection.
- Confirm packaged MCP exposes the generated Phase 3 commands and uses its sibling versioned worker.
- Smoke test a read-only CLI workflow and a read-only MCP workflow before publishing release artifacts.
- Record independent-machine acceptance without including private project, device, user, or machine identifiers.

## Release Artifacts

- Update version metadata and changelog entries.
- Publish the Windows installer and any agent/plugin bundles intended for the release.
- For a stable release, build publication artifacts with `-RequireSigning` and verify Authenticode signatures.
- For an explicitly unsigned preview, omit `-RequireSigning`, verify Authenticode reports `NotSigned`, and verify `UNSIGNED-NOTICE.txt` names the exact preview installer.
- In both cases, verify payload, installer, and published bundle checksums, the SPDX SBOM, provenance, and the exact V21 compatibility profile.
- Verify release download links, documentation links, and support/issue links from a clean browser session.
- Add known limitations for any workflows that are validated only on specific TIA Portal or device configurations.
- Download the published installer and repeat signature, checksum, `version`, `doctor`, CLI, and MCP smoke checks against the downloaded artifact.

## Publication sequence

Merge preparation and public publication can be authorized separately. Complete the documentation review and required PR checks, then merge when authorized so the default-branch README shows the accepted scope. Keep the repository private and releases draft until public visibility and publication are explicitly confirmed. Retain the release target at the exact accepted binary source commit.

When repository visibility changes, enable GitHub private vulnerability reporting and verify that **Report a vulnerability** is available. Its public route cannot be treated as verified while the repository is private. Verify issue templates, README assets and documentation links from an unauthenticated browser. Publish only the intended unsigned preview draft, with its unsigned name/notice and checked assets; leave historical drafts unpublished. Verify the public download and its checksum after publication. Raw colleague evidence and proprietary project archives remain private.
