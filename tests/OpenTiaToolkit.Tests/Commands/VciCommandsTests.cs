using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands;
using OpenTiaToolkit.Core.Commands.Vci;
using OpenTiaToolkit.Core.Models;
using System.Collections.Generic;
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

        private static CommandArgs Args(params (string Name, object Value)[] entries)
        {
            var values = new Dictionary<string, object?>();
            foreach (var entry in entries)
            {
                values[entry.Name] = entry.Value;
            }
            return new CommandArgs(values);
        }
    }
}
