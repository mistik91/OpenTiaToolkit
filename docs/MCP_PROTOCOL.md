# MCP Protocol Support

OpenTiaToolkit's vendor-free MCP host uses the stable `ModelContextProtocol` and `ModelContextProtocol.Core` 2.0.0 packages. It targets .NET Framework 4.8, communicates over newline-delimited JSON-RPC on stdio, and starts the existing isolated TIA Portal V21 worker through the same typed IPC contracts used by the CLI.

The implementation follows the official [MCP `2026-07-28` specification](https://modelcontextprotocol.io/specification/2026-07-28) and the [C# SDK 2.0.0 release](https://github.com/modelcontextprotocol/csharp-sdk/releases/tag/v2.0.0).

## Compatibility Matrix

| Client revision | Handshake and request model | OpenTiaToolkit behavior |
|---|---|---|
| `2026-07-28` | `server/discover`; every request carries protocol version, client identity, and client capabilities in `_meta` | Supported. Discovery, cache hints, and `resultType: "complete"` are emitted by the stable SDK. |
| `2025-11-25` | `initialize` / `notifications/initialized` | Supported and exercised by the raw stdio compatibility smoke. Modern-only result decoration is omitted. |
| `2025-06-18` | `initialize` / `notifications/initialized` | Supported by the stable SDK's downlevel negotiation. |
| `2025-03-26` | `initialize` / `notifications/initialized` | Supported by the stable SDK's downlevel negotiation. |
| `2024-11-05` | `initialize` / `notifications/initialized` | Supported by the stable SDK's downlevel negotiation. |

OpenTiaToolkit does not add a REST or HTTP endpoint. Stdout is reserved for protocol messages; diagnostics are written only to stderr. Closing stdin is the normal graceful-shutdown signal and also disposes the isolated worker.

## Advertised Capabilities

The server advertises MCP tools. The runtime command catalog is projected to MCP without a manually maintained expected total, then the three infrastructure tools `operation-status`, `cancel-operation`, and `reset-session` are added. Startup validation rejects duplicate, non-kebab-case, or infrastructure-colliding runtime names. `tools/list` is deterministic and preserves parameter names, required fields, JSON types, array item types, defaults, and `additionalProperties: false`.

Prompts, resources, sampling, elicitation, Tasks, Apps, and HTTP transports are not advertised or implemented. Tasks and Apps were evaluated as optional extensions: neither is required for the current local, serialized command surface, and advertising them without a real lifecycle or UI contract would be misleading.

## Tool Results and Errors

Successful and failed command outcomes both use MCP tool results:

- `structuredContent` contains `success`, `message`, typed `error`, `data`, and `correlationId`.
- A JSON text content block is included as a fallback for clients that do not consume structured content.
- Business, validation, policy, timeout, cancellation, and worker errors set `isError: true` while preserving the typed OpenTiaToolkit error code.
- Unknown or blank tool names are JSON-RPC Invalid Params (`-32602`), not tool-execution results.
- Worker results larger than 512 KiB spill to a UTF-8 JSON artifact under `%TEMP%\OpenTiaToolkit\CommandArtifacts`; the inline data becomes typed artifact metadata.

Calls into the TIA worker remain serialized. Concurrent domain calls return a typed conflict. `cancel-operation` requests cancellation, then terminates and replaces the active isolated worker. A configured tool timeout does the same and returns `TimedOut`. `reset-session` requests cooperative cancellation and waits for the configured grace period; it replaces the worker only after the active call quiesces, otherwise it returns a conflict and leaves that worker and TIA attachment intact.

## Validation

Build the Release hosts, then exercise the exact binaries:

```powershell
dotnet build .\src\OpenTiaToolkit.MCP\OpenTiaToolkit.MCP.csproj -c Release
dotnet build .\src\OpenTiaToolkit.CLI\OpenTiaToolkit.CLI.csproj -c Release
dotnet build .\src\OpenTiaToolkit.Worker.V21\OpenTiaToolkit.Worker.V21.csproj -c Release

.\scripts\Test-McpStdio.ps1 `
  -ExecutablePath .\src\OpenTiaToolkit.MCP\bin\Release\net48\OpenTiaToolkit.MCP.exe `
  -CommandCatalogExecutablePath .\src\OpenTiaToolkit.CLI\bin\Release\net48\OpenTiaToolkit.CLI.exe `
  -WorkerExecutablePath .\src\OpenTiaToolkit.Worker.V21\bin\Release\net48\OpenTiaToolkit.Worker.V21.exe
```

The smoke verifies stable 2.0.0 SDK assembly metadata, modern discovery, a downlevel initialize session, complete catalog and schema parity with the exact worker, structured and text results, protocol errors, policy denial, stderr separation, graceful shutdown, and absence of orphan workers. Installer and MCPB packaging run the same smoke against their staged payloads.

For an explicitly authorized live read-only campaign, use `scripts\Test-McpLiveReadOnly.ps1` with the exact Release MCP, CLI, worker, and selected TIA instance. It stays on modern `2026-07-28`, derives identifiers only from prior discovery results, writes a per-tool JSON coverage matrix, checks safe negative cases, and proves stable project identity and `tools/list` across reset/reconnect. Representative exports and large-result artifacts stay under `%TEMP%\OpenTiaToolkit` for evidence and are governed by `OPEN_TIA_ARTIFACT_RETENTION_DAYS` (default 7 days).

The official [`@modelcontextprotocol/conformance`](https://github.com/modelcontextprotocol/conformance) server runner was also evaluated. As of 2026-07-30, stable 0.1.16 has no `2026-07-28` scenarios; 0.2.0-alpha.10 includes those scenarios but its server command accepts only `--url <url>`. It cannot launch or attach to a stdio server binary, and OpenTiaToolkit deliberately exposes no HTTP endpoint. The official runner is therefore transport-inapplicable rather than silently bypassed; the raw stdio harness above covers the produced executable directly without adding a test-only production transport.
