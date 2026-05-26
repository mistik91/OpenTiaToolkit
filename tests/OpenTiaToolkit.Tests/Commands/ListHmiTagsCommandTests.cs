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
    public class ListHmiTagsCommandTests
    {
        [TestMethod]
        public async Task Execute_PassesRootPathToScopedTagEnumeration()
        {
            var hmiService = new Mock<IHmiService>();
            hmiService.SetupGet(service => service.IsHmiOpen).Returns(true);
            hmiService.SetupGet(service => service.CurrentHmiName).Returns("HMI_1");
            hmiService
                .Setup(service => service.GetTagsAsync("PV", "Analog_FT235_GEN"))
                .ReturnsAsync(new List<HmiTagInfo>
                {
                    new HmiTagInfo { Path = "Analog_FT235_GEN.PV" }
                });

            var command = new ListHmiTagsCommand(hmiService.Object);
            var result = await command.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    ["hmi-name"] = "HMI_1",
                    ["filter"] = "PV",
                    ["root-path"] = "Analog_FT235_GEN"
                }),
                NullLogger.Instance);

            Assert.IsTrue(result.Success, result.Error);
            hmiService.Verify(service => service.GetTagsAsync("PV", "Analog_FT235_GEN"), Times.Once);
        }
    }
}
