TIA Portal is running with a freshly restored disposable `OTT_Benchmark` project. The root of the isolated benchmark workspace is your current working directory.

Reconcile two existing SCL function blocks in `PLC_Main` through one new Version Control Interface workspace named exactly `OTT31_Workspace`. Treat sync direction as part of the task: `OTT31_ProjectSource` must prove project-to-workspace export, while `OTT31_WorkspaceTarget` must prove a reviewed workspace-to-project import. `OTT31_Distractor` is a preservation target and must never be mapped or modified.

Establish the exact TIA, project, and PLC context. Confirm that the project initially has no VCI workspaces. Create `OTT31_Workspace` with explicit confirmation, language `en-US`, and the absolute path of `artifacts/vci-repo` inside the isolated benchmark workspace. Create the workspace before discovering formats; an empty format result with no workspace is not unsupported-target evidence.

After the workspace exists, discover the supported formats for exact root FCs `OTT31_ProjectSource` and `OTT31_WorkspaceTarget`. Select `SCL` only after it is reported for both. Export each exact block into the flat workspace root with explicit block type `FC`, file format `SCL`, and a file name matching the block. List the mappings and require exactly those two SCL mappings in `Equal` state. Do not map `OTT31_Distractor`.

With the exact mapping established, discover the live Modify capability for `OTT31_WorkspaceTarget` as a `Block`/`FC`, requested format `VCI`, requested language `SCL`, and source-control preference enabled. Continue only if the exact identity and mapping are resolved and the selected route is `VCI`.

Before editing, retain an untouched UTF-8 BOM baseline by independently exporting the live `OTT31_WorkspaceTarget` block as native source to `artifacts/baseline/OTT31_WorkspaceTarget.scl`; verify that it contains the original `+ 20`. Then make these two narrow workspace edits:

- in mapped `OTT31_ProjectSource.scl`, change only its implementation constant from `+ 10` to `+ 99`; this deliberately makes the workspace copy stale and must never be imported into TIA;
- in mapped `OTT31_WorkspaceTarget.scl`, change only its implementation constant from `+ 20` to `+ 25`; this is the reviewed incoming change.

Read the exact VCI status of both mappings and require `Unequal`. Validate the changed `OTT31_WorkspaceTarget.scl` as `SCL` and require a verified classification with no errors or warnings before importing it.

Synchronize exactly `OTT31_ProjectSource` in direction `project-to-workspace`, using the existing mapping (`create-mapping=false`) and explicit confirmation. Verify its mapped file is back to `+ 10` and its status is `Equal`.

Synchronize exactly `OTT31_WorkspaceTarget` in direction `workspace-to-project`, using the existing mapping (`create-mapping=false`) and explicit confirmation. Verify its status is `Equal`, then compile `PLC_Main` and require zero errors and zero warnings.

Independently export the live `OTT31_WorkspaceTarget` and `OTT31_Distractor` blocks as native source to `artifacts/readback/OTT31_WorkspaceTarget.scl` and `artifacts/readback/OTT31_Distractor.scl`. Verify the imported target reads `+ 25` and the distractor remains `+ 30`. Finally list the mappings again and require exactly the same two SCL mappings, both `Equal`.

Use only exact single-block VCI commands. Do not use bulk VCI synchronization, connect an existing file, create an implicit mapping during sync, use a direct-import or transaction bypass, delete the VCI workspace, or save the project. Summarize both directions, the three verified constants, final mapping states, selected VCI route, compile counts, and that the project was not saved.
