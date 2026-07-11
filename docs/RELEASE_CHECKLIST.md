# Open Beta And Public Release Checklist

Use this checklist before publishing an open beta build or public release.

## Release Positioning

- Confirm the support statement is still accurate: OpenTiaToolkit supports validated TIA Portal versions only; V21 is the current target.
- Keep the README status section aligned with the intended audience: private validation, open beta, or general public release.
- Prefer "open beta" wording while mutation workflows are still being validated on varied real-world projects.
- Clearly state that users should start mutation workflows on disposable project copies and keep backups.

## Documentation

- Verify README, setup, CLI usage, MCP client configs, examples, and changelog all describe the same install path, executable names, and support level.
- Regenerate or refresh `docs/CLI_USAGE.md` when the shared command surface changes.
- Check docs for local machine paths, private project names, TODO notes, and placeholder credentials before publishing.
- Keep installed-product guidance separate from development-checkout guidance.
- Ensure the MCP client snippets point either to the installed product path or to a local build path users are expected to edit.

## Packaging

- Build the installer with `.\scripts\Build-Installer.ps1`.
- Validate staged payload contents with `.\scripts\Build-Installer.ps1 -StageOnly`.
- Confirm the installed layout includes the CLI, MCP server, docs, plugin packages, skills, and `scripts\Register-OpennessAllowList.ps1`.
- Confirm `OPEN_TIA_MCP_EXE` points to `C:\Program Files\OpenTiaToolkit\OpenTiaToolkit.MCP.exe` after installation.
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
- Register the exact CLI and MCP executables with Siemens Openness after each rebuild or installer staging change.
- Smoke test a read-only CLI workflow and a read-only MCP workflow before publishing release artifacts.

## Release Artifacts

- Update version metadata and changelog entries.
- Publish the Windows installer and any agent/plugin bundles intended for the release.
- Verify release download links, documentation links, and support/issue links from a clean browser session.
- Add known limitations for any workflows that are validated only on specific TIA Portal or device configurations.
