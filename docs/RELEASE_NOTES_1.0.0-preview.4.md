# OpenTiaToolkit 1.0.0-preview.4

Release date: 30 July 2026

This unsigned prerelease moves the agent-facing server to the stable MCP 2.0 stack, strengthens worker-session recovery, and keeps the full CLI and MCP command surfaces aligned.

## Highlights

- Migrated from the preview C# MCP SDK to stable `ModelContextProtocol` 2.0.0 and the MCP `2026-07-28` discovery and per-request metadata model.
- Retained initialize-based compatibility for older MCP clients while exposing the modern discovery-first flow to current clients.
- Derived all MCP tools and strict JSON Schemas from the runtime catalog: 182 shared commands plus `help`, `reset`, and `cancel-operation`.
- Added structured content with text fallback, case-insensitive argument handling, typed JSON-RPC errors, and UTF-8 spill artifacts for large results.
- Made reset cooperative and fail-closed: it waits for an active worker operation to quiesce before replacement and preserves the active session when the configured grace period expires.
- Made cancellation replace the affected worker after requesting cancellation, providing a clean follow-up session without sharing Siemens objects across operation boundaries.
- Added deterministic startup checks for invalid names, duplicate commands, and collisions with MCP infrastructure tools.
- Added stable-SDK raw-stdio, staged-installer, packaged-MCPB, and live read-only validation harnesses.
- Added canonical Unified HMI `contained-type` support and bound faceplate container types before importing interface values, including rollback-only dry-run initialization.

## Validation

- Release solution build completed with zero warnings and zero errors against the exact TIA Portal V21 PublicAPI.
- Passed 633 fast tests, 54 architecture tests, 3 MCP registry integration tests, and 4 worker IPC integration tests.
- Passed locked restore, formatting, generated-reference drift, packaged-skill drift, repository hygiene, and dependency vulnerability checks.
- Verified all 185 MCP tools, required/default schema metadata, modern and legacy protocol paths, positive calls, typed failures, path policy, case-insensitive arguments, graceful shutdown, and worker cleanup.
- Verified staged installer and MCPB copies match the exact validated MCP and worker binaries.
- Completed live read-only validation against an open TIA Portal V21 project, including project, device, PLC, block, HMI, and screen discovery plus policy-denied and invalid-request checks.

## Compatibility

- Supported: TIA Portal V21.
- Unsupported: TIA Portal V20 and earlier.
- Unvalidated and unsupported: TIA Portal V22 and later.
- Transport: MCP over stdio.
- Preferred protocol: MCP `2026-07-28`.
- Downlevel compatibility: initialize-based MCP clients, including `2025-11-25`.

The official MCP conformance CLI currently accepts an HTTP URL rather than a stdio executable, so its server suite cannot be run directly against OpenTiaToolkit's stdio-only host. The release instead exercises the packaged executable with exhaustive raw JSON-RPC stdio coverage.

## Unsigned Preview Notice

This preview is intentionally unsigned. Windows may show an unknown-publisher or SmartScreen warning. Verify `INSTALLER-SHA256SUMS.txt` before running the installer and review the accompanying `UNSIGNED-NOTICE.txt`.

Run `doctor` after installation and after every TIA Portal update or hotfix. Use disposable project copies for mutation workflows and keep independent backups.
