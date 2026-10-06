TIA Portal is running with a freshly restored disposable `OTT_Benchmark` project.

Perform a coordinated hardware and PLC-tag migration for every movable `Candidate` I/O row.

First compute the same compact hardware layout in both independent address spaces:

1. Keep each module whose name ends in `Occupied` fixed as that space's anchor.
2. Find every address row whose module name ends in `Candidate` and whose observed `IoType` matches the space.
3. Order candidate rows by hardware-tree `PositionNumber`.
4. Place them contiguously immediately after the anchor, deriving occupied byte ranges from each observed `Length`.

Then find the four Bool tags in `Default tag table` whose names identify those candidate module rows. Preserve each tag's table, name, data type, comment, and bit offset, but remap its logical byte address so it continues to reference the same hardware channel after compaction. Guard each tag update by matching its observed table, name, and current logical address.

Do not use destination constants from prior knowledge. Discover the tree, I/O map, and tags and calculate the complete migration.

Stage safety in this exact order before mutating anything:

1. Dry-run one complete four-operation hardware patch.
2. Dry-run one inline four-operation `modify-tags` batch.
3. Only if both previews succeed, apply the same hardware patch once.
4. Then apply the same tag batch once with confirmation.

If either preview fails, apply neither domain and do not retry. After successful applies, independently reread the complete I/O map, export the tags again, and compile `PLC_Main`. Report the final hardware row starts and tag logical addresses. Do not change any unrelated hardware or PLC tag field.
