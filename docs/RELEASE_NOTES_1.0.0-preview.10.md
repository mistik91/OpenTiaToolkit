# OpenTiaToolkit 1.0.0-preview.10

Unsigned release candidate for **TIA Portal V21**, prepared 23 November 2026. The [published preview.9](https://github.com/mistik91/OpenTiaToolkit/releases/tag/v1.0.0-preview.9) remains the download for installed use.

## Changes since preview.9

- **Unified HMI visual verification.** Capture before and after a screen patch using the Next Gen editor's WebView2 renderer. A windowed TIA instance can stay in the background while the agent captures. Open the screen and set its zoom in TIA. The PNG represents the engineering viewport and requires no compile. [Capture workflow](CLI_USAGE.md#capture-the-next-gen-screen-editor)
- **Unified HMI tag tables.** Create grouped tag tables and exchange simple native WinCCML tags. Imports verify the type and PLC binding of each requested tag before commit. [Tag workflow and limits](CLI_USAGE.md#unified-hmi-tag-tables-and-winccml)
- **AutomationML.** Inspect AR APC files, compare source-specified settings against a project, and apply supported changes. Existing projects can receive scoped additions such as supported remote I/O stations and PLC tags. Review reported catalog masks and conflicts before applying. [AutomationML workflow and limits](AUTOMATIONML.md)
- **Alarm and screen editing.** Bulk discrete-alarm preflight reads fewer properties and reuses reference discovery within each preflight. Results preserve completed transaction state when verification reads fail. Filtered JSON reads skip unrelated composite entries, and screen edits block V21 property-event and root-dynamization combinations that can erase bindings. [Alarm validation](alarm-bulk-performance-validation.md) · [HMI editing skill](../skills/hmi-unified-agent-editing/SKILL.md)
- **PLC, VCI, and hardware.** SCL generation respects supported subgroup destinations, and bulk SIMATIC SD export reads nested block groups in one traversal. Imported documents and VCI synchronization get fresh verification, and unsuccessful hardware changes report rollback and export diagnostics. [PLC export validation](PLC_DOCUMENT_EXPORT_VALIDATION.md) · [Changelog](../CHANGELOG.md#100-preview10-unsigned-release-candidate)

The source catalog exposes 193 CLI and MCP commands, plus three MCP session-management tools. [Command reference](generated/COMMAND_REFERENCE.md) · [MCP reference](generated/MCP_TOOL_REFERENCE.md)

## Validation and limits

The [HMI capture qualification](https://github.com/mistik91/OpenTiaToolkit/pull/19) exercised the Windows 10 and TIA V21 Update 2 workflow on a disposable project. An Openness caption edit appeared in a background capture, and reverting the edit restored the baseline PNG byte for byte. This is source-level workflow evidence. Candidate package checks and exact binary validation belong in the candidate's validation record.

The [preview.8 independent acceptance matrix](PREVIEW8_ACCEPTANCE.md) and [preview.9 release qualification](RELEASE_NOTES_1.0.0-preview.9.md#validation-and-limits) apply to those earlier builds. Verify the exact installer SHA-256 and run `doctor` when installing this candidate for validation. Exercise write workflows on disposable project copies with independent backups.
