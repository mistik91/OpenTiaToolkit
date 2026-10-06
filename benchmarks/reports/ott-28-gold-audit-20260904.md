# `ott-28` live gold audit — 2026-09-04

## Outcome

`ott-28` is live-solvable on the immutable
`tia-v21-unified-alarms-8` fixture and is ready for an initial model campaign.
The gold path passed complete canonical discovery, a hash-bound partial update,
computed ID and trigger-bit allocation, atomic update/create dry-run and apply,
fresh readback, exact-name guarded retirement, near-name preservation, and a
real second-operation rollback.

The shipped workflow skills were frozen throughout fixture design, live
qualification, and scorer implementation. No mixer-, alarm-name-, fixture-, or
`ott-28`-specific example was added to a skill.

## Immutable inputs

- Parent fixture archive:
  `C:\OpenTiaBench\Archives\OTT_Benchmark_V21_20260731.zap21`
- Parent SHA-256:
  `1C17F223F00F0A43EFD5E51A7C8E2DBE4DD424540AE0C9A799F102997205AB74`
- New fixture: `tia-v21-unified-alarms-8`
- New archive:
  `C:\OpenTiaBench\Archives\OTT_Benchmark_V21_UnifiedAlarms_20260904.zap21`
- New SHA-256:
  `316B538B73F1DF41FBC07DBD0DA5963F039E552568EB1984604FE854DBE9BD52`
- New size: `5,655,259` bytes
- TIA Portal: V21 Update 2
- Project: `OTT_Benchmark`
- Unified HMI: `HMI_RT_1`
- Active alarm-text culture: `en-US`

The new archive was marked read-only. Its size and SHA-256 were recomputed
after the independent restore, positive gold path, rollback campaign, and TIA
cleanup and remained unchanged.

## Fixture construction

The parent archive contained no ordinary Unified HMI tags and no discrete
alarms. A one-purpose fixture helper outside the repository used the official
V21 Openness API only to create tag table `OTT28 Fixture` and these internal
HMI tags:

| Tag | Data type | Validation |
| --- | --- | --- |
| `MixerAlarmBits` | `Int` | 0 diagnostics |
| `MixerSpeed` | `Real` | 0 diagnostics |
| `BatchNumber` | `DInt` | 0 diagnostics |

These are internal HMI tags, so the fixture intentionally contains no
HMI-to-PLC connection. A future benchmark that uses PLC-backed HMI tags must
provision and independently validate that connection.

The helper bootstrap executable SHA-256 was
`511BCCCF58171AD7ACD57E722F4260EDDC6AF02E706FFEEAC54AB4EB3B3BD592`
and was registered in the V21 Openness allow list before attachment. All alarm
creation and every subsequent read or mutation used the ordinary
OpenTiaToolkit Release CLI surface.

The baseline alarm batch first executed as a real rolled-back transaction with
`fail-on-warning=true`. All five proposed alarms had empty warning, error, and
Siemens validation collections. The identical batch then committed and the
project was explicitly saved before archival.

## Baseline state

| Alarm | ID | Trigger | Purpose | Canonical SHA-256 |
| --- | ---: | --- | --- | --- |
| `Mixer_Trip_Legacy_Display` | 2398 | `MixerAlarmBits`, bit 4 | near-name preservation distractor | `2eaba142159d40a68202a0e7939374a74495c34cade687565d18b1bff566925c` |
| `Mixer_Trip_Legacy` | 2399 | `MixerAlarmBits`, bit 3 | exact unreferenced retirement target | `f01f6f6649ff1a83369281154deebed14914bae2a35eb033b75dfda74c1278f8` |
| `Mixer_Overload` | 2400 | `MixerAlarmBits`, bit 0 | hash-bound partial-update target | `9eedff2c8971afc5c2c87259c9bf801cd8bcbed1b7fdf3e11a5ddda3054c641a` |
| `Mixer_Id_Distractor` | 2401 | `MixerAlarmBits`, bit 1 | occupied-ID distractor | `dc2a08671f6793af21c68e0b6da144e98854dc31808a7adfaf1d9b6dbd07f9ea` |
| `Mixer_Bit_Distractor` | 2403 | `MixerAlarmBits`, bit 2 | occupied-trigger distractor | `a3d9b4a89092ee072ae09b169e87a41ecb5ac33845308c94455985526b5cabb6` |

