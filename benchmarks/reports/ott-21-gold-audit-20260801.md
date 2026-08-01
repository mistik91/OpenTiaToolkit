# PASS — OTT-21 held-out transfer gold audit

TIA Portal: V21 Update 2

## Anti-overfitting boundary

`ott-21` was created to qualify the unchanged PLC/tag skill from commit
`81558825381cb8ff403491f8b4363a1d460dc09c`. The skill contains none of this
fixture's module names, tag names, baseline addresses, destination addresses,
bit offsets, or descending-position rule. No skill file was changed while the
fixture, case, scorer, or campaign was designed and audited.

## Immutable fixture

- Archive: `OTT_Benchmark_V21_Transfer_20260801.zap21`
- Size: `5,582,666` bytes
- SHA-256: `B57573BBD33E185D7B843CDED87362F6BF08932944388508BFD934D575C35A56`
- Read-only: yes

The fixture was built from the read-only V4 cross-domain archive in an
external disposable directory. The four movable tags were renamed and moved
to unrelated logical addresses with nonzero bit offsets. Parent hardware
modules were renamed through guarded `set-attributes`; Siemens V21 correctly
rejected a nested-item `Name` write during dry-run, rolled it back, and the
supported parent rename was used instead.

| Space | Role | Hardware row | Position | Baseline | Length |
| --- | --- | --- | ---: | ---: | ---: |
| Input | fixed | `RackInputsFixed` | 2 | 40 | 16 bits |
| Input | movable | `ConveyorSense` | 3 | 10 | 16 bits |
| Input | movable | `SkidSignals` index 0 | 6 | 20 | 16 bits |
| Output | fixed | `RackOutputsFixed` | 4 | 90 | 16 bits |
| Output | movable | `GateActuator` | 5 | 14 | 16 bits |
| Output | movable | `SkidSignals` index 1 | 6 | 28 | 16 bits |

Descending position order produces Input starts `42`, `44` and Output starts
`92`, `94`.

| Tag | Baseline | Expected |
| --- | --- | --- |
| `ConveyorPhotoeye` | `%I10.6` | `%I44.6` |
| `SkidReady` | `%I20.3` | `%I42.3` |
| `GateOpen` | `%Q14.5` | `%Q94.5` |
| `SkidValve` | `%Q28.1` | `%Q92.1` |

## Forward and inverse proof

The forward audit previewed the complete tag batch and complete hardware batch
before applying either. Both applies succeeded. Fresh address and tag reads
matched all eight expected final values, and PLC compilation completed with
zero errors and zero warnings.

The inverse audit changed the preview and apply ordering, restored both
domains, and compiled again. The restored files matched the archive baseline:

- Hardware CSV SHA-256:
  `1A1B0201C9C9CF2EAA94D874F1A7E667EEB41AD69FEAFEDDAC46D80ACEDDE97F`
- PLC tag CSV SHA-256:
  `59DEEABB9BE87E479739B007750DEED86BD26AFD850721C8B9D654270EE7F2AF`

## Adversarial proof

A fresh independent restore exercised two failure paths:

1. A valid hardware preview followed by a tag preview with one stale exact
   logical-address selector returned `planned: 3`, `applied: 0`, `failed: 1`.
   Neither domain was applied, and fresh hardware/tag hashes stayed equal to
   baseline.
2. A real two-operation hardware apply moved `ConveyorSense` successfully in
   operation 1, then collided `GateActuator` with `RackOutputsFixed` in
   operation 2. The result reported `Applied: false` and `RolledBack: true`.
   Fresh hardware and tag hashes again matched baseline, and compilation
   completed with zero errors and zero warnings.

Finally, a newly started TIA Portal process independently retrieved the
read-only archive. Its hardware and tag hashes matched the construction
baseline and it compiled with zero errors and zero warnings.
