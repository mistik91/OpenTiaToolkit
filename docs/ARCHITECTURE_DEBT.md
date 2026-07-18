# Architecture-debt ratchet

The architecture tests record the current physical-line baselines of known large production hotspots. They prevent those files from growing while later roadmap chapters reduce their responsibilities. A 1,000-line screening threshold also flags new, unlisted production files for review; file length is a warning signal, not proof that a type is a god class.

`PlcDocumentWorkflowService` is checked both per known hotspot file and as the sum of every matching partial file. Partial classes can improve navigation, but they retain shared state and coupling and therefore do not constitute decomposition.

Baselines should move downward when hotspots shrink or disappear. To intentionally raise a value, first review the architectural impact, then update `tests/OpenTiaToolkit.ArchitectureTests/god-class-debt-baseline.json` in the same change with the rationale. Tests never rewrite the baseline.

## Unified HMI screen JSON patch boundary

`HmiScreenJsonPatchApplier` is a compatibility facade for the three existing patch, filter-merge, and filter-validation entry points. The pure `HmiScreenJsonPatchEngine` clones the input screen, executes operations sequentially through a duplicate-checked dispatcher, and returns ordered operation results. Cohesive handlers under `Services/HmiScreenJsonPatch` own property/layout, item lifecycle, event, dynamization, and interface mutations; focused collaborators own aliases, filters, target resolution, scalar conversion, cloning, and result construction.

`HmiScreenJsonPatchOperationCatalog` is the ordered source of truth for supported operation names and normalization. When adding a future operation, update the catalog, the appropriate handler registration and implementation, filter metadata, capability hint, and characterization tests together. The former 1,609-line facade is now below the screening threshold, so its hotspot exception has been removed.
