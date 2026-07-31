TIA Portal is running with a freshly restored disposable `OTT_Benchmark` project.

In Unified HMI `HMI_RT_1`, edit screen `Overview` so that `StartButton.Text` changes from `Start` to exactly `Run`. Change nothing else.

Follow the focused JSON workflow: read a snapshot restricted to the target item and relevant properties, use its base hash, dry-run the targeted patch, apply it with backup enabled only if preflight is safe, then obtain a fresh focused snapshot and verify the exact result.
