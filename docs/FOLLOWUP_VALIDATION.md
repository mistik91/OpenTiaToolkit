# Batch trend and filtered-read follow-up

The three follow-ups from [the earlier audit](BATCH_READ_OPTIMIZATIONS.md) were reproduced on the merged PR #4 baseline and corrected. The live PLC campaign also exposed a pre-existing rollback verification ordering problem, which is included here because it prevented the failure campaign from completing with restoration proof.

## Behavior and boundaries

| Area | Previous behavior | Result |
| --- | --- | --- |
| Bulk HMI trends | The first row silently chose a shared archive's size and retention, even if later rows disagreed. Reversing rows changed the result. | Validate effective archive settings before resolving tags or writing. A conflict blocks every row in both preview and apply. Consistent batches configure each archive once. |
| Filtered HMI JSON export | Screen discovery read width, height, enabled state, and item counts for all screens before filtering. | Discover canonical paths, apply the existing filter, then export selected screens. Full screen-list metadata, matching, ordering, and missing-filter behavior are preserved. |
| PLC impact analysis | Each target query enumerated all tags, blocks, types, and sources again. | One lazy target inventory per `AnalyzeAsync` invocation. Consumer queries remain fresh per target. Planning, approval, and apply each create an independent inventory; standalone cross-reference calls remain fresh. |
| PLC type rollback | Canonical type export ran immediately after XML import, before rollback compilation. V21 rejected that export even though compilation later succeeded. | Restore all targets and dependencies, compile, then verify exact type identities and canonical hashes. Unproven hashes or compilation still retain `RecoveryRequired`. |

Trend defaults remain `logMaxSize=2000`, `durationDays=365`, and `segmentMaxSize=500`. The existing clamp to 1 remains part of effective-value comparison. Omission does not mean “inherit from another row.” Archive names use the existing ordinal, case-insensitive cache identity. Acquisition mode and cycle remain independent per tag; the read-only storage folder is excluded from conflict comparison.

The screen and cross-reference inventories are focused internal components. No command names, parameters, public `IPlcService` members, persistent cache, or cross-phase consumer cache were added. The small internal batch capability preserves existing service implementations and mock fallbacks. The architecture baseline changes only `PlcSurfaceService` from 78 to 80 lines for its two-line forwarding method; review confirmed it remains an expression-bodied delegation facade. The larger access and cross-reference implementations were reduced by extracting their inventory responsibilities instead of increasing their ceilings.

## Automated checks

The final Release build completed with **zero warnings and errors**. The final test soak passed **835 non-integration tests**, **10 worker/remote-registry integration tests**, and **54 architecture tests**, with no skips. Formatting verification passed; the formatter emitted its existing workspace-load warning. Generated references were current at **186 commands**; MCP exposure was checked against that catalog plus the three session tools. Repository hygiene, changed-skill validation, packaged skill synchronization, and the explicitly installed trend skill check passed.

Focused coverage includes conflicting settings in either order, individual sizing fields, defaults, normalized values, independent per-tag settings, fresh batches, lazy discovery, duplicate exact matches across kinds/groups, changed PLC selection, and independent analysis phases. The production analyzer test uses the real `PlcService` capability: five consumer queries read a 1,000-tag inventory once, query consumers five times, and discover a changed inventory on the next analysis. Create-only and empty analyses perform no inventory reads.

Rollback tests simulate a type that cannot export between import and compile. They retain recovery on wrong content, reject discarding an unverified package, and exercise explicit recovery. A false import return alone neither proves nor disproves restoration: exact read-back, matching hash, and the rollback compile condition provide the proof.

## TIA V21 end-to-end campaign

Validation ran on **TIA Portal V21 Update 2**, using the production `ToolkitSession`, host policy, IPC transport, and exact registered Release worker. Each domain retrieved an independent copy of the same read-only archive. Original projects and pre-existing private-repository workers were preserved.

Archive SHA-256: `287F7F097D9D8800A5ADBF227AEC920BDB2820EB72A0A02F42C3938A4F8547C2`.

Final worker SHA-256: `F8A455F4620595279DD67FA37121995FA51E479B8534ED965F3A90C4C1F851E9`.

Final Core SHA-256: `41434A49E207C7A6748B3C719DD4437ABB10774E91A0E87AFBEBF68471D54732`.

The final three campaigns completed **252 command round trips**, including expected safety failures:

- **Trends:** reject both conflicting row orders in preview and confirmed apply; independently prove zero logging tags and absent archives; deny unconfirmed apply; create two consistent shared trends with independent acquisition modes; change settings in a fresh batch; reject a conflicting update; block referenced-archive deletion; remove logging tags and archive with previews; verify zero logging counts after reopening in a fresh worker/TIA session.
- **Screens:** compare filtered payloads against the previous build, preserving canonical casing, mixed separators, nested paths, ordering, and silent missing filters. Create 63 screens across groups; verify metadata census and full export. Preview a width edit without hash change, apply with backup and read-back, reject a stale hash, preview and apply the inverse, and prove the original canonical hash. Close without saving and verify the original one-screen fixture in a fresh worker/TIA session.
- **PLC:** discover capabilities and create two types atomically; exercise two-target impact planning through the real provider; add an actual FC consumer after planning and after approval, proving both stale checks refresh consumer evidence; prove known consumers block deletion; remove the consumer and compile. Delete both types, then generate an intentionally invalid FC as the third operation. Siemens reports the undefined tag, and automatic rollback returns `RolledBackAndVerified`, removes the failed block/source, restores both type hashes, and compiles with zero errors. A fresh plan confirms restored hashes, successful guarded deletion removes both types, cleanup removes the source, and final/fresh-session inventories and compile return to baseline.

The diagnostic reproduction isolated the rollback issue: XML import succeeded, immediate type export failed, compilation succeeded, and the same export then succeeded. Both earlier `RecoveryRequired` packages subsequently passed explicit recovery with the corrected build. Each recovered project was saved and reopened in another fresh worker/TIA session; both type hashes still matched the retained baselines and compilation remained clean.

### Filtered-export measurements

Warm median elapsed time for a missing exact screen filter, three serial samples per size on independent copies of the same fixture:

| Screens | Before | After |
| --- | ---: | ---: |
| 1 | 11.65 ms | 7.12 ms |
| 21 | 67.99 ms | 27.22 ms |
| 63 | 170.79 ms | 45.50 ms |

These are local measurements of avoided projection work, not a general latency guarantee. Path discovery still traverses the screen hierarchy. The live PLC fixture proves transaction compatibility and freshness; the instrumented inventory test proves avoided reads, without claiming a large-project PLC timing improvement. No simulator download or online runtime behavior was needed for these offline changes.

## Retained evidence

The external evidence root is `C:\OTT\Followups\20261113\implementation`; it contains the exact worker manifest and registration record, standalone C# campaign source, per-command outcomes, audit logs, snapshots, patches/backups, retained transaction packages, TRX results, and console logs. Final independent runs are `plc-660347cc`, `trends-7dacbdcb`, and `screens-24257da0`; `final-e2e-console.log` records their successful completion before commit. Projects, Siemens binaries, and generated live artifacts are excluded from the PR.

The final audit confirmed unchanged archive and original-project hashes, clean final/fresh PLC inventories, zero-error/warning final compiles, and no campaign-owned TIA, worker, runner, or testhost process. The pre-existing private-repository MCP/worker processes and Siemens server were preserved. All 29 campaign transaction packages were copied to external evidence before their local store entries were discarded; neither incident was discarded while recovery remained unverified.
