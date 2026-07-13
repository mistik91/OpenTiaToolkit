# SimaticML patch recipes

## Common preflight

1. Keep `baseline.xml` immutable and copy it to a separate candidate.
2. Identify exactly one engineering object by local name: `SW.Blocks.*` for a block or `SW.Types.PlcStruct` for a PLC type.
3. Record object name, programming language, group path, object `ID`, and the sorted complete ID set.
4. Make the narrow edit, then compare those invariants before calling the toolkit validator.
5. Review a semantic summary: changed element paths, old/new attribute or text values, added/removed elements, and ID-set differences.

## Existing PLC type

Safe candidates preserve `SW.Types.PlcStruct`, its object ID, embedded name, interface namespace, and complete ID set. A member rename, comment/value edit, or supported type-text change may be attempted when it does not add/remove IDs. Adding or deleting members changes the ID set and is rejected by the current narrow-patch contract; use native SCL or VCI instead.

## Existing block

Preserve the block class, name, number, programming language, interface, compile-unit boundaries, and every unrelated network. For FlgNet, the default validator rejects topology changes. `allow-flgnet-topology=true` permits only an explicitly reviewed candidate that still preserves the exported ID set; it is not permission to synthesize new parts or wires.

## Stop conditions

- Candidate and baseline target different names, object classes, schema roots, or ID sets.
- The diff includes an unrelated interface, network, member, namespace, or compile unit.
- XML contains DTD/entity declarations or loses Siemens namespaces.
- The requested outcome requires object creation, new FlgNet IDs, broad topology generation, or a missing baseline.

At any stop condition, route to S7DCL, native SCL, or VCI rather than weakening validation.
