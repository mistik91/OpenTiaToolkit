# OTT-33 structured S7DCL data gold audit

## Scope and frozen environment

Branch `codex/s7dcl-data-benchmark` starts from merged library slice commit
`b018e35d63b549e668d5da083f258817c3623e46`. No production toolkit or skill
change was required by this gold path. TIA V21 Update 2 build 2100.0.121.1,
S7-1500 PLC_Main, CPU 6ES7 511-1AL03-0AB0/V3.1.

New fixture `tia-v21-s7dcl-data-12`:

- Archive: `E:\Documents\OpenTiaBench\Archives\OTT_Benchmark_V21_S7dclData_20261001.zap21`
- SHA-256: `A550A21C719511E7F019E4FBEEC1413EB8238762E8E38D9547EECF3E0B67D02E`
- Size: 5,597,695 bytes.
- Parent: independently hashed `tia-v21-baseline-1`.

Fixture construction sources are under `fixtures/builders/ott-33`. A disposable
hidden TIA instance (PID 20152) was launched and the baseline retrieved to
`E:\Documents\OpenTiaBench\Live\20261001-s7dcl-data-build-01`. The two seed
DBs are non-optimized and consistently non-retained. Native SCL creates only
the pre-existing UDT and consumer during fixture construction; it is not the
route permitted to benchmarked models.

## Pre-model diagnostic and safety proof

The first seed transaction `0bb118d4600f44c8893c2caebc6e6546` failed after
generating the UDT, because Siemens did not materialize the new DB. Full
rollback was verified with 0 errors/0 warnings. Minimal and structured shipped
S7DCL probe controls imported correctly and were then deleted by exact names.

A fixture-only helper, attached to the exact disposable project and refusing
existing-target overwrite, exposed Siemens' actual import result:
`Failure`, zero imported blocks, and an inconsistent retain/non-retain error
for the non-optimized DB. Offline validation did not reject this consistency
condition. The raw document-import wrapper reported success for a non-null
failure result; the persistent transaction correctly rejected the absent exact
target and rolled back. These are observed lower-level validation/reporting
limitations, not model failures. No production fix is included in this slice.

The seed was corrected to `S7_StandardRetain = FALSE`; it imported successfully.
Transaction `28bfd9d330ce44e1bf88497d871d32fc` created the UDT, consumer, and
backup and finished Succeeded. Independent compile: 0 errors/0 warnings.
Only the verified seed was saved and archived. Diagnostic probes are absent.

## Independently restored live gold paths

A fresh hidden TIA process (PID 18440) restored the archive to
`E:\Documents\OpenTiaBench\Live\20261001-s7dcl-data-gold-01`. Initial
compile: 0 errors/0 warnings. Root blocks were Main, OTT33_RecipeSettings,
OTT33_ReadRecipe, and OTT33_RecipeSettings_Backup. The new quality DB was absent.
Untouched SD baselines and full UDT XML were exported. Cross references found
three consumer reads of Enabled, Mode, and Target.

Both exact capabilities selected DirectDocuments. Recipe candidate validation
was valid/verified with only the expected non-optimized warning. New DB
validation was valid/verified with a project-type warning resolved against the
existing UDT. No new type was invented; canonical Siemens export resolved it
as `_.OTT33_BatchInfo`.

Forced transaction `55a92338756d406796bf6d81faebca61` changed the existing DB
first and then encountered a real Siemens import rejection for an intentionally
inconsistent new DB. Plan classified the existing target BodyOnly with identical
layout signatures and the second target Create. The first operation was actually
applied before the second failed. Outcome: RolledBackAndVerified, no recovery
required, original recipe canonical readback exactly equal to baseline, new DB
absent, rollback compile 0 errors/0 warnings. No project save occurred.

Positive transaction used the same untouched baseline with Implementation-only
authorization and one create-only new-DB operation. Outcome: Succeeded,
Applied/Verified true, no rollback, compile 0 errors/0 warnings. Canonical hashes:

- Existing DB: `28c0238d28dfd57e7df64f9ea90d48275dd2026e0475a678a265e1c835cf254a`
- New DB: `4ec98b98b1d859831c8bc96789b0e831b60565e7463d543cdcf6b1ddf3b014f8`

Independent canonical SD readback verified all four requested initializations,
all untouched members and values, nested structures, array bounds, and the
existing UDT reference. Backup, consumer, and Main SD exports were exactly
unchanged. Complete SW.Types.PlcStruct engineering XML was exactly unchanged
(export timestamp metadata excluded). Fresh compile: 0 errors/0 warnings.
The disposable gold project was closed without saving; the archive is unchanged.

## Scoring and campaign policy

The case totals 100 points with critical transaction, layout, identity,
initialization, preservation, and compile checks. Whole canonical SD patterns
preserve member order and reject extra declarations/initializations. Formatting
whitespace and line endings outside quoted tokens are tolerated; string case and
internal spaces remain significant. Full UDT engineering XML is checked.

Synthetic scorer tests cover 2 positive traces and 26 negative controls,
including altered unrelated values, identity, optimization/retention, nested
layout, UDT, array bounds, distractor/consumer/Main, overwritten baseline,
missing BOM, unguarded/widened replacement, skipped validation/plan/consumer
review, rollback instead of success, duplicate apply, warnings, raw import, and
save. These are scorer checks, not model trials or claims of exhaustive syntax
support.

First qualification: GLM-5.3 Flash High, three fresh trials, one-trial
checkpoints, user subscription authentication. GLM-5.3 High is the capability
fallback only. No skill tuning is justified by fixture-construction mistakes;
any model failure must be audited before escalating or changing generic skills.
