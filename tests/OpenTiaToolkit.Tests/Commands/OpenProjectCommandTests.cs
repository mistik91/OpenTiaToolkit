using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands;
using OpenTiaToolkit.Core.Commands.Portal;
using OpenTiaToolkit.Core.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Tests.Commands
{
    [TestClass]
    public class OpenProjectCommandTests
    {
        [TestMethod]
        public async Task ExecuteAsync_WhenOpenFails_IncludesPortalLastError()
        {
            var portalService = new Mock<IPortalService>();
            portalService.Setup(service => service.OpenProjectAsync(@"C:\Projects\Locked.ap21", null, false))
                .ReturnsAsync(false);
            portalService.SetupGet(service => service.LastError)
                .Returns("Project is locked by another TIA Portal instance.");

            var command = new OpenProjectCommand(portalService.Object);

            var result = await command.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    ["project-path"] = @"C:\Projects\Locked.ap21"
                }),
                NullLogger.Instance);

            Assert.IsFalse(result.Success);
            StringAssert.Contains(result.Message, "Project is locked by another TIA Portal instance.");
        }

        [TestMethod]
        public async Task ExecuteAsync_PassesExplicitNewInstanceSelection()
        {
            var portalService = new Mock<IPortalService>();
            portalService
                .Setup(service => service.OpenProjectAsync(@"C:\Projects\Line2.ap21", null, true))
                .ReturnsAsync(true);
            portalService
                .Setup(service => service.GetProjectInfoAsync())
                .ReturnsAsync(new ProjectInfo(
                    "Line2",
                    @"C:\Projects\Line2.ap21",
                    "V2100",
                    null,
                    portalProcessId: null,
                    isPortalVisible: false,
                    isToolkitOwned: true));

            var command = new OpenProjectCommand(portalService.Object);
            var result = await command.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    ["project-path"] = @"C:\Projects\Line2.ap21",
                    ["open-in-new-instance"] = true
                }),
                NullLogger.Instance);

            Assert.IsTrue(result.Success);
            portalService.Verify(
                service => service.OpenProjectAsync(@"C:\Projects\Line2.ap21", null, true),
                Times.Once);
        }
    }
}
