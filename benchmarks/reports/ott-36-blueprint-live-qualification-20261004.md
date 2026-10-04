# OTT-36 engineering-blueprint live qualification — 2026-10-04

## Outcome: independent live qualification PASS; model campaign pending

Work remains on `codex/ott-36-blueprint-benchmark`, based on PR #40's clean
`dcbc09a` and local checkpoints `ef1c779` / `519e862`. This report supersedes the
registration gate in the [fix checkpoint](ott-36-blueprint-coordinator-fix-20261004.md),
not the historical [preflight reproduction](ott-36-blueprint-preflight-20261004.md).
The user refreshed the exact rebuilt Release worker registration. Path, UTC
timestamp and SHA-256 all matched before live testing. Worker SHA-256:
`3E3DCC214ADFE9C165673EB9C8AFB7F0E0C5251F416F408557D5A32C47C53C5A`.

## Three independent disposable restores

All runs used serial Release MCP calls, a separately owned headless TIA V21
instance, exact PLC/HMI discovery, and the immutable `tia-v21-baseline-1` archive.
No user project was open at discovery. Projects were closed without saving;
owned portals and MCP/worker processes exited. Every `cleanup.json` confirms all
four cleanup/integrity conditions. The app-owned Debug MCP/worker were left alone.

| Run | Result | Proven behavior |
| --- | --- | --- |
| `ott36-fixed-gold-20261004-d` | PASS | Stale-context rejection; successful prefix, blocked HMI and stopped suffix; failed-token replay; fresh previews/recovery; successful-token replay; exact final PLC/HMI readback and unchanged Main |
| `ott36-fixed-continuation-20261004-e` | PASS_CONTINUATION_CONTROL | Explicit independent suffix after blocked HMI, retained structured details, overall incomplete result, replay rejection and exact preservation |
| `ott36-fixed-health-20261004-f` | PASS_HEALTH_ONLY | Fresh worker/TIA restore; original two tags, HMI item/geometry/text, native exports and clean baseline compilation |

All compilation checks had **0 errors / 0 warnings** (baseline/final for mutation
controls, baseline for the health-only restore). These are
three qualification controls, not three model trials. Blueprints remain sequential
orchestration, not atomic cross-command transactions: the prefix stays applied
after the middle safety block. Default failure returned two steps (true/false),
with the HMI step's `TransportSuccess=true`, `Success=false`, `PolicyDenied` and
original structured `SafetyBlocked=true` / `Applied=false` retained. Explicit
continuation returned three steps (true/false/true) and overall `Applied=false`.

Evidence is under `E:/Documents/OpenTiaBench/Artifacts/<run-name>/`; disposable
projects are under `C:/OTT/Live/<run-name>/`. Native phase CSV/screen exports,
`Main-before.xml`, `Main-after.xml`, command journals and qualification/cleanup
records remain outside Git. Apply tokens in qualification journals are replaced
by stable SHA-256 markers.

Immutable archive size 5,582,162 bytes, SHA-256:
`1C17F223F00F0A43EFD5E51A7C8E2DBE4DD424540AE0C9A799F102997205AB74`.
Baseline and preserved HMI hash:
`oVX3KE1qt9mS5oY0SjNxwNXVyybxzp9MvhKWNrKZSyM=`;
recovered label hash:
`dh7NEK8exhqE5ojv02Cq6CFx3wRyH+Bh3IBdOnimVI8=`.
Main canonical SHA-256, removing only document-level export metadata:
`66337a2def7a9405f0f35458b7a13d05985fa7097b696ec6f394d4823bcf31bb`.

## Frozen case and deterministic oracle

OTT-36 reuses the existing fixture. Its public `acceptance.json` declares task
identities, comments, label and preservation expectations, not executable solution
steps. Raw-byte SHA-256:
`868f51f18fba253c4c89858dc000aabc850c34e12bf9c84803f5cc8df035381e`.
Git pins LF line endings so the staged hash remains portable.

