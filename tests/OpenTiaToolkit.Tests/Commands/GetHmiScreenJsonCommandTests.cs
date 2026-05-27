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
    public class GetHmiScreenJsonCommandTests
    {
        [TestMethod]
        public async Task Execute_SuggestsCanonicalPathForNestedScreenLeafName()
        {
            var hmiService = new Mock<IHmiService>();
            hmiService.SetupGet(service => service.IsHmiOpen).Returns(true);
            hmiService.SetupGet(service => service.CurrentHmiName).Returns("HMI_1");
            hmiService
                .Setup(service => service.GetScreenJsonAsync("Valve", null, null))
                .ReturnsAsync((HmiScreenJsonSnapshotResult?)null);
            hmiService
                .Setup(service => service.GetScreensAsync(null, true))
                .ReturnsAsync(new[]
                {
                    new HmiScreenInfo { Name = "Valve", Path = "Popups/Valves/Valve" }
                });

            var command = new GetHmiScreenJsonCommand(hmiService.Object);
            var result = await command.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    ["hmi-name"] = "HMI_1",
                    ["screen-path"] = "Valve"
                }),
                NullLogger.Instance);

            Assert.IsFalse(result.Success);
            StringAssert.Contains(result.Message, "Did you mean 'Popups/Valves/Valve'?");
        }

        [TestMethod]
        public async Task Execute_ListsCanonicalPathsWhenLeafNameIsAmbiguous()
        {
            var hmiService = new Mock<IHmiService>();
            hmiService.SetupGet(service => service.IsHmiOpen).Returns(true);
            hmiService.SetupGet(service => service.CurrentHmiName).Returns("HMI_1");
            hmiService
                .Setup(service => service.GetScreenJsonAsync("Valve", null, null))
                .ReturnsAsync((HmiScreenJsonSnapshotResult?)null);
            hmiService
                .Setup(service => service.GetScreensAsync(null, true))
                .ReturnsAsync(new[]
                {
                    new HmiScreenInfo { Name = "Valve", Path = "Popups/A/Valve" },
                    new HmiScreenInfo { Name = "Valve", Path = "Popups/B/Valve" }
                });

            var command = new GetHmiScreenJsonCommand(hmiService.Object);
            var result = await command.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    ["hmi-name"] = "HMI_1",
                    ["screen-path"] = "Valve"
                }),
                NullLogger.Instance);

            Assert.IsFalse(result.Success);
            StringAssert.Contains(result.Message, "'Popups/A/Valve'");
            StringAssert.Contains(result.Message, "'Popups/B/Valve'");
        }
    }
}
