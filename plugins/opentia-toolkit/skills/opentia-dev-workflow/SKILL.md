---
name: opentia-dev-workflow
description: Use for developing the OpenTiaToolkit repository, including C# command/service changes, tests, documentation, MCP/CLI behavior, plugin packaging, Codex/Claude/OpenCode skill updates, risk-tiered validation, disposable TIA live-test campaigns, elevated Siemens Openness allow-list registration after rebuilds, and validation before handoff or merge.
---

# OpenTiaToolkit Dev Workflow

## Overview

Use this skill for repo work on OpenTiaToolkit itself. Preserve the shared command surface across Core, CLI, MCP, docs, tests, and packaged skills.

## Code Change Workflow

1. Inspect the existing command/service/test pattern and the current diff before editing.
2. Classify the change by the highest affected validation tier below.
3. Keep command names and arguments kebab-case for CLI-shaped APIs.
4. Add or update command schemas, CLI docs, MCP exposure, services, and tests together when behavior changes.
5. Treat documentation and skill payloads as executable contracts. Validate examples against the real parser and, when relevant, the actual Siemens object type and TIA version.
6. Prefer focused service tests for parsing, safety, diffing, and command result behavior, then run the gates required by the selected tier.

## Validation Ladder

Use the highest tier touched by the change. Do not impose a full live campaign on documentation-only work, and do not downgrade transaction, worker-boundary, or broad refactor changes because unit tests pass.

1. **Documentation and skills**: validate formatting, generated-reference drift, skill structure, packaged copies, and any focused behavior represented by changed examples.
2. **Vendor-free contracts and hosts**: add a Release build, non-integration and architecture tests, command-surface drift checks, and relevant CLI/MCP/IPC smoke tests.
3. **Worker or TIA behavior**: refresh the worker allow-list when rebuilt, prove the selected instance/project, start read-only, then run focused live integration on a disposable target when mutation behavior changed.
4. **Transactions or broad refactors**: use an immutable archive plus independent disposable copies, applicable adversarial cases, fresh-state verification, and a final process/repository audit.

Read [references/validation-ladder.md](references/validation-ladder.md) completely before Tier 3 or Tier 4 validation, and use its detailed gate matrix for lower tiers when the appropriate scope is unclear.

## Validation Commands

Use this fast unit-test command for most repo changes:

```powershell
dotnet test .\tests\OpenTiaToolkit.Tests\OpenTiaToolkit.Tests.csproj --filter "TestCategory!=Integration"
```

Use live integration tests only when TIA Portal V21, test project paths, and Openness registration are available:

```powershell
dotnet test .\tests\OpenTiaToolkit.Tests\OpenTiaToolkit.Tests.csproj --filter "TestCategory=Integration"
```

For Phase 3 compatibility changes, also run the architecture worker-package contract tests, `describe-api`, `diff-api-profile`, and the worker IPC integration tests. A worker rebuild changes its Siemens allow-list identity.

## Openness Allow-List Refresh

For live TIA CLI or MCP work, register the isolated V21 worker after rebuilding it. CLI and MCP are vendor-free hosts and no longer belong in the Siemens allow-list. The allow-list is tied to the exact worker path, UTC timestamp, and SHA-256 hash.

First verify whether the current shell is elevated:

```powershell
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
```

If it returns `False`, say that the rebuild can continue but the allow-list write requires an elevated Codex/session or an elevated shell. If it returns `True`, register the exact rebuilt executable before running live TIA validation.

For the V21 worker Debug build:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Register-OpennessAllowList.ps1 -ExecutablePath .\src\OpenTiaToolkit.Worker.V21\bin\Debug\net48\OpenTiaToolkit.Worker.V21.exe
```

Pass `-PortalVersion 21.0` when validating against TIA Portal V21. If the active TIA version differs, use the matching version-specific key, such as `22.0` for V22.

After registration, run a read-only smoke test before deeper live validation. Good CLI checks are:

```powershell
.\src\OpenTiaToolkit.CLI\bin\Debug\net48\OpenTiaToolkit.CLI.exe list-instances
.\src\OpenTiaToolkit.CLI\bin\Debug\net48\OpenTiaToolkit.CLI.exe connect-instance --instance-id <pid>
```

For MCP, use `portal-status`, `list-instances`, and then `connect-instance` through the OpenTia MCP tools.

## Skill And Plugin Packaging

Canonical skill sources live under `skills/`. Plugin copies under `plugins/opentia-toolkit-agent-plugin/skills`, `plugins/opentia-toolkit/skills`, `plugins/opentia-toolkit-claude-code/skills`, and `plugins/opentia-toolkit-opencode/.opencode/skills` should match the canonical skill content when packaging.

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

When a changed skill is also installed in the active Codex environment, verify or refresh that explicit installed copy in addition to plugin packages. Never infer or bulk-replace an active skill root:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Sync-PluginSkills.ps1 -SkillName opentia-dev-workflow -ActiveSkillsRoot="<absolute-active-skills-root>" -CheckOnly
powershell -ExecutionPolicy Bypass -File .\scripts\Sync-PluginSkills.ps1 -SkillName opentia-dev-workflow -ActiveSkillsRoot="<absolute-active-skills-root>"
```

Run the check again after refresh. Edit canonical skills first; do not hand-edit packaged or installed copies.

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

For mutation tests, never use the user's original open project as the test target. Follow the disposable-project, rollback, evidence, and process-ownership rules in [references/validation-ladder.md](references/validation-ladder.md). Treat environment-dependent skips as explicit skips, not passes.

## Completion And Merge Gate

Before handoff:

1. Run `git diff --check`, inspect the complete diff, and rerun every gate required by the selected tier.
2. Record the exact commit, build/test results, generated artifacts, live baselines and final-state evidence when applicable, and every justified skip.
3. Confirm the worktree state and that no toolkit-owned MCP, worker, testhost, or disposable TIA process was orphaned.
4. For merge work, fetch the actual remote default branch, confirm divergence and mergeability, open a pull request, wait for required remote checks, and merge only when GitHub reports the PR clean.
