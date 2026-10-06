# OpenTiaToolkit 1.0.0-preview.6

Status: unsigned release candidate, prepared 6 October 2026. Public publication, exact-artifact live validation, and clean-machine acceptance are pending. This document records the candidate scope; it is not a claim that a stable release has shipped.

## Changes since preview.5

- Added the portable Agent Plugins 1.0 integration with 18 canonical skills, launcher discovery for installed/custom layouts, and offline/staged contract validation.
- Hardened PLC code transaction planning, apply/rollback evidence, and disposal of never-applied plans so interrupted or inconsistent manifests retain recovery evidence.
- Expanded native SCL, structured S7DCL data, LAD, and PLC instance-DB workflows with capability checks and compiler diagnostics for baseline, candidate, and rollback compilation.
- Preserved actual per-target SIMATIC SD export support in bulk export instead of rejecting all organization blocks by type.
- Hardened PLCSIM Advanced behavior probes with authentic typed temporal observations, explicit acceptance vectors, instance guidance, and cleanup verification.
- Made blueprint completion reflect structured domain outcomes: safety blocks, dry-runs, validation failures, and rollbacks stop default orchestration and cannot be mistaken for successful mutations. Failed applies consume their one-use token; earlier successful steps remain applied.
- Improved identity-bound Multiuser local-session diagnostics and agent provisioning/readiness. Save and persistence verification remain explicit; check-in is performed in the owning TIA UI.
- Expanded qualification and regression coverage for alarms, trends, library reuse, VCI synchronization, SCL/data/LAD authoring, simulator behavior, blueprints, and Multiuser identity/save workflows.
- Clarified bounded HMI-tag discovery: `filter` narrows results but does not replace a concrete `root-path` or explicitly bounded full-tree scan.
- Prepared a clean source history for public distribution, updated onboarding and contribution templates, and aligned integration metadata with preview.6.
- Corrected release provenance subject names to remain relative to the staged payload, and made dependency advisories fail CI explicitly. Signed builds now require a valid Authenticode result.

## Validation and remaining gates

See the generated candidate verification record in the release assets for the exact commit, binaries, checksums, and completed checks. Public-source baseline checks on 6 October included a zero-warning Release build, 774 fast tests, 54 architecture tests, generated-reference and packaged-skill guards, dependency scanning, and exact staged stdio parity for 186 commands plus three MCP infrastructure tools. Repeat these checks for the final committed publication candidate.

The retained benchmark reports describe earlier workflow-specific live campaigns. They are historical evidence, not new live testing of this candidate's staged installer. Before publication:

1. Register the exact staged worker and run `doctor` plus read-only discovery against the intended V21 instance.
2. Complete applicable disposable-project integration and recovery gates from the [release checklist](RELEASE_CHECKLIST.md).
3. Verify installation on an independent clean machine and validate the actual downloadable installer/MCPB hashes.
4. Verify unauthenticated repository/download links and private vulnerability reporting.

Hosted CI validates the vendor-free hosts, architecture, packaging contracts, and documentation. Licensed V21 validation is restricted to manually dispatched reviewed default-branch commits.

## Compatibility and limitations

- TIA Portal V21 only. The retained benchmark baseline is V21 Update 2; run `doctor` after each update or hotfix.
- STEP 7, WinCC Unified, Startdrive, PLCSIM Advanced, and Multiuser features require the corresponding installed/licensed Siemens components.
- The shared command catalog remains 186 commands; MCP adds three infrastructure tools.
- Blueprint plans guard context and intent. Domain previews and target-level stale checks remain necessary. Blueprints execute sequentially and do not roll back earlier successful commands automatically.
- Simulator behavior and download workflows are guarded for PLCSIM Advanced; they do not authorize physical PLC downloads.
- Unified discrete-alarm engineering does not imply support for all analog, OPC UA, PLC, or runtime alarm types.
- Multiuser check-in remains a TIA UI boundary. Reading an edited local session does not prove persistence on Project Server.

## Unsigned preview

The candidate installer and toolkit binaries are unsigned. Verify `INSTALLER-SHA256SUMS.txt`, `MCPB-SHA256SUMS.txt`, payload checksums, and the accompanying SBOM/provenance. Start engineering changes on disposable project copies and maintain backups. Stable publication requires trusted signing under the project's [supply-chain policy](SUPPLY_CHAIN.md).
