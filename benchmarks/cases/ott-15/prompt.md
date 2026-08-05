TIA Portal is running with a freshly restored disposable `OTT_Benchmark` project.

Move the input addresses of module `DI_Candidate` to start at byte `66`.

Follow the hardware workflow: inspect the exact hardware tree and current I/O address map, build a patch guarded by the observed current start address, and dry-run it. If the dry-run succeeds, apply that exact patch with confirmation and independently read the I/O address map again to verify the result. Do not change any other hardware.
