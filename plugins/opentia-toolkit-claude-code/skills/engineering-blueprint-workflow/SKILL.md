---
name: engineering-blueprint-workflow
description: Use for OpenTiaToolkit declarative engineering blueprints across PLC, Unified HMI, and hardware outcomes, including validation, state-bound planning, one-use apply tokens, and focused post-apply verification.
---

# Engineering Blueprint Workflow

## Overview

Use blueprints for a small, related set of desired engineering mutations that should be reviewed and applied as one state-bound plan. A narrow context prelude may open/select TIA, a project, PLC, or HMI. Do not use blueprints as a general scripting language or for read-only discovery.

## Workflow

1. Establish the exact TIA instance, project, and PLC/HMI/hardware targets with `basic-tia-interaction` and the relevant domain skill.
2. Read the [bundled blueprint schema](references/engineering-blueprint.schema.json) before authoring a blueprint. It defines the file structure; use the active command schemas for domain parameter names and values.
3. Put each step's command parameters in `arguments`; standalone command examples using `args` are not blueprint steps. Keep each step narrowly related to one outcome.
4. Run `validate-blueprint --blueprint-path`.
5. Run `plan-blueprint --blueprint-path` and review the maximum risk, every step, warnings, state fingerprint, and expiry.
6. Apply once with `apply-blueprint --apply-token --confirm=true` only when the reviewed plan still matches the user’s intent.
7. Verify each affected domain with focused read commands.

## Safety

Never put `confirm`, passwords, secrets, or credential values in a blueprint. Apply tokens are short-lived, one-use, state-bound, and redacted; do not persist or reuse them. If TIA/project state changes, a token expires, a worker resets, or apply is uncertain, create and review a new plan. Keep `continueOnError=false` unless independent later steps are explicitly safe after an earlier failure.

Blueprint apply changes command-level `dry-run` values to `false` only after token validation. The outer `apply-blueprint` command still requires centralized mutation confirmation.
