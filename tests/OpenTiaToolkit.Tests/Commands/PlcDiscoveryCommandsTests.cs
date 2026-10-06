using OpenTiaToolkit.Core.Models;
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

        [TestMethod]
        public async Task CompareSoftware_WritesJsonWhenOutputPathIsProvided()
        {
            var outputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "compare.json");
            try
            {
                var service = CreateOpenPlcService("PLC_1");
                service.Setup(candidate => candidate.CompareSoftwareToOnlineAsync())
                    .ReturnsAsync(new PlcSoftwareCompareResult(
                        true,
                        true,
                        "PLC software differs from the online target: 1 difference(s).",
                        new[]
                        {
                            new PlcSoftwareCompareEntry("PLC_1/Main", "ObjectsDifferent", "Main", "Main", "Changed", true)
                        }));
                var command = new CompareSoftwareCommand(service.Object);

                var result = await command.ExecuteAsync(Args(
                    ("plc-name", "PLC_1"),
                    ("output-path", outputPath)), NullLogger.Instance);

                Assert.IsTrue(result.Success, result.Error);
                var artifact = result.Data as CommandArtifactResult;
                Assert.IsNotNull(artifact);
                Assert.AreEqual(outputPath, artifact!.OutputPath);
                Assert.AreEqual("json", artifact.Format);
                Assert.AreEqual("plc-software-compare", artifact.DataKind);
                Assert.AreEqual(1, artifact.ItemCount);
                Assert.IsTrue(File.Exists(outputPath));
                var json = File.ReadAllText(outputPath);
                StringAssert.Contains(json, "\"differenceCount\": 1");
                StringAssert.Contains(json, "\"state\": \"ObjectsDifferent\"");
            }
            finally
            {
                DeleteParentDirectory(outputPath);
            }
        }

        [TestMethod]
        public async Task CompareSoftware_RejectsUnsupportedTarget()
        {
            var service = CreateOpenPlcService("PLC_1");
            var command = new CompareSoftwareCommand(service.Object);

            var result = await command.ExecuteAsync(Args(
                ("plc-name", "PLC_1"),
                ("target", "project")), NullLogger.Instance);

            Assert.IsFalse(result.Success);
            StringAssert.Contains(result.Error, "Unsupported compare target");
        }

        [TestMethod]
        public async Task DownloadSoftware_DefaultDryRunPlansSimulatorDownload()
        {
            var service = CreateOpenPlcService("PLC_1");
            service.Setup(candidate => candidate.DownloadSoftwareAsync(true, true, false, false))
                .ReturnsAsync(new PlcSoftwareDownloadResult(
                    true,
                    true,
                    true,
                    false,
                    "Dry-run successful. PLC software download is planned for PLCSIM Advanced; no download was started."));
            var command = new DownloadSoftwareCommand(service.Object);

            var result = await command.ExecuteAsync(Args(
                ("plc-name", "PLC_1"),
                ("to-simulator", true)), NullLogger.Instance);

            Assert.IsTrue(result.Success, result.Error);
            var payload = result.Data as PlcSoftwareDownloadResult;
            Assert.IsNotNull(payload);
            Assert.IsTrue(payload!.DryRun);
            Assert.IsFalse(payload.Applied);
            service.VerifyAll();
        }

        [TestMethod]
        public async Task DownloadSoftware_OnlyChangesPassesDeltaOption()
        {
            var service = CreateOpenPlcService("PLC_1");
            service.Setup(candidate => candidate.DownloadSoftwareAsync(true, false, true, true))
                .ReturnsAsync(new PlcSoftwareDownloadResult(
                    true,
                    false,
                    true,
                    true,
                    "PLC software changes downloaded to PLCSIM Advanced.",
                    onlyChanges: true));
            var command = new DownloadSoftwareCommand(service.Object);

            var result = await command.ExecuteAsync(Args(
                ("plc-name", "PLC_1"),
                ("to-simulator", true),
                ("dry-run", false),
                ("confirm", true),
                ("only-changes", true)), NullLogger.Instance);

            Assert.IsTrue(result.Success, result.Error);
            var payload = result.Data as PlcSoftwareDownloadResult;
            Assert.IsNotNull(payload);
            Assert.IsTrue(payload!.OnlyChanges);
            Assert.IsTrue(payload.Applied);
            service.VerifyAll();
        }

        [TestMethod]
        public async Task DownloadSoftware_ReturnsFailureWhenRealHardwareIsRequested()
        {
            var service = CreateOpenPlcService("PLC_1");
            service.Setup(candidate => candidate.DownloadSoftwareAsync(false, false, true, false))
                .ReturnsAsync(new PlcSoftwareDownloadResult(
                    false,
                    false,
                    false,
                    false,
                    "Real-hardware download is blocked. Pass --to-simulator=true to use the guarded PLCSIM Advanced path."));
            var command = new DownloadSoftwareCommand(service.Object);

            var result = await command.ExecuteAsync(Args(
                ("plc-name", "PLC_1"),
                ("to-simulator", false),
                ("dry-run", false),
                ("confirm", true)), NullLogger.Instance);

            Assert.IsFalse(result.Success);
            StringAssert.Contains(result.Error, "Real-hardware download is blocked");
            service.VerifyAll();
        }

        [TestMethod]
        public async Task GetOnlineStatus_GoesOnlineByDefault()
        {
            var service = CreateOpenPlcService("PLC_1");
            service.Setup(candidate => candidate.GetOnlineStatusAsync(true))
                .ReturnsAsync(new PlcOnlineStatusResult(true, "Online", true, "PLC online state: Online."));
            var command = new GetOnlineStatusCommand(service.Object);

            var result = await command.ExecuteAsync(Args(("plc-name", "PLC_1")), NullLogger.Instance);

            Assert.IsTrue(result.Success, result.Error);
            var payload = result.Data as PlcOnlineStatusResult;
            Assert.IsNotNull(payload);
            Assert.IsTrue(payload!.IsOnline);
            service.VerifyAll();
        }

        [TestMethod]
        public async Task StartCpu_RequiresConfirmationFromService()
        {
            var service = CreateOpenPlcService("PLC_1");
            service.Setup(candidate => candidate.StartCpuAsync(false))
                .ReturnsAsync(new PlcCpuControlResult(false, "start", false, "Online", "CPU start requires --confirm=true."));
            var command = new StartCpuCommand(service.Object);

            var result = await command.ExecuteAsync(Args(("plc-name", "PLC_1")), NullLogger.Instance);

            Assert.IsFalse(result.Success);
            StringAssert.Contains(result.Error, "confirm=true");
            service.VerifyAll();
        }

        [TestMethod]
        public async Task StopCpu_PassesConfirmation()
        {
            var service = CreateOpenPlcService("PLC_1");
            service.Setup(candidate => candidate.StopCpuAsync(true))
                .ReturnsAsync(new PlcCpuControlResult(true, "stop", true, "Online", "CPU stop command executed."));
            var command = new StopCpuCommand(service.Object);

            var result = await command.ExecuteAsync(Args(
                ("plc-name", "PLC_1"),
                ("confirm", true)), NullLogger.Instance);

            Assert.IsTrue(result.Success, result.Error);
            var payload = result.Data as PlcCpuControlResult;
            Assert.IsNotNull(payload);
            Assert.AreEqual("stop", payload!.Action);
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
