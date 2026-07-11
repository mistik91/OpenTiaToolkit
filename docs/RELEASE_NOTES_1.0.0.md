# OpenTiaToolkit 1.0.0

Release date: 11 July 2026

OpenTiaToolkit 1.0.0 is the first stable release of the safety-first CLI and MCP automation platform for Siemens TIA Portal V21.

## Compatibility

- Supported: TIA Portal V21
- Unsupported: TIA Portal V20 and earlier
- Unsupported until separately implemented and validated: TIA Portal V22 and later

The `doctor`, `describe-api`, and `diff-api-profile` commands verify the installed V21 Siemens assemblies and public API against the committed compatibility profiles. A changed or unknown profile is reported as unvalidated; it is not silently treated as supported.

## Highlights

- Vendor-free CLI and MCP hosts backed by an isolated, serialized V21 worker process.
- Central mutation policy, typed outcomes, structured errors, correlation IDs, redacted JSONL audit records, and worker recovery.
- State-bound declarative blueprints with preview plans and ten-minute, single-use apply tokens.
- Broad PLC, Unified HMI, hardware, Startdrive, VCI, library, project-text, safety, multiuser, and security workflows.
- Generated CLI/MCP references, reusable agent workflow skills, paging and artifact streaming.
- Locked dependencies, vulnerability scanning, checksums, SPDX SBOM, in-toto provenance, and Authenticode release support.

## Validation

The release candidate passed vendor-free architecture and fast tests, worker and MCP integration tests, the complete generated CLI help harness, negative CLI cases, locked restore, formatting, documentation/skill drift checks, dependency vulnerability scanning, staged MCP initialization, packaged MCP tool enumeration, and a disposable blueprint/project/archive/reset lifecycle.

Representative read-only commands were also exercised against a live V21 project, including PLC, Unified HMI, Startdrive, safety, watch-table, technology-object, alarm, multiuser, and project-security surfaces. Independent-machine use completed without an observed product blocker. Private project, device, user, and machine identifiers are intentionally excluded.

## Operational guidance

- Run `doctor` after installation and after every TIA Portal update or hotfix.
- Review preview output and keep backups before applying engineering mutations.
- Register the exact installed V21 worker executable in the Siemens Openness allow-list.
- Do not copy Siemens Openness DLLs into the toolkit installation; they are resolved from the supported local TIA Portal installation.

## Known maintainability debt

Several Siemens-facing adapters and services remain large. Their decomposition is deferred because the 1.0 safety boundary isolates Siemens ownership and execution inside the disposable V21 worker. This is maintainability debt, not a known release-blocking functional defect.
