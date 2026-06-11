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
    public class UpdateTagsFromCsvCommandTests
    {
        [TestMethod]
        public async Task ExecuteAsync_ReturnsFailureWhenServiceReportsErrors()
        {
            var serviceResult = new TagUpdateResult
            {
                Summary = new TagUpdateSummary
                {
                    TotalCsvRows = 1,
                    TagsSkipped = 1
                }
            };
            serviceResult.Errors.Add(new TagUpdateError
            {
                Item = "102MotorRun",
                Error = "Multiple existing tags match base name"
            });

            var plcService = new Mock<IPlcService>();
            plcService.SetupGet(service => service.IsSoftwareOpen).Returns(true);
            plcService.SetupGet(service => service.CurrentPlcName).Returns("PLC_1");
            plcService
                .Setup(service => service.UpdateTagsFromCsvAsync("tags.csv", true))
                .ReturnsAsync(serviceResult);

            var command = new UpdateTagsFromCsvCommand(plcService.Object);
            var args = new CommandArgs(new Dictionary<string, object?>
            {
                ["plc-name"] = "PLC_1",
                ["csv-path"] = "tags.csv"
            });

            var result = await command.ExecuteAsync(args, NullLogger.Instance);

            Assert.IsFalse(result.Success);
            Assert.IsNotNull(result.Data);
            StringAssert.Contains(result.Message, "First error: 102MotorRun");
        }

        [TestMethod]
        public async Task ExecuteAsync_WritesCsvWhenOutputPathIsProvided()
        {
            var outputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "tag-update-results.csv");
            try
            {
                var serviceResult = new TagUpdateResult
                {
                    Summary = new TagUpdateSummary
                    {
                        TotalCsvRows = 2,
                        ExistingTagsScanned = 1,
                        TagsUpdated = 1,
                        TagsCreated = 1
                    }
                };
                serviceResult.Updated.Add(new TagUpdateEntry
                {
                    TagTableName = "Default tag table",
                    MatchedBy = "BaseName",
                    OldName = "101MotorRun",
                    NewName = "201MotorRun",
                    OldAddress = "%M1.0",
                    NewAddress = "%M2.0",
                    OldComment = "Old comment",
                    NewComment = "New comment"
                });
                serviceResult.Created.Add(new TagCreateEntry
                {
                    TagTableName = "Default tag table",
                    TagName = "PumpReady",
                    DataType = "Bool",
                    Address = "%M3.0",
                    Comment = "Created, from CSV"
                });

                var plcService = new Mock<IPlcService>();
                plcService.SetupGet(service => service.IsSoftwareOpen).Returns(true);
                plcService.SetupGet(service => service.CurrentPlcName).Returns("PLC_1");
                plcService
                    .Setup(service => service.UpdateTagsFromCsvAsync("tags.csv", true))
                    .ReturnsAsync(serviceResult);

                var command = new UpdateTagsFromCsvCommand(plcService.Object);
                var args = new CommandArgs(new Dictionary<string, object?>
                {
                    ["plc-name"] = "PLC_1",
                    ["csv-path"] = "tags.csv",
                    ["output-path"] = outputPath
                });

                var result = await command.ExecuteAsync(args, NullLogger.Instance);

                Assert.IsTrue(result.Success, result.Error);
                var artifact = result.Data as CommandArtifactResult;
                Assert.IsNotNull(artifact);
                Assert.AreEqual("csv", artifact!.Format);
                Assert.AreEqual("plc-tag-update-results", artifact.DataKind);
                Assert.AreEqual(2, artifact.ItemCount);
                CollectionAssert.AreEqual(PlcCommandSupport.TagUpdateCsvColumns.ToArray(), artifact.Columns!.ToArray());

                var csv = File.ReadAllText(outputPath);
                StringAssert.Contains(csv, "Kind,LineNumber,TagTableName,MatchedBy,OldName,NewName,OldAddress,NewAddress,DataType,Reason,OldComment,NewComment,Item,Error,Details");
                StringAssert.Contains(csv, "Updated,,Default tag table,BaseName,101MotorRun,201MotorRun,%M1.0,%M2.0,,,Old comment,New comment");
                StringAssert.Contains(csv, "Created,,Default tag table,,,PumpReady,,%M3.0,Bool,,,\"Created, from CSV\"");
            }
            finally
            {
                DeleteParentDirectory(outputPath);
            }
        }

        [TestMethod]
        public async Task ExecuteAsync_ReturnsFailureWithCsvArtifactWhenServiceReportsErrorsAndOutputPathIsProvided()
        {
            var outputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "tag-update-errors.csv");
            try
            {
                var serviceResult = new TagUpdateResult
                {
                    Summary = new TagUpdateSummary
                    {
                        TotalCsvRows = 1,
                        TagsSkipped = 1
                    }
                };
                serviceResult.Errors.Add(new TagUpdateError
                {
                    Item = "102MotorRun",
                    Error = "Multiple existing tags match base name",
                    Details = "Matched two existing tags",
                    LineNumber = 3
                });

                var plcService = new Mock<IPlcService>();
                plcService.SetupGet(service => service.IsSoftwareOpen).Returns(true);
                plcService.SetupGet(service => service.CurrentPlcName).Returns("PLC_1");
                plcService
                    .Setup(service => service.UpdateTagsFromCsvAsync("tags.csv", true))
                    .ReturnsAsync(serviceResult);

                var command = new UpdateTagsFromCsvCommand(plcService.Object);
                var args = new CommandArgs(new Dictionary<string, object?>
                {
                    ["plc-name"] = "PLC_1",
                    ["csv-path"] = "tags.csv",
                    ["output-path"] = outputPath
                });

                var result = await command.ExecuteAsync(args, NullLogger.Instance);

                Assert.IsFalse(result.Success);
                var artifact = result.Data as CommandArtifactResult;
                Assert.IsNotNull(artifact);
                Assert.AreEqual("plc-tag-update-results", artifact!.DataKind);
                StringAssert.Contains(result.Message, "CSV written to");
                StringAssert.Contains(File.ReadAllText(outputPath), "Error,3,,,,,,,,,,,102MotorRun,Multiple existing tags match base name,Matched two existing tags");
            }
            finally
            {
                DeleteParentDirectory(outputPath);
            }
        }

        [TestMethod]
        public async Task ExecuteAsync_LogsIntoSafetyProgramBeforeApplyingWhenPasswordProvided()
        {
            var serviceResult = new TagUpdateResult
            {
                Summary = new TagUpdateSummary
                {
                    TotalCsvRows = 1,
                    ExistingTagsScanned = 1,
                    TagsUpdated = 1
                }
            };

            var plcService = new Mock<IPlcService>();
            plcService.SetupGet(service => service.IsSoftwareOpen).Returns(true);
            plcService.SetupGet(service => service.CurrentPlcName).Returns("PLC_1");
            plcService
                .Setup(service => service.LoginToSafetyOfflineProgramAsync("secret"))
                .ReturnsAsync(new SafetyLoginResult(
                    success: true,
                    serviceAvailable: true,
                    passwordRequired: true,
                    alreadyLoggedIn: false,
                    message: "Logged into the safety offline program."));
            plcService
                .Setup(service => service.UpdateTagsFromCsvAsync("tags.csv", false))
                .ReturnsAsync(serviceResult);

            var command = new UpdateTagsFromCsvCommand(plcService.Object);
            var args = new CommandArgs(new Dictionary<string, object?>
            {
                ["plc-name"] = "PLC_1",
                ["csv-path"] = "tags.csv",
                ["dry-run"] = false,
                ["safety-password"] = "secret"
            });

            var result = await command.ExecuteAsync(args, NullLogger.Instance);

            Assert.IsTrue(result.Success);
            plcService.Verify(service => service.LoginToSafetyOfflineProgramAsync("secret"), Times.Once);
            plcService.Verify(service => service.UpdateTagsFromCsvAsync("tags.csv", false), Times.Once);
        }

        [TestMethod]
        public async Task ExecuteAsync_StopsBeforeApplyWhenSafetyLoginFails()
        {
            var plcService = new Mock<IPlcService>();
            plcService.SetupGet(service => service.IsSoftwareOpen).Returns(true);
            plcService.SetupGet(service => service.CurrentPlcName).Returns("PLC_1");
            plcService
                .Setup(service => service.LoginToSafetyOfflineProgramAsync("wrong"))
                .ReturnsAsync(new SafetyLoginResult(
                    success: false,
                    serviceAvailable: true,
                    passwordRequired: true,
                    alreadyLoggedIn: false,
                    message: "Failed to log into the safety offline program."));

            var command = new UpdateTagsFromCsvCommand(plcService.Object);
            var args = new CommandArgs(new Dictionary<string, object?>
            {
                ["plc-name"] = "PLC_1",
                ["csv-path"] = "tags.csv",
                ["dry-run"] = false,
                ["safety-password"] = "wrong"
            });

            var result = await command.ExecuteAsync(args, NullLogger.Instance);

            Assert.IsFalse(result.Success);
            StringAssert.Contains(result.Message, "Failed to log into the safety offline program");
            plcService.Verify(service => service.UpdateTagsFromCsvAsync(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
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
