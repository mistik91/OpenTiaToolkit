# Engineering blueprints

Engineering blueprints declare a small, reviewable sequence of desired PLC, Unified HMI, or hardware mutations. They are not unrestricted command scripts.

## Safe workflow

1. Write a blueprint that conforms to `schemas/engineering-blueprint.schema.json`.
2. Run `validate-blueprint --blueprint-path=<file>`.
3. Run `plan-blueprint --blueprint-path=<file>` and review every step, its risk, warnings, state fingerprint, and expiry.
4. Apply only the reviewed plan with `apply-blueprint --apply-token=<token> --confirm=true`.
5. Verify the result with focused PLC, HMI, or hardware reads.

Apply tokens are random, short-lived, one-use, redacted from the audit journal, and stored only in the worker process. A token is bound to the canonical blueprint and current portal/project fingerprint. A reset, expiry, replay, or project-state change requires a new plan. Blueprints cannot call other blueprint commands, contain arbitrary read-only steps, or embed confirmation. A narrow set of session/target context commands may precede desired-state mutations. During apply, the worker supplies confirmation only after token validation and changes command-level `dry-run` arguments to `false`.

Examples are provided under `examples/blueprints` for PLC tags, a Unified HMI screen, and PROFINET identity configuration. Replace their illustrative targets and addresses before planning.

## Completion and partial failures

Blueprints execute sequentially; they do not create a cross-command transaction or
automatically undo earlier successful steps. A plan lists commands, risks and
arguments, but does not run each domain's dry-run. Preview supported domain
mutations separately, retain their exact selectors/base hashes, and verify the
affected objects after apply. The portal/project fingerprint is a context guard,
not a replacement for command-level concurrency checks.

Each step's `Success` reflects completion, while `TransportSuccess` records the
original command-envelope success. `Data` retains the command's structured outcome
and guidance. Expected HMI safety blocks can therefore have
`TransportSuccess=true` and `Success=false` without changing standalone HMI APIs.

Explicit root-level `SafetyBlocked`, `ValidationBlocked`, `RolledBack`, or `DryRun`
flags set to `true`, and `Applied`, `Success`, `Verified`, or `StateMatched` flags
set to `false`, prevent step completion. Non-boolean values for these fields fail
closed. Nested preflight/backup/cleanup records are not mistaken for the overall
command result. Commands without these fields retain their existing transport
semantics; focused readback remains required.

By default, an incomplete step stops later steps. `continueOnError=true` permits
explicitly independent later steps but does not make the incomplete step or whole
blueprint successful. Overall `Applied=true` requires every planned step to
complete. Failed applies consume their one-use token too: inspect actual state,
then make and review a fresh recovery plan. Do not claim automatic rollback.

## Schema

Blueprint schema version `1.0` contains a name, optional description, optional `requiredWorker`, and one or more uniquely identified command steps. Keep blueprints narrow so review, failure handling, and audit history remain clear.
