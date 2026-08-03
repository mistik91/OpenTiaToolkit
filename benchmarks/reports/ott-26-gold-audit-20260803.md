# PASS - OTT-26 third-party Promag GSDML gold audit

TIA Portal: V21 Update 2

## Anti-overfitting boundary

`ott-26` was designed after the Siemens topology, structural-validator, and
brownfield benchmark work was complete. No skill was edited while the Promag
fixture contract, prompt, assertions, or gold transaction were designed.

At the frozen base commit (`8d42240`), the shipped OpenCode skill SHA-256
values were:

- `hardware-configuration-workflow`:
  `1526365EA8077DF55C11F4ADB28528C068754A970895049643C091E12D5D10D2`
- `basic-tia-interaction`:
  `F945E06387E6A3A6E41AA29A13F9D14C43ACF87BB3A5313540153D44F8824017`
- `opentia-toolkit-workflow`:
  `EB9453E4DAFA011373D947EB75B3AC1DCC33F9E59D21CA51340A0D3B40C8267C`

Those skills contain none of the `FT_101` identity, selected addresses,
Promag catalog identifiers, record index/offset, or vendor byte encodings.
Product-specific facts live only in the case contract and its audit.

## Immutable project fixture and installation-scoped catalog

- Fixture id: `tia-v21-topology-6`
- Archive: `OTT_Benchmark_V21_Topology_20260801.zap21`
- Size: `5,535,646` bytes
- SHA-256:
  `33193A83BE565BBD63486D66FFDE39404C76D9F8F01178DF28D435DBE3CC5732`
- Read-only: yes

The archive supplies `PLC_Main`, Ethernet subnet `CellPN`, controller IO
system `CellIO`, and the existing process-image baseline. It contains no
Promag station and cannot embed a machine-wide TIA hardware catalog package.

Focused live catalog discovery resolved the installed Endress+Hauser package:

- DAP:
  `GSD:GSDML-V2.31-EH-PROMAG 100-20151109.XML/DAP/DAP 1`
- totalizer module: `.../M/50`
- totalizer control: `.../SM/51`
- totalizer mode: `.../SM/58`

Endress+Hauser also publishes a newer download named
`EH_PN_FL_Promag100_0x843A_0x01_0x02_GSDML.zip`. The downloaded official
package had SHA-256
`04E2773E4AFF84992332BC331F3D99CD724ED6A62C5BA44937FD0B0B75247979`
and contained `GSDML-V2.31-EH-PROMAG 100-20200805.xml`. It was inspected but
not installed, so this case deliberately measures the proven 2015 catalog
contract rather than claiming cross-version equivalence. The vendor download
is available from the
[official Promag 100 product page](https://www.pl.endress.com/pl/Produkty/przeplyw/promag-p100).

Baseline evidence from the fresh restore:

- hardware tree:
  `21B5A95493232E763A1950C75FC543299E220F510752E8F8B508CF4AC614EA60`
- network topology:
  `544BD1EC06DA439C058DB69D64FCBDCF71B1927FC9438F3285C47EE658120A1F`
- comparable IO-address CSV:
  `59F175070A07DC73ED94805379CCBABF752553D979BE937D64CD861F22B287DD`

Input bytes 176-180 and output bytes 176-177 were free. The baseline compiled
with zero errors and zero warnings.

## Strict placement and positive proof

`can-plug-hardware-item` proved `TotalizerControl` at toolkit position 1 and
`TotalizerMode` at toolkit position 2 against the generated `Totalizer_1`
parent.

The first complete strict dry-run created the DAP and reached the control
plug. TIA placed that item at `FT_101/TotalizerControl`, not the requested
`FT_101/Totalizer_1/TotalizerControl`, and the transaction rolled back. The
second complete dry-run accepted only that observed displacement; it reached
the still-strict mode plug, which TIA placed at `FT_101/TotalizerMode` instead
of `FT_101/Totalizer_1/TotalizerMode`, then rolled back. The third complete
preview accepted both independently observed placements and succeeded.

The unchanged ten-operation apply then:

1. created the Promag DAP as `FT_101`, preserving its generated process
   modules and three totalizers;
2. added both totalizer submodules with named result references;
3. connected the generated `PN-IO` node and connector to `CellPN` and
   `CellIO`;
4. set IP `192.168.0.61`, station name `feed-flow-101`, and device number 61;
5. guarded dataset 32752, offset 45 from `80 DF` and wrote `80 AF`;
6. assigned generated `Volume flow` input start 176 through the station result
   and relative child; and
7. assigned the displaced control and mode output starts 176 and 177 through
   their actual plug results.

No generated address operation guessed an expected start. Fresh reads proved:

- the DAP, default process modules, `Totalizer_1` at position 11, and both
  root-reported submodules at toolkit positions 1 and 2;
- `CellPN`, `CellIO`, `192.168.0.61`, `feed-flow-101`, and device number 61;
- `Volume flow` input length 40 bits at byte 176;
- both output submodules with length 8 bits at bytes 176 and 177; and
- dataset 32752, offset 45, length 2 returned `80 AF`.

`PLC_Main` compiled with zero errors and zero warnings. Applied-state hashes
were:

- hardware tree:
  `7FF82EC1638557B8AFBD0DDD864CDA95CB2E02B610A896E34C9665997BCC4BD6`
- network topology:
  `9AEA303BC6405389AED8B3D80962550D8104F4809883D4EBB7BCBE9FAF9C46F6`
- comparable IO-address CSV:
  `67BF2F26388EA6FD503C868E7A04F5A52CC82CA3A7430A0AA804F6E2CFE79459`

## Inverse, scorer, and cleanup proof

A destructive inverse deleting `FT_101` with its observed device-type guard
was previewed, rolled back, and then applied. Fresh filtered tree, network, and
IO reads all returned empty arrays for `FT_101`; `PLC_Main` again compiled with
zero errors and zero warnings.

The mechanical scorer regression suite gives the representative trace 100 and
independently rejects:

- a GSD write without `expectedDataHex`;
- apply before the complete corrected preview;
- pre-enabling the unprobed sibling displacement; and
- omitting the independent GSD parameter readback.

The disposable project was closed without saving. The exact TIA process
started for the audit was stopped, no audit TIA process remained, and the
read-only fixture archive still matched its original SHA-256.

## Deliberate limits

This audit proves offline engineering and compilation, not communication with
a physical flowmeter. It also does not yet prove the current 2020 vendor XML
against the same case. Importing that package and measuring a version-tolerant
contract is the next separate benchmark slice; it must not silently replace
this pinned baseline.
