# OTT-34 brownfield LAD gold audit

## Scope and fixture

Branch `codex/ott-34-lad-authoring-benchmark` starts from merged OTT-33 commit
`f8c6fc155c81a0e35c0b1714a3d1175a9b546417`. No production toolkit or skill
change was required. TIA V21 Update 2 build 2100.0.121.1, S7-1500 PLC_Main,
CPU 6ES7 511-1AL03-0AB0/V3.1. This is standard LAD engineering, not an F-CPU
program, safety qualification, download, or runtime simulation.

Fixture `tia-v21-lad-brownfield-13`:

- Archive: `E:\Documents\OpenTiaBench\Archives\OTT_Benchmark_V21_LadBrownfield_20261001.zap21`
- SHA-256: `952FC2C686BFA2C40D0D339340D8E6DDDEABF9B14E24367BF4BAAE796671EACC`
- Size: 5,691,086 bytes; archive read-only.
- Parent baseline SHA-256: `1C17F223F00F0A43EFD5E51A7C8E2DBE4DD424540AE0C9A799F102997205AB74`.

Seed sources are versioned in `fixtures/builders/ott-34`. The parent was
restored to `E:\Documents\OpenTiaBench\Live\20261001-lad-build-01` in
previously empty TIA PID 18440. Initial compile: 0 errors/0 warnings. Seed
transaction `dceaa4cfcab640899fede5335702315e` safely created the optimized
LAD FB OTT34_Conveyor (58434) and unrelated LAD FC OTT34_Conveyor_Backup
(58435), with clean compilation and verified readback. Only this verified seed
was saved and archived. Main remained unchanged; there are no diagnostic blocks.

The FB has six Boolean inputs, four Boolean outputs and one nonretained static
RunDelay : TON_TIME. Its five ordered LAD networks contain fault indication,
start request, delayed run, a RequestSeen set, then a RequestSeen reset.
The reset's existing precedence and all network title/comment resources must
survive. The backup has a similar name but is not an authorized target.

## Independent positive live proof

A new hidden TIA instance (PID 13008) restored the archive to
`E:\Documents\OpenTiaBench\Live\20261001-lad-gold-01`. Initial compile:
0 errors/0 warnings. Fresh SD exports matched the seed byte-for-byte.
Untouched baselines for the FB, backup and Main were retained outside candidate
and readback directories. The paired FB .s7res resource file was retained.

The candidate changes only network 2 and network 3 implementation:

- RunRequest = (AutoStart OR JogRequest) AND Permit AND GuardClosed AND
  NOT Fault AND NOT Reset. Both branches share the interlocks; one ordinary coil.
- Existing RunDelay.TON receives RunRequest, PT = T#750ms, unbound ET,
  and Q drives one ordinary DelayedRun coil.

Both offline validators reported valid/verified with no errors. Fresh exact
Modify capability selected DirectDocuments. Transaction
`cc4cfbd54e6f47d781fa7bc2c03a3691` used one baseline-bound document operation
with `allowed-changes Implementation`, CleanCompile and zero new warnings.
Plan classified BodyOnly/Safe with identical interface/persistent-state signature
`fcf62d90586631d582c1ee7799080eef1e23ea901154b2c89254c53625e054c0`.
Apply finished Succeeded, Applied/Verified true, no rollback or recovery,
and final compilation 0 errors/0 warnings. Canonical FB result hash:
`11024a5231428de1bf97306e2a419a766f729d175125e8bb6489ec582eee3a9e`.

Independent SD and XML readback was exported to the gold `readback` directory.
The Boolean wiring oracle verified all 64 input combinations; true rows are
13, 14 and 15 with input order AutoStart, JogRequest, Permit, GuardClosed,
Fault, Reset (first input is the least significant bit). The timer oracle
verified actual IN/Q wiring, the original nonretained static TON_TIME receiver,
750ms typed preset and unbound ET. FB attributes, interface, statics, all five
network metadata records, networks 1/4/5, paired resources, backup and Main
were unchanged. Fresh compilation: 0 errors/0 warnings.
The gold project was closed without saving.

