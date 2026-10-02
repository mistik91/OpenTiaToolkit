# OTT-35 simulator environment preflight

Status: **BLOCKED ON LOCAL SIMULATOR CERTIFICATE TRUST**. No benchmark case,
gold behavior package, adversarial campaign, or model trial is qualified yet.
This is an environment checkpoint, not a model or skill score.

## Scope and baseline

- Branch: `codex/ott-35-plcsim-behavior-benchmark`, based on merged master
  `4b979a28aed731b4661847db59ff0d7e029d3d13`.
- TIA Portal V21 Update 2; PLCSIM Advanced V8.0 Runtime API x64.
- Immutable parent archive:
  `E:\Documents\OpenTiaBench\Archives\OTT_Benchmark_V21_20260731.zap21`.
- Archive size: 5,582,162 bytes. SHA-256 before and after preflight:
  `1C17F223F00F0A43EFD5E51A7C8E2DBE4DD424540AE0C9A799F102997205AB74`.
- Disposable copy:
  `E:\Documents\OpenTiaBench\Gold\20261002-ott35-preflight\OTT_Benchmark\OTT_Benchmark.ap21`.
- PLC: `PLC_Main`; CPU article `6ES7 511-1AL03-0AB0`.
- No original user project or existing simulator was open at initial discovery.

## Proven setup and validation

1. The Windows computer-use helper failed at initialization with
   `failed to write kernel assets ... (os error 3)`, including a kernel-reset
   retry. No Windows UI input was issued and no custom UI-automation fallback
   was used.
2. The initial visible TIA startup stalled. Only the newly created empty
   startup PID 5872 was terminated after parent/PID ownership verification.
3. The current Debug worker had no matching Openness allow-list identity.
   Its exact identity was registered; the freshly rebuilt Release worker was
   separately registered. Headless TIA startup then succeeded, followed by
   archive retrieval, project/PLC discovery, and compilation at 0 errors and
   0 warnings.
4. `Initialize-Simulator.ps1` used the installed Siemens Runtime API, not
   Windows UI automation, to register and power on `OTT35_Bench`. It reports
   CPU1511, the exact article above, Stop state, initial controller name `CPU`,
   and default local address `192.168.0.1`. Its WhatIf path was checked before
   the successful apply.
5. A runtime-manager owner must remain alive: its shutdown releases its
   registered simulator. The successful persistent owner is PowerShell PID
   1832 (exec session 98644), with a bounded four-hour lifetime starting around
   18:57 Europe/Rome. This is an intentionally retained setup process, not an
   untracked orphan. Do not stop it while a probe is running.
   Repeated setup correctly refused the existing instance, and a subsequent
   discovery proved that refusal did not change its identity or Stop state.
6. The fixture-only C# inspector was compiled x64 and allow-listed. It is
   bound to the exact disposable project above and performs no engineering
   writes or downloads. The observed download configuration was already
   configured: `PN/IE`, PC interface `PLCSIM` number 1, target `1 X1`.
7. Release build: 0 warnings, 0 errors. Release non-integration tests: 674
   passed. Architecture tests: 54 passed. No product code or skills changed.
   Benchmark definitions passed unchanged: 27 cases, 62 campaigns, 14 fixtures.
   The simulator helper also passed the PowerShell syntax check.

## Blocking evidence

The current Release CLI attached to disposable TIA PID 1032 and selected the
exact project/PLC. Simulator-only download dry-run succeeded. The confirmed
software download returned `Applied=false` with `Connect to module PLC_Main
failed`. The separate Release online-status control failed with:

> Connection to device cannot be established. The device is not trusted.
> Please check the certificate.

Both controls are retained under
`E:\Documents\OpenTiaBench\Artifacts\ott35-setup` as
`baseline-download.stdout.log` and `online-preflight.stdout.log`, alongside
the command files and test TRX files. The separate current-chat MCP read
reported the same certificate failure. The baseline Main SD readback is
retained in `main-readback`.

No behavioral probe, runtime tag write, code import, evidence promotion,
model request, or model retry was attempted. The failed initial connection
is not enough to distinguish all download prerequisites; after certificate
trust is resolved, the complete connection/download/compare gate must be
repeated rather than assuming the download now works.

The disposable project was closed without saving and the owning headless
TIA session was reset so the user can open this copy in visible TIA. The
original archive remains read-only and hash-identical. The simulator owner
is retained for the manual setup handoff.
Final discovery found zero TIA instances and exactly the retained simulator;
no CLI, fixture inspector, or testhost process remained active.

## Required handoff and next work

Open the disposable project above in visible TIA Portal. Select only the
local `OTT35_Bench` simulator and perform its initial hardware/software
download. Review the simulator's certificate and establish trust manually;
do not disable secure communication or bypass certificate checking. Verify
that `PLC_Main` can go online, then resume the preflight.

Once the environment gate is proven, design and freeze OTT-35 around a
disposable stateful control package: interlocked request, delayed activation,
interrupted dwell, reset priority, and restart. Assertions must observe real
primitive runtime values through the guarded behavior runner, not infer
behavior from compilation or download. The gold path and deliberately wrong
implementations must be checked before model quota is used. Exact offline
and simulator cleanup, retained unrelated differences, and absence of probe
runtime tags are required after success and failure.

Campaign policy remains three fresh GLM-5.3 Flash High trials, followed by
full GLM-5.3 High only for genuine Flash capability failures. Keep price,
reasoning effort, provider failures, and independent audits separate. Pause
for a generic skill/toolkit defect; never add a fixture-specific skill recipe.
