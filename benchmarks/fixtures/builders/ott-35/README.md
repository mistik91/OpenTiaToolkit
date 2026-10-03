# OTT-35 runtime behavior qualification checkpoint

Status: **not qualified; no model campaign is enabled**. See
[the live preflight report](../../../reports/ott-35-behavior-preflight-20261003.md).
The generic source-bound instance-DB correction is implemented and locally tested;
[fresh live validation is pending allow-list refresh](../../../reports/ott-35-instance-db-fix-20261003.md).

`Initialize-Simulator.ps1` owns a bounded PLCSIM Advanced instance. It must remain
alive during qualification; do not terminate it while a probe or recovery is active.

`Invoke-BehaviorQualification.ps1` starts its own Release MCP/worker and headless
TIA instance, restores an immutable archive to a new directory, and invokes the
guarded behavior-probe workflow. It never attaches to or mutates the user's open
project. Supply exact Release executable paths, a new restore/output directory,
an archive, package, vector, and the selected simulator instance name. Every MCP
request/response is retained in the output journal. Inputs must have the encoding
required by their format, including UTF-8 BOM for the code artifacts here.

Success requires offline and simulator cleanup, unchanged block/source inventories
and original Main export, clean final compilation, and the correct exact-evidence
reuse classification. `ExpectedStatus=Failed` is for independent negative controls,
not a way to turn an unqualified gold package into a pass.

An unknown mutation outcome, missing evidence, or unproven recovery deliberately
retains the owning MCP session and stops further qualification. Inspect the journal
and recover the exact disposable project before ending the retained process. Never
delete its recovery package or close the project merely to clear the failure.

The `gold/` candidate implements a 1-second, non-accumulating interlocked delay,
reset priority, and one activation count per rising active edge. It uses a native
SCL FB and instance DB, a global I/O DB, and a separate cyclic OB so the original
Main is preserved. The candidate acceptance vector in `cases/ott-35/` has 15 steps
and 26 assertions. It is independent of model output but has **not yet executed
successfully against this candidate**; timer margins still need live qualification.

Before publishing a case or campaigns: fix and independently verify generic native
instance-DB handling and rollback, rerun the gold package on a fresh restore, prove
semantic negative controls, then add bounded input staging and a structured behavior
evidence oracle to the harness. The intended campaign order remains GLM-5.3 Flash
High first, with GLM-5.3 High only for genuine model failures.
