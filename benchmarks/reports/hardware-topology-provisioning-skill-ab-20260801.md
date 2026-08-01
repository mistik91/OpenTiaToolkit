# PARTIAL IMPROVEMENT - topology skill qualification remains failed

The hardware skill changes are worth keeping, but DeepSeek V4 Flash still
does not qualify on `ott-22`. Across three frozen paid candidates, mean score
rose from 60.00 to 78.33 and then 81.67. The final candidate reached the
correct engineering state and compiled cleanly in all three trials, but every
trace retained a critical atomicity or retry-discipline failure. The case,
prompt, fixture, assertions, and pass threshold remained unchanged.

## Frozen evaluation surface

- Case/fixture/scorer commit: `619479a3b519e6f8926a466a65fdbc1bed3aef75`
- Campaign: `hardware-topology-provisioning-deepseek-v4-flash-candidate-20260801`
- Model: `openrouter/deepseek/deepseek-v4-flash`
- Catalog name: DeepSeek V4 Flash
- OpenCode: `1.18.10`
- Reasoning effort: `provider-default`
- Variant: none forced
- Catalog pricing: $0.14/M input tokens, $0.28/M output tokens
- Fixture: `tia-v21-topology-6`
- Fixture SHA-256:
  `33193A83BE565BBD63486D66FFDE39404C76D9F8F01178DF28D435DBE3CC5732`
- TIA Portal: V21 Update 2

The catalog advertises `high` and `xhigh` variants, but this campaign preserved
the previously agreed paid-model policy: provider-default reasoning with no
forced variant. Every paid run used three fresh restores, a clean Git state,
and the same $1 between-trial safety ceiling.

## Free endpoint interruption

The first candidate was initially started with
`opencode/deepseek-v4-flash-free` at `max`. Two trials timed out after 900
seconds with zero-byte event streams, and the third showed the same condition.
At the user's direction, the exact free-campaign process tree was stopped, its
open disposable project was closed without saving, and the paid campaign was
restarted from trial 1. Those partial free results are runtime evidence only
and are excluded from the paid comparison.

## Paid candidate progression

| Candidate | Frozen commit | Skill SHA-256 prefix | Trial scores | Mean | Pass | Mean duration | Mean reasoning | Cost |
| --- | --- | --- | --- | ---: | --- | ---: | ---: | ---: |
| 1 | `492cac7` | `4AC5EA9F` | 80, 50, 50 | 60.00 | 0/3 | 207.266 s | 5,469 | $0.19529541 |
| 2 | `a9d48e4` | `92E23151` | 85, 70, 80 | 78.33 | 0/3 | 236.286 s | 5,338 | $0.18052577 |
| 3 | `fd20172` | `9A6826F1` | 80, 80, 85 | 81.67 | 0/3 | 240.303 s | 5,285.67 | $0.21039365 |
| **Total** |  |  |  |  | **0/9** |  |  | **$0.58621483** |

Candidate artifacts, in order:

- `benchmarks/artifacts/20260801T095910014Z-campaign-hardware-topology-provisioning-deepseek-v4-flash-candidate-20260801`
- `benchmarks/artifacts/20260801T101443379Z-campaign-hardware-topology-provisioning-deepseek-v4-flash-candidate-20260801`
- `benchmarks/artifacts/20260801T102851204Z-campaign-hardware-topology-provisioning-deepseek-v4-flash-candidate-20260801`

## Generic skill changes

Candidate 1 added a bounded topology workflow:

- keep coordinated station creation inside the guarded hardware patch;
- use the dedicated topology and PROFINET operations;
- learn displaced placements through strict transactional previews; and
- apply one complete batch only after a successful full preview.

Candidate 2 documented exact public JSON contracts exposed by
`HardwarePatchOperation`:

- use `op`, `deviceName`, and `itemName`, not ignored aliases;
- keep `targetRef` at operation level;
- use exact `ipAddress`, `pnDeviceName`, and `pnDeviceNumber` fields; and
- never infer a sibling plug's displaced placement without its own failure.

Candidate 3 added target-binding checks for newly created child objects:

- every plug names its intended parent path;
- all interface operations bind the discovered interface child; and
- subnet and IO-system connections record explicit `nodeIndex` and
  `connectorIndex` values.

The final canonical and packaged skill hash is:
`9A6826F13940189D83E79E740C2C8BE71A6A31760E50BE96A77722EA155E4F10`.
Canonical, Codex, OpenCode, and Claude package copies were byte-identical.

## Final candidate findings

All three final traces passed catalog discovery, full corrected preview,
single apply, fresh hardware tree and network readback, and compilation with
zero errors and zero warnings. The remaining failures were deliberately not
softened:

- trial 1 used the exact network indexes and identity fields, but replaced the
  requested main-item name with a catalog-derived label and needed extra path
  retries;
- trial 2 produced the correct final six-operation batch, but first submitted
  an unnecessary unsupported subnet mask, creating an extra matching preview
  attempt; and
- trial 3 handled both placements and indexes cleanly, but pre-created the
  station with `add-device`, leaving station creation outside the reviewed
  transaction.

Thus the paid model is usable for reaching and verifying the requested TIA
end state, but it is not yet reliable enough for strict atomic, reviewable
topology automation.

## Anti-overfitting decision

Keep the final skill and stop tuning on `ott-22`. The additions describe exact
public toolkit fields, object binding, and transaction safety; they contain no
fixture identities, article numbers, names, paths, addresses, or expected
answer. Adding naming reminders or more task-shaped operation sequences now
would risk teaching this case rather than improving the generic workflow.

The benchmark remains unchanged and unsaturated. The next useful comparison
is either a stronger model on this exact frozen candidate or DeepSeek V4 Flash
at an explicitly versioned `high`/`xhigh` effort. It should not be another
`ott-22`-driven skill edit.

## Cleanup

The final disposable project was closed with `save=false`. TIA's close request
left the exact toolkit-owned PID 7928 tree resident, so its four verified
Siemens processes were stopped. Thirteen exact preflight, free, and paid
restore directories were moved to the Windows Recycle Bin. The immutable
archive, gold evidence, and ignored raw traces remain intact.