Every snapshot reported an empty Siemens `Validation` collection. The occupied
state makes ID 2402 the lowest free value in 2398-2405 and bit 5 the lowest
free value in 0-7.

## Positive gold path

The versioned two-alarm document contained:

1. a partial update of `Mixer_Overload`, bound to its baseline canonical hash,
   changing only class, priority, and the two requested English texts; and
2. creation of `Mixer_SpeedHigh` with computed ID 2402, computed bit 5, class
   `Critical`, priority 14, and ordered parameters `MixerSpeed`, `BatchNumber`.

The complete batch dry-run succeeded with zero warnings and rolled back. The
unchanged document then applied once. Fresh exact reads returned:

- `Mixer_Overload` canonical hash
  `dfc7126ba2e9948c3d76373a1e77c575d7dc3e9989de56a9767f1fb7e8f7f629`;
- `Mixer_SpeedHigh` canonical hash
  `0f12d0427b2f66a88fa16cdf673e192c7e6fbd13ad515314794daa684cb53322`;
- the preserved `Mixer_Overload` ID, origin, area, trigger, trigger mode, and
  parameter order; and
- empty Siemens validation diagnostics on both results.

A fresh exact read then bound retirement of `Mixer_Trip_Legacy` to hash
`f01f6f6649ff1a83369281154deebed14914bae2a35eb033b75dfda74c1278f8`.
The delete dry-run reported zero cross-references and rolled back. The same
hash applied once without `allow-referenced-delete`. Final reads proved the
exact target absent and `Mixer_Trip_Legacy_Display` unchanged at its baseline
hash. The gold project was closed without saving, so the restored fixture copy
returned to its archived baseline.

## Forced transaction rollback

A separate apply transaction first changed `Mixer_Overload.Priority` from 8
to 14, then attempted to create `Mixer_RollbackProbe` with deliberately invalid
Siemens bit values on the second operation. The command returned
`SiemensRejected` and stated that the batch was rolled back.

Fresh exact reads in the still-attached session proved:

- `Mixer_Overload` was back at priority 8 and baseline hash
  `9eedff2c8971afc5c2c87259c9bf801cd8bcbed1b7fdf3e11a5ddda3054c641a`;
- `Mixer_RollbackProbe` returned exact-name not found; and
- no project save occurred.

## Mechanical scorer qualification

`Test-HmiDiscreteAlarmCaseAssertions.ps1` proves a representative 100-point
trace and rejects at least these unsafe or incomplete variants:

- applying the reconciliation batch before its dry-run;
- splitting the two-alarm reconciliation into multiple previews/applies;
- choosing the wrong computed ID or trigger bit;
- omitting discovery of the internal HMI tag types and table;
- omitting the exact post-retirement not-found read;
- omitting fresh proof that the near-name distractor survived;
- enabling the referenced-delete override; and
- saving the disposable benchmark project.

The scorer binds baseline and final snapshots to the observed canonical hashes
and requires the two inventory reads, two exact mutation readbacks, exact
retirement hash, zero-reference deletion preview, committed deletion, and
ordered preview/apply/readback/retirement sequence.

## Cleanup

The approved source project and all toolkit-owned hidden TIA sessions were
closed. The dedicated visible V21 instance remains open with no project, as do
the pre-existing OpenTiaToolkit MCP and V21 worker processes. The independently
restored verification directory and runner `-PrepareOnly` directory are closed
and no longer used. Recursive removal was blocked by the execution policy, so
they remain for manual cleanup at:

- `C:\OTT\Verify\ott-28-archive-20260904`; and
- `C:\OTT\Bench\20260904T075846466Z-ott-28-t1-7BB7A1D3`.
