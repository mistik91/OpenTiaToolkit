---
name: simaticml-plc-fallback
description: Safely patch Siemens SimaticML PLC XML with OpenTiaToolkit when S7DCL or native source cannot represent or round-trip the required block/type feature. Use for exported PLC XML, FlgNet XML, exact metadata preservation, unsupported document formats, and narrow XML fallback imports.
---

# SimaticML PLC Fallback

1. Read [xml-rules.md](references/xml-rules.md).
2. Prove text paths are unsuitable through live format discovery or export failure.
3. Export the exact target and retain an untouched backup.
4. Patch the smallest subtree. Preserve namespaces, IDs, language attributes, interfaces, and unrelated compile units.
5. Import under a disposable name/project first when changing FlgNet topology.
6. Verify and compile; restore the backup on failure.

Do not generate general LAD FlgNet XML from scratch. Prefer S7DCL.
