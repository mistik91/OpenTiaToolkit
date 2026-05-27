using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands;
using OpenTiaToolkit.Core.Commands.Hardware;
using OpenTiaToolkit.Core.Models;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Tests.Commands
{
    [TestClass]
    public class HardwareCommandsTests
    {
        [TestMethod]
        public async Task ApplyHardwarePatch_DefaultsToDryRunAndParsesNestedTarget()
        {
            var service = new Mock<IHardwareService>();
            service.Setup(candidate => candidate.ApplyPatchAsync(
                    It.IsAny<IReadOnlyList<HardwarePatchOperation>>(),
                    true,
                    false,
                    null))
                .ReturnsAsync(new HardwarePatchResult { DryRun = true, RolledBack = true });
            var command = new ApplyHardwarePatchCommand(service.Object);

            var operations = JsonDocument.Parse(
                "[{\"op\":\"set-attributes\",\"target\":{\"deviceName\":\"Device_1\",\"itemPath\":[\"Module_1\"]},\"attributes\":{\"Name\":\"NewName\"}}]").RootElement.Clone();
            var result = await command.ExecuteAsync(Args(("operations", operations)), NullLogger.Instance);

            Assert.IsTrue(result.Success);
            service.Verify(candidate => candidate.ApplyPatchAsync(
                It.Is<IReadOnlyList<HardwarePatchOperation>>(values =>
                    values.Count == 1 &&
                    values[0].Target != null &&
                    values[0].Target!.DeviceName == "Device_1" &&
                    values[0].Target!.ItemPath[0] == "Module_1"),
                true,
                false,
                null), Times.Once);
        }

        [TestMethod]
        public async Task ApplyHardwarePatch_PreservesBlockedResultAsFailureData()
        {
            var service = new Mock<IHardwareService>();
            service.Setup(candidate => candidate.ApplyPatchAsync(
                    It.IsAny<IReadOnlyList<HardwarePatchOperation>>(),
                    false,
                    false,
                    "test"))
                .ReturnsAsync(new HardwarePatchResult { SafetyBlocked = true, SafetyMessage = "Cannot plug module." });
            var command = new ApplyHardwarePatchCommand(service.Object);
            var operations = JsonDocument.Parse("[{\"op\":\"plug-item\"}]").RootElement.Clone();

            var result = await command.ExecuteAsync(Args(
                ("operations", operations), ("dry-run", false), ("transaction-description", "test")), NullLogger.Instance);

            Assert.IsFalse(result.Success);
            Assert.IsInstanceOfType(result.Data, typeof(HardwarePatchResult));
        }

        [TestMethod]
        public async Task ApplyHardwarePatch_ParsesGuardedWriteFields()
        {
            var service = new Mock<IHardwareService>();
            service.Setup(candidate => candidate.ApplyPatchAsync(
                    It.IsAny<IReadOnlyList<HardwarePatchOperation>>(),
                    true,
                    false,
                    null))
                .ReturnsAsync(new HardwarePatchResult { DryRun = true, RolledBack = true });
            var command = new ApplyHardwarePatchCommand(service.Object);

            var operations = JsonDocument.Parse(
                "[{\"op\":\"set-gsd-parameter-data\",\"target\":{\"deviceName\":\"Master\"},\"datasetNumber\":47360,\"dataHex\":\"01 02\",\"expectedDataHex\":\"00 00\"},{\"op\":\"set-network-item-attributes\",\"target\":{\"deviceName\":\"Master\"},\"networkCollection\":\"Nodes\",\"networkItemIndex\":0,\"attributes\":{\"Address\":\"192.168.1.20\"},\"expectedAttributes\":{\"Address\":\"192.168.1.10\"}}]").RootElement.Clone();

            var result = await command.ExecuteAsync(Args(("operations", operations)), NullLogger.Instance);

            Assert.IsTrue(result.Success);
            service.Verify(candidate => candidate.ApplyPatchAsync(
                It.Is<IReadOnlyList<HardwarePatchOperation>>(values =>
                    values.Count == 2 &&
                    values[0].ExpectedDataHex == "00 00" &&
                    values[1].ExpectedAttributes != null &&
                    values[1].NetworkCollection == "Nodes" &&
                    values[1].NetworkItemIndex == 0 &&
                    values[1].ExpectedAttributes!.ContainsKey("Address")),
                true,
                false,
                null), Times.Once);
        }

        [TestMethod]
        public async Task ApplyHardwarePatch_ParsesProfinetIoAndDriveMutationFields()
        {
            var service = new Mock<IHardwareService>();
            service.Setup(candidate => candidate.ApplyPatchAsync(
                    It.IsAny<IReadOnlyList<HardwarePatchOperation>>(),
                    true,
                    false,
                    null))
                .ReturnsAsync(new HardwarePatchResult { DryRun = true, RolledBack = true });
            var command = new ApplyHardwarePatchCommand(service.Object);
            var operations = JsonDocument.Parse(
                "[{\"op\":\"configure-profinet-device\",\"target\":{\"deviceName\":\"AL1403\",\"itemPath\":[\"DAP\",\"PN-IO\"]},\"ipAddress\":\"192.168.101.231\",\"expectedIpAddress\":\"192.168.101.230\",\"pnDeviceName\":\"al1403-231\",\"pnDeviceNumber\":2,\"expectedPnDeviceNumber\":1},{\"op\":\"set-io-address-start\",\"target\":{\"deviceName\":\"AL1403\",\"itemPath\":[\"Proxy\"]},\"addressIndex\":1,\"startAddress\":80,\"expectedStartAddress\":79},{\"op\":\"change-drive-telegram-size\",\"target\":{\"deviceName\":\"G_1\",\"itemPath\":[\"CU\"]},\"driveObjectIndex\":0,\"telegramType\":\"MainTelegram\",\"ioType\":\"Input\",\"sizeInWords\":7,\"expectedSizeInWords\":6,\"keepOriginalAddress\":false}]").RootElement.Clone();

            var result = await command.ExecuteAsync(Args(("operations", operations)), NullLogger.Instance);

            Assert.IsTrue(result.Success);
            service.Verify(candidate => candidate.ApplyPatchAsync(
                It.Is<IReadOnlyList<HardwarePatchOperation>>(values =>
                    values.Count == 3 &&
                    values[0].PnDeviceName == "al1403-231" &&
                    values[0].PnDeviceNumber == 2 &&
                    values[0].ExpectedIpAddress == "192.168.101.230" &&
                    values[1].AddressIndex == 1 &&
                    values[1].ExpectedStartAddress == 79 &&
                    values[2].TelegramType == "MainTelegram" &&
                    values[2].SizeInWords == 7 &&
                    values[2].ExpectedSizeInWords == 6 &&
                    !values[2].KeepOriginalAddress),
                true,
                false,
                null), Times.Once);
        }

        [TestMethod]
        public async Task FindHardwareModuleTypes_UsesProjectProviderWithoutTarget()
        {
            var service = new Mock<IHardwareService>();
            service.Setup(candidate => candidate.FindModuleTypesAsync("GSD:IFM/M"))
                .ReturnsAsync(new[] { "GSD:IFM/M/IOLM" });
            var command = new FindHardwareModuleTypesCommand(service.Object);

            var result = await command.ExecuteAsync(Args(("partial-type-identifier", "GSD:IFM/M")), NullLogger.Instance);

            Assert.IsTrue(result.Success);
            service.VerifyAll();
        }

        [TestMethod]
        public async Task GetHardwareIoAddresses_PassesDeviceFilterAndAttributes()
        {
            var service = new Mock<IHardwareService>();
            service.Setup(candidate => candidate.GetIoAddressesAsync(
                    "AL1403_231",
                    It.Is<string[]>(names => names.Length == 2 && names[0] == "StartAddress")))
                .ReturnsAsync(new[] { new HardwareIoAddressInfo { Index = 0 } });
            var command = new GetHardwareIoAddressesCommand(service.Object);

            var result = await command.ExecuteAsync(Args(
                ("device-name", "AL1403_231"), ("attributes", new[] { "StartAddress", "IoType" })), NullLogger.Instance);

            Assert.IsTrue(result.Success);
            service.VerifyAll();
        }

        [TestMethod]
        public async Task GetHardwareDriveTelegrams_PassesDriveFilterAndAddressAttributes()
        {
            var service = new Mock<IHardwareService>();
            service.Setup(candidate => candidate.GetDriveTelegramsAsync(
                    "SINAMICS G_1",
                    It.Is<string[]>(names => names.Length == 3 && names[0] == "StartAddress")))
                .ReturnsAsync(new[] { new HardwareDriveTelegramInfo { TelegramNumber = "1" } });
            var command = new GetHardwareDriveTelegramsCommand(service.Object);

            var result = await command.ExecuteAsync(Args(
                ("device-name", "SINAMICS G_1"), ("address-attributes", new[] { "StartAddress", "IoType", "Length" })), NullLogger.Instance);

            Assert.IsTrue(result.Success);
            service.VerifyAll();
        }

        [TestMethod]
        public async Task GetGsdParameterData_ParsesTargetAndRange()
        {
            var service = new Mock<IHardwareService>();
            service.Setup(candidate => candidate.GetGsdParameterDataAsync(
                    It.Is<HardwareObjectRef>(target => target.DeviceName == "Master"),
                    47360,
                    0,
                    16))
                .ReturnsAsync(new HardwareGsdParameterData { Length = 16, DataHex = "00 01" });
            var command = new GetGsdParameterDataCommand(service.Object);
            var target = JsonDocument.Parse("{\"deviceName\":\"Master\"}").RootElement.Clone();

            var result = await command.ExecuteAsync(Args(
                ("target", target), ("dataset-number", 47360), ("length", 16)), NullLogger.Instance);

            Assert.IsTrue(result.Success);
            service.VerifyAll();
        }

        [TestMethod]
        public async Task GetHardwareFeatureAttributes_ParsesNetworkInterfaceRequest()
        {
            var service = new Mock<IHardwareService>();
            service.Setup(candidate => candidate.GetFeatureAttributesAsync(
                    It.Is<HardwareObjectRef>(target => target.DeviceName == "Master" && target.ItemPath[0] == "PN-IO"),
                    "NetworkInterface",
                    It.Is<string[]>(names => names.Length == 1 && names[0] == "InterfaceOperatingMode")))
                .ReturnsAsync(new HardwareFeatureAttributesInfo { FeatureService = "NetworkInterface" });
            var command = new GetHardwareFeatureAttributesCommand(service.Object);

            var result = await command.ExecuteAsync(Args(
                ("device-name", "Master"), ("item-path", "PN-IO"),
                ("feature-service", "NetworkInterface"), ("attributes", new[] { "InterfaceOperatingMode" })), NullLogger.Instance);

            Assert.IsTrue(result.Success);
            service.VerifyAll();
        }

        [TestMethod]
        public async Task GetHardwareNetworkItemAttributes_ParsesNodeAddressRequest()
        {
            var service = new Mock<IHardwareService>();
            service.Setup(candidate => candidate.GetNetworkItemAttributesAsync(
                    It.Is<HardwareObjectRef>(target => target.DeviceName == "Master" && target.ItemPath[0] == "PN-IO"),
                    "Nodes",
                    0,
                    It.Is<string[]>(names => names.Length == 1 && names[0] == "Address")))
                .ReturnsAsync(new HardwareNetworkItemAttributesInfo { CollectionName = "Nodes", Index = 0 });
            var command = new GetHardwareNetworkItemAttributesCommand(service.Object);

            var result = await command.ExecuteAsync(Args(
                ("device-name", "Master"), ("item-path", "PN-IO"),
                ("network-collection", "Nodes"), ("attributes", new[] { "Address" })), NullLogger.Instance);

            Assert.IsTrue(result.Success);
            service.VerifyAll();
        }

        [TestMethod]
        public async Task GetGsdParameterData_AcceptsCliJsonStringTarget()
        {
            var service = new Mock<IHardwareService>();
            service.Setup(candidate => candidate.GetGsdParameterDataAsync(
                    It.Is<HardwareObjectRef>(target => target.DeviceName == "Master" && target.ItemPath[0] == "Port_1"),
                    1,
                    0,
                    2))
                .ReturnsAsync(new HardwareGsdParameterData { Length = 2 });
            var command = new GetGsdParameterDataCommand(service.Object);

            var result = await command.ExecuteAsync(Args(
                ("target", "{\"deviceName\":\"Master\",\"itemPath\":[\"Port_1\"]}"),
                ("dataset-number", 1), ("length", 2)), NullLogger.Instance);

            Assert.IsTrue(result.Success);
            service.VerifyAll();
        }

        [TestMethod]
        public async Task GetGsdParameterData_AcceptsCliSlashDelimitedItemPath()
        {
            var service = new Mock<IHardwareService>();
            service.Setup(candidate => candidate.GetGsdParameterDataAsync(
                    It.Is<HardwareObjectRef>(target =>
                        target.DeviceName == "Master" &&
                        target.ItemPath.Length == 2 &&
                        target.ItemPath[1] == "Port_1"),
                    1,
                    0,
                    2))
                .ReturnsAsync(new HardwareGsdParameterData { Length = 2 });
            var command = new GetGsdParameterDataCommand(service.Object);

            var result = await command.ExecuteAsync(Args(
                ("device-name", "Master"), ("item-path", "Module/Port_1"),
                ("dataset-number", 1), ("length", 2)), NullLogger.Instance);

            Assert.IsTrue(result.Success);
            service.VerifyAll();
        }

        private static CommandArgs Args(params (string Name, object Value)[] entries)
        {
            var values = new Dictionary<string, object?>();
            foreach (var entry in entries)
            {
                values[entry.Name] = entry.Value;
            }
            return new CommandArgs(values);
        }
    }
}
