using OpenTiaToolkit.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands;
using OpenTiaToolkit.Core.Commands.Hmi;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Tests.Commands
{
    [TestClass]
    public class ProbeHmiFaceplateInterfaceCommandTests
    {
        [TestMethod]
        public async Task Execute_ReturnsInterfaceMetadata()
        {
            var hmiService = CreateOpenHmiService();
            hmiService
                .Setup(service => service.ProbeFaceplateInterfaceAsync(
                    "Screens/Main",
                    "Faceplate container_10",
                    "GEN",
                    false,
                    null,
                    true))
                .ReturnsAsync(CreateProbeResult(writeSucceeded: false));

            var command = new ProbeHmiFaceplateInterfaceCommand(hmiService.Object);
            var result = await command.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    ["hmi-name"] = "HMI_1",
                    ["screen-path"] = "Screens/Main",
                    ["item-name"] = "Faceplate container_10",
                    ["property"] = "GEN"
                }),
                NullLogger.Instance);

            Assert.IsTrue(result.Success, result.Error);
            var probe = result.Data as HmiFaceplateInterfaceProbeResult;
            Assert.IsNotNull(probe);
            Assert.AreEqual("GEN", probe!.Entries[0].PropertyName);
        }

        [TestMethod]
        public async Task Execute_ReadsItemNameFromPath()
        {
            var itemNamePath = Path.GetTempFileName();
            try
            {
                File.WriteAllText(itemNamePath, "Faceplate container_10");
                var hmiService = CreateOpenHmiService();
                hmiService
                    .Setup(service => service.ProbeFaceplateInterfaceAsync(
                        "Screens/Main",
                        "Faceplate container_10",
                        null,
                        false,
                        null,
                        true))
                    .ReturnsAsync(CreateProbeResult(writeSucceeded: false));

                var command = new ProbeHmiFaceplateInterfaceCommand(hmiService.Object);
                var result = await command.ExecuteAsync(
                    new CommandArgs(new Dictionary<string, object?>
                    {
                        ["hmi-name"] = "HMI_1",
                        ["screen-path"] = "Screens/Main",
                        ["item-name-path"] = itemNamePath
                    }),
                    NullLogger.Instance);

                Assert.IsTrue(result.Success, result.Error);
            }
            finally
            {
                File.Delete(itemNamePath);
            }
        }

        [TestMethod]
        public async Task Execute_WriteFailureReturnsStructuredFailure()
        {
            var hmiService = CreateOpenHmiService();
            var probeResult = CreateProbeResult(writeSucceeded: false);
            probeResult.WriteRequested = true;
            probeResult.WriteError = "set_Value failed";
            hmiService
                .Setup(service => service.ProbeFaceplateInterfaceAsync(
                    "Screens/Main",
                    "PV1",
                    "GEN",
                    true,
                    "ProbeValue",
                    true))
                .ReturnsAsync(probeResult);

            var command = new ProbeHmiFaceplateInterfaceCommand(hmiService.Object);
            var result = await command.ExecuteAsync(
                new CommandArgs(new Dictionary<string, object?>
                {
                    ["hmi-name"] = "HMI_1",
                    ["screen-path"] = "Screens/Main",
                    ["item-name"] = "PV1",
                    ["property"] = "GEN",
                    ["write"] = true,
                    ["value"] = "ProbeValue"
                }),
                NullLogger.Instance);

            Assert.IsFalse(result.Success);
            Assert.AreSame(probeResult, result.Data);
        }

        private static Mock<IHmiService> CreateOpenHmiService()
        {
            var hmiService = new Mock<IHmiService>();
            hmiService.SetupGet(service => service.IsHmiOpen).Returns(true);
            hmiService.SetupGet(service => service.CurrentHmiName).Returns("HMI_1");
            return hmiService;
        }

        private static HmiFaceplateInterfaceProbeResult CreateProbeResult(bool writeSucceeded)
        {
            return new HmiFaceplateInterfaceProbeResult
            {
                ScreenPath = "Screens/Main",
                ItemName = "Faceplate container_10",
                ItemType = "HmiFaceplateContainer",
                ContainedType = @"V0.0.9\SensorPV",
                TargetProperty = "GEN",
                WriteSucceeded = writeSucceeded,
                Entries =
                {
                    new HmiFaceplateInterfaceProbeEntry
                    {
                        PropertyName = "GEN",
                        Value = "Analog_PT355_GEN",
                        EntryType = "Siemens.Engineering.HmiUnified.UI.Parts.HmiFaceplateInterface",
                        ValuePropertyType = "System.String",
                        ValueDeclaringType = "Siemens.Engineering.HmiUnified.UI.Parts.HmiFaceplateInterface",
                        ValueCanRead = true,
                        ValueCanWrite = true,
                        IsTarget = true
                    }
                }
            };
        }
    }
}
