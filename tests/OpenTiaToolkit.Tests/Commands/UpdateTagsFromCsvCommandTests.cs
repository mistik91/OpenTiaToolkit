using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands;
using OpenTiaToolkit.Core.Commands.Plc;
using OpenTiaToolkit.Core.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Tests.Commands
{
    [TestClass]
    public class UpdateTagsFromCsvCommandTests
    {
        [TestMethod]
        public async Task ExecuteAsync_ReturnsFailureWhenServiceReportsErrors()
        {
            var serviceResult = new TagUpdateResult
            {
                Summary = new TagUpdateSummary
                {
                    TotalCsvRows = 1,
                    TagsSkipped = 1
                }
            };
            serviceResult.Errors.Add(new TagUpdateError
            {
                Item = "102MotorRun",
                Error = "Multiple existing tags match base name"
            });

            var plcService = new Mock<IPlcService>();
            plcService.SetupGet(service => service.IsSoftwareOpen).Returns(true);
            plcService.SetupGet(service => service.CurrentPlcName).Returns("PLC_1");
            plcService
                .Setup(service => service.UpdateTagsFromCsvAsync("tags.csv", true))
                .ReturnsAsync(serviceResult);

            var command = new UpdateTagsFromCsvCommand(plcService.Object);
            var args = new CommandArgs(new Dictionary<string, object?>
            {
                ["plc-name"] = "PLC_1",
                ["csv-path"] = "tags.csv"
            });

            var result = await command.ExecuteAsync(args, NullLogger.Instance);

            Assert.IsFalse(result.Success);
            Assert.IsNotNull(result.Data);
            StringAssert.Contains(result.Message, "First error: 102MotorRun");
        }

        [TestMethod]
        public async Task ExecuteAsync_LogsIntoSafetyProgramBeforeApplyingWhenPasswordProvided()
        {
            var serviceResult = new TagUpdateResult
            {
                Summary = new TagUpdateSummary
                {
                    TotalCsvRows = 1,
                    ExistingTagsScanned = 1,
                    TagsUpdated = 1
                }
            };

            var plcService = new Mock<IPlcService>();
            plcService.SetupGet(service => service.IsSoftwareOpen).Returns(true);
            plcService.SetupGet(service => service.CurrentPlcName).Returns("PLC_1");
            plcService
                .Setup(service => service.LoginToSafetyOfflineProgramAsync("secret"))
                .ReturnsAsync(new SafetyLoginResult(
                    success: true,
                    serviceAvailable: true,
                    passwordRequired: true,
                    alreadyLoggedIn: false,
                    message: "Logged into the safety offline program."));
            plcService
                .Setup(service => service.UpdateTagsFromCsvAsync("tags.csv", false))
                .ReturnsAsync(serviceResult);

            var command = new UpdateTagsFromCsvCommand(plcService.Object);
            var args = new CommandArgs(new Dictionary<string, object?>
            {
                ["plc-name"] = "PLC_1",
                ["csv-path"] = "tags.csv",
                ["dry-run"] = false,
                ["safety-password"] = "secret"
            });

            var result = await command.ExecuteAsync(args, NullLogger.Instance);

            Assert.IsTrue(result.Success);
            plcService.Verify(service => service.LoginToSafetyOfflineProgramAsync("secret"), Times.Once);
            plcService.Verify(service => service.UpdateTagsFromCsvAsync("tags.csv", false), Times.Once);
        }

        [TestMethod]
        public async Task ExecuteAsync_StopsBeforeApplyWhenSafetyLoginFails()
        {
            var plcService = new Mock<IPlcService>();
            plcService.SetupGet(service => service.IsSoftwareOpen).Returns(true);
            plcService.SetupGet(service => service.CurrentPlcName).Returns("PLC_1");
            plcService
                .Setup(service => service.LoginToSafetyOfflineProgramAsync("wrong"))
                .ReturnsAsync(new SafetyLoginResult(
                    success: false,
                    serviceAvailable: true,
                    passwordRequired: true,
                    alreadyLoggedIn: false,
                    message: "Failed to log into the safety offline program."));

            var command = new UpdateTagsFromCsvCommand(plcService.Object);
            var args = new CommandArgs(new Dictionary<string, object?>
            {
                ["plc-name"] = "PLC_1",
                ["csv-path"] = "tags.csv",
                ["dry-run"] = false,
                ["safety-password"] = "wrong"
            });

            var result = await command.ExecuteAsync(args, NullLogger.Instance);

            Assert.IsFalse(result.Success);
            StringAssert.Contains(result.Message, "Failed to log into the safety offline program");
            plcService.Verify(service => service.UpdateTagsFromCsvAsync(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
        }
    }
}
