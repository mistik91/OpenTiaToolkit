TIA Portal is running with a freshly restored disposable `OTT_Benchmark` project.

Move the output addresses of module `DQ_Candidate` to start at byte `130`.

Follow the hardware workflow: inspect the exact hardware tree and current I/O address map, confirm that the target address object is an output, build a patch guarded by the observed current start address, and dry-run it. If the dry-run succeeds, apply that exact patch with confirmation and independently read the I/O address map again to verify the result. Do not change any other hardware.
