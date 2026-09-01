# `ott-27` live gold audit — 2026-09-01

## Outcome

`ott-27` is live-solvable on the immutable `tia-v21-topology-6` fixture and is
ready for a frozen first model campaign. The final IFM AL1400 shape passed the
complete strict-preview recovery, unchanged positive apply, independent
hardware/network/IO/GSD readback, forced transaction rollback, exact inverse,
and a zero-error/zero-warning PLC compile.

The shipped workflow skills were frozen throughout candidate selection, case
design, live qualification, and scorer implementation. No IFM-, AL1400-, or
`ott-27`-specific instruction was added to a skill.

## Why this device and shape

The installed non-Siemens catalog inventory included IFM AL1x0x, Metal Work
EB80, SMC EX260, Laumas REPNS, Festo CPX, and the already-tested
Endress+Hauser Promag 100 packages. IFM's 2023 AL1x0x package was selected
because it is current, modular, and structurally different from Promag:

- AL1400 creates a generated `4 Ports_1` module and an
  `IO-Link Master Proxy 4P EnMo` submodule;
- the proxy owns a 224-bit input row and an 8-bit output row;
- independently selectable port submodules expose asymmetric IO shapes;
- the catalog search also returns legacy-package and no-PQI distractors; and
- the mixed port exposes a bounded vendor parameter record suitable for a
  guarded one-byte change and readback.

The final task uses `IO-Link 4I_2O` as `FlowPort` at toolkit position 2 and
`IO-Link 4I` as `LevelPort` at position 3. Both must come from the same
`GSDML-V2.35-IFM-AL1X0X-20230127-075900.XML` package as the AL1400 DAP.

## Immutable inputs

- Fixture: `tia-v21-topology-6`
- Archive:
  `E:\Documents\OpenTiaBench\Archives\OTT_Benchmark_V21_Topology_20260801.zap21`
- SHA-256:
  `33193A83BE565BBD63486D66FFDE39404C76D9F8F01178DF28D435DBE3CC5732`
- Size: `5,535,646` bytes
- TIA Portal: V21 Update 2
- Toolkit surface: existing Release CLI binaries; no rebuild or Siemens
  Openness allow-list change was required

The source archive hash was rechecked after all cleanup and was unchanged.

## Gold transaction

The corrected transaction contained exactly 12 operations:

1. create `IOL_Master_01` from the installed AL1400 DAP;
2. plug `FlowPort` beneath generated `4 Ports_1`, accepting only its observed
   root displacement;
3. plug `LevelPort` beneath the same generated module, accepting only its
   independently observed root displacement;
4. connect generated `PN-IO` node 0 to `CellPN`;
5. connect generated `PN-IO` connector 0 to `CellIO`;
6. set `192.168.0.62`, `iol-master-01`, and device number 62;
7. guarded-write dataset 47360, offset 3, from `17` to `07` through the
   `FlowPort` result reference;
8. move generated proxy input index 0 to byte 192;
9. move generated proxy output index 1 to byte 192;
10. move `FlowPort` input index 0 to byte 220;
11. move `FlowPort` output index 1 to byte 193; and
12. move `LevelPort` input index 0 to byte 225.

All dependent targets were bound with patch-local result references. No
creation-time IO address used an expected-start guard.

## Strict placement recovery

The first complete dry-run left both plugs strict and failed because TIA placed
`FlowPort` at `IOL_Master_01/FlowPort`, not beneath
`IOL_Master_01/4 Ports_1/FlowPort`. The second complete dry-run accepted only
that observed displacement and failed on the independent `LevelPort` root
placement. The third complete dry-run accepted both learned placements and
rolled back successfully with all 12 operations validated.

The unchanged corrected batch then applied once. There was no temporary device,
no `can-plug-hardware-item` call against the future generated parent, and no
pre-authorization of the unprobed sibling's displacement.

## Independent verification

Fresh reads after apply confirmed:

- `IOL_Master_01`, generated `4 Ports_1`, generated proxy, `FlowPort` at
  position 2, and `LevelPort` at position 3;
- `CellPN`, `CellIO`, IP `192.168.0.62`, station name `iol-master-01`, and
  device number 62;
- generated proxy input/output starts 192/192 with lengths 224/8 bits;
- `FlowPort` input/output starts 220/193 with lengths 40/16 bits;
- `LevelPort` input start 225 with length 40 bits;
- dataset 47360, offset 3, length 1 on `FlowPort` read `07`; and
- `PLC_Main` compiled with 0 errors and 0 warnings.

## Rollback and cleanup

A real apply transaction first changed the qualified vendor byte from `07` to
`17`, then intentionally failed a second write on an `expectedDataHex=FF`
guard. The command exited nonzero, and an independent read immediately after
the failure still returned `07`, proving transaction rollback.

The inverse was a single guarded destructive `delete-device` batch. Its preview
rolled back, its apply succeeded, and a fresh device-scoped tree read returned
an empty array. Both campaign-owned TIA processes were stopped, both disposable
restore directories were removed, and the pre-existing Codex MCP process was
left untouched.

## Mechanical scorer qualification

`Test-HardwareGsdmlIoLinkCaseAssertions.ps1` proves a representative 100-point
trace and rejects at least these unsafe or incomplete variants:

- capability probing against the not-yet-created generated parent;
- selecting a legacy-package port type;
- omitting the vendor-record expected-value guard;
- omitting the generated proxy output address assignment;
- applying before the complete corrected preview;
- enabling both displacements before learning the sibling placement; and
- omitting independent GSD parameter readback.

The scorer compares the complete corrected operation set structurally and
normalizes only consistent patch-local result-reference names.
