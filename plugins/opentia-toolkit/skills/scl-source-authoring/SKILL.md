---
name: scl-source-authoring
description: Author and safely generate Siemens TIA Portal PLC blocks from native .scl external source files with OpenTiaToolkit. Use for complex SCL expressions, IF/CASE, FOR/WHILE/REPEAT loops, arrays, state machines, conversions, FB statics, timers, counters, or source-based FC/FB/OB generation.
---

# SCL Source Authoring

1. Read [patterns.md](references/patterns.md) before writing stateful instructions or interfaces.
   Use `assets/Codex_SCL_Probe.scl` as the minimal live-tested complex-control-flow source shape.
2. Inspect project blocks, types, DBs, tags, cross-references, and conventions.
3. Prefer a new unique block name and number for the first compile.
4. Write complete native Siemens SCL as UTF-8 with BOM.
5. Run `import-source --dry-run=true`, then apply with `dry-run=false --confirm=true`.
6. Run `generate-blocks-from-source --dry-run=true`, then apply with `dry-run=false --confirm=true`.
7. Verify generated blocks and compile. Fix diagnostics in the source. Add callers only after the generated block compiles, then compile again.
8. Remove disposable sources with `delete-external-source --confirm=true` after deleting their generated test blocks.

Use S7DCL when LAD or mixed LAD/SCL is required. Do not force complex SCL through a limited JSON/token DSL.
