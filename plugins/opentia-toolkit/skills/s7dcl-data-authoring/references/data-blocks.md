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
- Keep start values type-correct and indices within bounds.
- Confirm every referenced PLC data type exists.
- Treat reorder, insertion, deletion, and type changes as layout changes requiring impact review.
- Require S7RES only when the S7DCL contains `MLC_*` references.
