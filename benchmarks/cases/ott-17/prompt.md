TIA Portal is running with a freshly restored disposable `OTT_Benchmark` project.

As one atomic hardware transaction, move the input addresses of `DI_Candidate` to start at byte `66` and the output addresses of `DQ_Candidate` to start at byte `128`.

Inspect the exact hardware tree and current I/O address map first. Build one patch containing both operations, each guarded by the observed current start address, and dry-run that single patch. Apply it only if the entire preview succeeds. If any operation fails, do not apply anything; independently read the I/O address map again and report whether both modules remained unchanged.
