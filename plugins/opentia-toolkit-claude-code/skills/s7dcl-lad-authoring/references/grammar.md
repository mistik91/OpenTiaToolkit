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
- Edge/negate: `P_Trig( #memory )`, `Not()`.
- Compare: `{ S7_Templates := "SrcType := Int" } GT_Contact( in1 := #A, in2 := #B )`; corresponding LT/EQ/NE/GE/LE forms.
- Move: `Move( in := value, out1 => target )`.
- Arithmetic: typed `Add`, `Sub`, `Mul`, `Div` with `in1`, `in2`, and `out`.
- Calls: use the syntax exported by the target project.
- Static timer: typed `#tonInst.TON( pt := T#500ms, et => )`, with `tonInst : TON_TIME` in FB `VAR`.

Avoid bare export forms such as `Gt(...)`, `PBox(...)`, or decorated `Move{ Card := ... }` unless live import proves them.

Use `wire#wN` as a branch junction. A LAD-preferred block may include complete `{ S7_Language := "SCL" }` networks.

When referencing `S7_NetworkTitle := "MLC_Title"`, define it once in `.s7res`:

```yaml
MultiLingualTexts:
  - id: MLC_Title
    en-US: Probe network
```
