# Architecture-debt ratchet

The original seven-chapter god-class hotspot roadmap is complete. All seven original exceptions have been removed and the historical hotspot map is empty. That result clears the planned hotspot list; it does not mean that all architecture debt has disappeared.

The vendor-free architecture suite applies four distinct controls:

- `screeningThreshold` preserves the 1,000-physical-line per-file review signal. Crossing it does not by itself classify a file or type as a god class.
- fully-qualified type aggregation is automatic. Roslyn discovers every class, record, struct, and interface in production source and combines all declaration parts by namespace, containing type, and generic arity. A partial type therefore cannot evade review by moving declarations into additional files.
- `historicalHotspots` holds exceptions from the completed roadmap. It is intentionally empty.
- `reviewedFileGrowthCeilings` records measured ratchets for reviewed responsibility clusters below 1,000 lines. These entries are review records, not a claim that every listed file is a god class. `typeAggregateExceptions` is available only for a legitimate reviewed aggregate; none currently exists.

Files and types may shrink or disappear without changing the baseline. To raise any value, first review the architectural impact, then update `tests/OpenTiaToolkit.ArchitectureTests/god-class-debt-baseline.json` in the same change with the rationale. Tests never rewrite the baseline.

## Residual responsibility review

The current reviewed ceilings are exact measured values, not targets:

- `PlcImportExportService.cs` (945) coordinates block and type import/export in XML and document formats. Its shared PLC selection, result shaping, and import/export symmetry make it acceptable for now, but the format branches remain separable. A likely next boundary is a document-format workflow distinct from XML block/type transfer.
- `V21HardwareAdapter.cs` (920) is the remaining large hardware boundary. It centralizes Siemens hardware, network, drive-telegram, GSD, feature-service, and attribute primitives behind one explicit adapter. Keeping vendor calls together is currently useful, but those capability families can evolve independently; drive/telegram access is the clearest next extraction.
- `PlcSurfaceService.cs` (895) owns cross-reference discovery plus external-source and ancillary PLC-surface operations. The shared reflection compatibility edge is cohesive enough to retain, while cross-reference projection and source lifecycle remain separate clusters. Cross-reference discovery is the likely next extraction.
- `PlcTagCsvWorkflow.cs` (894) owns one sequential CSV reconciliation transaction, including address-page policies, create/replace/delete behavior, restoration, and progress. Its ordering and rollback invariants justify one workflow today, but row planning and live mutation are distinct. A pure CSV reconciliation planner is the likely next boundary.
- `V21HmiDynamizationSynchronizer.cs` (759) owns read, validation, normalization, and synchronization for dynamizations and interface bindings. These share composite shapes and conversion rules, but read/projection and apply paths can grow independently. Export projection is the likely next extraction.
- `V21HmiEngineeringPrimitives.cs` (759) bounds reflection, engineering-property access, conversions, descriptors, and dynamic item-type discovery. It remains the intentional low-level Siemens edge, but conversion/property access and item-type discovery are separable. Item-type discovery and descriptor construction form the likely next boundary.
- `V21UnifiedHmiAccess.cs` (748) owns HMI resolution and normalized screen, tag, logging, geometry, and item-access primitives. It is acceptable as the shared typed access edge, but logging/tag access is a distinct capability. Logging access is the likely next extraction.

Two other near-threshold files were reviewed without residual ceilings. `HardwareCommands.cs` (996) is a multi-type command container whose individual commands are independently indexed; it is not one god class, and the global per-file screen already prevents growth. `HmiJsonModels.cs` (757) is a declarative multi-type contract/model container with no orchestration responsibility, so it is likewise not classified as a god class. Both conclusions should be revisited if their responsibilities or physical size change.

## Unified HMI screen JSON patch boundary

`HmiScreenJsonPatchApplier` is a compatibility facade for the three existing patch, filter-merge, and filter-validation entry points. The pure `HmiScreenJsonPatchEngine` clones the input screen, executes operations sequentially through a duplicate-checked dispatcher, and returns ordered operation results. Cohesive handlers under `Services/HmiScreenJsonPatch` own property/layout, item lifecycle, event, dynamization, and interface mutations; focused collaborators own aliases, filters, target resolution, scalar conversion, cloning, and result construction.

`HmiScreenJsonPatchOperationCatalog` is the ordered source of truth for supported operation names and normalization. When adding a future operation, update the catalog, the appropriate handler registration and implementation, filter metadata, capability hint, and characterization tests together. The former 1,609-line facade is now below the screening threshold, so its hotspot exception has been removed.

