using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands;
using OpenTiaToolkit.Core.Commands.Hardware;
using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Services;
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
        public async Task ApplyHardwarePatch_ParsesConfigureDriveTelegramFields()
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
                "[{\"op\":\"configure-drive-telegram\",\"targetRef\":\"drive\",\"driveObjectIndex\":0,\"telegramType\":\"MainTelegram\",\"telegramNumber\":352,\"expectedTelegramNumber\":1,\"inputStartAddress\":1040,\"expectedInputStartAddress\":1000,\"outputStartAddress\":1040,\"expectedOutputStartAddress\":1000}]").RootElement.Clone();

            var result = await command.ExecuteAsync(Args(("operations", operations)), NullLogger.Instance);

            Assert.IsTrue(result.Success);
            service.Verify(candidate => candidate.ApplyPatchAsync(
                It.Is<IReadOnlyList<HardwarePatchOperation>>(values =>
                    values.Count == 1 &&
                    values[0].Op == "configure-drive-telegram" &&
                    values[0].TelegramType == "MainTelegram" &&
                    values[0].TelegramNumber == 352 &&
                    values[0].ExpectedTelegramNumber == 1 &&
                    values[0].InputStartAddress == 1040 &&
                    values[0].ExpectedInputStartAddress == 1000 &&
                    values[0].OutputStartAddress == 1040 &&
                    values[0].ExpectedOutputStartAddress == 1000),
                true,
                false,
                null), Times.Once);
        }

        [TestMethod]
        public async Task ApplyHardwarePatch_ParsesTargetRefWithRelativeItemPath()
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
                "[{\"op\":\"connect-node-to-subnet\",\"targetRef\":\"drive\",\"target\":{\"itemPath\":[\"PROFINET interface\"]},\"subnetName\":\"PN/IE_1\"}]").RootElement.Clone();

            var result = await command.ExecuteAsync(Args(("operations", operations)), NullLogger.Instance);

            Assert.IsTrue(result.Success);
            service.Verify(candidate => candidate.ApplyPatchAsync(
                It.Is<IReadOnlyList<HardwarePatchOperation>>(values =>
                    values.Count == 1 &&
                    values[0].TargetRef == "drive" &&
                    HasSingleItemPath(values[0], "PROFINET interface")),
                true,
                false,
                null), Times.Once);
        }

        [TestMethod]
        public async Task ApplyHardwarePatch_LogsIntoSafetyProgramWhenPasswordProvided()
        {
            var service = new Mock<IHardwareService>();
            service.Setup(candidate => candidate.ApplyPatchAsync(
                    It.IsAny<IReadOnlyList<HardwarePatchOperation>>(),
                    true,
                    false,
                    null))
                .ReturnsAsync(new HardwarePatchResult { DryRun = true, RolledBack = true });
            var plcService = new Mock<IPlcService>();
            plcService.SetupGet(candidate => candidate.IsSoftwareOpen).Returns(false);
            plcService.Setup(candidate => candidate.OpenPlcAsync("PLC_1")).ReturnsAsync(true);
            plcService.Setup(candidate => candidate.LoginToSafetyOfflineProgramAsync("A1"))
                .ReturnsAsync(new SafetyLoginResult(
                    success: true,
                    serviceAvailable: true,
                    passwordRequired: true,
                    alreadyLoggedIn: false,
                    message: "Logged into the safety offline program."));
            var command = new ApplyHardwarePatchCommand(service.Object, plcService.Object);
            var operations = JsonDocument.Parse("[{\"op\":\"create-device\",\"typeIdentifier\":\"System:Device.G120C-2\",\"deviceName\":\"G_3\"}]").RootElement.Clone();

            var result = await command.ExecuteAsync(Args(
                ("operations", operations),
                ("plc-name", "PLC_1"),
                ("safety-password", "A1")), NullLogger.Instance);

            Assert.IsTrue(result.Success);
            plcService.Verify(candidate => candidate.OpenPlcAsync("PLC_1"), Times.Once);
            plcService.Verify(candidate => candidate.LoginToSafetyOfflineProgramAsync("A1"), Times.Once);
            service.Verify(candidate => candidate.ApplyPatchAsync(
                It.IsAny<IReadOnlyList<HardwarePatchOperation>>(),
                true,
                false,
                null), Times.Once);
        }

        [TestMethod]
        public async Task ApplyHardwarePatch_RequiresPlcNameWithSafetyPassword()
        {
            var service = new Mock<IHardwareService>();
            var plcService = new Mock<IPlcService>();
            var command = new ApplyHardwarePatchCommand(service.Object, plcService.Object);
            var operations = JsonDocument.Parse("[{\"op\":\"create-device\",\"typeIdentifier\":\"System:Device.G120C-2\",\"deviceName\":\"G_3\"}]").RootElement.Clone();

            var result = await command.ExecuteAsync(Args(
                ("operations", operations),
                ("safety-password", "A1")), NullLogger.Instance);

            Assert.IsFalse(result.Success);
            service.Verify(candidate => candidate.ApplyPatchAsync(
                It.IsAny<IReadOnlyList<HardwarePatchOperation>>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<string?>()), Times.Never);
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
        public async Task AuditHardwareTagMappings_PassesFiltersAndLimit()
        {
            var service = new Mock<IHardwareService>();
            service.Setup(candidate => candidate.AuditHardwareTagMappingsAsync(
                    "PLC_1",
                    "FLOW_METERS",
                    "Promag",
                    12))
                .ReturnsAsync(new HardwareTagMappingAuditResult
                {
                    PlcName = "PLC_1",
                    AddressedTagCount = 14,
                    UnmatchedTagCount = 14
                });
            var command = new AuditHardwareTagMappingsCommand(service.Object);

            var result = await command.ExecuteAsync(Args(
                ("plc-name", "PLC_1"),
                ("tag-table-filter", "FLOW_METERS"),
                ("catalog-search-text", "Promag"),
                ("max-unmatched-tags", 12)), NullLogger.Instance);

            Assert.IsTrue(result.Success);
            service.VerifyAll();
        }

        [TestMethod]
        public void HardwareTagAddressParser_ParsesByteWordDwordAndBitAddresses()
        {
            var inputDword = HardwareService.TryParseTagAddress("FLOW_METERS", "156FT220_Volume_Flow", "%ID1572");
            var outputByte = HardwareService.TryParseTagAddress("FLOW_METERS", "156FT220_TOT_1_Ctrl", "%QB1577");
            var inputBit = HardwareService.TryParseTagAddress("FLOW_METERS", "Ready", "%I1580.1");

            Assert.IsNotNull(inputDword);
            Assert.AreEqual("I", inputDword!.Area);
            Assert.AreEqual(1572, inputDword.StartAddress);
            Assert.AreEqual(1575, inputDword.EndAddress);
            Assert.IsNotNull(outputByte);
            Assert.AreEqual("Q", outputByte!.Area);
            Assert.AreEqual(1577, outputByte.EndAddress);
            Assert.IsNotNull(inputBit);
            Assert.AreEqual(1580, inputBit!.StartAddress);
            Assert.AreEqual(1, inputBit.Length);
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

        private static bool HasSingleItemPath(HardwarePatchOperation operation, string itemName)
        {
            return operation.Target != null &&
                operation.Target.ItemPath.Length == 1 &&
                operation.Target.ItemPath[0] == itemName;
        }
    }
}
