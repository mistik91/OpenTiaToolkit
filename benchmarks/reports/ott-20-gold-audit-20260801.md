# OTT-20 cross-domain Siemens gold audit

Date: 2026-08-01

## V4 fixture

`tia-v21-cross-domain-4` extends the mixed-address fixture with four Bool tags
in `Default tag table`:

| Tag | Baseline address | Mapped hardware row |
| --- | --- | --- |
| `DI_Candidate_Channel0` | `%I2.0` | `DI_Candidate`, Input index 0 |
| `DIDQ_Candidate_Input0` | `%I0.0` | `DIDQ_Candidate`, Input index 0 |
| `DQ_Candidate_Channel0` | `%Q2.0` | `DQ_Candidate`, Output index 0 |
| `DIDQ_Candidate_Output0` | `%Q0.0` | `DIDQ_Candidate`, Output index 1 |

Every tag has data type `Bool` and comment `OTT benchmark hardware mapping`.
The project compiled with 0 errors and 0 warnings, was archived to a new
read-only path, and was independently restored on a fresh TIA Portal V21
Update 2 process. The restored hardware, six total PLC tags, and compilation
were verified before mutation.

Archive identity:

- File: `OTT_Benchmark_V21_CrossDomain_20260801.zap21`
- Size: `5,535,034` bytes
- SHA-256: `C89FDAD14AF95CCC9DA216E4C82FD710A60774D9E7C4D9CB07A6185FB350999D`
- Read-only: yes

## Forward migration

Before any apply, the complete four-operation hardware patch and complete
four-update tag batch were independently dry-run. Both previews succeeded.

The hardware patch then moved Input candidates to bytes 66 and 68 and Output
candidates to bytes 130 and 132. The tag batch updated only logical addresses:
`%I66.0`, `%I68.0`, `%Q130.0`, and `%Q132.0`, respectively.

Fresh hardware discovery and tag export matched all eight results. Tag names,
table, Bool type, comments, and bit offsets were preserved. PLC compilation
completed with 0 errors and 0 warnings.

## Inverse and health check

A separately guarded hardware inverse and tag inverse were both previewed
before apply. After both inverse applies, fresh discovery restored hardware
candidate starts 2, 0, 2, and 0 and tag addresses `%I2.0`, `%I0.0`, `%Q2.0`,
and `%Q0.0`. The PLC compiled again with 0 errors and 0 warnings before save.
