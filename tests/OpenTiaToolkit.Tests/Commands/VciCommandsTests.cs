using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands;
using OpenTiaToolkit.Core.Commands.Vci;
using OpenTiaToolkit.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Tests.Commands
{
    [TestClass]
    public class VciCommandsTests
    {
        [TestMethod]
        public async Task CreateWorkspace_ForwardsRootAndCulture()
        {
            var service = new Mock<IVciService>();
            service.Setup(candidate => candidate.CreateWorkspaceAsync("Git", @"C:\VCI", "en-US"))
                .ReturnsAsync(new VciWorkspaceInfo { Name = "Git", RootPath = @"C:\VCI" });
            var command = new CreateVciWorkspaceCommand(service.Object);

            var result = await command.ExecuteAsync(Args(
                ("workspace-name", "Git"),
                ("root-path", @"C:\VCI"),
                ("workspace-language", "en-US")), NullLogger.Instance);

            Assert.IsTrue(result.Success);
            service.Verify(candidate => candidate.CreateWorkspaceAsync("Git", @"C:\VCI", "en-US"), Times.Once);
        }

        [TestMethod]
        public async Task DeleteWorkspace_RequiresExplicitConfirmation()
        {
            var service = new Mock<IVciService>();
            var command = new DeleteVciWorkspaceCommand(service.Object);

            var result = await command.ExecuteAsync(Args(("workspace-name", "Git")), NullLogger.Instance);

            Assert.IsFalse(result.Success);
            service.Verify(candidate => candidate.DeleteWorkspaceAsync(It.IsAny<string>()), Times.Never);
        }

        [TestMethod]
        public async Task ListMappings_WritesCsvWhenOutputPathIsProvided()
        {
            var outputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "mappings.csv");
            try
            {
                var service = new Mock<IVciService>();
                service.Setup(candidate => candidate.ListMappingsAsync("Git"))
                    .ReturnsAsync(new[]
                    {
                        new VciMappingInfo
                        {
                            WorkspaceName = "Git",
                            ObjectName = "FC_Test",
                            ObjectType = "FC",
                            DirectoryPath = "PLC",
                            FileNameWithoutExtension = "FC_Test",
                            FileFormat = "SimaticSD",
                            Status = "Different"
                        },
                        new VciMappingInfo
                        {
                            WorkspaceName = "Git",
                            ObjectName = "DB_Data",
                            ObjectType = "DB",
                            DirectoryPath = "PLC,Data",
                            FileNameWithoutExtension = "DB_Data",
                            FileFormat = "SimaticSD",
                            Status = "Equal"
                        }
                    });
                var command = new ListVciMappingsCommand(service.Object);

                var result = await command.ExecuteAsync(Args(
                    ("workspace-name", "Git"),
                    ("output-path", outputPath)), NullLogger.Instance);

                Assert.IsTrue(result.Success, result.Error);
                var artifact = result.Data as CommandArtifactResult;
                Assert.IsNotNull(artifact);
                Assert.AreEqual(outputPath, artifact!.OutputPath);
                Assert.AreEqual("csv", artifact.Format);
                Assert.AreEqual("vci-mappings", artifact.DataKind);
                Assert.AreEqual(2, artifact.ItemCount);
                Assert.IsTrue(File.Exists(outputPath));
                var csv = File.ReadAllText(outputPath);
                StringAssert.Contains(csv, "WorkspaceName,ObjectName,ObjectType,DirectoryPath,FileNameWithoutExtension,FileFormat,Status");
                StringAssert.Contains(csv, "Git,FC_Test,FC,PLC,FC_Test,SimaticSD,Different");
                StringAssert.Contains(csv, "Git,DB_Data,DB,\"PLC,Data\",DB_Data,SimaticSD,Equal");
            }
            finally
            {
                DeleteParentDirectory(outputPath);
            }
        }

        [TestMethod]
        public async Task SynchronizeBlock_ParsesDirectionAndMappingOptions()
        {
            var service = new Mock<IVciService>();
            service.Setup(candidate => candidate.SynchronizeBlockAsync(
                    "Git", "PLC_1", "FC_Test", VciSyncDirection.ProjectToWorkspace,
                    true, "PLC", "FC_File", "SimaticSD", "FC", "Blocks"))
                .ReturnsAsync(new VciSyncItemResult { ObjectName = "FC_Test", Success = true });
            var command = new SyncVciBlockCommand(service.Object);

            var result = await command.ExecuteAsync(Args(
                ("workspace-name", "Git"), ("plc-name", "PLC_1"), ("block-name", "FC_Test"),
                ("direction", "project-to-workspace"), ("create-mapping", true),
                ("relative-directory", "PLC"), ("file-name", "FC_File"), ("file-format", "SimaticSD"),
                ("block-type", "FC"), ("group-path", "Blocks")), NullLogger.Instance);

            Assert.IsTrue(result.Success);
            service.VerifyAll();
        }

        [TestMethod]
        public async Task SynchronizeBlocks_PreservesPartialFailureData()
        {
            var service = new Mock<IVciService>();
            service.Setup(candidate => candidate.SynchronizeBlocksAsync(
                    "Git", "PLC_1", VciSyncDirection.WorkspaceToProject,
                    "^FC_", new[] { "FC" }, false, "", null, false))
                .ReturnsAsync(new VciBulkSyncResult { CandidateCount = 2, SynchronizedCount = 1, FailedCount = 1 });
            var command = new SyncVciBlocksCommand(service.Object);

            var result = await command.ExecuteAsync(Args(
                ("workspace-name", "Git"), ("plc-name", "PLC_1"),
                ("direction", "workspace-to-project"), ("filter-regex", "^FC_"),
                ("block-types", new[] { "FC" })), NullLogger.Instance);

            Assert.IsFalse(result.Success);
            Assert.IsNotNull(result.Data);
            StringAssert.Contains(result.Message, "1 failed");
        }

        [TestMethod]
        public async Task SynchronizeBlocks_WritesJsonArtifactAndPreservesFailure()
        {
            var outputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "sync-blocks.json");
            try
            {
                var service = new Mock<IVciService>();
                service.Setup(candidate => candidate.SynchronizeBlocksAsync(
                        "Git", "PLC_1", VciSyncDirection.WorkspaceToProject,
                        "^FC_", new[] { "FC" }, false, "", null, false))
                    .ReturnsAsync(new VciBulkSyncResult
                    {
                        CandidateCount = 2,
                        SynchronizedCount = 1,
                        FailedCount = 1,
                        Items = new List<VciSyncItemResult>
                        {
                            new VciSyncItemResult { ObjectName = "FC_OK", Success = true },
                            new VciSyncItemResult { ObjectName = "FC_Bad", Success = false, Message = "Import failed" }
                        }
                    });
                var command = new SyncVciBlocksCommand(service.Object);

                var result = await command.ExecuteAsync(Args(
                    ("workspace-name", "Git"), ("plc-name", "PLC_1"),
                    ("direction", "workspace-to-project"), ("filter-regex", "^FC_"),
                    ("block-types", new[] { "FC" }), ("output-path", outputPath)), NullLogger.Instance);

                Assert.IsFalse(result.Success);
                var artifact = result.Data as CommandArtifactResult;
                Assert.IsNotNull(artifact);
                Assert.AreEqual(outputPath, artifact!.OutputPath);
                Assert.AreEqual("json", artifact.Format);
                Assert.AreEqual("sync-vci-blocks-result", artifact.DataKind);
                Assert.AreEqual(2, artifact.ItemCount);
                Assert.IsFalse(artifact.InlineOutputSuppressed);
                Assert.IsTrue(File.Exists(outputPath));
                StringAssert.Contains(File.ReadAllText(outputPath), "\"objectName\": \"FC_Bad\"");
            }
            finally
            {
                DeleteParentDirectory(outputPath);
            }
        }

        [TestMethod]
        public async Task SynchronizePlc_ParsesAllSupportedScope()
        {
            var service = new Mock<IVciService>();
            service.Setup(candidate => candidate.SynchronizePlcAsync(
                    "Git", "PLC_1", VciSyncDirection.ProjectToWorkspace, VciPlcScope.AllSupported,
                    true, "PLC", null, true))
                .ReturnsAsync(new VciBulkSyncResult { CandidateCount = 3, SynchronizedCount = 3, MappedCount = 3 });
            var command = new SyncVciPlcCommand(service.Object);

            var result = await command.ExecuteAsync(Args(
                ("workspace-name", "Git"), ("plc-name", "PLC_1"),
                ("direction", "project-to-workspace"), ("scope", "all-supported"),
                ("create-mappings", true), ("relative-directory", "PLC"),
                ("include-skipped", true)), NullLogger.Instance);

            Assert.IsTrue(result.Success);
            service.VerifyAll();
        }

        [TestMethod]
        public async Task SynchronizePlc_AutoWritesJsonWhenInlineResultExceedsThreshold()
        {
            var service = new Mock<IVciService>();
            service.Setup(candidate => candidate.SynchronizePlcAsync(
                    "Git", "PLC_1", VciSyncDirection.ProjectToWorkspace, VciPlcScope.Blocks,
                    false, "", null, true))
                .ReturnsAsync(new VciBulkSyncResult
                {
                    CandidateCount = 500,
                    SynchronizedCount = 500,
                    Items = Enumerable.Range(0, 500)
                        .Select(index => new VciSyncItemResult
                        {
                            ObjectName = "FC_" + index,
                            ObjectType = "FC",
                            ObjectPath = "Program blocks/FC_" + index,
                            Success = true,
                            Message = new string('x', 100)
                        })
                        .ToList()
                });
            var command = new SyncVciPlcCommand(service.Object);

            var result = await command.ExecuteAsync(Args(
                ("workspace-name", "Git"),
                ("plc-name", "PLC_1"),
                ("direction", "project-to-workspace"),
                ("include-skipped", true)), NullLogger.Instance);

            Assert.IsTrue(result.Success, result.Error);
            var artifact = result.Data as CommandArtifactResult;
            Assert.IsNotNull(artifact);
            Assert.AreEqual("json", artifact!.Format);
            Assert.AreEqual("sync-vci-plc-result", artifact.DataKind);
            Assert.IsTrue(artifact.InlineOutputSuppressed);
            Assert.IsTrue(File.Exists(artifact.OutputPath));
            File.Delete(artifact.OutputPath);
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
