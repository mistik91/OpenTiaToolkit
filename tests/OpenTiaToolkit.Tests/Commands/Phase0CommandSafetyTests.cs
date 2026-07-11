using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands;
using OpenTiaToolkit.Core.Commands.Hmi;
using OpenTiaToolkit.Core.Commands.Plc;
using OpenTiaToolkit.Core.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Tests.Commands
{
    [TestClass]
    public class Phase0CommandSafetyTests
    {
        [TestMethod]
        public async Task ImportHmiJson_DefaultsToSafeDryRunOptions()
        {
            var service = CreateOpenHmiService();
            service.Setup(candidate => candidate.ImportJsonAsync(
                    @"C:\Input\patch.json",
                    It.IsAny<HmiJsonFilterOptions?>(),
                    true,
                    It.Is<HmiJsonImportExecutionOptions>(options =>
                        options.FailOnWarning &&
                        options.RequireCleanPreflight &&
                        !options.BackupBeforeImport)))
                .ReturnsAsync(new HmiJsonImportResult { DryRun = true });
            var command = new ImportHmiJsonCommand(service.Object);

            var result = await command.ExecuteAsync(Args(
                ("hmi-name", "HMI_1"),
                ("input-path", @"C:\Input\patch.json")), NullLogger.Instance);

            Assert.IsTrue(result.Success);
            service.VerifyAll();
        }

        [TestMethod]
        public async Task ImportHmiJson_ApplyRequiresApplyAndConfirm()
        {
            var service = CreateOpenHmiService();
            var command = new ImportHmiJsonCommand(service.Object);

            var result = await command.ExecuteAsync(Args(
                ("hmi-name", "HMI_1"),
                ("input-path", @"C:\Input\patch.json"),
                ("dry-run", false)), NullLogger.Instance);

            Assert.IsFalse(result.Success);
            StringAssert.Contains(result.Message, "apply=true");
            service.Verify(candidate => candidate.ImportJsonAsync(
                It.IsAny<string>(),
                It.IsAny<HmiJsonFilterOptions?>(),
                It.IsAny<bool>(),
                It.IsAny<HmiJsonImportExecutionOptions?>()), Times.Never);
        }

        [TestMethod]
        public async Task ImportHmiJson_ConfirmedApplyDefaultsToBackupAndCleanPreflight()
        {
            var service = CreateOpenHmiService();
            service.Setup(candidate => candidate.ImportJsonAsync(
                    @"C:\Input\patch.json",
                    It.IsAny<HmiJsonFilterOptions?>(),
                    false,
                    It.Is<HmiJsonImportExecutionOptions>(options =>
                        options.FailOnWarning &&
                        options.RequireCleanPreflight &&
                        options.BackupBeforeImport)))
                .ReturnsAsync(new HmiJsonImportResult { DryRun = false });
            var command = new ImportHmiJsonCommand(service.Object);

            var result = await command.ExecuteAsync(Args(
                ("hmi-name", "HMI_1"),
                ("input-path", @"C:\Input\patch.json"),
                ("dry-run", false),
                ("apply", true),
                ("confirm", true)), NullLogger.Instance);

            Assert.IsTrue(result.Success);
            service.VerifyAll();
        }

        [TestMethod]
        public async Task ImportHmiJson_SafetyBlockIsCommandFailureWithStructuredData()
        {
            var service = CreateOpenHmiService();
            service.Setup(candidate => candidate.ImportJsonAsync(
                    It.IsAny<string>(),
                    It.IsAny<HmiJsonFilterOptions?>(),
                    true,
                    It.IsAny<HmiJsonImportExecutionOptions?>()))
                .ReturnsAsync(new HmiJsonImportResult
                {
                    DryRun = true,
                    SafetyBlocked = true,
                    BlockedReason = "tia-online-mode"
                });
            var command = new ImportHmiJsonCommand(service.Object);

            var result = await command.ExecuteAsync(Args(
                ("hmi-name", "HMI_1"),
                ("input-path", @"C:\Input\patch.json")), NullLogger.Instance);

            Assert.IsFalse(result.Success);
            Assert.IsInstanceOfType(result.Data, typeof(HmiJsonImportResult));
        }

        [TestMethod]
        public async Task ApplyHmiScreenJsonEdit_SafetyBlockIsCommandFailure()
        {
            var service = CreateOpenHmiService();
            service.Setup(candidate => candidate.ApplyScreenJsonEditAsync(
                    "Screens/Main",
                    It.IsAny<HmiJsonScreen>(),
                    It.IsAny<string?>(),
                    It.IsAny<HmiJsonFilterOptions?>(),
                    true,
                    It.IsAny<HmiJsonImportExecutionOptions?>(),
                    It.IsAny<string?>(),
                    false))
                .ReturnsAsync(new HmiScreenJsonEditResult { DryRun = true, SafetyBlocked = true });
            var command = new ApplyHmiScreenJsonEditCommand(service.Object);

            var result = await command.ExecuteAsync(Args(
                ("hmi-name", "HMI_1"),
                ("screen-path", "Screens/Main"),
                ("edited-screen", new HmiJsonScreen())), NullLogger.Instance);

            Assert.IsFalse(result.Success);
            Assert.IsInstanceOfType(result.Data, typeof(HmiScreenJsonEditResult));
        }

        [TestMethod]
        public async Task ApplyHmiScreenJsonPatch_SafetyBlockIsCommandFailure()
        {
            var service = CreateOpenHmiService();
            service.Setup(candidate => candidate.ApplyScreenJsonPatchAsync(
                    "Screens/Main",
                    It.IsAny<IReadOnlyList<HmiScreenJsonPatchOperation>>(),
                    It.IsAny<string?>(),
                    It.IsAny<HmiJsonFilterOptions?>(),
                    true,
                    It.IsAny<HmiJsonImportExecutionOptions?>(),
                    It.IsAny<string?>(),
                    false))
                .ReturnsAsync(new HmiScreenJsonEditResult { DryRun = true, SafetyBlocked = true });
            var command = new ApplyHmiScreenJsonPatchCommand(service.Object);

            var result = await command.ExecuteAsync(Args(
                ("hmi-name", "HMI_1"),
                ("screen-path", "Screens/Main"),
                ("operations", new HmiScreenJsonPatchOperation { Op = "set-property" })), NullLogger.Instance);

            Assert.IsFalse(result.Success);
            Assert.IsInstanceOfType(result.Data, typeof(HmiScreenJsonEditResult));
        }

        [TestMethod]
        public async Task CompileSoftware_FailedCompilationIsCommandFailureWithDiagnostics()
        {
            var service = new Mock<IPlcService>();
            service.SetupGet(candidate => candidate.IsSoftwareOpen).Returns(true);
            service.SetupGet(candidate => candidate.CurrentPlcName).Returns("PLC_1");
            service.Setup(candidate => candidate.CompileSoftwareAsync(It.IsAny<string?>()))
                .ReturnsAsync(new CompilationResult(false, 1, 0, "Compile error"));
            var command = new CompileSoftwareCommand(service.Object);

            var result = await command.ExecuteAsync(Args(("plc-name", "PLC_1")), NullLogger.Instance);

            Assert.IsFalse(result.Success);
            Assert.IsNotNull(result.Data);
            StringAssert.Contains(result.Message, "Compilation failed");
        }

        private static Mock<IHmiService> CreateOpenHmiService()
        {
            var service = new Mock<IHmiService>();
            service.SetupGet(candidate => candidate.IsHmiOpen).Returns(true);
            service.SetupGet(candidate => candidate.CurrentHmiName).Returns("HMI_1");
            return service;
        }

        private static CommandArgs Args(params (string Name, object? Value)[] values)
        {
            var arguments = new Dictionary<string, object?>();
            foreach (var value in values)
            {
                arguments[value.Name] = value.Value;
            }

            return new CommandArgs(arguments);
        }
    }
}
