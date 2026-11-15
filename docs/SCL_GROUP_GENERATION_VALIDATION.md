# SCL subgroup generation qualification

**PASS — TIA Portal V21, 15 November 2026.** This corrects the SCL destination issue discovered during [PLC document-export qualification](PLC_DOCUMENT_EXPORT_VALIDATION.md), including modification and rollback of existing nested targets.

## Cause and resulting behavior

Planning stored `group-path` and `type-group-path`, but execution discarded both and called parameterless source generation. Siemens generated new objects at the root, after which exact-target verification rejected the unexpected location.

The transaction now forwards its normalized destinations through the existing PLC service. A small source-generation helper resolves the requested group and invokes its compatible Siemens overload with `GenerateBlockOption.None`. An explicit destination never falls back to root. The lower-level `generate-blocks-from-source` command exposes the same optional parameters; preview validates the group and method without invoking generation. Existing root generation remains available.

The native V21 probe established an important boundary: each generation call can target a block subgroup **or** a type subgroup. Other declarations go to their root. Calling generation twice does not relocate existing objects. Requests for both subgroup destinations therefore fail before transaction mutation. Use separate TYPE and block sources within the same atomic transaction to target both subgroups, with dependencies where needed. This avoids rewriting source text or regenerating objects twice.

Siemens also reuses existing object locations. Planning and preflight reject a declared name found outside its requested group instead of risking an unintended modification. Missing destination groups, unavailable targeted APIs, confirmation failures, read-only targets, and ambiguous identities remain failures.

The rollback campaign exposed a second prerequisite: `get-block-info` searched only root blocks, so capability discovery reported an existing nested FC as missing. Exact block-info lookup now traverses the hierarchy. Discovery uses that metadata when the software tree identifies a unique nested target, preserving actual language, number, read-only state, and group identity. Ambiguous targets or unavailable metadata do not qualify a nested modification.

## Regression and host gates

The final Release build has **zero warnings and errors**. All **867 non-integration tests**, **54 architecture tests**, and **7 worker IPC tests** pass. The 25 added cases cover targeted overload selection, root aliases, previews/denials, missing groups/APIs, vendor failures without retry, normalized transaction forwarding, dual subgroup rejection, collisions at planning/preflight, recursive block metadata, and nested capability protection/ambiguity checks.

Full formatting, generated-reference validation, canonical skill validation, packaged/installed skill drift, CLI diagnostics, and MCP stdio smoke pass. Exposure remains **186 commands / 189 tools**, with optional destination parameters added to the existing generation command. No architecture debt ceilings were increased; the external-source service is smaller.

## Real Siemens campaigns

A hash-verified source archive was retrieved into a new native setup project. Only empty block/type test groups were saved and archived as `groups-baseline.zap21`. Native API overload experiments ran afterward without saving their generated objects. The toolkit campaigns retrieved independent copies of that immutable group fixture and verified hidden-portal ownership and project paths before reads or mutations.

| Campaign | Verified result |
| --- | --- |
| Native overload probe | Block destination places FCs in the requested block group and TYPEs at root; type destination places TYPEs in the requested type group and FCs at root. A second call does not move the existing objects. |
| Atomic success | One transaction creates nine objects through six sources: a root FC, separate nested TYPE and FC sources, a nested FB plus instance DB, and mixed TYPE+FC sources exercising each destination overload. All exact paths and canonical hashes are verified. |
| Preview and confirmation | Denied transaction apply, source preview, missing-group preview, unconfirmed generation, and dual subgroup generation leave inventories unchanged. Dual subgroup transaction planning fails before source import. |
| Late collision | Create a same-name root FC after planning a nested create. Capability revalidation rejects the stale plan before import; inventory remains unchanged. Unit tests separately exercise the repository location guard at preflight. |
| Inverse cleanup | Remove the instance DB before its FB and other fixture blocks; guarded type deletion follows an explicit review of its `ReviewRequired` layout warning. Sources and test targets are absent; compilation and a fresh worker/portal reopen match the original inventory. |
| Forced mid-transaction failure | Modify an existing nested TYPE and FC, create temporary TYPE/FC targets, then generate an FC with an undefined variable. Final compilation violates `CleanCompile` with one error. Automatic rollback returns `RolledBackAndVerified`. |
| Rollback proof | The two original canonical hashes are restored exactly; temporary TYPE, temporary FC, failing FC, and all imported sources are absent. All five target snapshots have restoration proof. Rollback compilation has zero errors/warnings. |
| Fresh rollback verification | Save only the verified restored disposable state, reopen through a fresh worker/portal, and obtain fresh planned baseline hashes equal to the retained originals. Discard the unapplied verification plan, perform inverse cleanup, save, then reopen again and verify the original inventory and clean compile. |

The restored TYPE hash is `5a53d84a4ee355b4af22bbd59b6bb6a3afbd761253550e534a1cb6c274a310e1`; the FC hash is `897db968c32f8a38d85d8c5ad17f3cf1be6967bd39f1d76d427f19ef20cf4bf4`. Both match in the failed transaction's restoration evidence and in the fresh worker's planned baseline snapshots.

These are offline V21 import/compile/rollback qualifications. They do not establish PLC runtime behavior, safety-language support, or compatibility with other TIA versions. Original projects and release artifacts were not modified.

## Retained evidence and reproduction

Evidence lives outside the repository at `C:\OTT\SclGroups\20261115`. It includes native setup/probe and worker campaign source, source artifacts, per-command arguments/outcomes, audits, registration records, TRX files, and transaction packages. Final passing runs are:

- `success-41d03036` (`success-qualified.log`, 120 command outcomes).
- `rollback-1762aec2` (`rollback-qualified.log`, 95 command outcomes).

The earlier harness failures and the nested-discovery reproduction remain recorded separately. All 31 campaign transaction packages were preserved before local discard; no recovery-required package was discarded. The final process audit matches the initial private-repository MCP/worker processes and Siemens server, with no campaign portal, worker, helper, runner, or testhost left running.

| Identity | SHA-256 |
| --- | --- |
| Immutable original archive | `287F7F097D9D8800A5ADBF227AEC920BDB2820EB72A0A02F42C3938A4F8547C2` |
| Immutable group fixture archive | `E7AD21CD3C85A7278CA9164F831573D9C2436B83BBF1AACEE48C35A21F167E3A` |
| Qualified worker | `05665523AC7EFE7D1A705C30CEDE5AA5892B7392361B60775821B331F48DABFA` |
| Qualified Core | `F221E5946A62E198C17A98CA60BD17B6C07630512C0AEC288B1B847255232CFF` |

Archive and original-project hashes are unchanged. The exact rebuilt worker was registered before live use. `Runner\Campaign.csproj` replays the retained `success` and `rollback` modes using those fixtures and the recorded worker path; it checks archive identity, creates unique run roots, and closes only owned sessions. Native setup refuses an existing destination. Canonical skills and CLI documentation describe the supported destination contract for ordinary use.
