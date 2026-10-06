# FAIL - held-out hardware topology provisioning qualification

The unchanged skills did not qualify on `ott-22`. DeepSeek V4 Flash Free
passed 0/3 trials with a mean score of 26.67. One trial eventually produced
the correct TIA hardware state, but it did not create and configure that state
through the required atomic, reviewable transaction. The benchmark is not
saturated and should not be softened.

## Frozen configuration

- Case/fixture/scorer commit: `619479a3b519e6f8926a466a65fdbc1bed3aef75`
- Branch: `experiment/opencode-benchmark-slice`
- Git state SHA-256:
  `B9D712A26B4F49A6CCFE3DAEE6AD8242A25B586A405BDB37BC99111315E56DFC`
- OpenCode: `1.18.10`
- Model: `opencode/deepseek-v4-flash-free`
- Reasoning effort / variant: `max` / `max`
- Cost class: free
- Fixture: `tia-v21-topology-6`
- Fixture SHA-256:
  `33193A83BE565BBD63486D66FFDE39404C76D9F8F01178DF28D435DBE3CC5732`
- TIA Portal: V21 Update 2

The hardware, basic-TIA, and router skill hashes remained equal to the hashes
recorded in the gold audit. No skill changed between task design and this
control campaign.

## Result

| Trial | Score | Pass | Duration | Reasoning tokens | Cost | Primary finding |
| ---: | ---: | --- | ---: | ---: | ---: | --- |
| 1 | 70 | no | 678.066 s | 35,593 | $0.000000 | Correct final state, non-atomic and highly exploratory workflow |
| 2 | 10 | no | 900.253 s | 1,747 | $0.000000 | Catalog resolved; timed out before any hardware patch |
| 3 | 0 | no | 900.235 s | 0 | $0.000000 | Runtime/provider timeout with an empty event stream |
| **Mean / total** | **26.67** | **0/3** | **826.185 s** | **12,446.67** | **$0.000000** | **3 critical failures; 2 runtime failures** |

The model status is `runtime-failure`: every trial had a critical failure and
two trials timed out. A model qualifies only when all three trials pass.

## Trial 1 - engineering success, workflow failure

The first trace discovered the installed catalog identifiers, existing
`CellPN` subnet and `CellIO` IO system, both displaced module placements, and
the final station topology. It applied a valid final configuration, reread the
tree and networks, and compiled with zero errors and zero warnings.

That result is not a safe qualification pass:

- it called the hardware patch tool 50 times: 49 dry-runs/probes and one apply;
- it created `RemoteCell` separately with `add-device`, outside the guarded
  topology transaction;
- it guessed many unsupported operation names and property shapes instead of
  using the documented topology operations;
- its final patch used two low-level network-item attribute operations rather
  than the single `configure-profinet-device` operation; and
- therefore neither its preview nor its apply contained the exact atomic
  operation set that had been independently proven by the gold audit.

The model did preview and apply its own six-operation remainder, but the
station already existed by then. A successful final state cannot prove that a
reviewed transaction was the state-changing transaction, so the exact
operation-set, corrected-preview, and preview-before-apply assertions correctly
failed.

One exploratory preview also attempted to set `SubnetMask` through the generic
network-item attribute surface. TIA rejected that setter on the new ET 200SP
interface; the task did not require a subnet mask, and omitting it allowed the
next preview to succeed.

## Trial 2 - planning stall

The second trace connected to the correct project, read the initial hardware
tree and networks, and resolved the exact IM, DI, and DQ catalog entries. It
then searched the isolated workspace for additional hidden skill reference
files and timed out. It made no `apply-hardware-patch` call, performed no
placement probe, changed no state, and did not compile.

This is evidence that the current guidance does not reliably move the model
from catalog discovery to a bounded topology plan, even when all identifiers
have already been found.

## Trial 3 - runtime failure

The scored third trial timed out after 900.235 seconds with a zero-byte event
file and zero reasoning tokens. It provides no engineering or skill evidence;
it is provider/runtime variance. It still counts against strict model
qualification.

The outer campaign host reached its own 30-minute limit while the original
third trial was running. The two completed results were preserved, the exact
orphaned OpenCode process tree was stopped, and only trial index 3 was rerun
against the same frozen commit, fixture, model, and `max` reasoning variant.
That rerun produced the scored zero-event timeout. The recovered campaign
contains exactly three trial results and reran no completed trial.

## Toolkit findings

The gold audit proves the toolkit can perform this task atomically, restore it
exactly, and roll back a real mid-transaction failure. The failure is not a
missing topology capability.

Two API rough edges are worth treating separately from skill evaluation:

- `find-hardware-catalog-items` rejected an otherwise valid request when a
  culture argument was supplied; the same focused query worked without it.
- the generic network-item attribute route exposed `SubnetMask`, but the TIA
  setter rejected it for this interface. The dedicated
  `configure-profinet-device` route is the clearer contract for the requested
  identity fields.

Neither issue requires weakening `ott-22`, and neither prevented the gold
solution.

## Decision and next experiment

Keep the case, fixture, scorer, and prompt unchanged. The next useful step is
a surgical, generic hardware-skill candidate followed by an A/B rerun of this
same frozen campaign. The candidate should teach a bounded topology workflow:

1. use focused catalog and live topology discovery;
2. form one complete result-reference-based transaction using documented
   operations;
3. use strict placement failure only to learn each displaced path;
4. issue one full corrected preview and then one identical apply; and
5. verify from fresh tree/network reads and compile.

The guidance must not mention `RemoteCell`, `CellPN`, `CellIO`, the article
numbers, addresses, or any other `ott-22` constant. A clean 3/3 candidate would
then demonstrate reusable topology guidance rather than benchmark memorization.

## Cleanup

The final project was closed with `save=false`, and the exact benchmark-owned
TIA process tree was stopped after the Portal close request left it resident.
Ten exact construction, test, and campaign restore directories were moved to
the Windows Recycle Bin. The immutable read-only fixture, gold evidence, and
ignored raw campaign artifacts remain intact.