The vendor-free HMI screen-patch architecture suite keeps the facade and handlers focused and Siemens-free, proves that the canonical catalog, default registry, and agent-support list remain synchronized, and protects normalized duplicate registration and central dispatch ownership. Licensed tests continue to own executable behavioral proof for normalization, sequential edits, clone/failure isolation, filter planning, and invariant scalar formatting.

## PLC document workflow boundary

`PlcDocumentWorkflowService` is now a small, non-partial compatibility facade. Pure SIMATIC SD validation, short-lived plan storage, exact Siemens target access, persistent transaction storage, plan integrity, capability and promotion binding, transaction coordination, impact analysis, rollback, qualification probes, behavior probes, and online-compare analysis have focused internal owners under `Services/PlcDocumentWorkflow`.

`PlcCodeFormatService` retains its public compatibility constructor, while normal internal composition uses `PlcDocumentValidator`, removing the former facade dependency cycle. The transaction-file hotspot and aggregate partial-class exceptions were removed.

## PLC tag workflow boundary

`PlcTagService` is a small, non-partial compatibility and composition facade. It preserves the four existing entry points and constructor shape; `PlcService` and `IPlcService` continue to expose only find/replace and explicit modification. The internal-only single-tag creation and CSV reconciliation entry points remain compatible without widening the public service contract.

`IPlcTagRepository` and `V21PlcTagRepository` form the narrow Siemens boundary. They produce a normalized `PlcTagCatalog` of table and tag state, including stable repository-owned handles, so matching and planning do not rediscover or reflect over Siemens objects. `PlcTagFindReplaceWorkflow`, `PlcTagModificationWorkflow`, `PlcTagCreationWorkflow`, and `PlcTagCsvWorkflow` own the four cohesive workflows. Pure `PlcTagModificationPlanner` and `PlcTagCsvRules` components own matching, patch validation, collision checks, CSV indexing, ambiguity reporting, and TIA-like name allocation.

Explicit modification planning mutates the normalized catalog sequentially, including during dry-runs. A planned rename, create, delete, or replace is therefore visible to every later operation. `PlcTagMutationBoundary` owns the per-tag compensating recreation section shared by explicit and CSV replacement. Cancellation is observed before an operation but not between deleting a source and completing replacement or restoration. Failed CSV recreation now restores the complete original tag best-effort and reports restoration failure without recording successful deleted/created entries.

Future tag operations should add a focused planner or workflow and extend only the tag-specific repository surface needed for Siemens access. They should reuse the catalog and mutation boundary instead of adding matching, reflection, or rollback logic to `PlcTagService`.

The hardware chapter removed `HardwareService.cs` from the historical hotspot baseline. The public service is now a small compatibility facade over synchronous query readers, a read-only PLC-tag catalog audit workflow, target/reference/value rules, and a one-transaction patch coordinator. Patch operations are owned by five domain handler groups behind a duplicate-checked catalog, so non-Siemens normalization, safety, result-reference, guard-comparison, address, and commit policy can be tested without a live TIA project. The vendor-free hardware architecture suite protects facade size, composition, handler grouping and registration, and narrow transaction ownership. `V21HardwareAdapter` remains the explicit large Siemens hardware boundary and is retained as residual review debt.

## Unified HMI Siemens boundary

`V21UnifiedHmiAdapter` is a non-partial compatibility facade. Its constructor and callable surface remain stable while it explicitly composes focused components under `Services/UnifiedHmi`; the extracted components do not inherit one another. `V21UnifiedHmiAccess` owns normalized screen/tag paths, project and device resolution, screen and tag traversal, logging access, geometry projection, and item creation. `V21HmiEngineeringPrimitives` owns the bounded Siemens reflection primitives and item-type descriptors, while `V21HmiItemPropertyAccessor` owns engineering-property and attribute resolution, invariant conversion, multilingual/rich-text handling, color and screen-reference conversion, and dynamic item-type discovery.

Composite engineering behavior is separated by concern: `V21HmiEventSynchronizer` owns screen/item events and property events, `V21HmiDynamizationSynchronizer` owns dynamizations and interface bindings, and `V21HmiMappingTableSynchronizer` owns mapping-table creation and projection. `V21HmiCompositeAccess` coordinates these focused components without owning their synchronization algorithms. `V21HmiScreenCopyEngine` owns guarded graph copying, scripts, compositions, exclusions, cleanup, and warning accumulation. Faceplate probing remains read-only unless the caller explicitly requests a write probe, including independent write and restoration reporting.

Future Siemens Unified HMI support belongs in the narrowest access or synchronizer component. It should not add traversal, reflection, conversion, copy, or composite synchronization logic back to the adapter facade.

## HMI JSON import boundary

