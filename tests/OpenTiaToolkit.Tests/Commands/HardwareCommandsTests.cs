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
                "[{\"op\":\"set-gsd-parameter-data\",\"target\":{\"deviceName\":\"Master\"},\"datasetNumber\":47360,\"dataHex\":\"01 02\",\"expectedDataHex\":\"00 00\"},{\"op\":\"set-attributes\",\"target\":{\"deviceName\":\"Master\"},\"attributes\":{\"Name\":\"Port_1\"},\"expectedAttributes\":{\"Name\":\"Port\"}}]").RootElement.Clone();

            var result = await command.ExecuteAsync(Args(("operations", operations)), NullLogger.Instance);

            Assert.IsTrue(result.Success);
            service.Verify(candidate => candidate.ApplyPatchAsync(
                It.Is<IReadOnlyList<HardwarePatchOperation>>(values =>
                    values.Count == 2 &&
                    values[0].ExpectedDataHex == "00 00" &&
                    values[1].ExpectedAttributes != null &&
                    values[1].ExpectedAttributes!.ContainsKey("Name")),
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
