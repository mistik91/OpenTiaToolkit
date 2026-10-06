TIA Portal is running with a freshly restored disposable `OTT_Benchmark` project.

In PLC `PLC_Main`, update only the comment of tag `OccupiedBefore` in `Default tag table` to exactly:

`Updated by benchmark OTT-03`

Use the normal guarded PLC tag workflow: inspect the target, preview the exact mutation, apply it only after a safe preview, independently verify the resulting tag, and compile the PLC. Do not change its name, address, data type, or any other tag.
