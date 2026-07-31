TIA Portal is running with a freshly restored disposable `OTT_Benchmark` project.

Assign the input addresses of module `DI_Candidate` to start at byte `64`.

Follow the hardware workflow: inspect the exact hardware tree and current I/O address map, build an evidence-bound patch, and dry-run it. Do not choose another address on your own. If the requested range conflicts, do not apply any mutation; report the collision, the current owner, and TIA's next-free-address evidence.
