# Unified HMI composite filtering qualification

**PASS — TIA Portal V21, 15–16 November 2026.** The baseline is master commit `d848104257f91753810d842afe57ca24231e9197` (merged SCL qualification); the candidate changes only composite export selection and its tests/documentation.

## Resulting behavior

The exporter passes the existing entry matcher through the concrete V21 adapter. Readers extract the identity and reject unmatched entries before reading scripts, binding values, converters, or nested interface dynamizations. Collection enumeration and identity reads still occur. No cache, second traversal, new command, or public filter syntax was introduced.

Case-insensitive matching, complete-entry suffix selection, ordering, omitted empty collections, and collection-failure warnings retain their existing contracts. A property event uses the existing normalized property/event key. Import and synchronization callers omit the optional predicate and continue reading complete current state.

## Automated gates

The final Release build has zero warnings/errors. All **883 non-integration tests**, **54 architecture tests**, and **7 worker IPC tests** pass without skips. Full formatting verification, generated command-reference validation, CLI diagnostics, MCP stdio smoke, canonical skill validation, and packaged/explicitly installed skill drift checks pass. Exposure remains **186 commands / 189 MCP tools**; no architecture debt ceiling changed.

The 16 new cases exercise the actual exporter helper, concrete adapter, and production readers using instrumented collection entries. For each composite kind they cover legacy payload equivalence, root/exact/case-insensitive/deeper-suffix/multiple/missing/unrelated filters, invalid identities, ordering, collection errors, and fresh unfiltered reads after a filtered read. Selected interfaces retain their complete nested dynamizations.

Four 1,000-entry cases show payload reads falling from **1,000 to 1** when one entry is selected. Excluded entries perform no payload, kind, or nested-tag reads. These are instrumented read counts, not a measured TIA wall-clock speedup; Siemens collection enumeration remains necessary.

## Real TIA campaigns

The exact baseline and candidate workers run serial commands through the worker IPC boundary. Each campaign opens its own hidden portal, retrieves an immutable archive into a unique directory, verifies project path and toolkit ownership, then closes its project without saving and closes only its own portal. Baseline/candidate JSON comparisons remove only `ExportedAtUtc`.

| Campaign | Checks |
| --- | --- |
| Disposable screen, 18 filter cases | Two screen events, two button events, and two property events. Full/default/root/exact/case/deeper-suffix/missing/mixed/scalar-only selections compare against the baseline. Empty root-dynamization selections are included. |
| Archived faceplate, 8 filter cases | Three persisted interface bindings, each with a nested dynamization. Root, single, case, deeper suffix, multiple, missing, and unrelated-property selections preserve the complete selected binding. |
| Archived screen item, 8 filter cases | Two persisted root dynamizations, one Tag and one ResourceList. Root, each entry, case, deeper suffix, multiple, missing, and unrelated-property selections compare against the baseline. |
| Candidate edit/read/inverse | Snapshot with root `EventHandlers`, dry-run and apply a single event edit, reject an old base hash, read the new script through an entry filter, preview/apply the inverse, and verify the original hash plus all composite state. A fresh worker/portal reopen matches the original full export. |

All 34 baseline/candidate filter comparisons pass, including matching command warning lists/counts. The edit check verifies that an export predicate does not leak into subsequent synchronization reads. The archive comparison campaigns are read-only; no production project is edited. Industrial project contents and raw scripts remain outside the public repository.

## Fixture limitations and follow-up evidence

An initial baseline-worker setup dry-run accepted a combined event/property-event/tag/script-dynamization patch, but its apply lost the TIA session. A separate native setup using the baseline adapter saved events and property events successfully; its attempted position and Boolean tag dynamizations were absent after reopening. A raw Siemens collection audit confirmed zero entries, rather than an exporter dropping persisted entries. These attempts are retained as fixture/write-path investigation evidence, not passing mutation qualification or candidate regressions. Their cause remains unestablished.

Existing persisted Tag/ResourceList bindings and faceplate bindings from a second immutable archive provide the meaningful live dynamization coverage instead. This change does not alter mutation behavior. The subsequent [dynamization write-safety investigation](HMI_DYNAMIZATION_WRITE_SAFETY_VALIDATION.md) reproduces the original fixture failures and establishes a guarded boundary for property events and root dynamizations.

HMI compilation was explicitly unavailable: the fixture's `HmiSoftware` did not expose `IEngineeringServiceProvider`/`ICompilable` through the checked Openness API. No HMI compile or runtime execution pass is claimed. Qualification covers offline V21 export/filter behavior and the focused event edit/read/inverse check, not HMI runtime behavior or other TIA versions.

## Evidence and identities

Evidence is retained locally at `C:\OTT\HmiComposite\20261115`: the external runner/setup/audit sources, exact registration manifests, per-command arguments/outcomes, canonical exports, failed attempts, native audits, and test/build logs. `Runner\Campaign.csproj` replays `baseline`, `candidate`, `face-baseline`, `face-candidate`, `face-dyn-baseline`, and `face-dyn-candidate`; `latest-<mode>.txt` identifies each completed passing run. Archive retrieval always uses a new run directory.

| Identity | SHA-256 |
| --- | --- |
| Original small archive | `287F7F097D9D8800A5ADBF227AEC920BDB2820EB72A0A02F42C3938A4F8547C2` |
| Disposable screen fixture archive | `A7182F39313B0575EF2526459953074FCF62B87889BA7B50FFFB15F46E443D2A` |
| Secondary archived-project copy | `392C02CFA27CF188293D7888B170A05ADC6A7B7489CEFD820F653AD6AD188C5F` |
| Baseline worker | `05665523AC7EFE7D1A705C30CEDE5AA5892B7392361B60775821B331F48DABFA` |
| Baseline Core | `F221E5946A62E198C17A98CA60BD17B6C07630512C0AEC288B1B847255232CFF` |
| Candidate worker | `1AA7A25F1B11BF560B31F8CF772B76F468A3245E1F4F2E427D337822C5B33613` |
| Candidate Core | `FA3B514117454FBAD9D9599CD3EE35998D77D92592EE8E992DDC36D1A35D9485` |

The final candidate worker was registered after its last build. Original archive/project hashes remain unchanged. The final process audit contains no campaign portal, worker, runner, helper, or testhost; remaining private-repository MCP/worker sessions and the Siemens server were left untouched. Four pre-existing private processes exited independently before the resumed campaign, so initial/final PID sets are not identical. Release artifacts and version numbers are unchanged.
