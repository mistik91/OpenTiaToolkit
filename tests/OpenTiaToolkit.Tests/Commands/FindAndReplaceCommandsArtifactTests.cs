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
    public class FindAndReplaceCommandsArtifactTests
    {
        [TestMethod]
        public async Task FindReplaceTags_WritesCsvWhenOutputPathIsProvided()
        {
            var outputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "find-replace.csv");
            try
            {
                var serviceResult = new FindReplaceResult
                {
                    Summary = new FindReplaceSummary
                    {
                        TotalProcessed = 2,
                        MatchesFound = 1,
                        ChangesApplied = 1
                    }
                };
                serviceResult.Changes.Add(new ChangeEntry
                {
                    ItemType = "Tag",
                    Path = "TagTable.Default tag table",
                    Before = "Motor_Speed",
                    After = "MotorSpeed"
                });
                serviceResult.Warnings.Add(new WarningEntry
                {
                    Code = "DuplicateTarget",
                    Message = "Target name already exists",
                    Path = "TagTable.Default tag table"
                });

                var plcService = CreateOpenPlcService("PLC_1");
                plcService
                    .Setup(service => service.FindAndReplaceTagsAsync(
                        It.Is<Dictionary<string, string>>(map => map["Motor_Speed"] == "MotorSpeed"),
                        false,
                        true,
                        false))
                    .ReturnsAsync(serviceResult);

                var command = new FindAndReplaceTagsCommand(plcService.Object);
                var result = await command.ExecuteAsync(Args(
                    ("plc-name", "PLC_1"),
                    ("rename-map", new Dictionary<string, string> { ["Motor_Speed"] = "MotorSpeed" }),
                    ("output-path", outputPath)), NullLogger.Instance);

                Assert.IsTrue(result.Success, result.Error);
                var artifact = result.Data as CommandArtifactResult;
                Assert.IsNotNull(artifact);
                Assert.AreEqual("csv", artifact!.Format);
                Assert.AreEqual("plc-tag-find-replace-results", artifact.DataKind);
                Assert.AreEqual(2, artifact.ItemCount);
                Assert.IsFalse(artifact.InlineOutputSuppressed);
                CollectionAssert.AreEqual(PlcCommandSupport.FindReplaceCsvColumns.ToArray(), artifact.Columns!.ToArray());

                var csv = File.ReadAllText(outputPath);
                StringAssert.Contains(csv, "Kind,ItemType,Path,Before,After,Name,Reason,Code,Message,Details");
                StringAssert.Contains(csv, "Change,Tag,TagTable.Default tag table,Motor_Speed,MotorSpeed");
                StringAssert.Contains(csv, "Warning,,TagTable.Default tag table,,,,,DuplicateTarget,Target name already exists");
            }
            finally
            {
                DeleteParentDirectory(outputPath);
            }
        }

        [TestMethod]
        public async Task FindReplaceDbMembers_AutoWritesCsvWhenInlineResultExceedsThreshold()
        {
            var serviceResult = new FindReplaceResult
            {
                Summary = new FindReplaceSummary
                {
                    TotalProcessed = 600,
                    MatchesFound = 600,
                    ChangesApplied = 600
                }
            };

            foreach (var index in Enumerable.Range(0, 600))
            {
                serviceResult.Changes.Add(new ChangeEntry
                {
                    ItemType = "DB Member",
                    Path = "GlobalDB.ProcessData.Member_" + index,
                    Before = "Old_" + index,
                    After = "New_" + index + "_" + new string('x', 80)
                });
            }

            var plcService = CreateOpenPlcService("PLC_1");
            plcService
                .Setup(service => service.FindAndReplaceDbMembersAsync(
                    It.IsAny<Dictionary<string, string>>(),
                    false,
                    true,
                    It.IsAny<string[]>()))
                .ReturnsAsync(serviceResult);

            var command = new FindAndReplaceDbMembersCommand(plcService.Object);
            var result = await command.ExecuteAsync(Args(
                ("plc-name", "PLC_1"),
                ("rename-map", new Dictionary<string, string> { ["Old"] = "New" })), NullLogger.Instance);

            Assert.IsTrue(result.Success, result.Error);
            var artifact = result.Data as CommandArtifactResult;
            Assert.IsNotNull(artifact);
            Assert.AreEqual("csv", artifact!.Format);
            Assert.AreEqual("plc-db-member-find-replace-results", artifact.DataKind);
            Assert.IsTrue(artifact.InlineOutputSuppressed);
            Assert.IsTrue(File.Exists(artifact.OutputPath));
            File.Delete(artifact.OutputPath);
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
