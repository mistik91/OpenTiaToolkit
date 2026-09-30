# PREPARATION — project-library benchmark preflight

Date: 2026-09-30

Branch: `codex/library-workflow-benchmark`, based on merged VCI slice `e27bd7c`.

No library model campaign has run. The project-library fixture still requires
one-time administrator registration of the compiled construction helper before
its first Siemens Openness attachment. The immutable fixture, case definition,
gold mutation audit, and inference qualification must not be reported as passed
until that construction and independent verification have completed.

## Proven discovery

A fresh disposable baseline restore had zero project-library master copies and
zero library types. It compiled with 0 errors and 0 warnings. Existing
`TestLibrary` was opened read-only: it contains 40 HMI master copies and 28
types, including `UDT/MyANALOG_LEVEL` versions 0.0.3 and 0.0.4. Its older version
is non-default and Committed; 0.0.4 is the default Committed version. These assets
are fixture-construction sources only, not a mutable production target.

The construction project is a new restore of immutable `tia-v21-vci-10` at:

`E:\Documents\OpenTiaBench\Live\20260930-library-gold-02\OTT_Benchmark\OTT_Benchmark.ap21`

Its campaign-owned TIA PID is 16276. The seed archive retains SHA-256
`B058929F708DB5A5781428238D04B627C4611A0F15C40CE46D4CCC742E76C773`.
The initially inspected baseline copy was closed without saving. The pending
construction copy remains open intentionally for the registered helper.

## Generic contract findings

1. `open-global-library` requires explicit `confirm=true` through MCP even with
   `read-only=true`. The unconfirmed call was PolicyDenied; the confirmed call
   opened `TestLibrary` read-only and unmodified. The canonical skill example
   now distinguishes file protection from portal-session mutation.
2. `copy-from-master-copy` dry-run currently resolves the source path and target
   group, but does not validate payload compatibility or collisions. A probe
   against HMI graphic `Design_2/D2_Control_1` returned success for `PLC_Main`,
   with `DryRun=true` and `Applied=false`. Fresh PLC inventory remained exactly
   the four seed blocks. The skill now requires independent compatibility and
   target-object evidence instead of treating that message as compatibility or
   overwrite approval. No incompatible master copy was applied.

Neither correction includes the fixture's paths, names, constants, or solution.
All four packaged skill copies match the canonical source. The installed Codex
skill root has no `library-workflow` directory; no new installation was inferred
or performed.

## Construction helper

Source: `benchmarks/fixtures/builders/Ott32LibraryFixtureBuilder.cs`.

Compiled executable:

`C:\OTT\Review\ott-32-library-fixture-20260930\builder\Ott32LibraryFixtureBuilder.exe`

Executable SHA-256:
`CAEB2AD0D7A3FAFBB1FED4E918F036E4B2D2E85066703928420052A2BC54FF63`.

The helper compiled against the installed Siemens V21 Base/Step7 public API;
its non-mutating `--describe` mode passed. It has not attached or constructed
the fixture yet. It requires an exact positive TIA PID, exactly one open
project, the fixed disposable project path above, an empty project library,
the expected three seed FCs, and a read-only unmodified source library before
any write. It does not save the project. A failed construction must be closed
without saving and restarted from a fresh archive restore, not resumed.

The intended fixture contains three same-leaf master copies under distinct
Approved/PLC, Legacy/PLC, and Approved/HMI paths; an empty exact reuse group;
untouched existing PLC distractors; and an older PLC library-type instance
alongside newer project-library definitions. The gold audit must prove all of
these rather than assume success from compilation of the helper.

Registration command, to run in an elevated PowerShell:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File "E:\Documents\Codex\OpenTiaToolkit\scripts\Register-OpennessAllowList.ps1" -ExecutablePath "C:\OTT\Review\ott-32-library-fixture-20260930\builder\Ott32LibraryFixtureBuilder.exe" -PortalVersion 21.0
```

The allow-list identity is tied to this exact executable's path, hash, and
timestamp. If a repair requires recompilation, verify/register the new identity
again. The non-elevated Codex shell must not silently write this privileged
registration or repurpose an already allow-listed executable.

## Completed preparation checks

- `npm test`: passed the existing suite (24 cases, 54 campaigns, 11 fixtures),
  including synthetic evaluator, batch, merge, and recovery checks. This does
  not qualify the pending library fixture or a model campaign.
- Canonical skill validation and full packaged-skill drift check: passed.
- Repository hygiene and `git diff --check`: passed.
- Fixture-helper compilation and non-mutating `--describe`: passed. Invalid
  PID `0` was rejected before attachment as expected.
- Source library data hashes and the immutable seed archive hash remained
  unchanged. The helper's exact executable allow-list registration was still
  absent at the final preflight check.

## Resume plan

After registration, verify the actual allow-list identity, perform construction
only on the exact disposable project, and independently inspect the result.
Compile cleanly, verify type-version/update evidence, archive the reset baseline
as read-only, and restore an independent copy for the gold dry-run/apply and
preservation proof. Only then freeze `ott-32`, its mechanical negative controls,
and the campaign revision. Run three fresh GLM-5.3 Flash High trials, escalating
to GLM-5.3 High only for a genuine model-capability failure.
