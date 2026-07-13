# S7DCL LAD instruction catalog

Every typed call must have its `S7_Templates` attribute immediately before the call in the same network. All output pins use `=>`; input pins use `:=`.

| Family | Verified forms | Required contract |
|---|---|---|
| Boolean | `Contact`, `I_Contact`, `Coil`, `I_Coil`, `S_Coil`, `R_Coil`, `Not` | One Boolean operand; do not invent named pins |
| Compare | `GT_Contact`, `LT_Contact`, `EQ_Contact`, `NE_Contact`, `GE_Contact`, `LE_Contact` | `SrcType`; `in1`, `in2` |
| Transfer | `Move` | `in`, `out1`; never emit decorated `Move{...}` |
| Arithmetic | `Add`, `Sub`, `Mul`, `Div` | `SrcType`; `in1`, `in2`, `out`; guard division separately |
| Analog | `Normalize`, `Scale` | Both `SrcType` and `DestType`; `min`, `value`, `max`, `out` |
| IEC instance timers | `#instance.TON`, `.TOF`, `.TP` | FB static instance; `pt`; use the exported receiver type |
| Timer coils | `TON_Coil`, `TOF_Coil`, `TP_Coil`, `TONR_Coil`, `RT_Coil` | `time_type := Time`; timer coils require `timer` and `pt`; reset takes the timer target |
| Edge | `P_Trig`, `P_Contact`, `N_Contact` | Trigger memory is Bool local/static; contact `bit` memory is retained FB static or `VAR_IN_OUT`, never FC `VAR_TEMP` |
| System | `ATTR_DB` | Exact pins `REQ`, `DB_NUMBER`, `RET_VAL`, `DB_LENGTH`, `ATTRIB`; referenced DB is a dependency |

## Branch form

A named wire is a junction, not a label. Emit exactly one path that introduces the wire and at least one matching path that joins it:

```scl
RUNG wire#powerrail Contact( #Primary ) wire#w1 Coil( #Run ) END_RUNG
RUNG wire#powerrail Contact( #Alternate ) END_RUNG wire#w1
```

Use distinct names for nested junctions and preserve their direction from a known-good export.

## Classification boundaries

- Treat quoted or unregistered calls as project-specific and `unknown-requires-live-probe`.
- Treat typed calls without the immediate template as probe-only, even if Siemens exported the inferred form.
- Reject `Gt`, `Lt`, `Eq`, `Ne`, `Ge`, `Le`, `PBox`, `NBox`, decorated `Move`, dangling junctions, F-language documents, and stateful FC locals.
- Do not infer pin names for a new instruction from its graphical appearance. Export a V21 example first.
