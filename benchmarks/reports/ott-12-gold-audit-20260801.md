# OTT-12 Siemens gold audit

Date: 2026-08-01
TIA Portal: V21 Update 2

## Fixture construction

The immutable `tia-v21-multi-address-3` fixture extends `tia-v21-io-2` with one catalog-resolved `DI 16x24VDC/DQ 16x24VDC/0.5A BA V1.1` module (`OrderNumber:6ES7 523-1BL00-0AA0/V1.1`) named `DIDQ_Candidate` at position 6.

Fresh hardware discovery reported two address objects on the same exact target:

| Index | IoType | Length | StartAddress |
| ---: | --- | ---: | ---: |
| 0 | Input | 16 bits | 0 |
| 1 | Output | 16 bits | 0 |

## Operation audit

The audited forward operation targeted `PLC_Main / DIDQ_Candidate / DIDQ_Candidate`, selected `addressIndex: 1`, guarded `expectedStartAddress: 0`, and requested `startAddress: 132`.

- Dry-run succeeded and reported `IoType: Output`, before `0`, written `132`.
- Confirmed apply succeeded.
- Fresh discovery reported input index 0 still at byte 0 and output index 1 at byte 132.
- A guarded inverse operation on index 1 was dry-run, applied, and independently verified to restore output byte 0 without changing input byte 0.
- PLC compilation completed with 0 errors and 0 warnings.

The project was saved only after the inverse verification. It was archived to a new path, restored into a new directory on a fresh TIA Portal process, rediscovered with the same two-row baseline, and compiled again with 0 errors and 0 warnings.

## Immutable archive identity

- File: `OTT_Benchmark_V21_MultiAddress_20260801.zap21`
- Size: `5,582,000` bytes
- SHA-256: `3B4F832B1F32ECDF20C8905E8CE07C429DC808F00C3C15B03688E0AB7E168585`
- Read-only: yes
