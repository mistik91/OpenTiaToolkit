# OpenTiaToolkit 1.0.0-preview.10

Unsigned preview for **TIA Portal V21**, released 23 November 2026 for wider testing. The release supplies a Windows installer, a portable Agent Plugins package, a Claude Desktop bundle, source, and SHA-256 checksums. [Preview.10 downloads](https://github.com/mistik91/OpenTiaToolkit/releases/tag/v1.0.0-preview.10)

## Changes since preview.9

- **Unified HMI visual verification.** Capture before and after a screen patch using the Next Gen editor's WebView2 renderer. A windowed TIA instance can stay in the background while the agent captures. Open the screen and set its zoom in TIA. The PNG represents the engineering viewport and requires no compile. [Capture workflow](CLI_USAGE.md#capture-the-next-gen-screen-editor)
- **Unified HMI tag tables.** Create grouped tag tables and exchange simple native WinCCML tags. Imports verify the type and PLC binding of each requested tag before commit. [Tag workflow and limits](CLI_USAGE.md#unified-hmi-tag-tables-and-winccml)
- **AutomationML.** Inspect AR APC files, compare source-specified settings against a project, and apply supported changes. Existing projects can receive scoped additions such as supported remote I/O stations and PLC tags. Review reported catalog masks and conflicts before applying. [AutomationML workflow and limits](AUTOMATIONML.md)
- **Alarm and screen editing.** Bulk discrete-alarm preflight reads fewer properties and reuses reference discovery within each preflight. Results preserve completed transaction state when verification reads fail. Filtered JSON reads skip unrelated composite entries, and screen edits block V21 property-event and root-dynamization combinations that can erase bindings. [Alarm validation](alarm-bulk-performance-validation.md) · [HMI editing skill](../skills/hmi-unified-agent-editing/SKILL.md)
- **PLC, VCI, and hardware.** SCL generation respects supported subgroup destinations, and bulk SIMATIC SD export reads nested block groups in one traversal. Imported documents and VCI synchronization get fresh verification, and unsuccessful hardware changes report rollback and export diagnostics. [PLC export validation](PLC_DOCUMENT_EXPORT_VALIDATION.md) · [Changelog](../CHANGELOG.md)

The source catalog exposes 193 CLI and MCP commands, plus three MCP session-management tools. [Command reference](generated/COMMAND_REFERENCE.md) · [MCP reference](generated/MCP_TOOL_REFERENCE.md)

## Validation and limits

The exact installer passed local Windows 10 and TIA V21 Update 2 acceptance. The installed CLI and MCP captured a disposable HMI screen, verified a caption edit and its reversal, and verified a native WinCCML round trip. The installed CLI also parsed a synthetic AutomationML fixture. Independent-machine acceptance and live AutomationML reconciliation remain open for further testing. [Validation record](https://github.com/mistik91/OpenTiaToolkit/releases/download/v1.0.0-preview.10/VALIDATION_PREVIEW10.md)

Verify the installer SHA-256 and run `doctor` after installing. Exercise write workflows on disposable project copies with independent backups. Earlier validation applies to the corresponding release artifacts. [Preview.8 independent acceptance](PREVIEW8_ACCEPTANCE.md) · [Preview.9 qualification](RELEASE_NOTES_1.0.0-preview.9.md#validation-and-limits)
