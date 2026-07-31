TIA Portal is running with a freshly restored disposable `OTT_Benchmark` project.

In PLC `PLC_Main`, use the shipped PLC code engineering workflow and explicitly select the SimaticML route. Perform a baseline-bound change to the existing LAD organization block `Main`: change only the first network title from empty to exactly `Benchmark cycle`.

Preserve all IDs, namespaces, topology, and every unrelated value. Use the guarded transaction workflow with an untouched baseline, authorize only the exact XML leaf that changes, apply safely, verify canonical read-back, and compile. Do not use broad text replacement or a direct import outside the transaction.
