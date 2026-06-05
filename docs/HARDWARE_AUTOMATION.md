# Generic Hardware Automation

OpenTiaToolkit exposes vendor-neutral TIA Portal hardware primitives. A device-specific agent skill can translate a manufacturer's GSDML or setup guide into these operations without adding product knowledge to the core toolkit.

## Tools

| Tool | Purpose |
|------|---------|
| `find-hardware-catalog-items` | Searches the installed TIA hardware catalog and returns usable type identifiers. |
| `get-hardware-tree` | Lists devices and nested device items with stable path-based targets. |
| `get-hardware-io-addresses` | Lists process-image address objects exposed by device items. |
| `get-hardware-drive-telegrams` | Lists Startdrive telegrams and their IO address assignments. |
| `get-hardware-item-capabilities` | Identifies network and GSD capabilities and optionally reads attributes. |
| `get-hardware-feature-attributes` | Reads attributes exposed by a feature service such as `NetworkInterface`. |
| `get-hardware-network-item-attributes` | Reads attributes from a network child, such as a PROFINET node. |
| `find-hardware-module-types` | Queries module identifiers exposed by the project-level `ModuleInformationProvider`. |
| `can-plug-hardware-item` | Validates a candidate module/submodule placement without changing the project. |
| `get-hardware-networks` | Lists nodes, IO controllers, IO connectors and connected IO systems. |
| `get-gsd-parameter-data` | Reads raw GSD/GSDML parameter record bytes. |
| `apply-hardware-patch` | Dry-runs or applies a transactional sequence of generic hardware edits. |

## Siemens Openness Mapping

This surface follows the hardware API model documented in the installed Siemens TIA Portal Openness V21 help:

| Toolkit behavior | Siemens Openness API |
|------------------|----------------------|
| Instantiate a catalog device or GSDML DAP | `project.Devices.CreateWithItem(typeIdentifier, itemName, deviceName)` |
| Test and plug modules or submodules | `HardwareObject.CanPlugNew(...)` and `HardwareObject.PlugNew(...)` |
| Resolve available module type identifiers | `project.HwUtilities.Find("ModuleInformationProvider")` and `FindModuleTypes(...)` |
| Inspect or relocate process-image IO assignment | `DeviceItem.Addresses` and writable `Address.StartAddress` |
| Inspect or configure native SINAMICS telegram IO | `DeviceItem.GetService<DriveObjectContainer>()`, `DriveObjects`, `Telegrams`, telegram `Addresses`, and Startdrive telegram mutation methods |
| Read or write GSD/GSDML record data | `DeviceItem.GetService<GsdDeviceItem>()`, `GetPrmData(...)` and `SetPrmData(...)` |
| Join a PROFINET subnet and IO system | `DeviceItem.GetService<NetworkInterface>()`, `Node.ConnectToSubnet(...)` and `IoConnector.ConnectToIoSystem(...)` |
| Configure PROFINET IP address and station name | `DeviceItem.GetService<NetworkInterface>().Nodes[...]` and node `GetAttribute(...)` / `SetAttributes(...)` methods |
| Configure PROFINET IO device number | `DeviceItem.GetService<NetworkInterface>().IoConnectors[...]` and connector `PnDeviceNumber` |

The item exposing `NetworkInterface` is catalog-defined and must be discovered from the hardware topology or capabilities; it cannot be assumed from a display name. In V21, IP settings and PROFINET station names (`Address`, `SubnetMask`, `PnDeviceName`) are owned by `NetworkInterface.Nodes`, while `PnDeviceNumber` is owned by `NetworkInterface.IoConnectors`. The `configure-profinet-device` operation updates these together and disables automatic station-name generation when `pnDeviceName` is provided. The TIA engineering display attribute `Name` remains a generic guarded `set-attributes` edit and is distinct from the PROFINET station name. Native Startdrive devices may decline optional metadata reads such as `DriveObjectNumber`; telegram number, type, and IO addresses remain the reliable report fields. Startdrive telegram insertion and erasure remain device- and project-capability dependent: the adapter checks `CanInsertTelegram(...)`, and TIA may reject deletion or require safety-program authorization. Likewise, record byte meaning is supplied by the GSDML/device documentation and belongs in a device skill, not this generic layer.

