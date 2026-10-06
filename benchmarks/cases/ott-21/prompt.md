TIA Portal is running with a freshly restored disposable `OTT_Benchmark` project.

Perform a coordinated hardware and PLC-tag migration for the movable process-image rows.

Treat Input and Output as independent address spaces. Keep the address row on `RackInputsFixed` fixed in Input space and the row on `RackOutputsFixed` fixed in Output space. The movable rows are every address row exposed by `ConveyorSense`, `GateActuator`, and `SkidSignals` whose observed `IoType` belongs to the current space.

Within each space, order the movable rows by their parent module's observed hardware-tree `PositionNumber` in descending order. Place the first movable row immediately after the fixed row's occupied byte range, then place each remaining row immediately after the preceding movable row with no gaps. Derive all occupied byte ranges from the observed `Length`; no destination addresses are supplied.

The four Bool tags whose comments begin with `transfer-map:` identify the corresponding module, I/O type, and bit offset. Preserve each tag's table, name, data type, comment, and bit offset, but update its logical byte address so it still references the same hardware channel after the move. Guard every update with the observed tag table, name, and complete current logical address.

Use one complete hardware batch and one complete tag batch. Successfully preview both complete batches before the first mutation. If either preview fails, apply neither domain. After both applies, independently reread the hardware addresses and export the tags, then compile `PLC_Main`. Do not change anything else.