## Actual compile-failure rollback proof

A separate fresh restore to
`E:\Documents\OpenTiaBench\Live\20261001-lad-rollback-01` retained all original
SD baselines. A disposable control FC OTT34_ForcedFailure (58436) was authored
with a structurally valid contact referencing deliberately undeclared
UnresolvedSignal. Offline document validation passed; live compilation must fail.
This is a deliberate semantic negative, not a model or toolkit defect.

Transaction `59369ea541854f8396c74c38d9f37de1` first applied the same valid FB
edit and then created the dependent control FC. Both operations actually applied
and read back before final compilation reported 1 error/0 warnings. Outcome:
RolledBackAndVerified, no recovery required. The control FC was removed, every
original SD file matched its untouched baseline SHA-256, the software tree
contained exactly Main/OTT34_Conveyor/OTT34_Conveyor_Backup, and compilation
returned to 0 errors/0 warnings. The project was closed without saving.
The read-only archive hash and size were rechecked unchanged.

## Scoring and limitations

The case totals 100 points with critical discovery, exact capability, verified
validation, transaction contract, preservation, circuit, and compile checks.
Canonical SD assertions retain quoted literal case and spaces while tolerating
layout whitespace. Only the two authorized network bodies are flexible; the
independent XML oracle decides their circuit meaning. Neither a textual list
of the right contacts nor the model's final claim is proof of correct wiring.

Oracle unit controls cover 6 equivalent positives, 35 circuit/XML negatives,
and 2 unsafe paths. Initial full-case controls covered 1 synthetic positive and
23 negatives, plus replay against the independent live-gold artifacts. The
post-cohort catalog audit expanded the full-case suite to 32 negatives and checks
every Mutating/Destructive/Online command against the prohibition guard; only
the two reviewed PLC transaction mutation primitives remain allowed. This does
not change the prompt, skills, point weights, or the engineering oracle.
Negatives include jog bypass, reversed interlocks, wrong outputs, latching,
cycles, invalid/duplicate connectors, unknown graph content, timer state/preset
and Q bypass, interface/static/resource/distractor changes, overwritten baseline,
missing BOM, unguarded/widened replacement, omitted validation/plan, duplicate
apply, rollback, warnings, raw import, save, and unrelated hardware mutations.
XML parsing prohibits DTDs, bounds size and graph complexity, confines relative
artifact paths and rejects reparse points below the workspace.

The evaluator deliberately supports only scalar local Boolean contacts,
ordinary coils and supported OR junctions for the combinational assertion,
plus the constrained three-part static TON network for the timer assertion.
All network parts, accesses and wires must be consumed. It is not a general
LAD simulator: PLC scan behavior, the 750ms timing transition and set/reset
execution precedence have not been runtime-tested. Existing set/reset networks
are preserved exactly rather than reinterpreted.

First campaign: three GLM-5.3 Flash High trials, fresh restores, one-trial
checkpoints, user subscription authentication. Full GLM-5.3 High is only the
audited capability fallback. No task-specific skill example or gold answer is
copied into the isolated model workspace.

## Pre-campaign gates

PASS: the full Windows PowerShell 5 benchmark `npm test` suite, including
the new oracle and whole-case negative controls; independent live-gold scorer
replay; definition validation (27 cases, 61 campaigns, 14 fixtures);
plugin skill synchronization check (zero drift); repository hygiene check;
generated command reference check (186 commands, current); and `git diff --check`.
Harness PrepareOnly independently restored the declared archive into
`C:\OTT\Bench\20261001T165731775Z-ott-34-t1-FEEA3D4C` before quota use.

No C# production, host, IPC, command or skill changes are included. Accordingly,
compiled-runtime rebuild/architecture tests were not required for this
benchmark-only slice; the existing built runtime is exercised by the live
gold, rollback, fixture preparation and subsequent model campaign.
