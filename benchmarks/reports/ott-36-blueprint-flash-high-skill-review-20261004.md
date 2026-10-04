# OTT-36 Flash High: partial campaign and skill-review gate

## Outcome

Two campaign trials reached the exact desired engineering state and independently
verified clean. Both unchanged traces score **100/100** under the corrected
semantic oracle. **Trial 3 was not run**: the campaign stopped at the agreed
skill-review gate after finding an actionable packaging gap. This is not a
completed three-trial qualification, a paired skill evaluation or evidence that
Flash always follows every workflow instruction. No skill was modified.

The generic blueprint coordinator fix remains independently live-qualified:
stale rejection, honest partial failure, stopped suffix, explicit continuation
with overall incomplete result, one-use tokens, fresh recovery and preservation.
See the [independent qualification report](ott-36-blueprint-live-qualification-20261004.md).

## Captures and cost

OpenCode **1.18.10**, `zai-coding-plan/glm-5.3-flash`, declared reasoning effort
**high**, variant **high**, provider-scoped subscription authentication. Each
trial used a fresh restore of the same immutable V21 archive. No provider error
or rate-limit signal appeared. No GLM-5.3 or Sol fallback was started.

| Capture | Inference revision | Raw score | Corrected replay | Seconds | Reported USD |
| --- | --- | --- | --- | --- | --- |
| Initial scorer calibration | `7e35249` | 15 | 100 | 598.893 | 0 |
| Campaign trial 1 | `7786b7f` | 100 | 100 | 488.924 | 0 |
| Campaign trial 2 | `7786b7f` | 15 | 100 | 539.914 | 0 |

The two campaign captures average **514.419 seconds (8.57 minutes)**. Reported
cost is zero for each task execution, not amortized subscription cost and not a
claim that this is a free API model. OpenRouter was not used.

Trial 1 reported 90,744 input / 6,834 output / 4,907 reasoning tokens; trial 2
84,823 / 6,429 / 5,453. These are OpenCode's separate counters; its total token
counter includes cache accounting and must not be equated to their sum.

Raw traces, metadata, results and the original commit-pinned checkpoints remain
unchanged outside Git. Replay results are separate files, never replacements.
The initial calibration is excluded from the formal campaign. The partial
campaign checkpoint records two accepted executions and trial 3 pending; it is
not silently migrated to the new scorer commit. A corrected-skill campaign needs
a fresh frozen revision and checkpoint rather than mixing skill revisions.

## Last scorer correction

Trial 2 selected each tag by exact tag table and name. `modify-tags` supports this
unambiguous identity; logical address is an optional additional selector guard.
The frozen public task declares identities and preservation, not one mandatory
JSON selector spelling. The old oracle incorrectly required the redundant
address key. The corrected oracle still requires exact table/name, an update of
only the desired comment, and an exact address when that optional guard is used.
Broad selectors, wrong identities/addresses and additional mutation fields fail.
Actual structured before/after data and the full native CSV inventory must still
prove exact types, addresses, comments and preservation.

The same safe normalization also applies to supported HMI planned arguments:
documented operation wrappers/aliases, safe defaults and scoped `working-path`
cannot change engineering intent. Unsupported arguments, unsafe flags, extra
wrapper keys and foreign artifact roots still fail. Preview and plan remain
bound to the same intent, exact filters and base hash. The acceptance contract,
model prompt, runtime binary and skills did not change for these replays.

Validation covers **54 adversarial controls**, supported equivalent identities
and HMI forms, full scorer integration and independent live-gold replay. The
same authentic capture cannot be assembled from complementary incomplete applies.

## Generic skill correction proposed, not yet applied

`engineering-blueprint-workflow` tells installed agents to start from
`schemas/engineering-blueprint.schema.json` or `examples/blueprints`. Those paths
exist at repository root, but neither the schema nor examples are bundled in the
canonical skill directory or any of its four shipped plugin variants. Trial 2
searched its skill directory and workspace for both and received no files. It
then repaired guessed blueprint JSON through two validation failures (object
where string expected; missing step id), and replaced a syntactically accepted
but empty `Arguments` dictionary before its real apply. Missing portable guidance
is a plausible contributor; these observations do not establish causation or
predict the size of a performance improvement.

Proposed surgical correction: bundle the existing generic schema as a skill-local
reference, update the skill's reference to that real path, and add a drift/packaging
check so the bundled schema stays aligned with its canonical repository source.
Do not add benchmark targets, comments, labels, tokens or an OTT-36 solution.
Qualify the packaged reference before a new Flash High campaign.

Read-only discovery also overlapped in both campaign trials, causing a guarded
conflict and a successful retry. Serial TIA calls are already explicitly required
by the current workflow and AGENTS instructions; that is an observed execution
slip, not evidence that the instruction is absent. Final-state success must not
hide it. No concurrent engineering apply was observed.

## Independent evidence and handoff

Separate Release MCP/worker attachments independently verified each still-open
final project before the next restore or cleanup: exactly two tags with correct
comments, Text=Run, preserved HMI geometry, immutable Main canonical hash, and
compile **0 errors / 0 warnings**. The independent verifier was first tested on
the pilot, whose final project also passed. The model did not author these proofs.

The campaign-owned project was then closed without saving, and the owned TIA
portal and keeper MCP exited successfully. All independent readback MCP processes
exited. Only the pre-existing app-owned Debug MCP/worker remain; they were not
changed. The archive SHA-256 and registered Release worker path/date/hash are
unchanged. Native exports remain available; discarded disposable edits were not
saved to the fixture or a user project.

The final scorer passed Windows PowerShell 5.1 and PowerShell 7 regression checks,
including independent live-gold replay. Full benchmark `npm test` passed again
with **29 cases / 72 campaigns / 15 fixtures**. Skill packaging drift remains
zero (identical copies can still share a missing reference), command reference
remains current at 186, and repository hygiene/whitespace checks passed. No worker
rebuild was necessary for the scorer corrections. The earlier runtime validation
remains documented separately: 765 unit, 54 architecture and 5 vendor-free IPC
tests passed, as did MCP modern/legacy exposure.

Evidence roots:

- `E:/Documents/OpenTiaBench/Artifacts/ott36-flash-high-20261004/` — original calibration and scorer replays.
- `E:/Documents/OpenTiaBench/Artifacts/ott36-flash-high-qualified-20261004/` — partial campaign, immutable native traces and separate selector replays.
- `E:/Documents/OpenTiaBench/Artifacts/ott36-flash-pilot-independent-20261004/` — independent pilot proof.
- `E:/Documents/OpenTiaBench/Artifacts/ott36-flash-qualified-t1-independent-20261004/` — independent trial 1 proof.
- `E:/Documents/OpenTiaBench/Artifacts/ott36-flash-qualified-t2-independent-20261004/` — independent trial 2 proof.
- `E:/Documents/OpenTiaBench/Artifacts/ott36-campaign-keeper-20261004/` — campaign portal ownership and cleanup records.

`evaluation-provenance.json` under the partial campaign artifact root binds the
replay scorer commit and file hashes to the original trace/metadata/result hashes;
inference metadata continues to identify the original captured revision.

Work stays on `codex/ott-36-blueprint-benchmark`; no push, PR or merge is part of
this checkpoint. Continue only after review of the portable schema correction.
