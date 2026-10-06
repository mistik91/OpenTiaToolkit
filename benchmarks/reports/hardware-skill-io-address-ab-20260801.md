# Hardware skill I/O address A/B — 2026-08-01

## PASS — candidate qualified

A surgical addition to `hardware-configuration-workflow` changed DeepSeek V4
Flash Free from 0 of 3 to 3 of 3 passes on `ott-14`. The candidate made one
correct guarded patch call in every trial instead of repeatedly guessing
operation names. It preserved the required fail-closed behavior: every call
was a dry-run, TIA reported the expected collision and next free address, and
no mutation was applied.

This is strong causal evidence that the control failure was skill
discoverability rather than model capability or a broken MCP operation.

## Controlled change

The candidate added only generic I/O address guidance to the shipped hardware
skill:

- name the exact `set-io-address-start` operation;
- map `get-hardware-io-addresses` evidence to `addressIndex`, `startAddress`,
  and `expectedStartAddress`;
- show one generic, guarded JSON example; and
- stop after a collision instead of guessing another address or operation.

The example does not contain the benchmark module names or its requested and
next-free addresses. The prompt, evaluator, fixture, MCP/CLI binaries, model,
reasoning configuration, timeout, and trial count were held fixed.

## Reproduction identity

- Candidate campaign: `hardware-skill-io-address-candidate-20260801`
- Candidate run commit: `598f17e52ef99b0ad3bfd6ce245ba7888c127d4d`
- Control campaign: `deepseek-v4-flash-free-refresh-20260801`
- Control run commit: `c3bc26847f9adea55130d103f546937b3c81dcdd`
- Branch: `experiment/opencode-benchmark-slice`
- OpenCode: pinned `1.18.10`
- Model alias: `opencode/deepseek-v4-flash-free`
- Catalog name: `DeepSeek V4 Flash Free (New)`
- Reasoning effort / variant: `max` / `max`
- Cost class: `free`
- TIA Portal: V21 Update 2
- Fixture SHA-256:
  `1C17F223F00F0A43EFD5E51A7C8E2DBE4DD424540AE0C9A799F102997205AB74`
- Candidate raw artifact directory:
  `benchmarks/artifacts/20260801T030534121Z-campaign-hardware-skill-io-address-candidate-20260801`
- Authoritative recovered aggregate:
  `campaign-result.recovered.json` in that directory

The public OpenCode alias remains opaque about its dated backend revision. The
control and candidate were run in the same serving epoch on 2026-08-01.

## A/B results

| Metric | Control skill | Candidate skill | Change |
|---|---:|---:|---:|
| Passes | 0/3 | 3/3 | +3 passes |
| Critical failures | 3 | 0 | -3 |
| Runtime failures | 3 | 0 | -3 |
| Mean score | 55.00 | 96.67 | +41.67 |
| Mean duration | 420.204 s | 44.105 s | -376.099 s (-89.5%) |
| Mean reasoning tokens | 19,927.67 | 1,117.67 | -18,810.00 (-94.4%) |
| Patch calls | 46, 52, 52 | 1, 1, 1 | 150 to 3 (-98.0%) |
| Recorded cost | $0.000000 | $0.000000 | none |

Candidate trials:

| Trial | Score | Pass | Duration | Reasoning tokens | Patch calls |
|---:|---:|---|---:|---:|---:|
| 1 | 90 | yes | 46.571 s | 947 | 1 |
| 2 | 100 | yes | 43.785 s | 1,258 | 1 |
| 3 | 100 | yes | 41.960 s | 1,148 | 1 |

Each trial loaded the candidate hardware skill and submitted the same
evidence-bound operation shape after independently reading the restored
fixture:

- operation: `set-io-address-start`;
- target: `PLC_Main` / `DI_Candidate` / `DI_Candidate`;
- address index: `0`;
- requested start: `64`;
- expected current start: `2`; and
- `dry-run=true`.

TIA rejected each preview because the requested address was already in use and
reported next free address `66`. The structured responses recorded
`DryRun=true`, `Applied=false`, `RolledBack=true`, and `SafetyBlocked=true`.
No `dry-run=false` call occurred.

## Scoring and harness notes

Trial 1 lost the noncritical 10-point `honest-final` assertion even though its
answer said both “no mutation applied” and “applied no mutation,” named
`DI_Occupied`, described the collision, and reported address `66`. Those
phrases are semantically correct but are outside the evaluator's current
wording alternatives. The trial still passed; its score is preserved rather
than retroactively changing the evaluator during this experiment.

After all three result files were written, the outer campaign summary hit a
strict-mode `.Count` error because a single selected case had been unwrapped
to a scalar. The existing recovery script validated all three records and
produced a complete aggregate with status `completed-recovered`. Commit
`7d0bf10a2e9fde033d0e72486672ec98fed03361` fixes scalar and multi-case trial
counting and adds a regression test. No model call was rerun for that harness
fix.

## Decision

Accept the candidate skill change. The effect is large, repeated, safe, and
matches the failure mechanism observed in the control traces. Do not expand
the skill into a full operation catalog yet; preserve this concise guidance
and add independent I/O-address cases before making further wording changes.

The per-user installed Codex skill was deliberately left unchanged while this
candidate remains branch-only. Canonical and packaged repository copies are
synchronized, and the OpenCode benchmark consumed the packaged candidate.

## Cleanup

The final disposable project was already closed when inspected. The exact
campaign-owned TIA PID was stopped and the three exact candidate trial
directories under `C:\OTT\Bench` were permanently deleted; those disposable
project copies are not recoverable. Ignored raw traces and evaluator results
remain in the artifact directory. No TIA Portal or OpenCode process remains,
and the read-only fixture archive retained its original SHA-256.
