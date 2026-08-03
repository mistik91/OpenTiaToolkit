# INVALID - third-party Promag GSDML paid V4 pilot

Date: 2026-08-03

## Outcome

This campaign is retained as diagnostic evidence but withdrawn from model
qualification. The frozen case required `can-plug-hardware-item` calls against
`FT_101/Totalizer_1` before the atomic patch created `FT_101`. The public
command can resolve only objects already present in the project, making that
critical assertion impossible. Paid DeepSeek V4 Flash produced:

| Trial | Canonical score | Qualified | Duration | Reasoning tokens | Cost |
| ---: | ---: | :---: | ---: | ---: | ---: |
| 1 | 45 | no | 362.127 s | 10,616 | $0.120476496 |
| 2 | 10 | no (timeout) | 900.245 s | 36,274 | $0.254750748 |
| 3 | 70 | no | 712.394 s | 13,811 | $0.189174412 |
| **Aggregate** | **41.67 mean** | **0/3** | **658.255 s mean** | **20,233.67 mean** | **$0.564401660** |

The original `validForModelComparison` fields establish endpoint/runtime
health only; they do not override the subsequently discovered case-contract
defect. Do not compare these scores with corrected `ott-26` campaigns or use
them to qualify V4. The recorded $0.564401660 remains real OpenRouter spend.

## Frozen boundary

- Campaign commit: `fd8b0cd5ce31594609107993c800037c40f4ab8e`
- Branch: `experiment/opencode-benchmark-slice`, clean in every trial
- Case: `ott-26`, pass score 90 with all assertions critical
- Fixture: `tia-v21-topology-6`, freshly restored for every trial
- Fixture SHA-256:
  `33193A83BE565BBD63486D66FFDE39404C76D9F8F01178DF28D435DBE3CC5732`
- Model: `openrouter/deepseek/deepseek-v4-flash`
- Reasoning effort and exact OpenCode variant: `xhigh`
- OpenCode: `1.18.10`
- Authentication: isolated projection of the OpenRouter API credential
- Catalog input/output/cache-read prices per million tokens:
  $0.14 / $0.28 / $0.028
- Hardware skill SHA-256:
  `1526365EA8077DF55C11F4ADB28528C068754A970895049643C091E12D5D10D2`
- Case/prompt SHA-256:
  `36273C724E41F9181B512CFD5C1053C250F7D171889EEEBD3299749FBA474CB6` /
  `7F679EB37D6DC06A4590332633BE7B1B92C9C9737C8DAEDF4EB2F4088C67B6BD`

No skill, case, prompt, fixture, or toolkit implementation changed during the
frozen model campaign.

## Siemens gold proof

The disposable TIA V21 Update 2 audit proved the intended engineering path
before model execution:

1. the complete strict batch exposed only the actual displaced placement of
   `TotalizerControl` and rolled back;
2. the complete control-corrected batch exposed only the displaced placement
   of `TotalizerMode` and rolled back;
3. the complete corrected batch previewed and then applied successfully;
4. fresh reads proved the generated default modules, both added submodules,
   `CellPN`/`CellIO` connections, PROFINET identity, input/output starts, and
   GSD dataset 32752 value `80 AF`;
5. `PLC_Main` compiled with zero errors and zero warnings; and
6. the exact delete inverse previewed and applied, leaving no `FT_101` tree,
   network, or IO-address state.

The project was closed without saving and the read-only fixture archive kept
its original hash. The exact toolkit-started TIA process and its verified
child were stopped; the gold and three campaign restore workspaces were sent
to the Windows Recycle Bin. The captured benchmark artifacts remain
available. The complete evidence is in
`reports/ott-26-gold-audit-20260803.md`.

## Trace audit

### Trial 1 - 45

V4 reached the correct final engineering state, completed every independent
readback, and compiled cleanly. It nevertheless made nine hardware-patch
calls instead of the required three previews plus one apply. It began with a
partial create-only preview, used only one of the two required `can-plug`
probes, tried incorrect network and GSD targets, and made two apply attempts.
Its final GSD write supplied `expectedData` rather than the guarded
`expectedDataHex` field, so the exact corrected transaction never matched.

### Trial 2 - 10

V4 performed baseline discovery, the focused catalog search, and both
`can-plug` probes, but never submitted a hardware patch. It exhausted the
900-second trial limit before mutation, readback, or compile. The endpoint
remained healthy; this is model execution failure evidence.

### Trial 3 - 70

V4 also reached the correct final state, passed all independent readbacks, and
compiled cleanly. Its final corrected preview and apply contained the exact
ten-operation transaction. Before that pair it used partial two-operation
batches, applied and deleted a temporary device, made several malformed or
mis-targeted GSD attempts, and accumulated eight placement/preview matches and
four apply-shaped calls. It therefore failed the bounded recovery and
preview/apply sequence even after the scorer correction below.

## Scorer correction found by the campaign

The execution-time scorer recorded trial 3 as 50 because it compared GSD hex
text literally: the case used `80 AF` / `80 DF`, while the model used `80AF` /
`80DF`. OpenTiaToolkit intentionally accepts whitespace, `0x`, hyphens,
commas, and either case for the same byte sequence.

The trace evaluator now canonicalizes `dataHex` and `expectedDataHex` with
those toolkit semantics. A positive regression covers `0x80af` and `80-df`;
the existing unguarded-write negative test still fails. Re-evaluating the
unchanged captured traces changes only trial 3 from 50 to 70, raising the mean
from the execution-time 35.00 to the canonical 41.67. Each trial directory
retains the original `result.json` and adds
`result-rescored-hex-normalized.json` for auditability.

This correction does not make the benchmark easier: it removes a textual
false negative while preserving every transaction-history failure.

## Corrected decision and boundary

Withdraw this campaign and rerun models only against the corrected case. The
generic hardware skill now distinguishes an existing plug parent, where
`can-plug-hardware-item` is valid, from a parent generated inside the same
patch, where the complete transactional dry-run is the only valid capability
preflight. This is a reusable command-lifecycle rule, not Promag-specific
tuning.

This case proves work with a third-party GSDML package already installed in
TIA. It deliberately does not claim coverage of importing a vendor package,
choosing among installed GSDML revisions, migrating a project to the newer
2020 Promag package, or communicating with a physical flowmeter. Those are the
next portability boundary, not reasons to weaken this case.

## Artifacts

- Formal campaign:
  `benchmarks/artifacts/20260803T113604713Z-campaign-hardware-gsdml-promag-deepseek-v4-flash-paid-xhigh-20260803`
- Catalog-only preflight:
  `benchmarks/artifacts/20260803T113528506Z-campaign-hardware-gsdml-promag-deepseek-v4-flash-paid-xhigh-20260803`
