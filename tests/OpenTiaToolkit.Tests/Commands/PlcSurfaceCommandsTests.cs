using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands;
using OpenTiaToolkit.Core.Commands.Plc;
using OpenTiaToolkit.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Tests.Commands
{
    [TestClass]
    public class PlcSurfaceCommandsTests
    {
        [TestMethod]
        public async Task ExportTags_WritesCsvWhenOutputPathIsProvided()
        {
            var outputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "tags.csv");
            try
            {
                var serviceResult = new PlcTagExportResult(
                    true,
                    "Exported 2 PLC tag(s).",
                    new[]
                    {
                        new PlcTagExportRow("Default tag table", "MotorRun", "Bool", "%M1.0", "Run feedback"),
                        new PlcTagExportRow("Default tag table", "Speed", "Int", "%MW2", "Speed, rpm")
                    });

                var plcService = CreateOpenPlcService("PLC_1");
                plcService
                    .Setup(service => service.ExportTagsAsync("en-US"))
                    .ReturnsAsync(serviceResult);

                var command = new ExportTagsCommand(plcService.Object);
                var result = await command.ExecuteAsync(Args(
                    ("plc-name", "PLC_1"),
                    ("output-path", outputPath)), NullLogger.Instance);

                Assert.IsTrue(result.Success, result.Error);
                var artifact = result.Data as CommandArtifactResult;
                Assert.IsNotNull(artifact);
                Assert.AreEqual("csv", artifact!.Format);
                Assert.AreEqual("plc-tags", artifact.DataKind);
                Assert.AreEqual(2, artifact.ItemCount);
                CollectionAssert.AreEqual(PlcCommandSupport.TagExportCsvColumns.ToArray(), artifact.Columns!.ToArray());

                var csv = File.ReadAllText(outputPath);
                StringAssert.Contains(csv, "TagTableName,Name,DataType,LogicalAddress,Comment");
                StringAssert.Contains(csv, "Default tag table,MotorRun,Bool,%M1.0,Run feedback");
                StringAssert.Contains(csv, "Default tag table,Speed,Int,%MW2,\"Speed, rpm\"");
            }
            finally
            {
                DeleteParentDirectory(outputPath);
            }
        }

        [TestMethod]
        public async Task GetCrossReferences_ReturnsFailureWhenServiceDoesNotSupportLookup()
        {
            var serviceResult = new PlcCrossReferenceResult(
                false,
                "MotorRun",
                "CrossReference service is not available for the current PLC software.");

            var plcService = CreateOpenPlcService("PLC_1");
            plcService
                .Setup(service => service.GetCrossReferencesAsync("MotorRun"))
                .ReturnsAsync(serviceResult);

            var command = new GetCrossReferencesCommand(plcService.Object);
            var result = await command.ExecuteAsync(Args(
                ("plc-name", "PLC_1"),
                ("name", "MotorRun")), NullLogger.Instance);

            Assert.IsFalse(result.Success);
            Assert.IsInstanceOfType(result.Data, typeof(PlcCrossReferenceResult));
            StringAssert.Contains(result.Message, "CrossReference service is not available");
        }

        private static Mock<IPlcService> CreateOpenPlcService(string plcName)
        {
            var service = new Mock<IPlcService>();
            service.SetupGet(candidate => candidate.IsSoftwareOpen).Returns(true);
            service.SetupGet(candidate => candidate.CurrentPlcName).Returns(plcName);
            return service;
        }

        private static CommandArgs Args(params (string Name, object Value)[] entries)
        {
            var values = new Dictionary<string, object?>();
            foreach (var entry in entries)
            {
                values[entry.Name] = entry.Value;
            }

            return new CommandArgs(values);
        }

        private static void DeleteParentDirectory(string path)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
