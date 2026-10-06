using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands;
using OpenTiaToolkit.Core.Commands.Portal;
using OpenTiaToolkit.Core.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Tests.Commands
{
    [TestClass]
    public class ProjectLifecycleCommandsTests
    {
        [TestMethod]
        public async Task CreateProjectCommand_CallsPortalService()
        {
            var portalService = new Mock<IPortalService>();
            portalService.SetupGet(service => service.IsConnected).Returns(true);
            portalService.Setup(service => service.CreateProjectAsync(@"C:\Projects\Boot.ap21"))
                .ReturnsAsync(new ProjectLifecycleResult("Boot", @"C:\Projects\Boot\Boot.ap21"));

            var command = new CreateProjectCommand(portalService.Object);

            var result = await command.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    ["project-path"] = @"C:\Projects\Boot.ap21"
                }),
                NullLogger.Instance);

            Assert.IsTrue(result.Success);
            StringAssert.Contains(result.Message, "Project created");
            portalService.Verify(service => service.CreateProjectAsync(@"C:\Projects\Boot.ap21"), Times.Once);
        }

        [TestMethod]
        public async Task AddDeviceCommand_CallsPortalService()
        {
            var portalService = new Mock<IPortalService>();
            portalService.SetupGet(service => service.IsConnected).Returns(true);
            portalService.Setup(service => service.AddDeviceAsync("6ES7 214-1AG40-0XB0", "V4.6", "PLC_1"))
                .ReturnsAsync(new DeviceInfo("PLC_1", "OrderNumber:6ES7 214-1AG40-0XB0/V4.6", null, null));

            var command = new AddDeviceCommand(portalService.Object);

            var result = await command.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    ["order-number"] = "6ES7 214-1AG40-0XB0",
                    ["version"] = "V4.6",
                    ["name"] = "PLC_1"
                }),
                NullLogger.Instance);

            Assert.IsTrue(result.Success);
            StringAssert.Contains(result.Message, "Device added");
            portalService.Verify(service => service.AddDeviceAsync("6ES7 214-1AG40-0XB0", "V4.6", "PLC_1"), Times.Once);
        }

        [TestMethod]
        public async Task ArchiveProjectCommand_CallsPortalService()
        {
            var portalService = new Mock<IPortalService>();
            portalService.SetupGet(service => service.IsConnected).Returns(true);
            portalService.Setup(service => service.ArchiveProjectAsync(@"C:\Backups\Boot.zap21", "Compressed"))
                .ReturnsAsync(new ProjectArchiveResult(@"C:\Backups\Boot.zap21", "Compressed"));

            var command = new ArchiveProjectCommand(portalService.Object);

            var result = await command.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    ["archive-path"] = @"C:\Backups\Boot.zap21"
                }),
                NullLogger.Instance);

            Assert.IsTrue(result.Success);
            StringAssert.Contains(result.Message, "Project archived");
            portalService.Verify(service => service.ArchiveProjectAsync(@"C:\Backups\Boot.zap21", "Compressed"), Times.Once);
        }

        [TestMethod]
        public async Task RetrieveProjectCommand_UsesUmacPasswordEnvironmentVariable()
        {
            const string envName = "OPENTIA_TEST_UMAC_PASSWORD";
            Environment.SetEnvironmentVariable(envName, "secret");

            try
            {
                ProjectAuthenticationOptions? capturedAuthentication = null;
                var portalService = new Mock<IPortalService>();
                portalService.SetupGet(service => service.IsConnected).Returns(true);
                portalService
                    .Setup(service => service.RetrieveProjectAsync(
                        @"C:\Backups\Boot.zap21",
                        @"C:\Projects",
                        false,
                        It.IsAny<ProjectAuthenticationOptions>()))
                    .Callback<string, string, bool, ProjectAuthenticationOptions?>((_, _, _, authentication) => capturedAuthentication = authentication)
                    .ReturnsAsync(new ProjectLifecycleResult("Boot", @"C:\Projects\Boot\Boot.ap21"));

                var command = new RetrieveProjectCommand(portalService.Object);

                var result = await command.ExecuteAsync(
                    new CommandArgs(new Dictionary<string, object?>
                    {
                        ["archive-path"] = @"C:\Backups\Boot.zap21",
                        ["target-directory"] = @"C:\Projects",
                        ["upgrade"] = false,
                        ["umac-user"] = "Engineer",
                        ["umac-password-env"] = envName,
                        ["umac-user-type"] = "global"
                    }),
                    NullLogger.Instance);

                Assert.IsTrue(result.Success);
                Assert.IsNotNull(capturedAuthentication);
                Assert.AreEqual("Engineer", capturedAuthentication!.UserName);
                Assert.AreEqual("global", capturedAuthentication.UserType);
                Assert.AreEqual(6, capturedAuthentication.Password.Length);
            }
            finally
            {
                Environment.SetEnvironmentVariable(envName, null);
            }
        }

        [TestMethod]
        public async Task OpenProjectCommand_ReturnsFailure_WhenUmacPasswordEnvironmentVariableIsMissing()
        {
            const string envName = "OPENTIA_MISSING_UMAC_PASSWORD";
            Environment.SetEnvironmentVariable(envName, null);

            var portalService = new Mock<IPortalService>();
            var command = new OpenProjectCommand(portalService.Object);

            var result = await command.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    ["project-path"] = @"C:\Projects\Protected.ap21",
                    ["umac-user"] = "Engineer",
                    ["umac-password-env"] = envName
                }),
                NullLogger.Instance);

            Assert.IsFalse(result.Success);
            StringAssert.Contains(result.Message, envName);
            portalService.Verify(
                service => service.OpenProjectAsync(
                    It.IsAny<string>(),
                    It.IsAny<ProjectAuthenticationOptions>(),
                    It.IsAny<bool>()),
                Times.Never);
        }
    }
}
