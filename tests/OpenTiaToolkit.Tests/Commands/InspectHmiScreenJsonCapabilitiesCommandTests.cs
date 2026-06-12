using OpenTiaToolkit.Core.Models;
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
    public class InspectHmiScreenJsonCapabilitiesCommandTests
    {
        [TestMethod]
        public async Task Execute_DefaultsToCompactOutputAndPatchableComposites()
        {
            var snapshot = CreateSnapshot();
            var hmiService = new Mock<IHmiService>();
            hmiService.SetupGet(service => service.IsHmiOpen).Returns(true);
            hmiService.SetupGet(service => service.CurrentHmiName).Returns("HMI_1");
            hmiService
                .Setup(service => service.GetScreenJsonAsync("Screens/Main", It.IsAny<HmiJsonFilterOptions?>(), null))
                .ReturnsAsync(snapshot);

            var command = new InspectHmiScreenJsonCapabilitiesCommand(hmiService.Object);
            var result = await command.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    ["hmi-name"] = "HMI_1",
                    ["screen-path"] = "Screens/Main",
                    ["item-names"] = new[] { "Btn1" },
                    ["properties"] = new[] { "Text" },
                    ["include-item-types"] = false,
                    ["include-writable-properties"] = false
                }),
                NullLogger.Instance);

            Assert.IsTrue(result.Success, result.Error);
            var capabilities = result.Data as HmiScreenJsonCapabilitiesResult;
            Assert.IsNotNull(capabilities);
            Assert.IsNull(capabilities!.Snapshot, "Capability inspection should stay compact unless include-snapshot=true.");
            Assert.IsNotNull(capabilities.RecommendedPatchArgs);
            Assert.AreEqual("Btn1", capabilities.Items.Single().Name);
            CollectionAssert.Contains(capabilities.Items.Single().AvailableCompositeProperties.ToList(), "EventHandlers");
            CollectionAssert.Contains(capabilities.Items.Single().AvailableCompositeProperties.ToList(), "PropertyEventHandlers");
            CollectionAssert.Contains(capabilities.Items.Single().AvailableCompositeProperties.ToList(), "Dynamizations");
            CollectionAssert.Contains(capabilities.Items.Single().AvailableCompositeProperties.ToList(), "Interface");
        }

        [TestMethod]
        public async Task Execute_IncludesSnapshotWhenRequested()
        {
            var snapshot = CreateSnapshot();
            var hmiService = new Mock<IHmiService>();
            hmiService.SetupGet(service => service.IsHmiOpen).Returns(true);
            hmiService.SetupGet(service => service.CurrentHmiName).Returns("HMI_1");
            hmiService
                .Setup(service => service.GetScreenJsonAsync("Screens/Main", It.IsAny<HmiJsonFilterOptions?>(), null))
                .ReturnsAsync(snapshot);

            var command = new InspectHmiScreenJsonCapabilitiesCommand(hmiService.Object);
            var result = await command.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    ["hmi-name"] = "HMI_1",
                    ["screen-path"] = "Screens/Main",
                    ["include-item-types"] = false,
                    ["include-writable-properties"] = false,
                    ["include-snapshot"] = true
                }),
                NullLogger.Instance);

            Assert.IsTrue(result.Success, result.Error);
            var capabilities = result.Data as HmiScreenJsonCapabilitiesResult;
            Assert.IsNotNull(capabilities);
            Assert.AreSame(snapshot, capabilities!.Snapshot);
        }

        private static HmiScreenJsonSnapshotResult CreateSnapshot()
        {
            var filters = new HmiJsonFilterOptions
            {
                Screens = new[] { "Screens/Main" },
                ItemNames = new[] { "Btn1" },
                Properties = new[] { "Text" }
            };

            return new HmiScreenJsonSnapshotResult
            {
                HmiName = "HMI_1",
                ScreenPath = "Screens/Main",
                BaseHash = "hash-1",
                SnapshotFilters = filters,
                RecommendedPatchArgs = new HmiScreenJsonRecommendedPatchArgs
                {
                    HmiName = "HMI_1",
                    ScreenPath = "Screens/Main",
                    BaseHash = "hash-1",
                    ItemNames = filters.ItemNames,
                    Properties = filters.Properties,
                    DryRun = true,
                    Backup = true
                },
                Screen = new HmiJsonScreen
                {
                    Path = "Screens/Main",
                    Name = "Main",
                    Items =
                    {
                        new HmiJsonItem
                        {
                            Name = "Btn1",
                            Type = "HmiButton",
                            Properties = { ["Text"] = "Start" }
                        }
                    }
                }
            };
        }
    }
}
