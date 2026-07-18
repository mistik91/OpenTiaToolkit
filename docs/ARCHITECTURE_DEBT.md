# Architecture-debt ratchet

The architecture tests record the current physical-line baselines of known large production hotspots. They prevent those files from growing while later roadmap chapters reduce their responsibilities. A 1,000-line screening threshold also flags new, unlisted production files for review; file length is a warning signal, not proof that a type is a god class.

`PlcDocumentWorkflowService` is checked both per known hotspot file and as the sum of every matching partial file. Partial classes can improve navigation, but they retain shared state and coupling and therefore do not constitute decomposition.

Baselines should move downward when hotspots shrink or disappear. To intentionally raise a value, first review the architectural impact, then update `tests/OpenTiaToolkit.ArchitectureTests/god-class-debt-baseline.json` in the same change with the rationale. Tests never rewrite the baseline.
