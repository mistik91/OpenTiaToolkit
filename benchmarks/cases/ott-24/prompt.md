TIA Portal is running with a freshly restored disposable `OTT_Benchmark` project.

Provision one new ET 200MP remote-I/O station without changing or renaming any existing device, subnet, IO system, module, tag, block, or HMI object.

First inspect the existing hardware tree, PROFINET topology, and process-image address map. Reuse the existing Ethernet subnet and controller IO system; do not create replacement network infrastructure. Confirm that input bytes 96-97 and output bytes 152-153 are free.

Use focused hardware-catalog searches to resolve the installed type identifiers for these requested article numbers:

- interface module `6ES7 155-5AA01-0AB0`; use the highest installed firmware version for this exact article number;
- digital input module `6ES7 521-1BH00-0AB0`; use the highest installed firmware version for this exact article number;
- digital output module `6ES7 522-1BH01-0AB0`; use the highest installed firmware version for this exact article number.

Create the station and its primary item as `QualityCell`. Populate it with `InspectionInputs` in position 2 and `RejectOutputs` in position 4. Assign the `InspectionInputs` input start to byte 96 and the `RejectOutputs` output start to byte 152. Connect the new station to the existing subnet and IO system. Configure its PROFINET interface with IP address `192.168.0.56`, station name `quality-cell`, and device number `56`.

Keep station creation, both module plugs, both process-image assignments, both network connections, and PROFINET identity in one complete transactional hardware batch. Use result references for every dependent object. For each address assignment, bind through its plug result and the actual generated IO-address child; do not guess an absolute project path or an unobserved current value.

Before opting into displaced placement, run a strict dry-run against the discovered rail parent. If TIA reports that it legally placed a module elsewhere, accept that exact reported placement only for the affected plug operation, retain the result-reference chain, and preview the complete corrected batch. Apply that same corrected batch exactly once only after its preview succeeds.

After applying, independently reread the new station's hardware tree, network topology, and IO address objects, then compile `PLC_Main`. Do not save the project, modify PLC tags, or relocate any existing address object.