The oracle checks one complete ordered lifecycle, authentic tool envelopes,
canonical validated-blueprint hashes using the worker's vendor-free serializer,
exact plan/token/step identities, structured partial/recovery evidence, fresh
successful recovery previews, consumed-token rejections, native manifest-bound
CSV exports and focused screen snapshots, immutable Main and clean compilation.
It does not score model text as engineering proof or combine complementary failed
calls. Equivalent supported scalar patch forms are accepted. Native evidence may
not be authored/edited by the model; only candidate blueprint files are writable.
No skill instruction or benchmark-specific skill example was added.

Reopening a stale fixture required a narrow harness correction: MCP file policy
now includes only the harness-created sibling `restored` directory, in addition
to the exact workspace and toolkit temp root. Parent directories and arbitrary
user/repository roots remain excluded; path resolution rejects junctions. Trial
metadata now records the selected restore root and TIA PID for independent binding.

Validation includes object/text positives, equivalent supported HMI syntax,
**50 adversarial controls**, full scorer positive/negative checks, and replay of
the independently captured live-gold envelopes with explicit path adaptation.
That replay is qualification evidence, never a model result. Release build, 765
unit tests (32 new regressions included), 54 architecture tests, 5 vendor-free IPC
tests, MCP modern/legacy exposure and generated/packaged drift checks passed at
`519e862`; no worker rebuild was needed for the harness/case slice.
The full benchmark `npm test` regression suite also passed with the newly
registered **29 cases / 72 campaigns / 15 fixtures**. Packaging drift remains
zero; generated command reference remains current at 186 commands. The new
oracle and runner scripts parse under PowerShell 7 and execute under Windows
PowerShell 5.1. Repository hygiene and `git diff --check` passed.

## Campaign policy

Freeze the case/scorer revision before inference. Run three
`zai-coding-plan/glm-5.3-flash` trials with declared effort **high** and OpenCode
variant **high**, checkpointing one trial per batch. Use fresh fixture restores,
explicit Release worker paths and provider-scoped existing subscription auth.
Provider/capture failures are invalid, not model failures. GLM-5.3 High is the
prepared capability fallback if Flash has valid failures; no Sol campaign is
authorized here. Subscription-reported zero cost is not amortized subscription
price. At the original `7e35249` checkpoint no model calls had been consumed.

## First capture: scorer calibration, not a model failure

The first Flash High capture at `7e35249` completed in 598.893 seconds, without a
provider failure or rate-limit signal. The original scorer incorrectly returned
15/100. Its native trace contains a complete successful lifecycle: exact stale
rejection, stopped partial failure, fresh recovery, replay rejections, native
readback, unchanged Main and clean compilation. The same immutable trace scores
**100/100** after correcting these generic evidence adapters:

- OpenCode stores rejected MCP command envelopes in `state.error`, not
  `state.output`. Only complete structured boolean-failure envelopes qualify;
  prose is never engineering evidence.
- `list-projects` and `get-project-info` are both documented ways to establish
  project identity. Both remain bound to the selected TIA PID and restore root.
- Supported HMI operation wrappers/aliases, safe command defaults and reordered
  filter sets represent equivalent preview intent. Optional preview artifact
  roots must remain inside the exact workspace artifacts; unsafe flags, extra
  wrapper keys, changed labels/filters and disabled backup still fail.

A separate Release MCP/worker attachment independently checked the pilot's
still-open final project: exact two-tag inventory and comments, Text=Run with
unchanged geometry, the immutable Main hash, and compile 0 errors / 0 warnings.
Evidence: `E:/Documents/OpenTiaBench/Artifacts/ott36-flash-pilot-independent-20261004/`.

Raw `result.json` and both intermediate replay files are retained unchanged under
`E:/Documents/OpenTiaBench/Artifacts/ott36-flash-high-20261004/`. This capture is
reported separately as scorer calibration. Do not use its original 15/100 as a
model failure or silently migrate its commit-pinned batch checkpoint. Freeze the
corrected scorer and run a fresh three-trial checkpointed qualification campaign.
The public task, immutable acceptance contract, runtime and skills are unchanged.
