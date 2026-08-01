TIA Portal is running with a freshly restored disposable `OTT_Benchmark` project.

Provision one new ET 200SP remote-I/O station without changing or renaming any existing device, subnet, IO system, module, tag, block, or HMI object.

First inspect the existing hardware tree and PROFINET topology. Reuse the existing Ethernet subnet and controller IO system; do not create replacement network infrastructure.

Use focused hardware-catalog searches to resolve the installed type identifiers for these requested article numbers:

- interface module `6ES7 155-6AU02-0BN0`; use the highest installed firmware version for this exact article number;
- digital input module `6ES7 131-6BF01-0BA0`;
- digital output module `6ES7 132-6BF01-0BA0`.

Create the station and its primary item as `RemoteCell`. Populate its rack with `GuardInputs` in position 1 and `ValveOutputs` in position 2. Connect the new station to the existing subnet and IO system. Configure its PROFINET interface with IP address `192.168.0.37`, station name `remote-cell`, and device number `37`.

Use one complete transactional hardware batch for station creation, both module plugs, both network connections, and PROFINET identity. Use result references for objects created earlier in that batch. Before opting into displaced placement, run a strict dry-run against the discovered rack parent. If TIA reports that it legally placed a module elsewhere, accept that exact reported placement only for the affected plug operations, retain result references, and preview the complete corrected batch. Apply that same corrected batch exactly once only after its preview succeeds.

After applying, independently reread the new station's hardware tree and the project network topology, then compile `PLC_Main`. Do not save the project and do not assign or relocate process-image addresses.
