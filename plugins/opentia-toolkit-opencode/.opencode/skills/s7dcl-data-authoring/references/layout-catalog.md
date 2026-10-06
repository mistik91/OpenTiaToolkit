# S7DCL global DB layout catalog

| Construct | Authoring rule | Impact rule |
|---|---|---|
| Scalar | Use an explicit Siemens type and one unique member name | Type or order changes alter layout |
| String | Use reviewed `String[n]` or `WString[n]` bounds | Length changes alter layout and external payload size |
| Array | Use ascending fixed bounds and calculate total elements | Bound, dimension, or element-type changes alter layout |
| Structure | Preserve `STRUCT`/`END_STRUCT`, nested order, attributes, and quoted names | Never flatten or reorder during an unrelated edit |
| Project UDT | Use the exact exported type spelling and group-resolved target | Require dependency evidence; name equality alone is insufficient |
| Retain | Preserve exported member attributes and `S7_StandardRetain` unless startup behavior is the requested change; do not invent member-level `S7_Retain` | Review warm/cold restart behavior |
| Start value | Assign each supported top-level member at most once with a type-correct literal | Initialization changes are functional changes |

## Safe edit algorithm

1. Export the exact DB and retain its block number, optimization flag, and unmodified member order.
2. Map every requested change to a member path. Reject ambiguous quoted/unquoted names.
3. Classify the edit as value-only, append-only layout, or destructive layout.
4. Inspect cross-references and HMI/recipe/OPC UA/communication consumers for every layout edit.
5. Validate, transact, canonical-export, and compile. Re-export and compare the complete member tree, not only the changed line.

The complex asset demonstrates syntax, not permission to copy its layout into an existing DB. Instance DBs and PLC types use different workflows.
