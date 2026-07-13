# SimaticML PLC XML Rules

- Preserve every namespace and schema version emitted by the target TIA version. In V21, `Document` itself may be unqualified while nested interface sections carry Siemens namespaces.
- Block IDs and FlgNet `UId` values are different identifier spaces; FlgNet UIds are decimal and unique in scope.
- Preserve block-level and per-compile-unit programming-language declarations.
- Avoid dangling wires, missing operands, duplicate IDs, and references to removed parts.
- XML-escape values and avoid comments inside fragile FlgNet content.
- Patch exported known-good XML instead of composing it from memory.
- Treat `Engineering` and `DocumentInfo` as volatile export metadata. Baseline binding canonicalizes those sections away but compares the complete engineering object payload.
- Keep the candidate and untouched baseline as separate immutable artifacts for real patches. An identical file is useful only for a round-trip probe.
- PLC types export as `SW.Types.PlcStruct`. Preserve the embedded `<Name>`, `ID` set, interface namespace, members, and exact type group. Existing types support baseline-bound transactional patch/import with canonical XML verification and XML rollback; creation still belongs to validated SCL or VCI.
