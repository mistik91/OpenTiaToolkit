# Import-Verified S7DCL LAD Grammar

```scl
{ S7_BlockNumber := "59001"; S7_Optimized := "TRUE"; S7_PreferredLanguage := "LAD"; S7_Version := "0.1" }
FUNCTION "Codex_LadProbe" : Void
VAR_INPUT InA : Bool; InB : Bool; END_VAR
VAR_OUTPUT OutQ : Bool; END_VAR
{ S7_Language := "LAD" }
NETWORK
RUNG wire#powerrail Contact( #InA ) I_Contact( #InB ) Coil( #OutQ ) END_RUNG
END_NETWORK
END_FUNCTION
```

- Contacts/coils: `Contact`, `I_Contact`, `Coil`, `S_Coil`, `R_Coil`, `I_Coil`.
- Edge/negate: `P_Trig( #memory )`, `Not()`, and exported edge contacts `P_Contact( operand := signal, bit := memory )` / `N_Contact(...)`.
- Compare: `{ S7_Templates := "SrcType := Int" } GT_Contact( in1 := #A, in2 := #B )`; corresponding LT/EQ/NE/GE/LE forms.
- Move: `Move( in := value, out1 => target )`.
- Arithmetic: typed `Add`, `Sub`, `Mul`, `Div` with `in1`, `in2`, and `out`.
- Scaling: `{ S7_Templates := "[SrcType := Int, DestType := Real]" } Normalize( min := ..., value := ..., max := ..., out => ... )`; `Scale` uses the same four pins and both source/destination types.
- DB attributes: `ATTR_DB( REQ := ..., DB_NUMBER := ..., RET_VAL => ..., DB_LENGTH => ..., ATTRIB => ... )`. This exact pin shape round-tripped in TIA V21; the referenced DB remains a project dependency.
- Calls: use the syntax exported by the target project.
- Static timer: typed `#tonInst.TON( pt := T#500ms, et => )`, with `tonInst : TON_TIME` in FB `VAR`.
- Timer coils: `{ S7_Templates := "time_type := Time" } TON_Coil( timer => timerTarget, pt := T#500ms )`; the same `timer`/`pt` pin rule applies to `TOF_Coil`, `TP_Coil`, and retentive `TONR_Coil`. `RT_Coil(timerTarget)` resets a retentive timer.

Avoid bare export forms such as `Gt(...)`, `PBox(...)`, or decorated `Move{ Card := ... }` unless live import proves them. Siemens exports may omit `S7_Templates SrcType` when connected operands make the type inferable; accept that form only as `unknown-requires-live-probe`. Agent-authored typed calls should emit the immediately preceding template; an earlier template elsewhere in the network does not type a later call.

The validator rejects known export-only forms, missing named pins (including edge contacts, scaling, and timer coils), unpaired `wire#wN` junctions, local FC timer instances, incorrectly placed FB stateful instances, invalid edge memory, duplicate resources, and F-language documents. Missing type templates, project-specific block calls, and unregistered instructions are classified `unknown-requires-live-probe` rather than assumed safe.

Use `wire#wN` as a branch junction. A LAD-preferred block may include complete `{ S7_Language := "SCL" }` networks.

When referencing `S7_NetworkTitle := "MLC_Title"`, define it once in `.s7res`:

```yaml
MultiLingualTexts:
  - id: MLC_Title
    en-US: Probe network
```
