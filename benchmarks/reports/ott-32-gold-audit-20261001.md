# PASS — ott-32 approved project-library reuse gold audit

- Date: 2026-10-01
- Branch: `codex/library-workflow-benchmark`, preparation commit `de29881`
- TIA Portal: V21 Update 2; disposable `OTT_Benchmark` projects only
- PLC: `PLC_Main`, S7-1500

## Immutable fixture

| Field | Value |
| --- | --- |
| Fixture ID | `tia-v21-library-11` |
| Archive | `C:\OpenTiaBench\Archives\OTT_Benchmark_V21_Library_20261001.zap21` |
| Bytes | 5,701,716 |
| SHA-256 | `B749366ECD7CFD0564FEC06881526373BF0DBE14F9BE9ECCD1043D0071166749` |
| Read-only | yes |
| Parent fixture | `tia-v21-vci-10` |

The project library contains three master copies named `Offset`:

- `OTT32_Standards/Approved/PLC/Offset`: FC `OTT32_ApprovedOffset`, +10;
- `OTT32_Standards/Legacy/PLC/Offset`: legacy FC payload, +20;
- `OTT32_Standards/Approved/HMI/Offset`: HMI control, not PLC-compatible.

The approved FC is absent from PLC software and `OTT32_Reuse` is empty.
`Main`, root FC2 `OTT32_ApprovedOffset_Legacy` (+20), and root FC3
`OTT32_ExistingOffset` (+30) are preservation targets.

Project-library `UDT/MyANALOG_LEVEL` has Committed versions 0.0.3 and 0.0.4;
only 0.0.4 is default. Its existing PLC instance remains on 0.0.3. The project
update report explicitly identifies the instance path, current 0.0.3, newer
0.0.4, and `Out-of-date`. The benchmark authorizes inspection, not an upgrade.

## Construction and source protection

The fixture-only helper was compiled against the installed Siemens V21 API.
The elevated session registered its exact path, UTC timestamp, and SHA-256
in both Siemens allow-list keys; executable identity remained
`CAEB2AD0D7A3FAFBB1FED4E918F036E4B2D2E85066703928420052A2BC54FF63`.
It attached only to the hard-guarded disposable construction project and opened
`TestLibrary` read-only. It created library masters, materialized the older
UDT instance, and refreshed library definitions without updating the instance.
The helper is not exposed to benchmarked models.

The first unsaved helper session exited before independent inspection after
the last Openness attachment was disposed. No archive or successful audit was
claimed for that session. Construction was repeated from the unchanged saved
seed while retaining the idle primary attachment; no concurrent TIA calls
were made. Independent reads proved the resulting state before saving.

Construction compiled with 0 errors and 1 intentional warning: the PLC instance
uses a library version without the Default designation. The archive was saved,
created once, marked read-only, and restored into an independent project on a
fresh TIA process. Source library data hashes stayed unchanged:

- `PEData.plf`: `1625365751D73016FD7A61D53360E9ED1A031CF31DB9A6139991D783A16BD0A3`;
- `PEData.idx`: `73D53AC070115F990F11A71C72077CD7032610533B06FBB5B064337ECF1297A3`.

The parent archive also retains
`B058929F708DB5A5781428238D04B627C4611A0F15C40CE46D4CCC742E76C773`.
The mutable construction source and binary/archive artifacts remain outside Git.

## Independent gold path

Restore: `C:\OpenTiaBench\Live\20261001-library-gold-03`.

1. Full master/type/version discovery confirmed the embedded project-library
   assets and the outdated instance without opening any global library.
2. Exact dry-run into `OTT32_Reuse` returned success, `DryRun=true`,
   `Applied=false`; the software still contained only its three baseline blocks.
3. An unconfirmed non-dry-run call was PolicyDenied. A fresh inventory still
   contained exactly those same three blocks.
4. One confirmed copy returned created identity `OTT32_ApprovedOffset`/FC.
5. Fresh hierarchical readback proved the exact path
   `PLC_Main/Blocks/OTT32_Reuse/OTT32_ApprovedOffset`.
6. Recursive XML export independently proved FC1, SCL, DInt Value/Result,
   and the +10 implementation. The root FC native-source exports retained
   byte-identical baseline hashes: legacy
   `28B393328F85B90F125F58547C92FD18D05CB33B632088911EA3A80340DA9F6F`
   and existing
   `8259F37EAC9030A38A2E9A272916D4ED5269501A8BE51D5DDEA623EBE4590E84`.
7. The final update check still reported current 0.0.3 versus default 0.0.4.
   The exported `SW.Types.PlcStruct` subtree was identical to the baseline;
   the whole export differed only in `DocumentInfo/Created`.
8. Incremental compile returned 0 errors and 0 warnings. It compiled the new
   FC and did not repeat the already-known non-default-version warning.
   Consequently the rubric permits 0 or 1 warnings and requires independent
   update-check preservation evidence; zero warnings do not imply an upgrade.
9. The disposable gold project was closed without saving.

## Proven generic skill corrections

The earlier preflight already proved read-only global opening requires explicit
MCP confirmation, and a master-copy dry-run does not establish domain or
collision safety. This gold audit additionally proved a grouped-block read
limit: flat `get-blocks`/`get-block-info` and the native-source export precheck
inspect root blocks only. Neither a bare leaf nor a guessed group-prefixed name
resolved the copied FC there, although the software tree and recursive
`export-block`/`export-plc` XML export did.

The library skill now uses hierarchy plus recursive XML for grouped readback
instead of encouraging a repeated copy. Its guidance contains no fixture
names, paths, constants, UDT versions, or benchmark solution. No runtime
implementation was changed. The root-only PLC browse limitation is still an
explicit future toolkit issue, not a model failure or a claimed fix.

## Pre-inference gates

- All 12 actual gold artifacts satisfy the case's mechanical file assertions.
- Two representative synthetic traces pass at 100, including both supported
  compile-warning outcomes; 16 negative controls are rejected, covering wrong
  domain/path/destination, preview ordering, duplicate copy, collision baseline,
  wrong payload, changed preservation targets, type upgrade/default mistakes,
  unauthorized mutations/save, compile errors, and nonbaseline warning counts.
- Full `npm test` passes: 25 cases, 56 campaigns, 12 fixtures, and all synthetic,
  evaluator, batch, merge, and recovery checks.
- Canonical skill validation, full packaged-skill drift check, repository
  hygiene, and `git diff --check` pass. The explicit active Codex root has no
  installed `library-workflow` copy; none was implicitly installed.

The campaign is frozen before inference: GLM-5.3 Flash High first, three fresh
archive restores, with GLM-5.3 High reserved for genuine capability failure.

The later pilot exposed an additional compile-before-XML prerequisite,
confirmed on another fresh restore. The generic correction and separate
completed cohort are documented in
[the verification report](library-workflow-glm-5-3-flash-high-20261001.md).
