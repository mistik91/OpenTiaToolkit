# Companion UX decision

Status: accepted, 11 July 2026.

Phase 3 evaluated a dedicated VS Code extension and an in-TIA add-in. The decision is to keep the companion surface MCP-native for this release.

The repository already ships VS Code MCP configurations, generated tool schemas, CLI fallbacks, agent skills, progress reporting, artifact spill, blueprint planning, and state-bound apply tokens. A bespoke extension would duplicate command discovery and safety UX, while an in-TIA add-in would introduce a second Siemens-loaded process and deployment trust boundary.

The supported companion experience is VS Code or another MCP client connected to the vendor-free MCP host, with generated command references and blueprint JSON Schema completion. Revisit a thin extension only if it consumes the same MCP/catalog contracts without its own mutation path. Revisit an in-TIA add-in after version packages beyond V21 prove the worker contract across releases.
