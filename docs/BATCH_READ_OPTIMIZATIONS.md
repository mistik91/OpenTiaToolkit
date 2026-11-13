# Batch and filtered-read optimization review

This page records PR #4. Its three follow-ups are implemented and qualified in [follow-up validation](FOLLOWUP_VALIDATION.md).

This change removes redundant Siemens reads while preserving the command catalog, matching rules, mutation policy, transaction boundaries, and freshness checks. It introduces no persistent cache or new service abstraction.

## Included changes

| Commands | Avoidable work | Change |
| --- | --- | --- |
| `apply-unified-discrete-alarms` | Repeated class, culture, shared-tag, and ID queries for every row | Exact-name reference results live only within one preflight; IDs come from its complete canonical snapshot. |
| `get-blocks --filter` | Full metadata and multilingual-comment reads before rejecting block names | Compile the regex once and check the name before reading metadata. Invalid regex behavior still falls back to the full list. |
| `search-blocks --search-in name` | Full metadata reads for every block before a literal name search | Apply the existing ordinal, case-insensitive substring rule before reading metadata. Comment and combined searches still inspect comments on all candidate blocks. |

The PLC change reads each block name once. An instrumented 1,000-block test selecting one block observes five metadata getters instead of 5,000. It also verifies output metadata, literal regex characters in searches, Turkish-culture matching, type filtering, invalid regex fallback, comment searches, stable ordering, and fresh subsequent reads. The implementation uses one private read helper shared by the two existing entry points.

See [alarm preflight qualification](alarm-preflight-cache-validation.md) for the independent real-TIA preflight measurement and mutation round-trip evidence. An alarm preflight speedup does not imply the same end-to-end gain for PLC browsing.

## Other audited paths

| Area | Finding | Disposition |
| --- | --- | --- |
| PLC tag modification and find/replace | Current workflows already read the vendor catalog once and operate on mutable local tag state. Some matching scans are linear in memory. | Keep the existing semantics. Adding more indexes needs a measured workload and must track ordered rename/create/delete state correctly. |
| Legacy PLC CSV reconciliation | `PlcTagService.UpdateTagsFromCsvAsync` discards cached table names and rereads names during row processing, but has no current command caller in `src`. | Leave this dormant path out of the command-performance change. Decide separately whether to retain or remove the legacy implementation. |
| HMI JSON export | Full screen metadata was projected before filtering. | Follow-up uses canonical path discovery before projecting selected screens; see [follow-up validation](FOLLOWUP_VALIDATION.md). |
| PLC cross-reference discovery | Multi-target impact analysis repeated the complete target inventory. | Follow-up shares target discovery within one analysis, with fresh consumer queries and independent inventories for plan, approval, and apply. |
| HMI trends | Conflicting rows silently used the first shared archive settings. | Follow-up blocks the entire conflicting batch in preview and apply before writes; consistent settings retain the batch-local archive cache. |
| VCI bulk synchronization | Per-object before/after status reads verify whether synchronization is needed and whether it succeeded. | Retain these reads; their removal would change verification behavior. |
| Bulk HMI data-log deletion | Logging-tag references are already scanned once for the requested log set. | No repeated full reference scan per log to remove. |

Recovery journals, explicit confirmation, complete canonical snapshots, fresh hashes, and post-write verification remain part of the safety contract. Their presence alone is not evidence of unnecessary work. This review prioritizes redundant vendor calls demonstrated by code paths and getter counts.

## Validation

The Release build uses the committed SDK policy. Non-integration tests, architecture tests, worker/MCP integration, formatting, generated references, and skill-package drift checks must pass together. The command surface remains unchanged.

On 13 November 2026, the expanded Release build passed with zero warnings/errors; **821** non-integration tests, **54** architecture tests, and **10** worker/remote-registry tests passed. Full formatting, generated-reference validation at **186 commands**, and packaged-skill drift checks passed. Both baseline and expanded candidate workers executed and passed the two live campaigns below. The PLC fixture contained one block, so its live measurements establish compatibility; the instrumented 1,000-block test establishes avoided metadata reads. No large-project PLC timing claim is made.

`PlcBrowseWorkerIntegrationTests` uses the same explicit disposable opt-in, archive, and exact worker variables documented in the alarm qualification. It retrieves its own independent project copy and compares filtered, unfiltered, name-only, comment, combined, and type-filtered results through `ToolkitSession`. It also verifies no-match results, invalid regex fallback, and complete metadata equality. The PLC campaign is read-only and closes without saving.

Run the two worker campaigns serially against each reviewed build with:

```powershell
dotnet test .\tests\OpenTiaToolkit.Tests\OpenTiaToolkit.Tests.csproj -c Release --no-build --filter 'FullyQualifiedName~UnifiedDiscreteAlarmWorkerCacheIntegrationTests|FullyQualifiedName~PlcBrowseWorkerIntegrationTests'
```

Both tests must execute and pass; an inconclusive or skipped fixture is not a live validation pass. Retain the immutable archive hash, exact worker identity, command outcomes, test results, and final process audit outside the repository.
