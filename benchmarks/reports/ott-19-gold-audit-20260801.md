# OTT-19 Siemens gold audit

Date: 2026-08-01

The `tia-v21-multi-address-3` fixture was restored into a disposable project
on a dedicated TIA Portal V21 Update 2 process. Fresh discovery established the
following independent address spaces:

| Space | Role | Module / row | Index | Start | Length |
| --- | --- | --- | ---: | ---: | ---: |
| Input | fixed anchor | `DI_Occupied` | 0 | 64 | 16 bits |
| Input | candidate 1 | `DI_Candidate` | 0 | 2 | 16 bits |
| Input | candidate 2 | `DIDQ_Candidate` / Input | 0 | 0 | 16 bits |
| Output | fixed anchor | `DQ_Occupied` | 0 | 128 | 16 bits |
| Output | candidate 1 | `DQ_Candidate` | 0 | 2 | 16 bits |
| Output | candidate 2 | `DIDQ_Candidate` / Output | 1 | 0 | 16 bits |

Each 16-bit row occupies two bytes. Preserving hardware-tree order within each
space therefore yields Input starts 66 and 68, and Output starts 130 and 132.

One four-operation guarded patch was dry-run and then confirmed:

1. `DI_Candidate`, index 0, expected 2, written 66;
2. `DIDQ_Candidate`, Input index 0, expected 0, written 68;
3. `DQ_Candidate`, index 0, expected 2, written 130;
4. `DIDQ_Candidate`, Output index 1, expected 0, written 132.

Both calls succeeded. A fresh address read confirmed that `DI_Occupied`
remained 64 and `DQ_Occupied` remained 128 while all four candidates matched
the computed compact layout. PLC compilation completed with 0 errors and 0
warnings.

A separate four-operation inverse patch was also dry-run and confirmed. Fresh
readback restored the exact archive baseline (candidate starts 2, 0, 2, 0),
and the PLC compiled again with 0 errors and 0 warnings before save.

Archive SHA-256:
`3B4F832B1F32ECDF20C8905E8CE07C429DC808F00C3C15B03688E0AB7E168585`.
