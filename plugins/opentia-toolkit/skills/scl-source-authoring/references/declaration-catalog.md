# Native SCL declaration catalog

| Declaration | Use | Transaction behavior |
|---|---|---|
| `FUNCTION` | Stateless calculation or orchestration | Exact FC target; no retained local timer/counter/edge state |
| `FUNCTION_BLOCK` | Stateful behavior and reusable instances | Exact FB target; declare IEC state in static `VAR` |
| `ORGANIZATION_BLOCK` | PLC execution event | Probe event/number semantics in the target CPU before replacement |
| `DATA_BLOCK` | Source-owned global or instance data | Treat layout and FB coupling as dependencies; prefer S7DCL for surgical global DB edits |
| `TYPE` | Source-owned PLC data type | Exact PLC type target and `type-group-path`; canonical XML snapshot/read-back |

## Multi-target source rules

1. Parse and review every declaration before import. One source may intentionally generate several blocks and types.
2. Keep mutually dependent declarations in one source when separate transaction operations would form a dependency cycle.
3. Use one `group-path` for generated blocks and one `type-group-path` for generated types. Split sources when targets must land in different groups.
4. Reject duplicate declaration identities and any collision with an existing external source name.
5. Treat generated external sources as temporary transaction artifacts unless the user explicitly wants to retain source objects.

## Native instance DB declarations

Use [Codex_SCL_Instance_Probe.scl](../assets/Codex_SCL_Instance_Probe.scl) for the declaration shape, not application behavior. It declares an FB and its instance DB in one source. The DB header has attributes and metadata such as `VERSION` **before** the bare quoted FB name, then `BEGIN`/`END_DATA_BLOCK`. Do not put `VERSION` after the binding or invent a colon, `OF`, or `FB` keyword around it. The FB owns the instance interface; do not repeat a global DB `VAR`/`STRUCT` layout in its instance DB.

OpenTiaToolkit requires exactly one matching `FUNCTION_BLOCK` or `TYPE` declaration in that same source to classify a named DB binding offline. An FB binding means `InstanceDB`; a TYPE binding means a UDT-based `GlobalDB`. A quoted name alone, including the name of an FB already in the project, cannot prove the subtype. Discover a fresh capability for every generated target with its actual type; a global DB capability is not interchangeable with an instance DB capability.

Native instance DB generation is create-only in this transaction route: replacing an existing instance DB is blocked because lossless replacement rollback is not supported. Use a new unique DB identity or select a separately proven workflow for an existing object. Do not bypass this by relabeling the DB. Static subtype validation does not prove Siemens header grammar, expression types, or compile success; retain the normal guarded probe and cleanup requirements.

## Stateful FB checklist

- Put TON/TOF/TP, counters, and R/F edge instances in static `VAR`, never `VAR_TEMP`.
- Call every stateful instance cyclically before consuming its outputs.
- Define safe outputs on disabled and default state-machine paths.
- Bound loops and array indices; guard division and conversion overflow.
- Treat the Siemens compile as the type checker. Static validation proves structure and declared targets, not expression semantics.
