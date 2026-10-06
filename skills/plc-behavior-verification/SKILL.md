---
name: plc-behavior-verification
description: Use for OpenTiaToolkit PLC code behavioral verification in PLCSIM Advanced, including disposable multi-format probe packages, versioned reset/stimulate/observe/cleanup vectors, simulator capability discovery, exact behavioral evidence, and interrupted-run recovery.
---

# PLC Behavior Verification

Use this only after `$basic-tia-interaction`, `$plc-block-and-tag-workflow`, and the relevant format-authoring skills have established the open project, selected PLC, and valid disposable code artifacts.

## Safety invariants

- Use only `run-plc-code-behavior-probe`; never reproduce its steps with standalone online writes.
- Every package object must have a fresh `Create` capability. Never use production blocks, protected/fail-safe targets, or an existing object as a probe.
- The package must contain a disposable contract DB and scheduler OB. Every vector tag must be a child of a DB declared by that package.
- Real-hardware download is blocked. Stop if PLCSIM identity, CPU article number, controller name, online comparison, or exact fingerprints do not match.
- A run is reusable only when code signature, environment, vector hash, and simulator fingerprint all match and status is `Verified`.
- `Failed` means behavior failed but cleanup was proven. `Quarantined` means cleanup is unproven; run recovery before any further probe.

## Workflow

1. Validate each format artifact with its format skill.
2. Create one package manifest following [package-and-vector-schema.md](references/package-and-vector-schema.md). Use [Codex_Behavior_Package.json](assets/Codex_Behavior_Package.json) as a minimal multi-format example.
3. Run `validate-plc-behavior-package --package-path=...` and `validate-plc-behavior-vector --vector-path=...`.
4. Discover a fresh `Create` capability for every declared FC/FB/DB/OB. Pass every id exactly once.
5. Run `discover-plc-simulator-capabilities --plc-name=...`; pass `instance-name` whenever selection is not exact and unique.
6. Query `query-plc-behavior-evidence`. Reuse only an exact verified match.
7. Run `run-plc-code-behavior-probe ... --only-changes=true --confirm=true`.
8. Require `Verified`, `cleanupProven=true`, `behavior.simulatorCleanupProven=true`, the same offline/online difference set recorded before the probe, and no remaining disposable runtime tags. Pre-existing unrelated differences are retained as a baseline; the probe may not add any.
9. If interrupted or quarantined, run `recover-plc-code-behavior-probe --evidence-id=... --confirm=true`. Recovery never promotes behavior evidence.

## Vector design

- Reset every stateful input explicitly.
- Keep polling bounded. Use `stableMilliseconds` for debounce, timers, and asynchronous convergence.
- Restore every written tag in `cleanup`.
- Use typed primitive tags only. Whole structures and arrays are not supported by the Runtime API.
- Separate scenarios into named steps with observable outcomes; do not infer behavior from compile or download success.
- Keep the scheduler OB in native SCL unless an exported-and-reimported SIMATIC SD OB shape has already been proven for the target. SIMATIC SD OB import may assign a free cycle-OB number, so exact identity verification must never be relaxed.
