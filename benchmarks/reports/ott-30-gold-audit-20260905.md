# ott-30 native SCL gold audit — 2026-09-05

## Scope

The audit proves the native-SCL authoring path for `ott-30` against a fresh, unsaved restore of fixture `tia-v21-baseline-1`. The candidate contains one new PLC data type and one new stateful function block in a single UTF-8 BOM SCL source and a single capability-bound PLC code transaction.

## Immutable baseline

- Archive: `OTT_Benchmark_V21_20260731.zap21`
- SHA-256: `1C17F223F00F0A43EFD5E51A7C8E2DBE4DD424540AE0C9A799F102997205AB74`
- Size: 5,582,162 bytes
- TIA Portal: V21 Update 2, build `2100.0.121.1`
- Project: `OTT_Benchmark`
- PLC: `PLC_Main`, S7-1500
- Baseline compile: 0 errors, 0 warnings

## Candidate and offline validation

- External audit source: `C:\OpenTiaBench\Artifacts\ott-30-gold-20260905\OTT30_RecipeWindowMonitor.scl`
- Candidate SHA-256: `B5393C8C4276FBEE18C7BD7495E98F5B6B8617DEED7B5101E261F9573E6351C2`
- Encoding: UTF-8 with BOM (`EF BB BF`)
- Declared objects: `OTT30_RecipeWindow` (`TYPE`) and `OTT30_RecipeWindowMonitor` (`FB`)
- Offline result: valid, `SCL`, `verified`, no dependencies, errors, or warnings

The source exercises a UDT-backed interface, fixed arrays, a bounded `FOR`, absolute-tolerance handling, lowest-index selection, a three-state `CASE` with safe `ELSE`, and static `R_TRIG` and `TON_TIME` instances.

## Live transaction proof

Fresh Create capability discovery resolved both exact missing targets and selected `DirectScl`. Transaction `53032480688d427f87ade95e2208d0cd` bound both capability IDs under `CleanCompile` and contained one `GenerateSclSource` operation.

Planning classified both targets as safe creates with `BaselineHash=missing`, produced one apply token, and retained a zero-error/zero-warning baseline compilation. Apply returned:

- state `Succeeded`;
- `Applied=true` and `Verified=true`;
- no rollback and no recovery requirement;
- final compile 0 errors, 0 warnings;
- canonical type hash `846160c6f0eaf012a83eb211cb476c202625eb1c72c9585dba7216d0b1059d36`;
- canonical FB hash `596958bf182b01ec03b74d3399bd98ec8a098dace1b5dac8cddb8107be7158d4`.

## Independent readback and cleanup

`get-block-info` returned `OTT30_RecipeWindowMonitor` as FB number 1 in SCL. The FB exported successfully as SCL and the type exported successfully as XML. Fresh Modify capability discovery found both exact existing targets and again selected `DirectScl`; the FB discovery also observed live SCL-source export. An independent compile completed with 0 errors and 0 warnings.

The project was closed explicitly without saving. The immutable archive was not changed.
