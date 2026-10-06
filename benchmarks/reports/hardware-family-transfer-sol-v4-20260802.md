# ET 200MP family-transfer comparison: Sol qualifies, V4 Free does not

## Decision

GPT-5.6 Sol at maximum reasoning qualified on the held-out `ott-24` ET 200MP
family transfer with three scores of 100. DeepSeek V4 Flash Free at its maximum
advertised variant reached and verified the correct final project in all three
trials but scored 60, 60, and 75 because it added forbidden hardware retries.

No skill change is retained. Every V4 failure violated guidance that was
already explicit before `ott-24` was designed: keep the intended rail parent on
every plug retry and omit optional PROFINET fields the task did not request.
Changing the skill again would repeat existing wording rather than address a
new transferable ambiguity.

## Frozen comparison

- Case: `ott-24`, pass score 90, all assertions critical
- Fixture: `tia-v21-topology-6`
- Fixture SHA-256:
  `33193A83BE565BBD63486D66FFDE39404C76D9F8F01178DF28D435DBE3CC5732`
- Trace-generation commit: `96cd8ab`
- Branch: `experiment/opencode-benchmark-slice`
- Git state during all trials: clean
- Git-state SHA-256:
  `B9D712A26B4F49A6CCFE3DAEE6AD8242A25B586A405BDB37BC99111315E56DFC`
- OpenCode: `1.18.10`
- Hardware skill SHA-256:
  `1526365EA8077DF55C11F4ADB28528C068754A970895049643C091E12D5D10D2`

The case, scorer, prompt, fixture, skills, and OpenCode version were identical
for both campaigns. Each trial independently restored the same read-only
archive and used reasoning effort `max` with variant `max`.

Catalog preflight reported:

| Model | Provider/auth | Context | Output limit | Reported price |
| --- | --- | ---: | ---: | ---: |
| `openai/gpt-5.6-sol` | OpenAI OAuth subscription | 500,000 | 128,000 | $0 |
| `opencode/deepseek-v4-flash-free` | OpenCode free endpoint, no credential | 200,000 | 128,000 | $0 |

Zero Sol cost is subscription accounting, not a claim that the underlying plan
is economically free. V4 Free consumed no OpenRouter credit.

## Results

| Model | Scores | Qualified | Mean duration | Mean reasoning tokens | Reported total cost |
| --- | --- | ---: | ---: | ---: | ---: |
| GPT-5.6 Sol max | 100 / 100 / 100 | 3/3 | 334.763 s | 4,421 | $0 |
| DeepSeek V4 Flash Free max | 60 / 60 / 75 | 0/3 | 273.271 s | 14,842 | $0 |

Sol durations were 344.105, 361.510, and 298.673 seconds. V4 durations were
352.522, 205.229, and 262.063 seconds. There were no timeouts, provider errors,
quota failures, or nonzero stderr logs in either campaign.

## Sol trace audit

Every Sol trace used the exact required hardware sequence:

1. one strict eight-operation preview that learned the input-module placement;
2. one complete retry accepting only that placement, which learned the output
   placement;
3. one complete corrected preview accepting both observed placements;
4. one apply of the identical corrected operation set.

All dependent targets used result-reference chains, both address writes omitted
unobserved expected-state guards, and no hardware mutation probe preceded the
complete candidate. Each run then independently reread tree, network, and IO
state and compiled `PLC_Main` with zero errors and warnings.

The raw campaign result is under ignored artifacts at:
`artifacts/20260802T064607712Z-campaign-hardware-family-transfer-gpt-5-6-sol-max-20260802`.

## V4 trace audit

All three V4 traces ultimately applied the correct eight-operation transaction,
proved the requested final state, and compiled cleanly. They failed only the
strict transaction-discipline assertions:

- Trial 1 issued six hardware patches. It first omitted the rail target from
  both plugs, then later added the unrequested `subnetMask` field. The first
  caused an avoidable root-target failure and the latter caused an unsupported
  setter failure before the successful preview. Score: 60.
- Trial 2 issued five hardware patches. Its placement-learning sequence was
  otherwise correct, but it added `subnetMask: 255.255.255.0`; ET 200MP rejected
  that optional setter, producing one extra corrected-operation preview.
  Score: 60.
- Trial 3 issued five hardware patches. After learning the input displacement,
  it dropped `target.itemPath: ["Rail_0"]` on the next retry, producing an
  avoidable root-target failure before restoring the rail target. Score: 75.

The initial V4 candidate also guessed `PROFINET interface_1` in each trial.
The first placement failure returned the newly created item's actual tree, and
V4 corrected subsequent batches to `PROFINET interface`; this did not add a
separate retry because the earlier strict plug failed first.

The raw campaign result is under ignored artifacts at:
`artifacts/20260802T070444538Z-campaign-hardware-family-transfer-deepseek-v4-flash-free-max-20260802`.

## Skill evaluation

The held-out family transfer validates the retained generic skill change from
`ott-23`: Sol transferred the generated-child guard rule and result-reference
binding without any case-shaped constants. The new V4 failures do not expose
missing knowledge:

- the skill already states that `targetRef` alone addresses the device root and
  is not a substitute for the intended rack, module, or submodule parent;
- it already says to omit `subnetMask` and other optional identity fields when
  the task does not require them, and to treat a rejected optional setter as
  unsupported for that interface;
- none of the skills contains an `ott-24` station name, article number, address,
  position, or network identity.

The appropriate next step is therefore a new capability case, not another
topology wording change driven by V4's repeated disregard of existing rules.
