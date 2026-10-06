# PASS - OTT-22 topology provisioning gold audit

TIA Portal: V21 Update 2

## Anti-overfitting boundary

`ott-22` is a new topology domain rather than another address-remapping
variant. No skill was edited while the task, fixture, assertions, or gold
operations were designed. At the design boundary (`d60779c`), the canonical
skill SHA-256 values were:

- `hardware-configuration-workflow`: `1EE8472F1030771B03EFCD53968F951F9D49B7EA4F1A927C4BEA33FF431427DC`
- `basic-tia-interaction`: `F945E06387E6A3A6E41AA29A13F9D14C43ACF87BB3A5313540153D44F8824017`
- `opentia-toolkit-workflow`: `EB9453E4DAFA011373D947EB75B3AC1DCC33F9E59D21CA51340A0D3B40C8267C`

The skills contain none of the fixture's `CellPN`/`CellIO` names, requested
station and module names, article-number combination, IP address, station
name, or PROFINET device number.

## Immutable fixture

- Fixture id: `tia-v21-topology-6`
- Archive: `OTT_Benchmark_V21_Topology_20260801.zap21`
- Size: `5,535,646` bytes
- SHA-256: `33193A83BE565BBD63486D66FFDE39404C76D9F8F01178DF28D435DBE3CC5732`
- Read-only: yes

The fixture was created from the read-only held-out transfer archive. It adds
only a pre-existing Ethernet subnet `CellPN`, connects the `PLC_Main`
PROFINET controller node to that subnet, and creates controller IO system
`CellIO`. It contains no `RemoteCell` station.

An independent restore established these canonical hashes:

- complete hardware tree:
  `21B5A95493232E763A1950C75FC543299E220F510752E8F8B508CF4AC614EA60`
- complete hardware network topology:
  `544BD1EC06DA439C058DB69D64FCBDCF71B1927FC9438F3285C47EE658120A1F`

The restored fixture compiled with zero errors and zero warnings.

## Strict placement and positive proof

Focused catalog lookup resolved:

- IM 155-6 PN ST: `OrderNumber:6ES7 155-6AU02-0BN0/V6.4`
- DI 8x24VDC ST: `OrderNumber:6ES7 131-6BF01-0BA0/V0.0`
- DQ 8x24VDC/0.5A ST: `OrderNumber:6ES7 132-6BF01-0BA0/V0.0`

A first strict dry-run created the station transiently and requested
`GuardInputs` below `Rack_0`. TIA placed it at the station root instead. The
toolkit rejected that mismatch, reported the actual path, and rolled the
transaction back. A second dry-run opted in only for that observed input
placement; it independently reached `ValveOutputs`, reported the corresponding
root placement, and rolled back again. Fresh tree and network hashes remained
exactly equal to baseline after both probes.

The corrected six-operation batch opted into the two observed displaced
placements and used result references for the new station. Both dry-run and
apply succeeded. Fresh reads proved:

- `RemoteCell` is an `System:Device.ET200SP` station using the requested V6.4
  interface module;
- `GuardInputs` is at position 1 and `ValveOutputs` is at position 2;
- its node is connected to `CellPN` with address `192.168.0.37` and station
  name `remote-cell`;
- connector 0 is connected to `CellIO` with PROFINET device number `37`;
- `PLC_Main` compiles with zero errors and zero warnings.

## Inverse and adversarial proof

A guarded destructive preview and apply deleted only `RemoteCell`. Fresh tree
and network exports matched both baseline hashes exactly, and compilation
again completed with zero errors and zero warnings.

A separate real apply then created `RollbackCell`, successfully plugged an
input module at position 1, and attempted an output module at the already
occupied position 1. Siemens rejected operation 3. The result reported
`Applied: false` and `RolledBack: true`; fresh tree and network hashes matched
baseline and the PLC compiled cleanly.

Finally, a newly started TIA Portal process independently retrieved the
read-only archive. Both canonical hashes matched again and compilation
completed with zero errors and zero warnings.
