TIA Portal is running with a freshly restored disposable `OTT_Benchmark` project.

Compact all movable hardware rows in both process-image address spaces around the fixed `Occupied` anchors.

Treat Input and Output as independent spaces. In each space:

1. Keep the module whose name ends in `Occupied` fixed.
2. Find every address row whose module name ends in `Candidate` and whose observed `IoType` matches that space.
3. Order those candidate rows by their module's observed hardware-tree `PositionNumber`.
4. Place the first candidate immediately after the anchor's occupied byte range, then place each remaining candidate immediately after the preceding candidate with no gaps.

Derive every occupied byte range from the observed `Length`; no destination addresses are supplied. Leave every non-address property unchanged.

Inspect the exact hardware tree and current I/O map first. Build one guarded hardware patch containing all required moves, listing the Input operations first and then the Output operations while preserving the computed order within each space. Dry-run that complete patch exactly once. Apply that same complete patch exactly once only if the preview succeeds. If preview fails, do not retry or apply.

Finally, independently reread the I/O map and report the start address of both fixed anchors and every moved candidate row, including the `IoType` and address index used for each row.
