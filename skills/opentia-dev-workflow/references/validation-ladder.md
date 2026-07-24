# OpenTiaToolkit Validation Ladder

Use this reference to select proportionate repository validation and to run safe, evidence-backed live TIA campaigns. Apply the highest tier touched by a change. Do not report a skipped or unavailable gate as passed.

## Contents

- [Tier 1: documentation and skills](#tier-1-documentation-and-skills)
- [Tier 2: vendor-free contracts and hosts](#tier-2-vendor-free-contracts-and-hosts)
- [Tier 3: worker or TIA behavior](#tier-3-worker-or-tia-behavior)
- [Tier 4: transactions and broad refactors](#tier-4-transactions-and-broad-refactors)
- [Disposable-project safety protocol](#disposable-project-safety-protocol)
- [Adversarial coverage](#adversarial-coverage)
- [Evidence and cleanup](#evidence-and-cleanup)
- [Pull request and merge gate](#pull-request-and-merge-gate)

## Tier 1: Documentation And Skills

Use Tier 1 when changes cannot affect compiled runtime behavior.

Minimum gates:

1. Run `git diff --check` and inspect every changed example.
2. Run `scripts\Generate-CommandReference.ps1 -CheckOnly` when command, CLI, MCP, schema, or generated documentation surfaces are involved.
3. Validate every changed canonical skill with the skill creator `quick_validate.py`.
4. Run `scripts\Sync-PluginSkills.ps1`, then `scripts\Sync-PluginSkills.ps1 -CheckOnly`.
5. If the skill is actively installed, run the sync script with explicit `-SkillName` and `-ActiveSkillsRoot`, then repeat with `-CheckOnly`.
6. Run focused tests for the parser, command, or behavior represented by changed examples.
7. Search all canonical and packaged documentation surfaces for stale forms of the corrected example.

Do not require live TIA validation for prose-only changes when the corrected behavior was already proven by focused tests or preserved live evidence.

## Tier 2: Vendor-Free Contracts And Hosts

Use Tier 2 for Core contracts, command schemas, generated command surfaces, host orchestration, CLI/MCP behavior, worker packaging, or IPC changes that do not themselves require Siemens engineering objects.

Minimum gates:

1. Run all Tier 1 gates that apply.
2. Build affected projects in Release configuration.
3. Run non-integration and architecture tests.
4. Validate the generated command reference and command catalog.
5. Compare CLI command exposure and MCP `tools/list` exposure with the intended catalog. Avoid hard-coded expected counts in the skill; record the observed counts in evidence.
6. Run focused host/worker IPC tests when session, transport, cancellation, crash, compatibility, or package resolution behavior changed.
7. Exercise read-only CLI and MCP startup/status paths.

For compatibility changes, include `describe-api`, `diff-api-profile`, the worker-package contract tests, and worker IPC integration tests.

## Tier 3: Worker Or TIA Behavior

Use Tier 3 when a change touches the V21 worker, Siemens adapters, project discovery, PLC/HMI/hardware reads, compilation, or a focused mutation.

Minimum gates:

1. Run Tier 2 first.
2. If the V21 worker was rebuilt, register that exact worker executable in the Siemens Openness allow-list. Do not register vendor-free CLI or MCP hosts.
3. Start with `list-instances`, connect to the intended instance, and verify the selected project independently.
4. Run a read-only domain smoke test before mutation.
5. Use a disposable project or a test object inside a disposable unsaved session for mutations.
6. Dry-run first when the command supports it.
7. Apply with explicit confirmation, perform a fresh read to verify the result, then execute and verify cleanup or the inverse change.
8. Compile after PLC or hardware mutations when supported.

Use the domain skill for operation-specific safety. Keep MCP calls serial.

## Tier 4: Transactions And Broad Refactors

Use Tier 4 for transaction coordinators, rollback behavior, worker/host decomposition, cross-domain changes, major refactors, or changes whose failure could leave a project partially mutated.

Minimum gates:

1. Run all applicable lower-tier gates.
2. Create one immutable archive and retrieve independent copies for portal lifecycle, PLC, hardware, and HMI when those failure domains are in scope.
3. Capture baseline inventories, compilation results, and hashes for reversible exported state.
4. Test success, dry-run, inverse/cleanup, and applicable adversarial paths.
5. Force at least one real mid-transaction failure and verify rollback from a fresh read or canonical hash.
6. Reopen the disposable project in a fresh worker/TIA session and repeat final health reads.
7. Finish with a build/test soak, archive-integrity check, process audit, and clean-worktree check.

## Disposable-Project Safety Protocol

1. Discover and record the user's original project path, TIA PID, and relevant child processes.
2. Never mutate the original open project during a validation campaign.
3. Create or reuse an explicit baseline archive, record its size and SHA-256, and make it read-only when practical.
4. Retrieve or copy the archive to a unique short run root such as `C:\OTT\Live\<run-id>` to reduce TIA and Windows path-length risk.
5. Use separate copies per failure domain when tests can interfere with each other.
6. Keep mutation artifacts under an ignored or external evidence root.
7. Before terminating a process, prove it belongs to the toolkit campaign. Never kill the user's original TIA instance or its helper children.
8. After a timeout, allow the active serial operation to settle, reconnect, and inspect state before retrying. Terminate only toolkit-owned MCP, worker, testhost, or disposable TIA processes when recovery requires it.
9. Close disposable sessions without saving when persistence is not under test. Otherwise apply and verify an explicit inverse cleanup.
10. Recompute the baseline archive hash at the end.

## Adversarial Coverage

Select the cases applicable to the changed contract:

- mutation without confirmation is denied before writes;
- stale base hash, stale plan, or changed target is rejected;
- one-use plan or blueprint token replay is rejected;
- ambiguous selection and duplicate-name collisions fail closed;
- partial success followed by a later failure rolls back the whole transaction;
- restored canonical hash or fresh exported state matches the pre-transaction state;
- worker crash, reset, reconnect, and cancellation do not corrupt the host session;
- invalid paths and repeated open/close operations fail cleanly or remain idempotent;
- address collisions or unsafe online/download targets remain blocked.

Do not manufacture unsafe parameter values merely to claim coverage. Record a safety skip when no reversible target with known semantics exists.

## Evidence And Cleanup

Record:

- branch and exact commit;
- TIA version, source project, disposable paths, and archive SHA-256;
- build configuration and warning/error counts;
- exact test totals and durable TRX/report paths;
- observed CLI command and MCP tool exposure;
- baseline and final inventories, hashes, and compile results;
- dry-run, apply, verification, inverse, and rollback outcomes;
- explicit environment or safety skips and their reasons;
- final process list, worktree state, and remote tracking state.

Use a single report for a large campaign. Lead with `PASS`, `FAIL`, or `BLOCKED`, then distinguish required failures from safe skips and documentation findings.

## Pull Request And Merge Gate

1. Fetch the remote and resolve its actual default branch instead of assuming `main` or `master`.
2. Confirm the feature branch is pushed, clean, and not behind its target.
3. Use a merge-tree or pull-request mergeability check before opening the PR.
4. Put the validation summary and justified skips in the PR body.
5. Wait for required remote checks. A locally successful campaign does not replace CI.
6. Merge only when the PR is clean and required checks pass.
7. Fetch the remote again and verify the target branch contains the merge commit.