`HmiJsonImportService` is a non-partial compatibility facade preserving the existing constructor, `ImportAsync`, and static forwarding seams used by existing tests and screen-edit composition. `HmiJsonImportCoordinator` owns dry-run and required-gate preflight plus backup ordering. It composes `IHmiJsonDocumentExecutor`, `IHmiJsonBackupExporter`, `IHmiJsonImportTransactionRunner`, and `IHmiJsonImportReportWriter` boundaries so workflow behavior can be characterized without a live Siemens session. `HmiJsonImportTransactionRunner` is the sole owner of Siemens `ExclusiveAccess`, the undo transaction, commit-on-success, rollback projection, and nested online-mode classification. Explicit dry-runs and required apply-gate preflights execute inside rollback-only `ExclusiveAccess` transactions and never commit, so temporary validation objects cannot persist after normal transaction disposal. TIA online mode may block transactional preflight or apply with `tia-online-mode`; the toolkit never calls `GoOffline`, leaving the transition for the caller to perform manually.

`HmiJsonDocumentExecutor` owns document loading, effective filter selection, and deterministic screen ordering. The transaction runner writes the final report only after transaction metadata and safety information are complete. `HmiJsonScreenItemImporter` owns screen/item lifecycle semantics and type-mismatch recreation; its temporary validation items are additionally protected by the transaction runner's rollback-only preflight boundary. `HmiJsonScalarPropertyImporter` and `HmiJsonScreenPropertyAccessor` own scalar validation/application; `HmiJsonCompositeImportCoordinator` owns composite change accounting and immediate owner reacquisition before live interface writes. `HmiJsonCompositeMergeRules`, `HmiJsonImportSafetyPolicy`, `HmiJsonImportResultBuilder`, and `HmiJsonImportReportWriter` own their corresponding policies. Entry filters continue to merge unfiltered current state, while root composite filters retain authorized deletion behavior. Base hashes and exact filter-shape invariants remain in the agent edit and pure patch layers; the pure `HmiScreenJsonPatch` components remain Siemens-free.

Future JSON import behavior should be added to the document executor or a narrower lifecycle, scalar, composite, safety, transaction, or reporting collaborator. It must not open transactions in processors or rebuild orchestration in the compatibility facade.

## V21 Openness capability boundary

`V21OpennessAdapter` is now a small, non-partial composition and forwarding facade retained for constructor and test compatibility. Focused components under `Services/V21` own type/service resolution, PLC and Unified HMI software discovery, shared engineering-object primitives, PLC tags and structures, compilation and diagnostic projection, online/compare/download/CPU operations, and XML/document import-export behavior. Extracted components never depend back on the facade. Production compile, verification, import-export, inspection, and hardware boundaries use their narrow capabilities while legacy constructors continue accepting the facade.

Future reflection belongs in the narrowest V21 component. Type lookup and generic `GetService` remain in `V21TypeServiceResolver`; higher-level tag, compile, online, or document algorithms must not migrate into it or a new general reflection helper. `V21HardwareAdapter` and `V21UnifiedHmiAdapter` remain the focused hardware and Unified HMI boundaries.

## Portal session lifecycle boundary

`PortalSessionService` is a small, non-partial compatibility facade. `PortalInstanceWorkflow`, `PortalProjectOpenWorkflow`, and `PortalProjectLifecycleWorkflow` separately own instance attachment, exact project-open selection, and project persistence/mutation ordering. `PortalRuntimeAdapter` is the bounded edge for static process discovery, attachment, and portal construction. `PortalSessionStateCoordinator` is the sole owner of candidate-then-commit state transitions, dependent PLC/HMI selection clearing, owned-resource release, obsolete attachment disposal, and cleanup-on-dispose. `PortalSessionPolicy` owns local-session recognition, path/archive splitting, archive mode and device identifier parsing, secure UMAC delegate construction, lifecycle result shaping, and inner-exception projection.

The dependency direction is `PortalService -> PortalSessionService -> portal workflows -> state/policy and Siemens runtime operations`. State is committed only after a candidate has attached/opened and passed exact-path verification; cleanup failures after a commit are logged without erasing the new valid state. UMAC passwords remain `SecureString` values passed directly to Siemens credentials and are never projected into logs or results.

Future portal selection, matching, ownership, persistence, and authentication changes belong in these focused portal collaborators rather than either public facade or a command. Static portal process discovery and construction must remain inside the portal workflow/runtime edge.

Chapter 7 removed the final two original hotspot exceptions (`V21OpennessAdapter.cs` and `PortalSessionService.cs`). The planned seven-chapter hotspot roadmap is complete and the historical hotspot baseline is empty; the residual review records above remain active architecture debt controls.
