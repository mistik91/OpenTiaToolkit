TIA Portal is running with a freshly restored disposable `OTT_Benchmark` project.

Move only the output addresses of the mixed I/O module `DIDQ_Candidate` to start at byte `132`. Leave its input addresses unchanged.

Inspect the exact hardware tree and current I/O address map first. Select the address row by its observed `IoType`, not by assuming an index. Build one guarded hardware patch using the exact target and current start address, dry-run it, and apply it only if the preview succeeds. Independently read the I/O address map again and report the final input and output start addresses. Do not change any other hardware.
