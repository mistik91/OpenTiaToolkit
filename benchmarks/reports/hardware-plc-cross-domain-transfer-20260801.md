# PASS — held-out cross-domain transfer qualification

The unchanged PLC/tag skill transferred cleanly to `ott-21`: all three
immutable traces rescore to 100 under the corrected semantic evaluator. The
case changed hardware and tag identities, baseline and destination ranges,
hardware ordering, and tag bit offsets. No task-specific skill text was added.

## Frozen configuration

- Case/fixture/scorer commit at execution: `9e2434eb78dac1a87245237c1dc7aed0f3f81c4f`
- Skill candidate commit: `81558825381cb8ff403491f8b4363a1d460dc09c`
- Model: `opencode/deepseek-v4-flash-free`
- OpenCode: `1.18.10`
- Reasoning effort: `max`
- Variant: `max`
- Cost class: free
- Fixture SHA-256:
  `B57573BBD33E185D7B843CDED87362F6BF08932944388508BFD934D575C35A56`

## Scorer correction

The initial campaign summary reported scores `84.21`, `84.21`, and `100`.
Trials 1 and 2 supplied the hardware batch using the MCP's valid direct-array
form, while trial 3 used the equally valid `{operations:[...]}` wrapper. The
new `toolOperationSet` evaluator initially followed only
`operations.operations`, so it falsely rejected both direct arrays even though
their four operations were byte-for-byte equivalent and both applies
succeeded.

The generic evaluator now resolves the declared `operations` property and
normalizes an optional inner `operations` wrapper before performing exact-cardinality,
order-independent, deep-subset matching. Regressions cover direct and wrapped
forms, shuffled operation order, shuffled property order, missing exact tag
selector fields, and apply-before-second-preview rejection. No benchmark
threshold, prompt, expected engineering value, trace, or skill changed.

Rescoring the original `events.jsonl` and `metadata.json` files produced:

| Trial | Corrected score | Pass | Duration | Reasoning tokens | Hardware input form | Cost |
| ---: | ---: | --- | ---: | ---: | --- | ---: |
| 1 | 100 | yes | 112.267 s | 4,963 | direct array | $0.000000 |
| 2 | 100 | yes | 111.545 s | 4,484 | direct array | $0.000000 |
| 3 | 100 | yes | 114.304 s | 4,248 | wrapped array | $0.000000 |
| **Mean / total** | **100.00** | **3/3** | **112.705 s** | **4,565.00** |  | **$0.000000** |

## Engineering result

Every trace:

- discovered the renamed hardware and all four nonzero-bit tags;
- derived Input starts `42`, `44` and Output starts `92`, `94` from observed
  positions and 16-bit lengths;
- used exact `tagTable`, `name`, and complete `logicalAddress` selectors;
- previewed both complete domains before the first apply;
- issued exactly one hardware preview/apply and one tag preview/apply;
- independently reread hardware and tags; and
- compiled with zero errors and zero warnings.

This is positive transfer evidence for the generic selector and cross-domain
preview guidance, not merely a repeat of `ott-20`.

## Residual efficiency finding

All three runs recovered from attempts to write an MCP export into a path that
was not jointly readable by the MCP file policy and the isolated OpenCode
workspace. Trials 1 and 2 also recovered from one transient active-worker
conflict. These did not alter engineering state or the corrected score, but
they identify a generic artifact-routing and serial-operation efficiency
opportunity for a later skill or harness improvement.
