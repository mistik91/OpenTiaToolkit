using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands;
using OpenTiaToolkit.Core.Commands.Libraries;
using OpenTiaToolkit.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Tests.Commands;

[TestClass]
public class LibraryCommandsTests
{
    [TestMethod]
    public async Task OpenGlobalLibrary_DefaultsToReadOnly()
    {
        var service = new Mock<ILibraryService>();
        service.Setup(candidate => candidate.OpenGlobalLibraryAsync(@"C:\Libs\Std.al21", true, false))
            .ReturnsAsync(new GlobalLibraryInfoModel { Name = "Std", Path = @"C:\Libs\Std.al21", IsOpen = true });
        var command = new OpenGlobalLibraryCommand(service.Object);

        var result = await command.ExecuteAsync(Args(("path", @"C:\Libs\Std.al21")), NullLogger.Instance);

        Assert.IsTrue(result.Success, result.Error);
        service.VerifyAll();
    }

    [TestMethod]
    public async Task ListMasterCopies_WritesJsonWhenOutputPathIsProvided()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "master-copies.json");
        try
        {
            var service = new Mock<ILibraryService>();
            service.Setup(candidate => candidate.ListMasterCopiesAsync("global", "Std", "Motion", true))
                .ReturnsAsync(new[]
                {
                    new LibraryMasterCopyInfo { Name = "MotorFB", Path = "Motion/MotorFB", FolderPath = "Motion", Author = "Controls" }
                });
            var command = new ListMasterCopiesCommand(service.Object);

            var result = await command.ExecuteAsync(Args(
                ("library-scope", "global"),
                ("global-library", "Std"),
                ("folder-path", "Motion"),
                ("output-path", outputPath)), NullLogger.Instance);

            Assert.IsTrue(result.Success, result.Error);
            var artifact = result.Data as CommandArtifactResult;
            Assert.IsNotNull(artifact);
            Assert.AreEqual("library-master-copies", artifact!.DataKind);
            Assert.AreEqual(1, artifact.ItemCount);
            Assert.IsTrue(File.Exists(outputPath));
            StringAssert.Contains(File.ReadAllText(outputPath), "\"name\": \"MotorFB\"");
            service.VerifyAll();
        }
        finally
        {
            DeleteParentDirectory(outputPath);
        }
    }

    [TestMethod]
    public async Task CopyFromMasterCopy_PreservesServiceFailureData()
    {
        var service = new Mock<ILibraryService>();
        service.Setup(candidate => candidate.CopyFromMasterCopyAsync(
                "project", "Motion/MotorFB", "PLC_1", "Program Blocks", null, false, false))
            .ReturnsAsync(new CopyFromMasterCopyResult
            {
                Success = false,
                DryRun = false,
                MasterCopyPath = "Motion/MotorFB",
                PlcName = "PLC_1",
                TargetGroupPath = "Program Blocks",
                Message = "Copying from a master copy requires confirm=true when dry-run is false."
            });
        var command = new CopyFromMasterCopyCommand(service.Object);

        var result = await command.ExecuteAsync(Args(
            ("master-copy-path", "Motion/MotorFB"),
            ("plc-name", "PLC_1"),
            ("target-group-path", "Program Blocks"),
            ("dry-run", false)), NullLogger.Instance);

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Error, "confirm=true");
        Assert.IsInstanceOfType(result.Data, typeof(CopyFromMasterCopyResult));
        service.VerifyAll();
    }

    [TestMethod]
    public async Task ListLibraryTypes_ForwardsScopeAndRecursion()
    {
        var service = new Mock<ILibraryService>();
        service.Setup(candidate => candidate.ListLibraryTypesAsync("project", null, "Types", false))
            .ReturnsAsync(new[]
            {
                new LibraryTypeInfo { Name = "Valve", Path = "Types/Valve", VersionCount = 2 }
            });
        var command = new ListLibraryTypesCommand(service.Object);

        var result = await command.ExecuteAsync(Args(
            ("folder-path", "Types"),
            ("recursive", false)), NullLogger.Instance);

        Assert.IsTrue(result.Success, result.Error);
        var items = result.Data as IReadOnlyList<LibraryTypeInfo>;
        Assert.IsNotNull(items);
        Assert.AreEqual("Valve", items!.Single().Name);
        service.VerifyAll();
    }

    [TestMethod]
    public async Task GetLibraryTypeVersions_ReturnsFailureWhenTypePathMissing()
    {
        var service = new Mock<ILibraryService>();
        var command = new GetLibraryTypeVersionsCommand(service.Object);

        var result = await command.ExecuteAsync(CommandArgs.Empty, NullLogger.Instance);

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Error, "type-path");
        service.Verify(candidate => candidate.GetLibraryTypeVersionsAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [TestMethod]
    public async Task CheckLibraryUpdates_WritesJsonArtifact()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "updates.json");
        try
        {
            var service = new Mock<ILibraryService>();
            service.Setup(candidate => candidate.CheckLibraryUpdatesAsync("global", "Std", true))
                .ReturnsAsync(new LibraryUpdateCheckResult
                {
                    Success = true,
                    LibraryScope = "global",
                    GlobalLibrary = "Std",
                    IncludeUpToDate = true,
                    Message = "Library update check found 1 message(s).",
                    Messages = new[]
                    {
                        new LibraryUpdateCheckMessage { Path = "MotorFB", Description = "Outdated instance" }
                    }
                });
            var command = new CheckLibraryUpdatesCommand(service.Object);

            var result = await command.ExecuteAsync(Args(
                ("library-scope", "global"),
                ("global-library", "Std"),
                ("include-up-to-date", true),
                ("output-path", outputPath)), NullLogger.Instance);

            Assert.IsTrue(result.Success, result.Error);
            var artifact = result.Data as CommandArtifactResult;
            Assert.IsNotNull(artifact);
            Assert.AreEqual("library-update-check", artifact!.DataKind);
            Assert.AreEqual(1, artifact.ItemCount);
            StringAssert.Contains(File.ReadAllText(outputPath), "\"description\": \"Outdated instance\"");
            service.VerifyAll();
        }
        finally
        {
            DeleteParentDirectory(outputPath);
        }
    }

    private static CommandArgs Args(params (string Name, object? Value)[] entries)
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
