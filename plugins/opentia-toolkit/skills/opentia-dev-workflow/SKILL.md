---
name: opentia-dev-workflow
description: Use for developing the OpenTiaToolkit repository, including C# command/service changes, tests, documentation, MCP/CLI behavior, plugin packaging, Codex/Claude/OpenCode skill updates, elevated Siemens Openness allow-list registration after rebuilds, live CLI/MCP smoke tests, and validation of repo changes before handoff.
---

# OpenTiaToolkit Dev Workflow

## Overview

Use this skill for repo work on OpenTiaToolkit itself. Preserve the shared command surface across Core, CLI, MCP, docs, tests, and packaged skills.

## Code Change Workflow

1. Inspect the existing command/service/test pattern before editing.
2. Keep command names and arguments kebab-case for CLI-shaped APIs.
3. Add or update command schemas, CLI docs, MCP exposure, services, and tests together when behavior changes.
4. Prefer focused service tests for parsing, safety, diffing, and command result behavior.
5. Run the relevant fast tests before handoff. Use integration tests only when the change requires live TIA validation or the user asks for it.

## Validation Commands

Use this fast unit-test command for most repo changes:

```powershell
dotnet test .\tests\OpenTiaToolkit.Tests\OpenTiaToolkit.Tests.csproj --filter "TestCategory!=Integration"
```

Use live integration tests only when TIA Portal V21, test project paths, and Openness registration are available:

```powershell
dotnet test .\tests\OpenTiaToolkit.Tests\OpenTiaToolkit.Tests.csproj --filter "TestCategory=Integration"
```

## Openness Allow-List Refresh

For live TIA CLI or MCP work, do not skip Siemens Openness registration after rebuilding the executable. The allow-list is tied to the exact executable path, UTC file timestamp, and SHA-256 file hash, so every rebuild of `OpenTiaToolkit.CLI.exe` or `OpenTiaToolkit.MCP.exe` can require a refresh.

First verify whether the current shell is elevated:

```powershell
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
```

If it returns `False`, say that the rebuild can continue but the allow-list write requires an elevated Codex/session or an elevated shell. If it returns `True`, register the exact rebuilt executable before running live TIA validation.

For the MCP Debug build:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Register-OpennessAllowList.ps1 -ExecutablePath .\src\OpenTiaToolkit.MCP\bin\Debug\net48\OpenTiaToolkit.MCP.exe
```

For the CLI Debug build:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Register-OpennessAllowList.ps1 -ExecutablePath .\src\OpenTiaToolkit.CLI\bin\Debug\net48\OpenTiaToolkit.CLI.exe
```

Pass `-PortalVersion 21.0` when validating against TIA Portal V21. If the active TIA version differs, use the matching version-specific key, such as `22.0` for V22.

After registration, run a read-only smoke test before deeper live validation. Good CLI checks are:

```powershell
.\src\OpenTiaToolkit.CLI\bin\Debug\net48\OpenTiaToolkit.CLI.exe list-instances
.\src\OpenTiaToolkit.CLI\bin\Debug\net48\OpenTiaToolkit.CLI.exe connect-instance --instance-id <pid>
```

For MCP, use `portal-status`, `list-instances`, and then `connect-instance` through the OpenTia MCP tools.

## Skill And Plugin Packaging

Canonical skill sources live under `skills/`. Plugin copies under `plugins/opentia-toolkit/skills`, `plugins/opentia-toolkit-claude-code/skills`, and `plugins/opentia-toolkit-opencode/.opencode/skills` should match the canonical skill content when packaging.

When adding a skill:

1. Create or update `skills/<skill-name>/SKILL.md`.
2. Include `agents/openai.yaml` for Codex-facing skills.
3. Validate with the skill creator `quick_validate.py`.
4. Run `powershell -ExecutionPolicy Bypass -File .\scripts\Sync-PluginSkills.ps1` to copy canonical skills into plugin packages that bundle skills.
5. Update plugin README/docs and client config notes when the included skill set changes.

Before handoff, run the packaging drift guard:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Sync-PluginSkills.ps1 -CheckOnly
```

Use the existing packaging script when installing or zipping a single skill:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Package-HmiUnifiedAgentSkill.ps1 -SkillSourcePath .\skills\<skill-name> -PackageName <skill-name>.zip
```

## Live TIA Testing

For live tests against an open project, start with `opentia-toolkit-workflow` and `basic-tia-interaction`. Prefer read-only smoke tests unless the change specifically requires mutation.

Good read-only smoke tests:

- `list-instances`
- `connect-instance` or `connect-portal`
- `list-projects` or `get-project-info`
- `list-devices`
- Domain-specific read such as `list-plcs`, `list-hmis`, `get-hardware-tree`, or `list-vci-workspaces`

Keep MCP calls serial and verify project context after reconnecting.
