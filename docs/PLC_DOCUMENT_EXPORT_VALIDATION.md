# Recursive bulk PLC document export qualification

**PASS — TIA Portal V21, 15 November 2026.** This qualifies the focused `export-blocks-as-docs` change against the released preview.9 worker and a disposable offline PLC fixture.

## Contract and implementation

Bulk document export now traverses root and nested block groups once, captures each selected block handle and name, and calls the same Siemens document API as single export. It does not project browse metadata or resolve each selected name again. There is no persistent inventory or new service abstraction; the unused browse-service dependency was removed from the existing composition.

The existing case-insensitive name regex, invalid-pattern error, flat output, per-target failures, result envelope, and command parameters remain. Recursive coverage is an intentional scope correction: the previous bulk command omitted nested blocks that single export could find. Case-insensitive collisions among selected filenames fail those targets before their document calls; distinct targets continue. Collision handling is covered by instrumented objects, since the real fixture has unique PLC block names.

The live campaign caught a .NET Framework interop detail before handoff: `Directory.CreateDirectory(absolutePath).ToString()` returns the final folder name, which Siemens rejects as a relative `directoryInfo` argument. The exporter creates the directory once and constructs an explicit `DirectoryInfo(exportPath)`, preserving the single-export argument representation. The fake document API now checks that representation so the regression also fails without TIA.

## Avoided reads

The standalone probe invokes the production service with counted objects. The regression suite covers both a full export and a regex selecting only the last of 1,000 blocks.

| Root blocks | Previous name reads | Current name reads | Previous metadata reads | Current metadata reads |
| --- | ---: | ---: | ---: | ---: |
| 100 | 5,150 | 100 | 500 | 0 |
| 1,000 | 501,500 | 1,000 | 5,000 | 0 |

These counts measure discovery work, not Siemens document generation or disk throughput. No wall-clock speedup guarantee is inferred.

## Real TIA results

The immutable source archive was retrieved to a new campaign directory. The saved fixture contains one LAD OB, twelve root SCL FCs, a nested FC, and an FB plus its instance DB two groups deep. Each session starts its own hidden portal and verifies project ownership and path before reading the PLC.

| Check | Result |
| --- | --- |
| Released preview.9 single exports | All 16 blocks export successfully. |
| Released preview.9 bulk export | Only the 13 root blocks are selected and exported. |
| Candidate bulk export | All 16 root/nested targets export successfully. |
| Output equivalence | All 16 candidate bulk documents match candidate single exports and released single exports byte for byte. |
| Case-insensitive nested-name regex | Exactly the requested nested block exports. |
| No-match regex | Zero targets, no files, no per-target errors; existing command-level failure status retained. |
| Invalid regex | Zero targets, explicit error, no output directory. |
| Per-target filesystem failure | A directory occupying `Main.s7dcl` fails only Main; the other 15 targets export. |
| Health and freshness | Software tree unchanged; baseline/final/fresh-session compiles have zero errors and warnings. Reopen uses a fresh worker and portal. |

Siemens emitted only `.s7dcl` files for this fixture; multilingual `.s7res` payloads were not exercised. Import round-tripping, online execution, simulator download, and other TIA versions are outside this read-only export qualification.

Fixture preparation also reproduced a separate existing source-generation issue: a transaction requesting a subgroup generated at the root, then rejected the unexpected identity. It returned `RolledBackAndVerified`; a fresh native fixture check confirmed only Main remained and no source objects survived. Native Siemens group-targeted generation prepared the export fixture from the already validated sources, removed those sources, and compiled cleanly before saving. This diagnostic setup does not qualify or fix toolkit SCL subgroup generation. Its terminal package was copied to evidence before explicit discard.

## Automated gates and reproduction

The final Release build has zero warnings/errors. All **842 non-integration tests**, **54 architecture tests**, **7 worker IPC tests**, and **1 real-TIA document-export integration test** pass. Full formatting, generated-reference validation, skill validation, packaged/installed skill drift checks, CLI diagnostics, and MCP stdio smoke pass. Exposure remains **186 commands / 189 MCP tools**. Architecture debt ceilings were not raised.

The opt-in integration test requires an archive with exactly one PLC, unique block names, multiple block types, exportable root/nested blocks, and a block at least two groups deep. It retrieves its own copy and never saves it:

```powershell
$env:OPEN_TIA_TEST_DOCUMENT_EXPORT_DISPOSABLE = 'true'
$env:OPEN_TIA_TEST_DOCUMENT_EXPORT_ARCHIVE_PATH = '<absolute fixture .zap21 path>'
$env:OPEN_TIA_TEST_DOCUMENT_EXPORT_WORKER_PATH = '<absolute registered V21 worker path>'
$env:OPEN_TIA_TEST_DOCUMENT_EXPORT_ROOT = 'C:\OTT\DocumentExportTests'
dotnet test tests\OpenTiaToolkit.Tests\OpenTiaToolkit.Tests.csproj -c Release --filter 'FullyQualifiedName~PlcDocumentExportWorkerIntegrationTests'
```

Register the exact worker after building it and before the live test. The test records command outcomes and audit logs, compares complete emitted files, and closes only its owned sessions.

## Retained evidence

Evidence is external at `C:\OTT\BulkExport\20261115`, including reviewed setup helpers, source validation/transaction outcomes, registration manifests, baseline exports, final document hashes, live audit logs, and TRX files. The passing live run is `Live\docs-255168c4255341dfa4a2453850346229`; `released-candidate-documents.json` records all 16 before/after hashes. Earlier fixture/test failures remain recorded separately.

| Identity | SHA-256 |
| --- | --- |
| Immutable source archive | `287F7F097D9D8800A5ADBF227AEC920BDB2820EB72A0A02F42C3938A4F8547C2` |
| Disposable document fixture archive | `9F93977BB89AC30002DF68646A948FC7067EA68FD1D0078043474AEBFE0DB2F6` |
| Released preview.9 worker | `8A5C16A28977160DBB682566616CAE548E9D07809AC10207C5E734C16E50A436` |
| Final candidate worker | `9A85ED68E901FA694ACB2949F04EDD6A20E36F28139038FDA70AAF8520F75C4D` |
| Final candidate Core | `B006DE99840E9D480CFE34B1B5572DD7CC8144E73D8BF610575CE052BBCDF986` |

The final audit confirms unchanged source archive, original project, and fixture archive hashes. All campaign portals, workers, setup helpers, and testhosts closed. The pre-existing private-repository MCP/worker processes and Siemens server were preserved. No project archives, Siemens assemblies, or live generated artifacts are included in the change.
