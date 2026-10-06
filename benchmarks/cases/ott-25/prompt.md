TIA Portal is running with a freshly restored disposable `OTT_Benchmark` project.

Modernize the existing ET 200MP remote-I/O station `LegacyCell` without recreating or renaming the station, changing its subnet or IO-system connections, or modifying any PLC tag, block, HMI object, or other device.

First inspect the hardware tree, PROFINET topology, and process-image address map. Establish the exact existing module identities, paths, positions, address types, lengths, start addresses, and PROFINET identity. Confirm that input bytes 104-105 and output bytes 160-161 are free.

Replace the existing output module `LegacyOutputs` in position 4 with a new module named `ModernOutputs`. Use a focused hardware-catalog search for article number `6ES7 522-1BH01-0AB0` and select the highest installed firmware version for that exact article number. Move the retained `LegacyInputs` input start from its observed byte 72 to byte 104. Assign the new `ModernOutputs` output start to byte 160. Change the station's PROFINET identity from its observed baseline to IP address `192.168.0.67`, station name `packaging-cell`, and device number `67`.

Keep deletion of the old output module, the guarded retained-input move, the guarded PROFINET identity change, the replacement plug, and the generated replacement-output address assignment in one complete transactional hardware batch. Guard every pre-existing target or value with the identity/state you actually observed. Bind the new output address through the replacement plug's result and its actual generated IO-address child; do not guess an absolute path or an expected start for an object that does not exist before the batch.

Treat the batch as destructive. Before opting into displaced placement, run one strict dry-run against the discovered rail parent. If TIA reports that it legally placed the replacement elsewhere, accept only that exact reported displacement, preserve the complete operation set and result-reference chain, and preview the corrected batch. Apply that same corrected batch exactly once, with both mutation and destructive confirmation, only after its preview succeeds.

After applying, independently reread `LegacyCell`'s hardware tree, network topology, and IO address objects, then compile `PLC_Main`. Do not save the project.
