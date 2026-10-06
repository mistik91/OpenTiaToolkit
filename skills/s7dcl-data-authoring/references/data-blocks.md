# S7DCL Global DB Rules

```scl
{ S7_BlockNumber := "59010"; S7_Optimized := "TRUE"; S7_StandardRetain := "FALSE"; S7_Version := "0.1" }
DATA_BLOCK Codex_DataProbe
VAR Enable : Bool; Counter : DInt; Values : Array[0..3] of Real; END_VAR
Enable := FALSE;
Counter := 0;
END_DATA_BLOCK
```

- Preserve `S7_Optimized`; changing it can invalidate absolute addresses.
- Preserve block number, member order, comments, attributes, retain settings, and every unrequested start value.
- Keep start values type-correct, unique per top-level member, and indices within bounds.
- Keep every array dimension ascending and review the total element count. Use `String[1..254]` lengths and reviewed `WString` lengths.
- Balance every `Struct`/`END_STRUCT`; do not flatten nested layouts.
- Confirm every referenced PLC data type exists.
- Treat reorder, insertion, deletion, and type changes as layout changes requiring impact review.
- Treat `S7_Optimized := "FALSE"` as address-sensitive. Check absolute PLC access, HMI tags, recipes, OPC UA exposure, and communication mappings before apply.
- Treat changes to retained members or `S7_StandardRetain` as startup-behavior changes, not cosmetic edits.
- Require S7RES only when the S7DCL contains `MLC_*` references.
- Instance DBs are coupled to an FB interface and are not authored as global DB documents.
