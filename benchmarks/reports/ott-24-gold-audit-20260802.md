# PASS - OTT-24 ET 200MP family-transfer gold audit

TIA Portal: V21 Update 2

## Anti-overfitting boundary

`ott-24` was designed after all `ott-23` skill tuning and scoring work stopped.
It transfers the same generic workflow to a different Siemens remote-I/O
family, ET 200MP, with unrelated catalog identifiers, device and module names,
rail positions, network identity, and process-image ranges. No skill was edited
while the task, assertions, or gold operations were designed.

At the design boundary (`2bd334e`), active skill SHA-256 values were:

- `hardware-configuration-workflow`:
  `1526365EA8077DF55C11F4ADB28528C068754A970895049643C091E12D5D10D2`
- `basic-tia-interaction`:
  `52F97EC4E51112AEF298A89F7C1166B575CA89F00FC9CFBF465936543FEEA479`
- `opentia-toolkit-workflow`:
  `249A0C8D4C3A23F5539522E35688E17F8DABE7D3F4EC13D3BCE24D6A7F9A0299`

The skills contain none of the new station or module names, article numbers,
positions, IP address, station name, device number, or IO starts.

## Immutable fixture

- Fixture id: `tia-v21-topology-6`
- Archive: `OTT_Benchmark_V21_Topology_20260801.zap21`
- Size: `5,535,646` bytes
- SHA-256:
  `33193A83BE565BBD63486D66FFDE39404C76D9F8F01178DF28D435DBE3CC5732`
- Read-only: yes

The baseline contains `CellPN` and controller IO system `CellIO`, but no
`QualityCell` station. Existing input ranges are 10-11, 20-21, and 40-41;
existing output ranges are 14-15, 28-29, and 90-91. Requested input bytes
96-97 and output bytes 152-153 were independently free.

Baseline hashes:

- complete hardware tree:
  `21B5A95493232E763A1950C75FC543299E220F510752E8F8B508CF4AC614EA60`
- complete hardware network topology:
  `544BD1EC06DA439C058DB69D64FCBDCF71B1927FC9438F3285C47EE658120A1F`
- comparable hardware IO-address CSV:
  `1A1B0201C9C9CF2EAA94D874F1A7E667EEB41AD69FEAFEDDAC46D80ACEDDE97F`

## Catalog and exact retry proof

Focused live-catalog lookup resolved the highest installed exact versions:

- IM 155-5 PN ST: `OrderNumber:6ES7 155-5AA01-0AB0/V4.1`
- DI 16x24VDC HF: `OrderNumber:6ES7 521-1BH00-0AB0/V2.2`
- DQ 16x24VDC/0.5A HF: `OrderNumber:6ES7 522-1BH01-0AB0/V1.1`

The first strict dry-run reported `QualityCell/InspectionInputs` instead of
the requested `QualityCell/Rail_0/InspectionInputs` and rolled back. A second
strict dry-run accepted only that observed displacement, successfully assigned
input start 96 through result reference `inspection-inputs` plus generated
child `InspectionInputs`, then reported the independent displacement at
`QualityCell/RejectOutputs` and rolled back. Neither generated address write
used a guessed expected-state guard. The complete corrected dry-run accepted
only the two reported placements, succeeded, and rolled back.

## Positive proof

One eight-operation batch:

1. created `QualityCell` using the V4.1 interface module;
2. plugged `InspectionInputs` at position 2 and named its result;
3. assigned its generated-child input start to 96 through that result;
4. plugged `RejectOutputs` at position 4 and named its result;
5. assigned its generated-child output start to 152 through that result;
6. connected node 0 to `CellPN`;
7. connected connector 0 to `CellIO`; and
8. configured `192.168.0.56`, `quality-cell`, and device number 56.

Independent reads proved the exact catalog identities, positions, generated
child paths, IO types, 16-bit lengths, starts, subnet, IO system, and PROFINET
identity. `PLC_Main` compiled with zero errors and zero warnings.

## Inverse, adversarial rollback, and fresh-process proof

A destructive preview and apply deleted only `QualityCell`. Complete tree,
network, and comparable IO exports matched all three baseline hashes exactly.

A separate real apply executed all eight valid operations and appended a ninth
DI plug into occupied new position 4. Siemens rejected `CollisionProbe` after
the first eight operations had succeeded. The transaction reported
`Applied: false`, `RolledBack: true`; fresh tree, network, and IO exports again
matched the baseline hashes exactly.

Finally, a newly started TIA Portal process independently restored the
read-only archive, previewed and applied the complete positive transaction,
and reproduced byte-identical applied tree and network snapshots. Independent
readback and compilation again passed, after which the inverse restored all
three baseline hashes exactly. Both toolkit-owned TIA processes were closed
without saving.
