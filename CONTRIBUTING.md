# Contributing to OpenTiaToolkit

Start with a small, focused change and an issue describing the engineering problem, target TIA version, and expected behavior. For security problems, use [SECURITY.md](SECURITY.md) instead of a public issue.

## Development setup

Full product builds require Windows, TIA Portal V21 PublicAPI, the .NET SDK pinned in `global.json`, and .NET Framework 4.8 targeting tools. Vendor-free host and architecture work can be validated without a Siemens installation. See [the developer guide](docs/DEV_GUIDE.md) for project boundaries and command implementation patterns.

```powershell
dotnet restore .\OpenTiaToolkit.sln --locked-mode
dotnet build .\OpenTiaToolkit.sln -c Release --no-restore
dotnet test .\tests\OpenTiaToolkit.Tests\OpenTiaToolkit.Tests.csproj -c Release --no-build --filter "TestCategory!=Integration"
dotnet test .\tests\OpenTiaToolkit.ArchitectureTests\OpenTiaToolkit.ArchitectureTests.csproj -c Release --no-build
```

The fast Core suite needs the installed Siemens reference assemblies even though it excludes live integration tests. Hosted CI builds the vendor-free hosts and runs architecture tests. Run only the gates applicable to the change; report unavailable or skipped gates explicitly.

## Making a change

- Keep commands and arguments in kebab-case. Update schemas, shared command behavior, generated docs, and affected CLI/MCP tests together.
- Preserve explicit mutation confirmation, dry-run defaults, stale-state checks, truthful structured outcomes, rollback evidence, and cancellation boundaries.
- Edit canonical skills in `skills/`; synchronize packaged copies with `scripts/Sync-PluginSkills.ps1`.
- Write focused regression tests for changed behavior. Avoid proprietary projects, credentials, machine-specific paths, and Siemens binaries in patches or examples.
- Run `git diff --check`, applicable tests, `scripts/Generate-CommandReference.ps1 -CheckOnly`, and `scripts/Sync-PluginSkills.ps1 -CheckOnly` before a PR. Use `dotnet format OpenTiaToolkit.sln --no-restore --verify-no-changes` for compiled source changes.

## Live validation

Use uniquely named disposable project copies. Establish exact portal/project identity, read first, preview before applying, verify through fresh reads, and prove cleanup or rollback. Never run validation mutations against a user's original project. Register only the exact isolated worker after rebuilding or signing it. Keep evidence local unless it has been reviewed for public sharing.

For transaction changes, include failure and stale-state cases, rollback verification, and fresh-session readback. See [the release checklist](docs/RELEASE_CHECKLIST.md) and [architecture/recovery guide](docs/PRODUCTION_ARCHITECTURE.md).

## Pull requests

Describe the problem and resulting behavior, then list completed validation and explicit skips. Use the PR template. New TIA major-version support requires a dedicated worker, compatibility profile, and live evidence before it can be advertised. Maintainers run licensed release validation only from reviewed trusted commits; fork PRs use hosted checks.

Contributions are provided under the repository's [MIT License](LICENSE). Keep discussion respectful, specific, and focused on reproducible engineering behavior.
