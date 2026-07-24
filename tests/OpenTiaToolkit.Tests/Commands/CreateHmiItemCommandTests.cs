using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands;
using OpenTiaToolkit.Core.Commands.Hmi;
using OpenTiaToolkit.Core.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Tests.Commands
{
    [TestClass]
    public class CreateHmiItemCommandTests
    {
        [TestMethod]
        public async Task Execute_PassesCanonicalContainedTypeToService()
        {
            var service = CreateOpenHmiService();
            service
                .Setup(candidate => candidate.CreateScreenItemAsync(
                    "Screens/Main",
                    "faceplate-container",
                    "Faceplate_1",
                    10,
                    20,
                    72,
                    28,
                    @"V0.0.1\Slider_UNIFIED"))
                .ReturnsAsync(new HmiScreenItemInfo { Name = "Faceplate_1" });

            var command = new CreateHmiItemCommand(service.Object);
            var result = await command.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    ["hmi-name"] = "HMI_1",
                    ["screen-path"] = "Screens/Main",
                    ["item-type"] = "faceplate-container",
                    ["item-name"] = "Faceplate_1",
                    ["left"] = 10,
                    ["top"] = 20,
                    ["width"] = 72,
                    ["height"] = 28,
                    ["contained-type"] = @"V0.0.1\Slider_UNIFIED"
                }),
                NullLogger.Instance);

            Assert.IsTrue(result.Success, result.Error);
            service.VerifyAll();
        }

        [TestMethod]
        public async Task Execute_NormalizesFaceplateTypeAlias()
        {
            var service = CreateOpenHmiService();
            service
                .Setup(candidate => candidate.CreateScreenItemAsync(
                    "Screens/Main",
                    "faceplate-container",
                    "Faceplate_1",
                    null,
                    null,
                    null,
                    null,
                    @"V0.0.1\Slider_UNIFIED"))
                .ReturnsAsync(new HmiScreenItemInfo { Name = "Faceplate_1" });

            var command = new CreateHmiItemCommand(service.Object);
            var result = await command.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    ["hmi-name"] = "HMI_1",
                    ["screen-path"] = "Screens/Main",
                    ["item-type"] = "faceplate-container",
                    ["item-name"] = "Faceplate_1",
                    ["faceplate-type"] = @"V0.0.1\Slider_UNIFIED"
                }),
                NullLogger.Instance);

            Assert.IsTrue(result.Success, result.Error);
            service.VerifyAll();
        }

        [TestMethod]
        public async Task Execute_RejectsConflictingContainedTypeAliases()
        {
            var service = CreateOpenHmiService();
            var command = new CreateHmiItemCommand(service.Object);

            var result = await command.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    ["hmi-name"] = "HMI_1",
                    ["screen-path"] = "Screens/Main",
                    ["item-type"] = "faceplate-container",
                    ["item-name"] = "Faceplate_1",
                    ["contained-type"] = @"V0.0.1\Slider_UNIFIED",
                    ["faceplate-type"] = @"V0.0.2\Slider_Light_UNIFIED"
                }),
                NullLogger.Instance);

            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.Error!.Contains("must match"));
            service.Verify(
                candidate => candidate.CreateScreenItemAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>(),
                    It.IsAny<string?>()),
                Times.Never);
        }

        [TestMethod]
        public void Schema_ExposesCanonicalAndCompatibilityParameters()
        {
            var command = new CreateHmiItemCommand(CreateOpenHmiService().Object);
            var names = command.Schema.Parameters.Select(parameter => parameter.Name).ToList();

            CollectionAssert.Contains(names, "contained-type");
            CollectionAssert.Contains(names, "faceplate-type");
        }

        private static Mock<IHmiService> CreateOpenHmiService()
        {
            var service = new Mock<IHmiService>();
            service.SetupGet(candidate => candidate.IsHmiOpen).Returns(true);
            service.SetupGet(candidate => candidate.CurrentHmiName).Returns("HMI_1");
            return service;
        }
    }
}
