# PASS - OTT-23 topology and addressing transfer gold audit

TIA Portal: V21 Update 2

## Anti-overfitting boundary

`ott-23` was designed after skill tuning stopped on `ott-22`. The case reuses
the immutable topology baseline but changes the station/module identities,
module widths and catalog identifiers, positions, PROFINET identity, and
process-image ranges. It also adds two result-referenced address assignments
through generated module children; `ott-22` did not assign process-image
addresses.

No skill was edited while the task, assertions, or gold operations were
designed. At the design boundary (`2190a06`), canonical skill SHA-256 values
were:

- `hardware-configuration-workflow`:
  `9A6826F13940189D83E79E740C2C8BE71A6A31760E50BE96A77722EA155E4F10`
- `basic-tia-interaction`:
  `F945E06387E6A3A6E41AA29A13F9D14C43ACF87BB3A5313540153D44F8824017`
- `opentia-toolkit-workflow`:
  `EB9453E4DAFA011373D947EB75B3AC1DCC33F9E59D21CA51340A0D3B40C8267C`

The skills contain none of the new station/module names, nonadjacent positions,
article-number pair, IP address, station name, device number, or IO starts.

## Immutable fixture

- Fixture id: `tia-v21-topology-6`
- Archive: `OTT_Benchmark_V21_Topology_20260801.zap21`
- Size: `5,535,646` bytes
- SHA-256:
  `33193A83BE565BBD63486D66FFDE39404C76D9F8F01178DF28D435DBE3CC5732`
- Read-only: yes

The baseline contains `CellPN` and controller IO system `CellIO`, but no
`PackagingBay` station. Its existing input ranges are 10-11, 20-21, and 40-41;
its output ranges are 14-15, 28-29, and 90-91. Requested input bytes 72-73 and
output bytes 140-141 were therefore independently free.

Baseline hashes:

- complete hardware tree:
  `21B5A95493232E763A1950C75FC543299E220F510752E8F8B508CF4AC614EA60`
- complete hardware network topology:
  `544BD1EC06DA439C058DB69D64FCBDCF71B1927FC9438F3285C47EE658120A1F`
- complete hardware IO-address CSV:
  `1A1B0201C9C9CF2EAA94D874F1A7E667EEB41AD69FEAFEDDAC46D80ACEDDE97F`

## Catalog and strict-placement proof

Focused catalog lookup resolved:

- IM 155-6 PN ST: `OrderNumber:6ES7 155-6AU02-0BN0/V6.4`
- DI 16x24VDC ST: `OrderNumber:6ES7 131-6BH01-0BA0/V0.0`
- DQ 16x24VDC/0.5A ST: `OrderNumber:6ES7 132-6BH01-0BA0/V0.0`

The first strict dry-run reported `PackagingBay/Photoeyes` instead of the
requested `PackagingBay/Rack_0/Photoeyes` and rolled back. After accepting only
that placement, the transaction assigned input start 72 through result
reference `photoeyes` plus relative generated child `Photoeyes`, then reported
the independent output displacement at `PackagingBay/Pneumatics` and rolled
back. The complete corrected dry-run succeeded and rolled back.

Fresh tree, network, and IO hashes after these probes remained exactly equal to
all three baseline hashes.

## Positive proof

One eight-operation batch:

1. created `PackagingBay` using the V6.4 interface module;
2. plugged `Photoeyes` at position 3 and named its result;
3. assigned its generated child input start to 72 through that result;
4. plugged `Pneumatics` at position 5 and named its result;
5. assigned its generated child output start to 140 through that result;
6. connected node 0 to `CellPN`;
7. connected connector 0 to `CellIO`; and
8. configured `192.168.0.48`, `packaging-bay`, and device number 48.

Fresh tree, network, and IO reads proved every requested identity, position,
connection, address type, 16-bit length, and start. `PLC_Main` compiled with
zero errors and zero warnings.

## Inverse and adversarial proof

A guarded destructive preview and apply deleted only `PackagingBay`. Fresh
tree, network, and IO exports matched all baseline hashes exactly.

A separate real apply then executed the complete valid eight-operation state
under unrelated rollback identities and appended a ninth module plug into the
already occupied new position 5. Siemens rejected `LateCollision` only after
the prior eight operations had succeeded. The transaction reported
`Applied: false`, `RolledBack: true`. Fresh tree, network, and IO hashes again
matched baseline, and compilation completed with zero errors and warnings.

Exploratory duplicate IO-address and duplicate-IP values were accepted by this
TIA API path and are not claimed as failure or rollback evidence.

Finally, a newly started TIA Portal process independently restored the
read-only archive. All three baseline hashes matched again and `PLC_Main`
compiled with zero errors and zero warnings.
