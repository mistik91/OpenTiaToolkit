TIA Portal is running with a freshly restored disposable `OTT_Benchmark` project.

Provision one IFM AL1400 PROFINET IO-Link master without changing or renaming any existing device, subnet, IO system, module, address object, PLC tag, block, or HMI object.

First inspect the existing hardware tree, PROFINET topology, and process-image address map. Reuse the existing Ethernet subnet `CellPN` and controller IO system `CellIO`; do not create replacement network infrastructure. Confirm that input bytes 192-229 and output bytes 192-194 are free.

Run one focused hardware-catalog search for `AL1400` and one for `IO-Link 4I`. Resolve the installed AL1400 DAP plus the `IO-Link 4I_2O` and `IO-Link 4I` submodule type identifiers from the same current GSDML package. The second search contains legacy-package and no-PQI alternatives; do not mix versions or choose the no-PQI variants. The generated `4 Ports_1` module does not exist until the DAP is created inside the transaction, so do not call `can-plug-hardware-item` against that future parent and do not create a temporary station as a probe.

Create the station and its primary item as `IOL_Master_01`. Preserve the DAP-generated `4 Ports_1` module and `IO-Link Master Proxy 4P EnMo` submodule. Add the mixed-input/output submodule as `FlowPort` at toolkit position 2 and the input-only submodule as `LevelPort` at toolkit position 3, both requested beneath `4 Ports_1`.

Connect the generated `PN-IO` interface node index 0 to `CellPN` and IO connector index 0 to `CellIO`. Configure IP address `192.168.0.62`, station name `iol-master-01`, and device number `62`.

Assign the generated proxy input and output address objects (indexes 0 and 1) to bytes 192 and 192. Assign `FlowPort` input and output address objects (indexes 0 and 1) to bytes 220 and 193. Assign `LevelPort` input address index 0 to byte 225. Bind the two added port addresses through their plug result references, and bind the generated proxy addresses through the station result plus its discovered relative child path. Do not add expected-start guards to address objects created in this transaction.

The installed vendor contract defines the port options byte in GSD parameter dataset 47360 at byte offset 3. On `FlowPort`, the creation default `17` enables pull/plug diagnostics; write `07` to disable that option while preserving the other enabled bits. Guard the write with expected data `17` and bind it through the `FlowPort` plug result.

Keep station creation, both port plugs, both network connections, PROFINET identity, the guarded vendor-record change, and all five process-image assignments in one complete transactional hardware batch. Use result references for every dependent object.

Preview the complete batch first with neither plug opted into displaced placement. If TIA reports a different actual parent for `FlowPort`, accept only that reported displacement for that plug and retry the complete batch while leaving `LevelPort` strict. If TIA then reports a different actual parent for `LevelPort`, accept only that reported displacement and preview the complete corrected batch once. Never pre-enable displacement for an unprobed sibling. Apply that same corrected batch exactly once only after its complete preview succeeds.

After applying, independently reread `IOL_Master_01`'s hardware tree, PROFINET topology, and IO address objects. Reread dataset 47360 at offset 3 for length 1 on `FlowPort` and verify `07`, then compile `PLC_Main`. Do not save the project.
