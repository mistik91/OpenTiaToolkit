using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands;
using OpenTiaToolkit.Core.Commands.Plc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Tests.Commands
{
    [TestClass]
    public class PlcDiscoveryCommandsTests
    {
        [TestMethod]
        public async Task GetSoftwareTree_WritesJsonWhenOutputPathIsProvided()
        {
            var outputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "software-tree.json");
            try
            {
                var service = CreateOpenPlcService("PLC_1");
                service.Setup(candidate => candidate.GetSoftwareTreeAsync(3))
                    .ReturnsAsync(new SoftwareTreeNode(
                        "PLC_1",
                        "PlcSoftware",
                        "PLC_1",
                        new[]
                        {
                            new SoftwareTreeNode("Program blocks", "Blocks", "PLC_1/Program blocks", Array.Empty<SoftwareTreeNode>())
                        }));
                var command = new GetSoftwareTreeCommand(service.Object);

                var result = await command.ExecuteAsync(Args(
                    ("plc-name", "PLC_1"),
                    ("depth", 3),
                    ("output-path", outputPath)), NullLogger.Instance);

                Assert.IsTrue(result.Success, result.Error);
                var artifact = result.Data as CommandArtifactResult;
                Assert.IsNotNull(artifact);
                Assert.AreEqual(outputPath, artifact!.OutputPath);
                Assert.AreEqual("json", artifact.Format);
                Assert.AreEqual("plc-software-tree", artifact.DataKind);
                Assert.AreEqual(2, artifact.ItemCount);
                Assert.IsFalse(artifact.InlineOutputSuppressed);
                Assert.IsTrue(File.Exists(outputPath));
                var json = File.ReadAllText(outputPath);
                StringAssert.Contains(json, "\"plcName\": \"PLC_1\"");
                StringAssert.Contains(json, "\"name\": \"Program blocks\"");
            }
            finally
            {
                DeleteParentDirectory(outputPath);
            }
        }

        [TestMethod]
        public async Task GetBlocks_WritesCsvWhenOutputPathIsProvided()
        {
            var outputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "blocks.csv");
            try
            {
                var service = CreateOpenPlcService("PLC_1");
                service.Setup(candidate => candidate.GetBlocksAsync("Motor"))
                    .ReturnsAsync(new[]
                    {
                        new BlockInfo("Motor_FB", "FB", "SCL", "12", comment: "Main motor"),
                        new BlockInfo("Motor_DB", "DB", null, "21", isReadOnly: true, comment: "Data, with comma")
                    });
                var command = new GetBlocksCommand(service.Object);

                var result = await command.ExecuteAsync(Args(
                    ("plc-name", "PLC_1"),
                    ("filter", "Motor"),
                    ("output-path", outputPath)), NullLogger.Instance);

                Assert.IsTrue(result.Success, result.Error);
                var artifact = result.Data as CommandArtifactResult;
                Assert.IsNotNull(artifact);
                Assert.AreEqual(outputPath, artifact!.OutputPath);
                Assert.AreEqual("csv", artifact.Format);
                Assert.AreEqual("plc-blocks", artifact.DataKind);
                Assert.AreEqual(2, artifact.ItemCount);
                Assert.IsTrue(File.Exists(outputPath));
                var csv = File.ReadAllText(outputPath);
                StringAssert.Contains(csv, "Name,Type,Language,Number,IsReadOnly,Comment");
                StringAssert.Contains(csv, "Motor_FB,FB,SCL,12,False,Main motor");
                StringAssert.Contains(csv, "Motor_DB,DB,,21,True,\"Data, with comma\"");
            }
            finally
            {
                DeleteParentDirectory(outputPath);
            }
        }

        [TestMethod]
        public async Task GetBlocks_AutoWritesCsvWhenInlineResultExceedsThreshold()
        {
            var service = CreateOpenPlcService("PLC_1");
            service.Setup(candidate => candidate.GetBlocksAsync(string.Empty))
                .ReturnsAsync(Enumerable.Range(0, 700)
                    .Select(index => new BlockInfo(
                        "Block_" + index,
                        "FB",
                        "SCL",
                        index.ToString(),
                        comment: new string('x', 80)))
                    .ToList());
            var command = new GetBlocksCommand(service.Object);

            var result = await command.ExecuteAsync(Args(("plc-name", "PLC_1")), NullLogger.Instance);

            Assert.IsTrue(result.Success, result.Error);
            var artifact = result.Data as CommandArtifactResult;
            Assert.IsNotNull(artifact);
            Assert.AreEqual("csv", artifact!.Format);
            Assert.AreEqual("plc-blocks", artifact.DataKind);
            Assert.IsTrue(artifact.InlineOutputSuppressed);
            Assert.IsTrue(File.Exists(artifact.OutputPath));
            File.Delete(artifact.OutputPath);
        }

        [TestMethod]
        public async Task SearchBlocks_WritesCsvWhenOutputPathIsProvided()
        {
            var outputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "search.csv");
            try
            {
                var service = CreateOpenPlcService("PLC_1");
                service.Setup(candidate => candidate.SearchBlocksAsync("Motor", "comment", "FB"))
                    .ReturnsAsync(new[]
                    {
                        new BlockInfo("Motor_FB", "FB", "SCL", "12", comment: "Motor logic")
                    });
                var command = new SearchBlocksCommand(service.Object);

                var result = await command.ExecuteAsync(Args(
                    ("plc-name", "PLC_1"),
                    ("pattern", "Motor"),
                    ("search-in", "comment"),
                    ("filter-type", "FB"),
                    ("output-path", outputPath)), NullLogger.Instance);

                Assert.IsTrue(result.Success, result.Error);
                var artifact = result.Data as CommandArtifactResult;
                Assert.IsNotNull(artifact);
                Assert.AreEqual(outputPath, artifact!.OutputPath);
                Assert.AreEqual("plc-block-search-results", artifact.DataKind);
                Assert.AreEqual(1, artifact.ItemCount);
                Assert.IsTrue(File.Exists(outputPath));
                StringAssert.Contains(File.ReadAllText(outputPath), "Motor_FB,FB,SCL,12,False,Motor logic");
            }
            finally
            {
                DeleteParentDirectory(outputPath);
            }
        }

        [TestMethod]
        public async Task SearchBlocks_DefaultsToNameSearch()
        {
            var service = CreateOpenPlcService("PLC_1");
            service.Setup(candidate => candidate.SearchBlocksAsync("Motor", "name", null))
                .ReturnsAsync(Array.Empty<BlockInfo>());
            var command = new SearchBlocksCommand(service.Object);

            var result = await command.ExecuteAsync(Args(
                ("plc-name", "PLC_1"),
                ("pattern", "Motor")), NullLogger.Instance);

            Assert.IsTrue(result.Success, result.Error);
            service.VerifyAll();
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
