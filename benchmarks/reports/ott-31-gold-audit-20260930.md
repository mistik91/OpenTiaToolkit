# PASS — ott-31 bidirectional VCI gold audit

- Date: 2026-09-30
- Branch: `codex/vci-sync-benchmark`
- TIA Portal: V21 Update 2, build `2100.0.121.1`
- Project: disposable `OTT_Benchmark` copies only
- PLC: `PLC_Main`, S7-1500

## Immutable fixture

| Field | Value |
| --- | --- |
| Fixture ID | `tia-v21-vci-10` |
| Archive | `C:\OpenTiaBench\Archives\OTT_Benchmark_V21_Vci_20260930.zap21` |
| Bytes | 5,595,516 |
| SHA-256 | `B058929F708DB5A5781428238D04B627C4611A0F15C40CE46D4CCC742E76C773` |
| Read-only | yes |
| Parent fixture | `tia-v21-baseline-1` |

The fixture adds three root SCL FCs and no VCI workspace definition:

| Block | Baseline implementation | Fresh source SHA-256 |
| --- | --- | --- |
| `OTT31_ProjectSource` | `#Result := #Value + 10;` | `9DB2505B49C737242470904971E360C1D9CB6C292F1ED971E873982E3FDD7A46` |
| `OTT31_WorkspaceTarget` | `#Result := #Value + 20;` | `CFC6162A54D6A58938C6252CD418DAFB21678333AE7C2BF3D54F154570178468` |
| `OTT31_Distractor` | `#Result := #Value + 30;` | `60794504D85A62942A14DC8F30CD937F6B7C105F569E8DB8A4A69D68749A3DA8` |

The archive was restored on a fresh TIA process into a second disposable root.
That restore contained zero VCI workspaces, all three exact sources matched the
hashes above, and a fresh PLC compile returned 0 errors and 0 warnings.

## Fixture construction safety

The first fixture-source attempt used an invalid FC return assignment. Its
capability-bound transaction reached a three-error compile, automatically
rolled back all three newly created targets, recompiled at 0 errors and 0
warnings, and reported `RolledBackAndVerified=true` with no recovery required.
The corrected UTF-8 BOM source used `Void` FCs with explicit `Result : DInt`
outputs, validated as `SCL`/`verified`, and was applied once through a new
transaction. That transaction created exactly the three missing FCs, compiled
0/0, and canonically verified every target before the fixture was archived.

## Workspace and format preflight

A new workspace `OTT31_Gold` was created below the disposable audit root. TIA
reported no supported formats before any workspace existed; after creation,
all three FCs reported `SCL`, `SimaticML`, and `SimaticSD`. This demonstrates
that V21 format discovery is workspace-dependent and justifies the generic
workspace-before-format ordering in the VCI skill.

The three FCs were exported as flat-root SCL mappings for the audit. Every
initial mapping reported `Equal`. The benchmark maps only the two task targets;
the distractor was mapped in the gold audit solely to verify its comparison
state and was removed with the disposable workspace before fixture creation.

## Directional gold proof

Two mapped files were changed outside TIA:

- `OTT31_ProjectSource.scl`: `+10` to the deliberately stale `+99`;
- `OTT31_WorkspaceTarget.scl`: `+20` to the reviewed incoming `+25`.

Both exact statuses changed from `Equal` to `Unequal`. A
workspace-to-project call for `OTT31_WorkspaceTarget` without confirmation was
rejected by policy and left its status `Unequal`, proving no mutation.

The confirmed directional path then succeeded:

1. exact `project-to-workspace` synchronization restored
   `OTT31_ProjectSource.scl` from `+99` to `+10`;
2. exact `workspace-to-project` synchronization imported `+25` into
   `OTT31_WorkspaceTarget`;
3. all exact mappings returned `Equal`;
4. an independent native-source export read back `+10`, `+25`, and `+30`;
5. a fresh PLC compile returned 0 errors and 0 warnings.

The imported `+25` file also passed the toolkit SCL validator as `verified`
with a UTF-8 BOM, one exact FC declaration, and no errors or warnings.

## Reset and cleanup

The incoming target was synchronized back to `+20`, the project recompiled
0/0, and independent sources proved the original `+10/+20/+30` fixture state.
The VCI workspace definition was deleted and a fresh workspace listing returned
zero entries before the project was saved and archived. The original audit
instance exited before the archive was independently restored. Only disposable
projects and workspace files were changed.
