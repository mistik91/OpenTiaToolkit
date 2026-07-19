# Architecture-debt ratchet

The architecture tests record the current physical-line baselines of known large production hotspots. They prevent those files from growing while later roadmap chapters reduce their responsibilities. A 1,000-line screening threshold also flags new, unlisted production files for review; file length is a warning signal, not proof that a type is a god class.

Aggregate checks are optional baseline entries. The ratchet retains a synthetic fixture proving that spreading a class across partial files is detected when an aggregate rule is configured, without retaining an exception for a class that has been decomposed.

Baselines should move downward when hotspots shrink or disappear. To intentionally raise a value, first review the architectural impact, then update `tests/OpenTiaToolkit.ArchitectureTests/god-class-debt-baseline.json` in the same change with the rationale. Tests never rewrite the baseline.

## Unified HMI screen JSON patch boundary

`HmiScreenJsonPatchApplier` is a compatibility facade for the three existing patch, filter-merge, and filter-validation entry points. The pure `HmiScreenJsonPatchEngine` clones the input screen, executes operations sequentially through a duplicate-checked dispatcher, and returns ordered operation results. Cohesive handlers under `Services/HmiScreenJsonPatch` own property/layout, item lifecycle, event, dynamization, and interface mutations; focused collaborators own aliases, filters, target resolution, scalar conversion, cloning, and result construction.

`HmiScreenJsonPatchOperationCatalog` is the ordered source of truth for supported operation names and normalization. When adding a future operation, update the catalog, the appropriate handler registration and implementation, filter metadata, capability hint, and characterization tests together. The former 1,609-line facade is now below the screening threshold, so its hotspot exception has been removed.

## PLC document workflow boundary

`PlcDocumentWorkflowService` is now a small, non-partial compatibility facade. Pure SIMATIC SD validation, short-lived plan storage, exact Siemens target access, persistent transaction storage, plan integrity, capability and promotion binding, transaction coordination, impact analysis, rollback, qualification probes, behavior probes, and online-compare analysis have focused internal owners under `Services/PlcDocumentWorkflow`.

`PlcCodeFormatService` retains its public compatibility constructor, while normal internal composition uses `PlcDocumentValidator`, removing the former facade dependency cycle. The transaction-file hotspot and aggregate partial-class exceptions were removed.

## PLC tag workflow boundary

`PlcTagService` is a small, non-partial compatibility and composition facade. It preserves the four existing entry points and constructor shape; `PlcService` and `IPlcService` continue to expose only find/replace and explicit modification. The internal-only single-tag creation and CSV reconciliation entry points remain compatible without widening the public service contract.

`IPlcTagRepository` and `V21PlcTagRepository` form the narrow Siemens boundary. They produce a normalized `PlcTagCatalog` of table and tag state, including stable repository-owned handles, so matching and planning do not rediscover or reflect over Siemens objects. `PlcTagFindReplaceWorkflow`, `PlcTagModificationWorkflow`, `PlcTagCreationWorkflow`, and `PlcTagCsvWorkflow` own the four cohesive workflows. Pure `PlcTagModificationPlanner` and `PlcTagCsvRules` components own matching, patch validation, collision checks, CSV indexing, ambiguity reporting, and TIA-like name allocation.

Explicit modification planning mutates the normalized catalog sequentially, including during dry-runs. A planned rename, create, delete, or replace is therefore visible to every later operation. `PlcTagMutationBoundary` owns the per-tag compensating recreation section shared by explicit and CSV replacement. Cancellation is observed before an operation but not between deleting a source and completing replacement or restoration. Failed CSV recreation now restores the complete original tag best-effort and reports restoration failure without recording successful deleted/created entries.

Future tag operations should add a focused planner or workflow and extend only the tag-specific repository surface needed for Siemens access. They should reuse the catalog and mutation boundary instead of adding matching, reflection, or rollback logic to `PlcTagService`.

The hardware chapter removed `HardwareService.cs` from the baseline. The public service is now a small compatibility facade over synchronous query readers, a read-only PLC-tag catalog audit workflow, target/reference/value rules, and a one-transaction patch coordinator. Patch operations are owned by five domain handler groups behind a duplicate-checked catalog, so non-Siemens normalization, safety, result-reference, guard-comparison, address, and commit policy can be tested without a live TIA project. General V21 adapter, Unified HMI adapter, HMI import, and portal-session decomposition chapters remain outstanding.
