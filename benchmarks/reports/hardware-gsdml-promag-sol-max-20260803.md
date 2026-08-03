# FAIL - GPT-5.6 Sol maximum-effort Promag GSDML ceiling

Date: 2026-08-03

## Decision

The corrected `ott-26` case is valid, live-solvable, and not saturated. GPT-5.6
Sol at `max` demonstrated full capability once, but did not qualify reliably:
the final skill candidate scored 80, 100, and 80, so only 1/3 trials passed.
Qualification requires all three.

Keep the two generic hardware-skill corrections discovered during this work:

1. do not call `can-plug-hardware-item` for a parent that will be created or
   generated inside the same atomic patch; use the complete dry-run as that
   capability preflight; and
2. use the exact GSD write fields `dataHex` and `expectedDataHex`, not guessed
   aliases.

Do not add Promag-specific paths or retry examples to the generic skill. The
two remaining failures targeted the GSD record at the device root once before
correcting to the generated `Promag 100 PNIO` item. The skill already tells the
model to obtain generated-child paths from live dry-run output, and one trial
used that evidence correctly and scored 100.

## Invalid pilot and case repair

The first Sol run at commit `aae7914` recorded 80, 80, and 10, but is withdrawn
from model comparison. The case required `can-plug-hardware-item` against
`FT_101/Totalizer_1` before the patch created `FT_101`. The public command can
resolve only objects already present in the open project; all six mandated
calls returned `Device not found: FT_101`. Trial 3 stopped after those failures,
proving that the defect materially affected model behavior.

Commit `a7e807e` repaired the contract generically:

- the prompt forbids creating a temporary station merely to run the check;
- the scorer rejects a `can-plug` call against the future parent;
- the representative mechanical trace still scores 100;
- a new negative trace proves that an impossible capability probe fails; and
- the gold audit now identifies the complete transactional dry-runs as the
  capability and placement proof.

The earlier paid V4 pilot used the same impossible requirement and is also
withdrawn. Its recorded spend remains real, but its scores are not a valid
baseline for the corrected case.

## Frozen valid campaigns

Both valid campaigns used:

- model: `openai/gpt-5.6-sol`;
- reasoning effort / OpenCode variant: `max` / `max`;
- OpenCode: `1.18.10`;
- authentication: provider-scoped OpenAI OAuth subscription credential;
- catalog status: active, tool-call capable, release date 2026-07-09;
- reported input/output cost: zero subscription accounting;
- case and prompt SHA-256:
  `8D2A7D16D7B7D92F92A11B890BD5AA7760B4C49D668A4D8122FCCA6E527252F7` /
  `935CAD2FDA0D87AE3DD66D222306B5A6783EB642D522A7F29E9FF95B15855F57`;
- fixture: `tia-v21-topology-6`, freshly restored for every trial; and
- fixture SHA-256:
  `33193A83BE565BBD63486D66FFDE39404C76D9F8F01178DF28D435DBE3CC5732`.

The zero reported cost means use of an external subscription allowance, not
that Sol is economically free.

| Campaign | Commit | Scores | Passed | Mean score | Mean duration | Mean reasoning tokens | Validator issues |
| --- | --- | --- | ---: | ---: | ---: | ---: | ---: |
| Corrected-case control | `a7e807e` | 80 / 75 / 80 | 0/3 | 78.33 | 499.464 s | 8,439.67 | 3 |
| Exact GSD-field candidate | `f6436f4` | 80 / 100 / 80 | 1/3 | 86.67 | 431.988 s | 7,370.33 | 0 |

All six trials completed normally, were valid for comparison, had no runtime
or endpoint failure, and reported $0 request cost. The candidate raised mean
score by 8.34 points, produced the first qualifying trace, reduced mean
duration by 67.476 seconds, and reduced mean reasoning use by 1,069.34 tokens.
With only three stochastic trials, those aggregate differences are descriptive;
the direct validator change from one missing-field rejection in every control
trial to zero in every candidate trial is the stronger mechanism evidence.

## Corrected-case control trace audit

Every control trial applied the exact ten-operation transaction, independently
reread tree, network, IO, and GSD state, and compiled with zero errors and zero
warnings. None called `can-plug-hardware-item`.

- Trial 1 (80) made four dry-runs and one apply. After the two required
  displacement failures, its first corrected batch omitted `expectedDataHex`;
  the structural validator rejected it before TIA and the next patch corrected
  it.
- Trial 2 (75) made two catalog searches, five dry-runs, and one apply. Its
  first batch omitted `expectedDataHex`; a later corrected-placement batch
  targeted GSD data at the device root before selecting `Promag 100 PNIO`.
- Trial 3 (80) followed the same five-dry-run pattern as trial 2 but used one
  catalog search.

The control therefore isolated two independent errors: the exact guard-field
omission and generated GSD feature target selection.

## Exact-field skill candidate trace audit

Only the generic `dataHex` / `expectedDataHex` clarification changed. The case,
prompt, scorer, model settings, fixture, OpenCode runtime, and timeout were
identical to the control. Hardware skill SHA-256 for the candidate is
`B392950EAE5F305E89058A05DE24D4B7EE628207BDC2900561B48ED46C00D5FC`.

- Trial 1 (80) made four dry-runs and one apply. Both displacement probes were
  exact, but the first fully displaced batch targeted GSD data at the device
  root; the next complete preview corrected it.
- Trial 2 (100) made exactly three complete dry-runs and one unchanged apply:
  strict control displacement, control-corrected mode displacement, successful
  corrected preview, then apply. All independent readbacks and compile passed.
- Trial 3 (80) repeated trial 1's one extra device-root GSD attempt, then
  corrected, applied, reread, and compiled successfully.

All three candidate trials used exactly one catalog search, made no impossible
capability probe, included `expectedDataHex` from their first patch, and
triggered no structural-validator intervention.

## Cleanup and artifacts

The final disposable project was closed with `save=false`. The exact
toolkit-started TIA PID 14224 and its verified child processes were stopped.
All nine Sol restore workspaces were sent to the Windows Recycle Bin; captured
benchmark artifacts remain available. The read-only fixture archive retained
its original hash.

- Invalid pilot:
  `benchmarks/artifacts/20260803T123521693Z-campaign-hardware-gsdml-promag-gpt-5-6-sol-max-20260803`
- Corrected-case control:
  `benchmarks/artifacts/20260803T130807388Z-campaign-hardware-gsdml-promag-gpt-5-6-sol-max-20260803`
- Exact-field skill candidate:
  `benchmarks/artifacts/20260803T133525624Z-campaign-hardware-gsdml-promag-gpt-5-6-sol-max-20260803`
- Catalog/auth preflight:
  `benchmarks/artifacts/20260803T123428512Z-campaign-hardware-gsdml-promag-gpt-5-6-sol-max-20260803`
