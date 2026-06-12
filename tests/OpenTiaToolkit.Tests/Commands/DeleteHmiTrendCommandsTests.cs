using OpenTiaToolkit.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands;
using OpenTiaToolkit.Core.Commands.Hmi;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Tests.Commands
{
    [TestClass]
    public class DeleteHmiTrendCommandsTests
    {
        [TestMethod]
        public async Task RemoveLoggingTag_RequiresExplicitConfirmation()
        {
            var service = OpenHmiService();
            var command = new RemoveHmiLoggingTagCommand(service.Object);

            var result = await command.ExecuteAsync(Args(
                ("hmi-name", "HMI_1"),
                ("tag-path", "Motor.Speed"),
                ("datalog-name", "TrendLog")), NullLogger.Instance);

            Assert.IsFalse(result.Success);
            service.Verify(candidate => candidate.RemoveLoggingTagAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string?>()), Times.Never);
        }

        [TestMethod]
        public async Task RemoveLoggingTag_PassesExpectedDataLogAndOptionalName()
        {
            var service = OpenHmiService();
            service.Setup(candidate => candidate.RemoveLoggingTagAsync("Motor.Speed", "TrendLog", true, "SpeedArchiveTag"))
                .ReturnsAsync(new HmiLoggingTagDeleteResult
                {
                    TagPath = "Motor.Speed",
                    LoggingTagName = "SpeedArchiveTag",
                    DataLogName = "TrendLog",
                    Deleted = true
                });
            var command = new RemoveHmiLoggingTagCommand(service.Object);

            var result = await command.ExecuteAsync(Args(
                ("hmi-name", "HMI_1"),
                ("tag-path", "Motor.Speed"),
                ("datalog-name", "TrendLog"),
                ("logging-tag-name", "SpeedArchiveTag"),
                ("confirm", true)), NullLogger.Instance);

            Assert.IsTrue(result.Success, result.Error);
            service.VerifyAll();
        }

        [TestMethod]
        public async Task DeleteDataLog_PreservesBlockingReferences()
        {
            var service = OpenHmiService();
            service.Setup(candidate => candidate.DeleteDataLogAsync("TrendLog", true))
                .ReturnsAsync(new HmiDataLogDeleteResult
                {
                    DataLogName = "TrendLog",
                    Error = "Data log 'TrendLog' is still referenced by 1 logging tag(s).",
                    References = new List<HmiLoggingTagReferenceInfo>
                    {
                        new HmiLoggingTagReferenceInfo
                        {
                            TagPath = "Motor.Speed",
                            LoggingTagName = "Motor_Speed",
                            DataLogName = "TrendLog"
                        }
                    }
                });
            var command = new DeleteHmiDataLogCommand(service.Object);

            var result = await command.ExecuteAsync(Args(
                ("hmi-name", "HMI_1"),
                ("datalog-name", "TrendLog"),
                ("confirm", true)), NullLogger.Instance);

            Assert.IsFalse(result.Success);
            Assert.IsInstanceOfType(result.Data, typeof(HmiDataLogDeleteResult));
            Assert.AreEqual(1, ((HmiDataLogDeleteResult)result.Data!).References.Count);
            service.VerifyAll();
        }

        [TestMethod]
        public async Task DeleteDataLog_RequiresExplicitConfirmation()
        {
            var service = OpenHmiService();
            var command = new DeleteHmiDataLogCommand(service.Object);

            var result = await command.ExecuteAsync(Args(
                ("hmi-name", "HMI_1"),
                ("datalog-name", "TrendLog")), NullLogger.Instance);

            Assert.IsFalse(result.Success);
            service.Verify(candidate => candidate.DeleteDataLogAsync(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
        }

        private static Mock<IHmiService> OpenHmiService()
        {
            var service = new Mock<IHmiService>();
            service.SetupGet(candidate => candidate.IsHmiOpen).Returns(true);
            service.SetupGet(candidate => candidate.CurrentHmiName).Returns("HMI_1");
            return service;
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
