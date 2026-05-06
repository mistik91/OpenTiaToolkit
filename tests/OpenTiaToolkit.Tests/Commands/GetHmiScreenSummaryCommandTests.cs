using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands;
using OpenTiaToolkit.Core.Commands.Hmi;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Tests.Commands
{
    [TestClass]
    public class GetHmiScreenSummaryCommandTests
    {
        [TestMethod]
        public async Task Execute_ReturnsItemAndContainedTypeCounts()
        {
            var hmiService = new Mock<IHmiService>();
            hmiService.SetupGet(service => service.IsHmiOpen).Returns(true);
            hmiService.SetupGet(service => service.CurrentHmiName).Returns("HMI_1");
            hmiService
                .Setup(service => service.GetScreenJsonAsync(
                    "Screens/Main",
                    It.Is<HmiJsonFilterOptions>(filters => filters.Properties.Contains("ContainedType")),
                    null))
                .ReturnsAsync(CreateSnapshot());

            var command = new GetHmiScreenSummaryCommand(hmiService.Object);
            var result = await command.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    ["hmi-name"] = "HMI_1",
                    ["screen-path"] = "Screens/Main"
                }),
                NullLogger.Instance);

            Assert.IsTrue(result.Success, result.Error);
            var summary = result.Data as HmiScreenSummaryResult;
            Assert.IsNotNull(summary);
            Assert.AreEqual(3, summary!.ItemCount);
            Assert.AreEqual(2, summary.ItemTypes.Single(entry => entry.Name == "HmiFaceplateContainer").Count);
            Assert.AreEqual(1, summary.FaceplateContainedTypes.Single(entry => entry.Name == @"V0.0.9\SensorPV").Count);
            Assert.AreEqual(1, summary.FaceplateContainedTypes.Single(entry => entry.Name == @"V0.0.16\Valve_Simple").Count);
            Assert.IsNull(summary.Items);
        }

        [TestMethod]
        public async Task Execute_IncludesCompactItemsWhenRequested()
        {
            var hmiService = new Mock<IHmiService>();
            hmiService.SetupGet(service => service.IsHmiOpen).Returns(true);
            hmiService.SetupGet(service => service.CurrentHmiName).Returns("HMI_1");
            hmiService
                .Setup(service => service.GetScreenJsonAsync("Screens/Main", It.IsAny<HmiJsonFilterOptions?>(), null))
                .ReturnsAsync(CreateSnapshot());

            var command = new GetHmiScreenSummaryCommand(hmiService.Object);
            var result = await command.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    ["hmi-name"] = "HMI_1",
                    ["screen-path"] = "Screens/Main",
                    ["include-items"] = true
                }),
                NullLogger.Instance);

            Assert.IsTrue(result.Success, result.Error);
            var summary = result.Data as HmiScreenSummaryResult;
            Assert.IsNotNull(summary);
            Assert.AreEqual(3, summary!.Items!.Count);
            Assert.AreEqual(@"V0.0.9\SensorPV", summary.Items.Single(item => item.Name == "PV1").ContainedType);
        }

        private static HmiScreenJsonSnapshotResult CreateSnapshot()
        {
            return new HmiScreenJsonSnapshotResult
            {
                HmiName = "HMI_1",
                ScreenPath = "Screens/Main",
                BaseHash = "hash-1",
                Screen = new HmiJsonScreen
                {
                    Path = "Screens/Main",
                    Name = "Main",
                    Items =
                    {
                        new HmiJsonItem
                        {
                            Name = "PV1",
                            Type = "HmiFaceplateContainer",
                            Properties = { ["ContainedType"] = @"V0.0.9\SensorPV" }
                        },
                        new HmiJsonItem
                        {
                            Name = "V1",
                            Type = "HmiFaceplateContainer",
                            Properties = { ["ContainedType"] = @"V0.0.16\Valve_Simple" }
                        },
                        new HmiJsonItem
                        {
                            Name = "Text_1",
                            Type = "HmiText"
                        }
                    }
                }
            };
        }
    }
}
