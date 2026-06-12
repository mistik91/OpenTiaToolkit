using OpenTiaToolkit.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands;
using OpenTiaToolkit.Core.Commands.Hmi;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Tests.Commands
{
    [TestClass]
    public class BulkHmiTrendCommandsTests
    {
        [TestMethod]
        public async Task CreateTrends_ParsesConfigsAndDefaultsToDryRun()
        {
            var service = OpenHmiService();
            service.Setup(candidate => candidate.CreateTrendsAsync(It.IsAny<IReadOnlyList<HmiTrendCreateRequest>>(), true))
                .ReturnsAsync((IReadOnlyList<HmiTrendCreateRequest> configs, bool _) =>
                {
                    Assert.AreEqual(2, configs.Count);
                    Assert.AreEqual("Motor.PV", configs[0].TagPath);
                    Assert.AreEqual("MotorLog", configs[0].DataLogName);
                    Assert.IsTrue(configs[0].Cyclic);
                    return new HmiTrendBulkResult { Operation = "create-hmi-trends", DryRun = true, RequestedCount = 2, PlannedCount = 2 };
                });

            var result = await new CreateHmiTrendsCommand(service.Object).ExecuteAsync(Args(
                ("hmi-name", "HMI_1"),
                ("configs", Json("[{\"tagPath\":\"Motor.PV\",\"dataLogName\":\"MotorLog\",\"cyclic\":true},{\"tagPath\":\"Motor.SP\",\"dataLogName\":\"MotorLog\"}]"))),
                NullLogger.Instance);

            Assert.IsTrue(result.Success, result.Error);
            service.VerifyAll();
        }

        [TestMethod]
        public async Task RemoveLoggingTags_AppliedRunRequiresConfirmation()
        {
            var service = OpenHmiService();
            var result = await new RemoveHmiLoggingTagsCommand(service.Object).ExecuteAsync(Args(
                ("hmi-name", "HMI_1"),
                ("configs", Json("[{\"tagPath\":\"Motor.PV\",\"dataLogName\":\"MotorLog\"}]")),
                ("dry-run", false)), NullLogger.Instance);

            Assert.IsFalse(result.Success);
            service.Verify(candidate => candidate.RemoveLoggingTagsAsync(
                It.IsAny<IReadOnlyList<HmiLoggingTagRemoveRequest>>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
        }

        [TestMethod]
        public async Task DeleteDataLogs_PreservesBlockedBatchData()
        {
            var service = OpenHmiService();
            service.Setup(candidate => candidate.DeleteDataLogsAsync(It.IsAny<IReadOnlyList<HmiDataLogDeleteRequest>>(), false, true))
                .ReturnsAsync(new HmiTrendBulkResult
                {
                    Operation = "delete-hmi-data-logs",
                    DryRun = true,
                    RequestedCount = 1,
                    BlockedCount = 1,
                    Results = new List<HmiTrendBulkItemResult> { new HmiTrendBulkItemResult { DataLogName = "TrendLog", Status = "Blocked" } }
                });

            var result = await new DeleteHmiDataLogsCommand(service.Object).ExecuteAsync(Args(
                ("hmi-name", "HMI_1"),
                ("configs", Json("[{\"dataLogName\":\"TrendLog\"}]"))), NullLogger.Instance);

            Assert.IsFalse(result.Success);
            Assert.IsInstanceOfType(result.Data, typeof(HmiTrendBulkResult));
            service.VerifyAll();
        }

        private static Mock<IHmiService> OpenHmiService()
        {
            var service = new Mock<IHmiService>();
            service.SetupGet(candidate => candidate.IsHmiOpen).Returns(true);
            service.SetupGet(candidate => candidate.CurrentHmiName).Returns("HMI_1");
            return service;
        }

        private static JsonElement Json(string json)
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
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
