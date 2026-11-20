# Unified HMI WinCCML validation

The 20 November 2026 campaign used a disposable restore of an existing TIA Portal V21 engineering archive with two WinCC Unified HMIs. The installed Public API versions were Base `2100.0.121.1` and WinCC Unified `2100.200.5.1`. The Release CLI and MCP used the same V21 worker. The live measurements below came from the MCP entry point on one workstation and describe individual calls.

| Existing table | Live outcome | Elapsed time |
| --- | --- | ---: |
| 25 simple symbolic tags in a nested group | Exported a standalone `.hmi.yml`, previewed all 25 collisions, rejected an apply without explicit overwrite, and verified all 25 tags after confirmed reimport. The before and after WinCCML exports had identical SHA-256 hashes. | 1.91 s export with HMI already selected, 0.71 s preview, 2.89 s verified apply |
| 166 tags including structures | ZIP preserved the native table and `StructureTypes.def.hmi.yml`. The command reported the output as outside the Toolkit import scope. | 4.82 s |
| 369 tags including structures | ZIP preserved the native table and `StructureTypes.def.hmi.yml`. The command reported the output as outside the Toolkit import scope. | 5.19 s |
| 11 internal tags | Exported successfully. The compatibility check identified a non-Bool initial value that the importer does not accept. | 1.84 s |

The nested table exposed a V21 type representation difference. WinCCML writes `DataType: LTime` with `ControllerDataType: S7PTransTimeType` for the tested PLC `Time` tags. Openness reports PLC/tag `DataType = Time` and `HmiDataType = LTime`. The verifier checks both readback types for that exact combination. It continues to verify the PLC binding and native validation diagnostics before commit.

A second import changed one existing tag's PLC symbol to a nonexistent member. The native validator did not flag that tag, but the Toolkit rejected the readback mismatch. An export in the same open session immediately after the failure matched the baseline SHA-256 for the full 25-tag table. This confirms why import needs both exact readback and native validation.

The export count describes table-level tags. Structure definitions and member data can make exports with similar counts substantially different in size. The campaign measured larger-table export only. Large simple-tag import throughput and structured-tag import remain unqualified.

The disposable copy was closed without saving after both runs. The source archive's SHA-256 was unchanged. The Release solution built with zero warnings or errors, and 919 non-integration tests and 54 architecture tests passed. The generated catalog contains 190 commands. MCP exposes those commands and three infrastructure tools, for 193 tools total. See the [CLI and MCP workflow](CLI_USAGE.md#unified-hmi-tag-tables-and-winccml) for commands, supported inputs, and overwrite policy.