## Hardware Targets

Hardware operations address a device and nested item names:

```json
{
  "deviceName": "IO_Master_01",
  "itemPath": ["8 Ports", "Port_1"],
  "expectedTypeIdentifier": "GSD:..."
}
```

`itemPath` is an array because item names originate in hardware catalogs and may contain punctuation. Include `expectedTypeIdentifier` on write operations when available to prevent writing to an item that has been replaced.

For CLI reads, `--device-name` and slash-delimited `--item-path` are convenient alternatives, for example:

```powershell
get-hardware-item-capabilities --device-name="PLC_1" --item-path="CPU/PROFINET interface_1"
get-hardware-io-addresses --device-name="AL1403_231" --attributes="StartAddress,IoType,Length"
get-hardware-drive-telegrams --device-name="SINAMICS G_1" --address-attributes="StartAddress,IoType,Length"
get-hardware-network-item-attributes --device-name="IO_Master_01" --item-path="DAP/PN-IO" --network-collection="Nodes" --network-item-index=0 --attributes="Address,SubnetMask,IpProtocolSelection"
```

## Transactional Patches

`apply-hardware-patch` defaults to `dry-run=true`. All operations run inside a Siemens Openness transaction:

- A dry run performs validation and mutations in TIA, then disposes the transaction without committing it.
- An apply run commits only if every operation succeeds.
- `delete-device`, `delete-item`, `disconnect-node`, `disconnect-io-connector`, and `erase-drive-telegram` require `confirm-destructive=true`.
- Use `plc-name` and `safety-password` on `apply-hardware-patch` when a dry-run or apply mutates fail-safe drive/project data; dry-runs still execute Siemens mutations inside a rolled-back transaction and may require safety authorization.
- Saving the project remains a separate explicit action.

Supported operations:

| Operation | Required fields |
|-----------|-----------------|
| `create-device` | `typeIdentifier`, `deviceName` |
| `create-device-with-item` | `typeIdentifier`, `deviceName`, `itemName` |
| `plug-item` | `target` or `targetRef`, `typeIdentifier`, `itemName`, `positionNumber` |
| `set-attributes` | `target` or `targetRef`, `attributes` |
| `get-feature-attributes` | `target` or `targetRef`, `featureService`, `attributeNames` |
| `set-feature-attributes` | `target` or `targetRef`, `featureService`, `attributes` |
| `get-network-item-attributes` | `target` or `targetRef`, `networkCollection`, `networkItemIndex`, `attributeNames` |
| `set-network-item-attributes` | `target` or `targetRef`, `networkCollection`, `networkItemIndex`, `attributes` |
| `configure-profinet-device` | `target` or `targetRef`; one or more of `ipAddress`, `subnetMask`, `pnDeviceName`, `pnDeviceNumber` |
| `set-io-address-start` | device-item `target` or `targetRef`, `addressIndex`, `startAddress` |
| `change-drive-telegram` | Startdrive device-item `target` or `targetRef`, `telegramType`, `telegramNumber` |
| `configure-drive-telegram` | Startdrive device-item `target` or `targetRef`, `telegramType`, `telegramNumber`; one or both of `inputStartAddress`, `outputStartAddress` |
| `set-drive-telegram-address` | Startdrive device-item `target` or `targetRef`, `telegramType`, `ioType`, `startAddress` |
| `change-drive-telegram-size` | Startdrive device-item `target` or `targetRef`, `telegramType`, `ioType`, `sizeInWords` |
| `insert-drive-telegram` | Startdrive device-item `target` or `targetRef`, `telegramType`, `telegramNumber` |
| `erase-drive-telegram` | Startdrive device-item `target` or `targetRef`, `telegramType` |
| `get-gsd-parameter-data` | `target` or `targetRef`, `datasetNumber`, `byteOffset`, `length` |
| `set-gsd-parameter-data` | `target` or `targetRef`, `datasetNumber`, `byteOffset`, `dataHex` |
| `create-subnet` | `typeIdentifier`, `subnetName` |
| `connect-node-to-subnet` | `target` or `targetRef`, `nodeIndex`, `subnetName` |
| `disconnect-node` | `target` or `targetRef`, `nodeIndex` |
| `create-io-system` | `target` or `targetRef`, `controllerIndex`, `ioSystemName` |
| `connect-io-connector` | `target` or `targetRef`, `connectorIndex`, `ioSystemName` |
| `disconnect-io-connector` | `target` or `targetRef`, `connectorIndex` |
| `delete-item` | `target` or `targetRef` |
| `delete-device` | `target` or `targetRef` |

