# SimaticML PLC XML Rules

- Preserve the namespace and schema version emitted by the target TIA version.
- Block IDs and FlgNet `UId` values are different identifier spaces; FlgNet UIds are decimal and unique in scope.
- Preserve block-level and per-compile-unit programming-language declarations.
- Avoid dangling wires, missing operands, duplicate IDs, and references to removed parts.
- XML-escape values and avoid comments inside fragile FlgNet content.
- Patch exported known-good XML instead of composing it from memory.
