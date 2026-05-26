# MCP Example: VCI S7DCL Synchronization

Use a disposable workspace first. VCI maintains mappings between PLC objects and repository files; unlike one-off export/import, synchronization uses those mappings in a selected direction.

```text
list-instances
connect-instance --instance-id=12345
list-projects
open-plc --plc-name="PLC_1"

create-vci-workspace --workspace-name="PLC_Repo" --root-path="C:\Repo\PLC_1" --workspace-language="en-US"
get-vci-block-formats --plc-name="PLC_1" --block-name="FC_Control" --block-type="FC"
export-vci-block --workspace-name="PLC_Repo" --plc-name="PLC_1" --block-name="FC_Control" --block-type="FC" --relative-directory="." --file-format="SimaticSD"

sync-vci-block --workspace-name="PLC_Repo" --plc-name="PLC_1" --block-name="FC_Control" --block-type="FC" --direction="project-to-workspace"
sync-vci-block --workspace-name="PLC_Repo" --plc-name="PLC_1" --block-name="FC_Control" --block-type="FC" --direction="workspace-to-project"

sync-vci-plc --workspace-name="PLC_Repo" --plc-name="PLC_1" --direction="project-to-workspace" --scope="all-supported"
```

`sync-vci-plc` and `sync-vci-blocks` return compact results by default: skipped counts are retained, while routine skipped-item entries are omitted. Add `include-skipped=true` only when a full audit list is needed.

To reconnect an existing repository that already contains `.vci` metadata, call `create-vci-workspace` without `workspace-language`, then use `connect-vci-block` to map existing files.

Before using `workspace-to-project` on production content, verify the mapped file and its comparison status carefully.
