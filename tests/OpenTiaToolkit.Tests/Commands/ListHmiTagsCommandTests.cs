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
            HmiTagBrowseOptions? capturedOptions = null;
            hmiService
                .Setup(service => service.GetTagsAsync(It.IsAny<HmiTagBrowseOptions>()))
                .Callback<HmiTagBrowseOptions>(options => capturedOptions = options)
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
            hmiService.Verify(service => service.GetTagsAsync(It.IsAny<HmiTagBrowseOptions>()), Times.Once);
            Assert.IsNotNull(capturedOptions);
            Assert.AreEqual("PV", capturedOptions!.Filter);
            Assert.AreEqual("Analog_FT235_GEN", capturedOptions.RootPath);
            Assert.AreEqual(200, capturedOptions.MaxResults);
            Assert.AreEqual(4, capturedOptions.MaxDepth);
            Assert.IsTrue(capturedOptions.IncludeDetails);
            Assert.IsFalse(capturedOptions.IncludeLoggingCounts);
        }

        [TestMethod]
        public async Task Execute_BlocksUnscopedScanUnlessExplicitlyAllowed()
        {
            var hmiService = new Mock<IHmiService>();
            hmiService.SetupGet(service => service.IsHmiOpen).Returns(true);
            hmiService.SetupGet(service => service.CurrentHmiName).Returns("HMI_1");

            var command = new ListHmiTagsCommand(hmiService.Object);
            var result = await command.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    ["hmi-name"] = "HMI_1",
                    ["filter"] = "PV"
                }),
                NullLogger.Instance);

            Assert.IsFalse(result.Success);
            StringAssert.Contains(result.Error, "allow-full-scan=true");
            hmiService.Verify(service => service.GetTagsAsync(It.IsAny<HmiTagBrowseOptions>()), Times.Never);
        }

        [TestMethod]
        public async Task Execute_AllowsBoundedFullScanWhenExplicitlyRequested()
        {
            var hmiService = new Mock<IHmiService>();
            hmiService.SetupGet(service => service.IsHmiOpen).Returns(true);
            hmiService.SetupGet(service => service.CurrentHmiName).Returns("HMI_1");

            HmiTagBrowseOptions? capturedOptions = null;
            hmiService
                .Setup(service => service.GetTagsAsync(It.IsAny<HmiTagBrowseOptions>()))
                .Callback<HmiTagBrowseOptions>(options => capturedOptions = options)
                .ReturnsAsync(new List<HmiTagInfo>());

            var command = new ListHmiTagsCommand(hmiService.Object);
            var result = await command.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    ["hmi-name"] = "HMI_1",
                    ["allow-full-scan"] = true,
                    ["max-results"] = 25,
                    ["max-depth"] = 2,
                    ["include-details"] = false,
                    ["include-logging-counts"] = true
                }),
                NullLogger.Instance);

            Assert.IsTrue(result.Success, result.Error);
            Assert.IsNotNull(capturedOptions);
            Assert.IsNull(capturedOptions!.RootPath);
            Assert.AreEqual(25, capturedOptions.MaxResults);
            Assert.AreEqual(2, capturedOptions.MaxDepth);
            Assert.IsFalse(capturedOptions.IncludeDetails);
            Assert.IsTrue(capturedOptions.IncludeLoggingCounts);
        }
    }
}
