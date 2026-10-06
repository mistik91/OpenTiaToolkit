# Hardware pre-call invariant rewrite: rejected after paid V4 A/B

## Decision

Do not retain the proposed hardware-skill rewrite. Paid DeepSeek V4 Flash at
`xhigh` improved its mean `ott-24` score from 56.67 to 61.67, but both control
and candidate remained 0/3 and the candidate still violated the exact retry
invariants the rewrite was intended to reinforce. The original generic skill
text was restored in commit `bd10bba`.

The candidate was deliberately generic. It consolidated parent binding,
request-only optional fields, single-error retry deltas, and preview/apply
identity into a mandatory pre-call audit without adding any `ott-24` article
number, object name, path, address, network identity, or expected answer. Its
rejection is therefore based on measured ineffectiveness, not overfitting.

## Controlled paid comparison

- Case: `ott-24`, pass score 90, all assertions critical
- Fixture: `tia-v21-topology-6`
- Fixture SHA-256:
  `33193A83BE565BBD63486D66FFDE39404C76D9F8F01178DF28D435DBE3CC5732`
- Model: `openrouter/deepseek/deepseek-v4-flash`
- Provider/auth: OpenRouter API key
- Reasoning effort and variant: `xhigh`
- OpenCode: `1.18.10`
- Control commit: `57328f565be88173202e197b9cb91b34b51d5372`
- Candidate trace commit: `fe4c51c4854cbbf8f0e8699e50d5e6f962f69a3e`
- Candidate skill commit: `58b5bb5`
- Control/restored skill SHA-256:
  `1526365EA8077DF55C11F4ADB28528C068754A970895049643C091E12D5D10D2`
- Candidate skill SHA-256:
  `53E4D05E2D86860D1D6B30E64002BE5216B2C5156E1C8072FBADE2874D838A9D`

The case, prompt, scorer, fixture archive, model, effort, OpenCode version,
toolkit binaries, TIA process, and trial count were held constant. The control
ran from a separate clean worktree at the pre-change commit; the candidate ran
from the clean candidate commit. Each trial restored the same read-only fixture
archive.

Catalog preflight reported an active model, variants `high` and `xhigh`, a
1,048,576-token context, and a 393,216-token output limit. Catalog prices were
$0.14 per million input tokens, $0.28 per million output tokens, and $0.028 per
million cache-read tokens.

## Results and cost

| Arm | Scores | Qualified | Mean score | Mean duration | Mean reasoning tokens | Total cost |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| Original skill | 55 / 60 / 55 | 0/3 | 56.67 | 326.995 s | 10,143.67 | $0.29080640 |
| Pre-call audit candidate | 75 / 55 / 55 | 0/3 | 61.67 | 364.236 s | 10,771 | $0.30876524 |

Control trial costs were $0.09489217, $0.08815954, and $0.10775470.
Candidate trial costs were $0.07744276, $0.11031213, and $0.12101034. The
complete paid A/B cost $0.59957164. Because neither arm passed a trial,
cost-per-passed-task is undefined.

The candidate changed only one scored outcome consistently enough to be
visible in the aggregate: `exact-corrected-operation-set` passed in trial 1.
It still failed in trials 2 and 3. Both arms failed
`strict-placement-probes` and `corrected-preview-succeeded` in all three
trials. The candidate failed
`preview-before-apply-without-guessed-guards` in all three trials, compared
with two of three control trials.

## Trace audit

The candidate did stop adding the unrequested `subnetMask` seen in the paid
control. That narrow improvement did not transfer to the other pre-call rules:

- Candidate trial 1 started its complete eight-operation patch with both plugs
  bound to the device root, then enabled displaced placement for both sibling
  plugs after observing only the first displacement. A later retry used a bad
  generated-child address path before reaching a successful preview and apply.
- Candidate trial 2 first previewed device creation alone, then used
  six-operation partial batches before eventually constructing the complete
  eight-operation patch. Its retries moved plug parents between an invented
  nested path, the rail, and the device root, and enabled displacement for both
  siblings prematurely.
- Candidate trial 3 began with a three-operation placement batch. Its later
  complete batches bound both address writes to the station result instead of
  their plug results and duplicated the generated-child names in their paths;
  the IO-connector operation also omitted its explicit connector index. The
  required corrected operation set therefore matched zero times.

The control was also unsatisfactory. It produced three, three, and four
operation sets matching the corrected topology shape where exactly one clean
preview and one identical apply were required. Two trials added the unsupported
unrequested subnet mask, and the other moved from a three-operation placement
probe to the full batch. Both arms ultimately applied a patch and compiled
successfully, but correct final state does not satisfy the required transaction
and retry discipline.

Raw paid traces are retained under ignored artifacts at:

- `artifacts/paid-v4-control-57328f5-20260803/20260803T032626324Z-campaign-hardware-family-transfer-deepseek-v4-flash-paid-xhigh-skill-invariants-20260803`
- `artifacts/20260803T034318457Z-campaign-hardware-family-transfer-deepseek-v4-flash-paid-xhigh-skill-invariants-20260803`

## Free-endpoint attempt

The maximum-effort `opencode/deepseek-v4-flash-free` candidate campaign is not
a skill result. All three trials ended with `503 The request queue is full`
before any hardware patch, scoring 10, 10, and 0. A separate retry canary hit
the same 503. The free endpoint consumed $0 and its traces are retained under
`artifacts/20260803T030834897Z-campaign-hardware-family-transfer-deepseek-v4-flash-free-max-skill-invariants-20260803`.

## Conclusion

This experiment brought a useful negative result: stronger formatting and a
mandatory checklist do not reliably make V4 preserve a complex transactional
batch across retries. The remaining failure is better treated as model/tool
execution capability. Further skill tuning should wait for a genuinely new,
transferable ambiguity; enforcing the invariant mechanically belongs in the
tool schema, patch builder, or validation layer rather than another restatement
of the same prose.
