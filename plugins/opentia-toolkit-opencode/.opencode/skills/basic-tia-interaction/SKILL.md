---
name: basic-tia-interaction
description: Use when connecting to Siemens TIA Portal with OpenTiaToolkit, selecting among running TIA instances, finding an already-open project, opening a specified project, or preparing PLC/HMI work before specialized skills.
---

# Basic TIA Interaction

## Overview

Use this skill before PLC, HMI, tag, block, or Unified HMI editing work when the correct TIA Portal instance/project context is not already proven in the current turn. Its job is to select the right running TIA Portal instance and establish the open project before any domain-specific work starts.

Do not conclude that no project is open from a connect response alone. Project discovery is a normal follow-up step after connecting.

## Standard Workflow

1. Identify any user-provided targets: TIA instance id, project name/path, PLC name, HMI name, and whether the user says the project is already open.
2. Call `list-instances` first when a running TIA Portal instance is expected.
3. If exactly one instance is running, connect to it with `connect-instance`, then call `list-projects` or `get-project-info` to verify the project.
4. If multiple instances are running, probe them before choosing:
   - Connect to each instance by id.
   - Record the instance id, TIA version if available, and any open project name/path from `list-projects` or `get-project-info`.
   - If the user specified a project name/path, choose the matching instance automatically.
   - If the user did not specify a project and more than one candidate has an open project, ask which instance/project to use.
5. If no project is open in the selected instance, open the user-specified project path. If no path was provided, ask for the project path.
6. After the project is verified, open or list the requested PLC/HMI target only as needed.

## Current Open Instance Requests

When the user says "connect to the current open instance", "the project is already open", or similar:

- Treat an open project as expected.
- Do not use `open-project` unless project discovery proves no project is open or the user supplied a different project path.
- After `connect-instance` or `connect-portal`, immediately call `list-projects` or `get-project-info`.
- If project discovery finds the open project, continue without warning the user that no project was open.

## Multiple Instances

If `list-instances` returns more than one TIA Portal process:

- Do not pick an arbitrary instance.
- Probe each instance and map instance id to open project.
- If the prompt named a project, use the instance whose open project name/path matches.
- If exactly one instance has an open project and no project was specified, use that instance.
- If several instances are plausible and no project was specified, ask the user to choose by project name/path and instance id.

Example decision summary:

```text
Found multiple TIA instances:
- PID 1234: Project Line1, path C:\Projects\Line1\Line1.ap21
- PID 5678: no open project

Using PID 1234 because the prompt specified project Line1.
```

## Project Matching

Treat these as project matches when the user specified a project:

- Exact project path match, case-insensitive.
- Same normalized file name, such as `Line1.ap21`.
- Same project name without extension, such as `Line1`.

If two instances match the same project name but different paths, ask the user to choose. Do not guess between duplicate project names.

## Target Selection

After the project is selected:

- For PLC work, use `list-plcs` or `open-plc` with the requested PLC name.
- For HMI work, use `list-hmis` or `open-unified-hmi` with the requested HMI name.
- If the requested HMI/PLC name is not found exactly, list candidates and use an obvious exact-like alias only when the mapping is unambiguous. Otherwise ask.

## Recovery

- If an Openness command reports that no project is open, call `list-projects` before asking the user for a path.
- If a connected instance has stale or surprising state, run `portal-status` and `list-projects`.
- If the user provided a project path and no running instance has it open, connect to the intended instance or start one, then call `open-project`.
- If opening a project changes the selected instance/project, re-run target discovery before continuing.

## Hand-Off

Once the correct TIA instance and project are verified, continue with the task-specific skill or tools. For Unified HMI screen JSON edits, use `hmi-unified-agent-editing` after this skill establishes the project and HMI context.
