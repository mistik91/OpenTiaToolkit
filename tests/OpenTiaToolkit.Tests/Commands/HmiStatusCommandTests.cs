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
    public class HmiStatusCommandTests
    {
        [TestMethod]
        public async Task Execute_ReturnsHealthyStatusForMatchingOpenHmiAndScreen()
        {
            var portalService = new Mock<IPortalService>();
            portalService.SetupGet(service => service.IsConnected).Returns(true);
            portalService.SetupGet(service => service.Version).Returns("V2100");
            portalService
                .Setup(service => service.GetInstancesAsync())
                .ReturnsAsync(new[] { new TiaPortalInstance(1234, "V2100", true) });
            portalService
                .Setup(service => service.GetProjectInfoAsync())
                .ReturnsAsync(new ProjectInfo("Project1", @"C:\Project1.ap21", "V2100", null));

            var hmiService = new Mock<IHmiService>();
            hmiService.SetupGet(service => service.IsHmiOpen).Returns(true);
            hmiService.SetupGet(service => service.CurrentHmiName).Returns("HMI_1");
            hmiService
                .Setup(service => service.GetScreenInfoAsync("Main"))
                .ReturnsAsync(new HmiScreenInfo { Name = "Main", Path = "Main", Width = 1920, Height = 1080 });

            var command = new HmiStatusCommand(portalService.Object, hmiService.Object);
            var result = await command.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    ["hmi-name"] = "HMI_1",
                    ["screen-path"] = "Main"
                }),
                NullLogger.Instance);

            Assert.IsTrue(result.Success, result.Error);
            var status = result.Data as HmiStatusResult;
            Assert.IsNotNull(status);
            Assert.IsTrue(status!.Healthy);
            Assert.AreEqual("Project1", status.ProjectName);
            Assert.AreEqual(1, status.RunningInstances.Count);
        }

        [TestMethod]
        public async Task Execute_CapturesScreenProbeExceptionAsDiagnostic()
        {
            var portalService = new Mock<IPortalService>();
            portalService.SetupGet(service => service.IsConnected).Returns(true);
            portalService
                .Setup(service => service.GetInstancesAsync())
                .ReturnsAsync(System.Array.Empty<TiaPortalInstance>());

            var hmiService = new Mock<IHmiService>();
            hmiService.SetupGet(service => service.IsHmiOpen).Returns(true);
            hmiService.SetupGet(service => service.CurrentHmiName).Returns("HMI_1");
            hmiService
                .Setup(service => service.GetScreenInfoAsync("Main"))
                .ThrowsAsync(new System.InvalidOperationException("Disposed HMI software"));

            var command = new HmiStatusCommand(portalService.Object, hmiService.Object);
            var result = await command.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    ["hmi-name"] = "HMI_1",
                    ["screen-path"] = "Main"
                }),
                NullLogger.Instance);

            Assert.IsTrue(result.Success, result.Error);
            var status = result.Data as HmiStatusResult;
            Assert.IsNotNull(status);
            Assert.IsFalse(status!.Healthy);
            Assert.IsTrue(status.Errors[0].Contains("Disposed HMI software"));
        }
    }
}
