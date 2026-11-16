# Unified HMI dynamization write-safety qualification

**PASS — TIA Portal V21, 16 November 2026.** This follows the fixture failures retained during [composite filtering qualification](HMI_COMPOSITE_FILTERING_VALIDATION.md). The baseline is merged PR #10, commit `519f08f5cfa2177e17fd820a13505cfef5753894`.

## Finding and supported boundary

Tag and Script dynamizations work individually and together. Both native creation and public patch workflows persist them across fresh worker/portal sessions. Ordinary event handlers also coexist with them.

The failing interaction is between **property-event handlers and root dynamizations on the same owner**. Direct typed Siemens calls reproduce it without toolkit synchronization code: create two Tag bindings, create a property-event handler, observe the binding count fall from two to zero, and save/reopen with both bindings absent. Reversing the order can terminate the TIA session. `Text.Change` and `Visible.Change` each reproduce the unsafe interaction. These observations use the installed V21 SDK file version `2100.200.5.1`; they do not establish behavior on other Siemens builds.

Reordering the writes is not a reliable workaround. A property event can hide existing bindings that reappear when the handler is removed. An attempted deletion-first switch also lost the session. Those experiments are retained, and no ordering workaround is included in production.

The importer now checks the effective current and requested collections before processing that screen/item's properties or composites. It rejects changed coexistence and direct switches, including when filters omit the conflicting existing collection. Empty/new-owner preflight is checked too. Read failures fail closed. The check reuses the existing filter, merge, and comparison rules; no new command or transaction mechanism was added.

Independent owners can use the two kinds. Unchanged coexistence remains readable/importable, and ordinary event edits remain available. For an intentional switch, clear **both** collections with root filters, verify and save the cleared state, reopen, and take a fresh snapshot before creating the other kind. Moving a handler to another owner is not an automatic workaround: it can change its event source and therefore its behavior.

## Automated and live validation

The Release build has zero warnings/errors. All **898 non-integration tests**, **54 architecture tests**, and **7 worker IPC tests** pass. The 15 new cases cover new/existing owners, screen/item scope, omitted peer collections, entry filters, unrelated filters, unchanged state, direct-switch rejection, explicit clearing, different owners, and unreadable state. The two existing broad HMI integration scenarios now put property events on a distinct fixture item instead of exercising unsafe coexistence; they compile, while their corresponding public workflows are qualified by the disposable campaign below.

Formatting, CLI diagnostics, MCP stdio smoke, generated-reference validation, canonical skill validation, and packaged/explicitly installed skill drift checks pass. Exposure remains **186 commands / 189 tools**; no architecture debt ceiling or release version changed.

All Siemens campaigns use immutable archive retrieval into unique directories, owned hidden portals, verified project paths, serial commands, and explicit dry-runs. The final candidate checks:

| Case | Verification |
| --- | --- |
| Unsafe creation | Same-owner property events plus Tag/Script bindings are rejected in dry-run and confirmed apply, with unchanged snapshots and a healthy HMI session. |
| Existing peer state | Adding property events to a bound item, or bindings to an item with property events, is rejected even when the peer collection is outside the requested filter. |
| Scope/filter safeguards | Screen-level mixing and an entry-filter patch are denied; the latter also exercises the existing root-filter requirement. |
| Direct switch | Clearing one kind while creating the other in the same apply is rejected before unsafe SDK calls. |
| Supported combinations | Screen/item ordinary events, Tag/Script bindings on one item, and property events on another are applied and persist across reopening. |
| Staged switch | Clear both owners, verify exact baseline restoration, save/reopen, then apply the opposite distribution of property events and bindings. Inverse cleanup restores the baseline again. |
| Real late failure | A direct import with both preflight gates disabled writes one item's text, then the guard rejects a later owner's mixed composites. The existing transaction rolls back: `DryRun=false`, `PropertySetCount=1`, `PropertyFailedCount=1`, `RolledBack=true`. Fresh reads and a fresh portal reopen match the baseline exactly. |

The rollback test disables gates only inside its owned disposable project to exercise the existing rollback path; normal command defaults remain unchanged. HMI compilation/runtime execution and other TIA versions are outside this qualification. No HMI compile pass is claimed.

## Evidence and identities

Local evidence root: `C:\OTT\HmiDynamization\20261116`. It contains the isolated baseline worker, native probe and public runner sources, exact allow-list manifests, per-command arguments/outcomes, canonical snapshots, retained failures, TRX files, and host/build logs. `SafetyRunner\Campaign.csproj` replays the full safety campaign or the focused `rollback-only` mode. Passing runs are `safety-b6c28d88` (120 command outcomes) and `rollback-da67469d` (29); `latest-safety.txt` and `latest-rollback.txt` identify them. The final proof audit also checks exact script bodies, binding tags, event identities, and separation of the two composite kinds in both supported distributions.

Earlier harness failures are retained separately, including the distinction between an inner dry-run preflight safety block and the patch result's top-level flags, and the direct import's expected failed command outcome. A preflight `PropertySetCount` is a planned count; only the final `DryRun=false` run qualifies the actual-write rollback claim.

| Identity | SHA-256 |
| --- | --- |
| Immutable original archive | `287F7F097D9D8800A5ADBF227AEC920BDB2820EB72A0A02F42C3938A4F8547C2` |
| Baseline worker | `1AA7A25F1B11BF560B31F8CF772B76F468A3245E1F4F2E427D337822C5B33613` |
| Baseline Core | `FA3B514117454FBAD9D9599CD3EE35998D77D92592EE8E992DDC36D1A35D9485` |
| Earlier Core used for native control discovery | `F221E5946A62E198C17A98CA60BD17B6C07630512C0AEC288B1B847255232CFF` |
| Candidate worker | `F52EE560C5AD16CB5523A8BEA7E9896468C0206615C2912492642D82213B330D` |
| Candidate Core | `18150410BEADC8536EFD738F8FABB9686148A2786F514FC398587FE834242482` |

The candidate worker was registered after its final build. Original archive/project hashes remain unchanged. The final process audit matches the initial Siemens server and four private-repository MCP/worker processes; no campaign portal, worker, helper, runner, or testhost remains. Release artifacts were not modified.

Native controls follow the documented [Siemens property-event API](https://docs.tia.siemens.cloud/r/en-us/v21/functions-for-accessing-the-data-of-an-hmi-unified-device/hmisoftware/screens/screenitems/propertyeventhandlers/description-propertyeventhandlers); the failure behavior above comes from the retained experiments, not a claim that Siemens documents that limitation.
