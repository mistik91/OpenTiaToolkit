TIA Portal is running with a freshly restored disposable `OTT_Benchmark` project.

Provision one new ET 200SP remote-I/O station without changing or renaming any existing device, subnet, IO system, module, tag, block, or HMI object.

First inspect the existing hardware tree, PROFINET topology, and process-image address map. Reuse the existing Ethernet subnet and controller IO system; do not create replacement network infrastructure. Confirm that input bytes 72-73 and output bytes 140-141 are free.

Use focused hardware-catalog searches to resolve the installed type identifiers for these requested article numbers:

- interface module `6ES7 155-6AU02-0BN0`; use the highest installed firmware version for this exact article number;
- digital input module `6ES7 131-6BH01-0BA0`;
- digital output module `6ES7 132-6BH01-0BA0`.

Create the station and its primary item as `PackagingBay`. Populate it with `Photoeyes` in position 3 and `Pneumatics` in position 5. Assign the `Photoeyes` input start to byte 72 and the `Pneumatics` output start to byte 140. Connect the new station to the existing subnet and IO system. Configure its PROFINET interface with IP address `192.168.0.48`, station name `packaging-bay`, and device number `48`.

Keep station creation, both module plugs, both process-image assignments, both network connections, and PROFINET identity in one complete transactional hardware batch. Use result references for every dependent object. For each address assignment, bind through its plug result and the actual generated IO-address child; do not guess an absolute project path.

Before opting into displaced placement, run a strict dry-run against the discovered rack parent. If TIA reports that it legally placed a module elsewhere, accept that exact reported placement only for the affected plug operation, retain the result-reference chain, and preview the complete corrected batch. Apply that same corrected batch exactly once only after its preview succeeds.

After applying, independently reread the new station's hardware tree, network topology, and IO address objects, then compile `PLC_Main`. Do not save the project, modify PLC tags, or relocate any existing address object.
