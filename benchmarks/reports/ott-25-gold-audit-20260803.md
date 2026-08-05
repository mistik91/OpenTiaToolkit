# PASS - OTT-25 brownfield modernization gold audit

TIA Portal: V21 Update 2

## Anti-overfitting boundary

`ott-25` was designed after the `ott-24` skill and runtime-validator experiments
were complete. It changes the task shape from greenfield station creation to an
in-place, destructive brownfield replacement. No skill was edited while the
fixture, prompt, assertions, or gold transaction were designed.

At the frozen design commit (`90707df`), the shipped OpenCode skill SHA-256
values were:

- `hardware-configuration-workflow`:
  `1526365EA8077DF55C11F4ADB28528C068754A970895049643C091E12D5D10D2`
- `basic-tia-interaction`:
  `F945E06387E6A3A6E41AA29A13F9D14C43ACF87BB3A5313540153D44F8824017`
- `opentia-toolkit-workflow`:
  `EB9453E4DAFA011373D947EB75B3AC1DCC33F9E59D21CA51340A0D3B40C8267C`

The skills contain none of the new legacy/replacement names, baseline or
destination addresses, old or new PROFINET identities, or brownfield fixture
identity.

## Immutable fixture

- Fixture id: `tia-v21-brownfield-7`
- Archive: `OTT_Benchmark_V21_Brownfield_20260803.zap21`
- Size: `5,541,335` bytes
- SHA-256:
  `256BED153DF00421DBA65B1F9FFB2625CE09E8715C430A5038CE074F46889073`
- Read-only: yes

The archive extends the prior topology fixture with `LegacyCell`, an existing
ET 200MP station connected to `CellPN` and `CellIO`. Its retained HF input
module `LegacyInputs` is at position 2 and input bytes 72-73. Its BA output
module `LegacyOutputs` is at position 4 and output bytes 140-141. The initial
PROFINET identity is `192.168.0.57`, `legacy-cell`, device number 57.

Complete baseline hashes from a fresh restore were:

- hardware tree:
  `6B0A80B544A6F48E0A875CEAD91E6140C94CE55FBFDC7074F2C49D77275EEAED`
- network topology:
  `EC7FB6906F507E784C8963CD74F2E9C72CCBD05AE98686C18AB6215B08DAB14B`
- comparable IO-address CSV:
  `29800EF6C2ED63BD1743F0BE5C211A8CA294C394AEA79EC967F9041E1FEE90D3`

The construction session compiled `PLC_Main` with zero errors and zero
warnings, saved, archived to a new path, and closed its toolkit-owned TIA
process. The archive was made read-only only after independent restore tests.

## Strict placement and positive proof

Focused catalog discovery resolved replacement article number
`6ES7 522-1BH01-0AB0` to the highest installed exact identifier
`OrderNumber:6ES7 522-1BH01-0AB0/V1.1`.

The first complete strict dry-run used the discovered `Rail_0` parent. Siemens
reported that `ModernOutputs` was legally placed at
`LegacyCell/ModernOutputs`, not
`LegacyCell/Rail_0/ModernOutputs`, and rolled the transaction back. The
complete corrected transaction accepted only that observed displacement.

One five-operation batch then:

1. deleted `LegacyOutputs` with its observed type-identifier guard;
2. moved retained `LegacyInputs` from input start 72 to 104 with an observed
   start guard;
3. changed all three PROFINET identity fields with their observed baseline
   guards;
4. plugged `ModernOutputs` at position 4 and named its result; and
5. assigned output start 160 through that result plus the generated
   `ModernOutputs` child, without an unobservable expected-address guess.

The corrected dry-run succeeded and rolled back. The unchanged confirmed
apply committed. Independent reads proved the BA module was absent, the HF
replacement was at position 4, the retained input start was 104, the generated
output start was 160, and the identity was `192.168.0.67`, `packaging-cell`,
device number 67. `PLC_Main` compiled with zero errors and zero warnings.

Applied-state hashes were:

- hardware tree:
  `0DE5EDE33349B6E938D80C2CF554DA49734ECDAFDC671A17B03B1BDE1F714917`
- network topology:
  `8DE9B85A808EA09BE75C0023CE4B0DC9844A0CE5F51D3C52267B4E0B44A04C5A`
- comparable IO-address CSV:
  `B5829864F62D3331A71E55AAE2CAC791920E64933799B7B528E339E2CF170D63`

## Exact inverse and live late-failure rollback

A complete destructive inverse was previewed and applied. It deleted the HF
replacement, restored the retained input and all three identity fields using
their applied-state guards, recreated the original BA output at position 4,
and assigned its generated child back to output start 140. Fresh tree,
network, and IO reads matched all three baseline hashes byte for byte.

The adversarial proof used a separately launched, externally owned TIA
instance so a failed CLI process could detach without closing the live project.
It ran all five valid modernization operations and appended a sixth operation
that moved the newly created output to occupied output byte 90. Siemens failed
only on that final operation and reported next free address 92. A second CLI
process attached to the same still-running TIA instance before it was closed.
Its tree, network, and IO reads again matched all three baseline hashes byte for
byte, proving rollback of the deletion, retained-input move, identity change,
replacement plug, and first generated-address assignment. The live rolled-back
project compiled with zero errors and zero warnings.

The project was closed without saving, the exact test-owned TIA process was
stopped, and no test TIA process remained.
