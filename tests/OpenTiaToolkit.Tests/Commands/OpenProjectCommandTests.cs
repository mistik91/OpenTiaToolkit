using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands;
using OpenTiaToolkit.Core.Commands.Portal;
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
            portalService.Setup(service => service.OpenProjectAsync(@"C:\Projects\Locked.ap21", null))
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
    }
}
