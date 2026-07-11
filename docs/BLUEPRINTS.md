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

## Schema

Blueprint schema version `1.0` contains a name, optional description, optional `requiredWorker`, and one or more uniquely identified command steps. Keep blueprints narrow so review, failure handling, and audit history remain clear.