`resultId` can name a created device or plugged item so later operations in the same transaction can use `targetRef`. When an operation needs a child of a referenced item, combine `targetRef` with a relative `target.itemPath`, for example `{ "targetRef": "drive", "target": { "itemPath": ["PROFINET interface"] } }`.
For guarded writes, `set-attributes`, `set-feature-attributes`, and `set-network-item-attributes` accept `expectedAttributes`, and `set-gsd-parameter-data` accepts `expectedDataHex`. `configure-profinet-device` accepts `expectedIpAddress`, `expectedSubnetMask`, `expectedPnDeviceName`, and `expectedPnDeviceNumber`; IO-address operations accept `expectedStartAddress`; Startdrive telegram number and size changes accept `expectedTelegramNumber` and `expectedSizeInWords`. `configure-drive-telegram` also accepts `expectedInputStartAddress` and `expectedOutputStartAddress`; it changes or inserts the requested telegram, verifies that Startdrive exposes the required input/output address objects, then assigns input/output starts. Startdrive telegram addresses are only exposed after the drive is connected to the relevant subnet and IO system; connect new PROFINET drives with `connect-node-to-subnet` and `connect-io-connector` before assigning telegram starts. The patch fails before writing if a current value does not match. Startdrive size values are in words, matching the Siemens API. Successful write operation results include prior and written values.
The CLI also accepts `--operations-path=<file.json>` for patches; MCP callers normally send `operations` directly as structured JSON.

Example:

```json
{
  "operations": [
    {
      "op": "create-device",
      "resultId": "device",
      "typeIdentifier": "GSD:...",
      "deviceName": "IO_Master_01"
    },
    {
      "op": "plug-item",
      "targetRef": "device",
      "typeIdentifier": "GSD:...",
      "itemName": "Port module",
      "positionNumber": 1
    }
  ],
  "dry-run": true
}
```

Example PROFINET and GSDML process-image edit:

```json
{
  "operations": [
    {
      "op": "configure-profinet-device",
      "target": { "deviceName": "AL1403_231", "itemPath": ["AL1403_231_DAP", "PN-IO"] },
      "ipAddress": "192.168.101.231",
      "expectedIpAddress": "192.168.101.231",
      "pnDeviceName": "al1403-231",
      "pnDeviceNumber": 2
    },
    {
      "op": "set-io-address-start",
      "target": { "deviceName": "AL1403_231", "itemPath": ["IO-Link Master Proxy 8P EnMo"] },
      "addressIndex": 0,
      "startAddress": 79,
      "expectedStartAddress": 79
    }
  ],
  "dry-run": true
}
```

## Device Skills

The core layer intentionally does not know product-specific record meanings. For example, an IFM AL1403 skill can:

1. Find the installed GSDML catalog identifiers.
2. Map an IO-Link sensor process image requirement to a catalog submodule.
3. Encode documented AL1403 parameter bytes.
4. Submit generic dry-run and apply patches.

This keeps vendor versions and GSDML-specific layouts in the skill while the toolkit provides reusable TIA engineering operations.
