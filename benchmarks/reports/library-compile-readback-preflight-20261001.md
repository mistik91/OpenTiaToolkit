# PASS — generic library compile/readback correction preflight

The original Flash High pilot at `2064de2395cf54d802ff49aaab823d1f49deff9c`
completed one fresh trial, scored 91, and failed the critical required XML
readback/export assertions. Duration was 551.249 seconds, with zero reported
incremental subscription cost and no provider, rate-limit, or harness failure.
The remaining two original trials were not run. This is an incomplete pilot,
not a model qualification campaign.

The model selected the correct master, copied once with preview/confirmation,
preserved the existing objects and old UDT instance, and compiled with 0/0.
It recovered the copied implementation through recursive SD document export,
but failed the explicitly requested XML artifact. Its final answer incorrectly
attributed XML rejection to grouped-block lookup. This is distinct from a
raw code-generation or task-planning capability failure.

Raw pilot evidence is retained under:

`benchmarks/artifacts/20261001T053844009Z-batch-library-reuse-glm-5-3-flash-high-20261001`

The model attempted XML export before compile; it did not retry XML after
compile. An independent post-trial export of the same grouped FC succeeded
immediately. That diagnostic wrote outside the model workspace and did not
repair or rescore the model's missing artifact.

## Controlled fresh-state reproduction

Restore: `E:\Documents\OpenTiaBench\Live\20261001-library-compile-order-04`.

Using the same immutable fixture and exact approved copy:

1. Empty destination and absent payload were independently verified.
2. One exact dry-run and confirmed copy succeeded.
3. Recursive `export-block` using the known created leaf before compilation
   returned `SiemensRejected`; no XML file was created.
4. `compile-software` succeeded with 0 errors and 0 warnings.
5. Recursive XML export of the same leaf succeeded after compilation, with
   the exact FC identity/interface and +10 implementation.
6. A fresh update check still reported the retained 0.0.3 instance versus
   default 0.0.4. The disposable project was closed without saving.

The library archive and parent archive retain their pinned hashes. No toolkit
runtime change, fixture replacement, prompt change, or rubric relaxation was
made. The generic skill correction orders hierarchy verification, successful
compile, and recursive XML payload readback; it distinguishes this prerequisite
from the separately proven root-only flat browse/source-export precheck.
There are no case names, constants, paths, or UDT versions in that guidance.

The post-correction cohort is separately frozen in
`campaigns/library-compile-readback-glm-5-3-flash-high-20261001.json` and will
use three fresh restores at the same Flash High settings. Do not combine it
with the incomplete original pilot or treat the root-only browse issue as fixed.

Before inference, the controlled post-compile XML satisfied the unchanged
payload rubric. Canonical skill validation, packaged-skill drift check,
repository hygiene, `git diff --check`, and the full `npm test` suite passed
(25 cases, 57 campaigns, 12 fixtures, including both positive and all 16
negative library controls).

The separate three-trial cohort subsequently completed. Its results and the
post-cohort noncritical Markdown-summary adjudication are documented in
[the verification report](library-workflow-glm-5-3-flash-high-20261001.md).
