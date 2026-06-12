using OpenTiaToolkit.Core.Models;
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

        [TestMethod]
        public async Task Execute_ReturnsArtifactManifestWhenIncludeScreenIsFalse()
        {
            var snapshot = new HmiScreenJsonSnapshotResult
            {
                GeneratedAtUtc = "2026-06-09T00:00:00Z",
                HmiName = "HMI_1",
                ScreenPath = "Screens/Main",
                BaseHash = "abc",
                ExportPath = @"C:\Temp\unified_hmi_export.json",
                RecommendedPatchArgs = new HmiScreenJsonRecommendedPatchArgs
                {
                    HmiName = "HMI_1",
                    ScreenPath = "Screens/Main",
                    BaseHash = "abc"
                },
                Screen = new HmiJsonScreen
                {
                    Path = "Screens/Main",
                    Name = "Main",
                    Items = new List<HmiJsonItem>
                    {
                        new HmiJsonItem { Name = "Text_1", Type = "HmiText" }
                    }
                }
            };

            var hmiService = new Mock<IHmiService>();
            hmiService.SetupGet(service => service.IsHmiOpen).Returns(true);
            hmiService.SetupGet(service => service.CurrentHmiName).Returns("HMI_1");
            hmiService
                .Setup(service => service.GetScreenJsonAsync("Screens/Main", null, null))
                .ReturnsAsync(snapshot);

            var command = new GetHmiScreenJsonCommand(hmiService.Object);
            var result = await command.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    ["hmi-name"] = "HMI_1",
                    ["screen-path"] = "Screens/Main",
                    ["include-screen"] = false
                }),
                NullLogger.Instance);

            Assert.IsTrue(result.Success, result.Error);
            var artifact = result.Data as CommandArtifactResult;
            Assert.IsNotNull(artifact);
            Assert.AreEqual("json", artifact!.Format);
            Assert.AreEqual("unified-hmi-screen-json", artifact.DataKind);
            Assert.AreEqual(1, artifact.ItemCount);
            Assert.IsTrue(artifact.InlineOutputSuppressed);
            StringAssert.Contains(artifact.Reason, "include-screen=false");
        }
    }
}
