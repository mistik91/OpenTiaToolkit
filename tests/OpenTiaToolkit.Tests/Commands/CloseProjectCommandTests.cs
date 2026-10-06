using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands;
using OpenTiaToolkit.Core.Commands.Portal;
using OpenTiaToolkit.Core.Models;

namespace OpenTiaToolkit.Tests.Commands
{
    [TestClass]
    public class CloseProjectCommandTests
    {
        [TestMethod]
        public async Task ExecuteAsync_WhenCloseFails_IncludesPortalLastError()
        {
            var portalService = new Mock<IPortalService>();
            portalService.Setup(service => service.GetProjectInfoAsync())
                .ReturnsAsync(new ProjectInfo("Line1", @"C:\Projects\Line1.ap21", "V2100", null));
            portalService.Setup(service => service.CloseProjectAsync(false)).ReturnsAsync(false);
            portalService.SetupGet(service => service.LastError)
                .Returns("Siemens Openness returned from close, but the project is still open.");

            var command = new CloseProjectCommand(portalService.Object);
            var result = await command.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?> { ["save"] = false }),
                NullLogger.Instance);

            Assert.IsFalse(result.Success);
            StringAssert.Contains(result.Message, "the project is still open");
        }

        [TestMethod]
        public async Task ExecuteAsync_WhenCloseSucceeds_ReturnsClosedProjectIdentity()
        {
            var portalService = new Mock<IPortalService>();
            portalService.Setup(service => service.GetProjectInfoAsync())
                .ReturnsAsync(new ProjectInfo(
                    "Line1",
                    @"C:\Projects\Line1.ap21",
                    "V2100",
                    DateTime.UtcNow,
                    portalProcessId: 1234,
                    isPortalVisible: true,
                    isToolkitOwned: false));
            portalService.Setup(service => service.CloseProjectAsync(false)).ReturnsAsync(true);

            var command = new CloseProjectCommand(portalService.Object);
            var result = await command.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?> { ["save"] = false }),
                NullLogger.Instance);

            Assert.IsTrue(result.Success);
            Assert.IsNotNull(result.Data);
            StringAssert.Contains(result.Data!.ToString(), "Line1");
        }
    }
}
