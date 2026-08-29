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

## Stateful FB checklist

- Put TON/TOF/TP, counters, and R/F edge instances in static `VAR`, never `VAR_TEMP`.
- Call every stateful instance cyclically before consuming its outputs.
- Define safe outputs on disabled and default state-machine paths.
- Bound loops and array indices; guard division and conversion overflow.
- Treat the Siemens compile as the type checker. Static validation proves structure and declared targets, not expression semantics.
