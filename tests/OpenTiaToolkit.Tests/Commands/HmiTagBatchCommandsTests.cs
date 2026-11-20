using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands;
using OpenTiaToolkit.Core.Commands.Hmi;
using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Services;
using Siemens.Engineering.HmiUnified.HmiTags;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Tests.Commands;

[TestClass]
public class HmiTagBatchCommandsTests
{
    private const string WinCcMl = """
        #Version: 2.0
        SimpleTags:
          Internal:
            DataType: Bool
            InitialValue: True
          External:
            DataType: Bool
            AccessMode: SymbolicAccess
            Connection: LINE
            ControllerDataType: S7PTransBitType
            PlcTag: Area.Status
        """;

    [TestMethod]
    public void WinCcMl_ReadsExpectedValuesAndNormalizesInternalBool()
    {
        var tags = HmiTagBatchInput.ReadWinCcMl(WinCcMl);

        Assert.AreEqual(2, tags.Count);
        Assert.AreEqual("1", tags[0].InitialValue);
        Assert.IsNull(tags[0].Connection);
        Assert.AreEqual("LINE", tags[1].Connection);
        Assert.AreEqual("Area.Status", tags[1].PlcTag);
    }

    [TestMethod]
    public void WinCcMl_RejectsAdditionalTagSections()
    {
        Assert.ThrowsException<InvalidDataException>(() =>
            HmiTagBatchInput.ReadWinCcMl(WinCcMl + "\nArrayTags: {}\n"));
    }

    [TestMethod]
    public void Verifier_RejectsUnresolvedSymbolEvenWhenValidatorDoesNotFlagIt()
    {
        var expected = HmiTagBatchInput.ReadWinCcMl(WinCcMl)[1];
        var readback = new object[] { "External", "Bool", "LINE", "PLC", "", HmiAccessMode.SymbolicAccess, "", HmiTagType.Simple, "Table", "Bool" };

        var failure = Assert.ThrowsException<InvalidOperationException>(() =>
            HmiTagBatchService.Verify(expected, "Table", "PLC", readback, () => Array.Empty<string>()));

        StringAssert.Contains(failure.Message, "External");
        StringAssert.Contains(failure.Message, "binding");
    }

    [TestMethod]
    public void Verifier_AcceptsWinCcMlTimeOnlyWhenBothNativeTypesMatch()
    {
        var input = WinCcMl.Replace("DataType: Bool\n    AccessMode", "DataType: LTime\n    AccessMode")
            .Replace("S7PTransBitType", "S7PTransTimeType");
        var expected = HmiTagBatchInput.ReadWinCcMl(input)[1];
        var readback = new object[]
        {
            "External", "Time", "LINE", "PLC", "Area.Status", HmiAccessMode.SymbolicAccess, "", HmiTagType.Simple, "Table", "LTime"
        };
        HmiTagBatchService.Verify(expected, "Table", "PLC", readback, () => Array.Empty<string>());

        readback[9] = "Time";
        Assert.ThrowsException<InvalidOperationException>(() =>
            HmiTagBatchService.Verify(expected, "Table", "PLC", readback, () => Array.Empty<string>()));
    }

    [TestMethod]
    public async Task Import_ReturnsFailureAndCleansStagingWhenVerificationRejectsNativeSuccess()
    {
        var input = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".hmi.yml");
        DirectoryInfo? stage = null;
        var hmi = new Mock<IHmiService>();
        hmi.SetupGet(value => value.IsHmiOpen).Returns(true);
        hmi.SetupGet(value => value.CurrentHmiName).Returns("HMI");
        hmi.Setup(value => value.ExecuteTagBatchAsync(It.IsAny<HmiTagBatchRequest>(), It.IsAny<CancellationToken>()))
            .Callback<HmiTagBatchRequest, CancellationToken>((request, _) =>
            {
                stage = request.ImportDirectory!;
                Assert.IsTrue(Path.IsPathRooted(stage.ToString()));
                var files = stage.GetFiles();
                Assert.HasCount(1, files);
                Assert.AreEqual(WinCcMl, File.ReadAllText(files[0].FullName));
                HmiTagBatchService.Verify(request.Tags[1], "Table", "PLC",
                    new object[] { "External", "Bool", "LINE", "PLC", "", HmiAccessMode.SymbolicAccess, "", HmiTagType.Simple, "Table", "Bool" },
                    () => new[] { "The property PLC tag is invalid." });
            })
            .ReturnsAsync(new HmiTagBatchResult());

        try
        {
            File.WriteAllText(input, WinCcMl);
            var command = new ImportHmiTagsCommand(hmi.Object);
            var result = await command.ExecuteAsync(new CommandArgs(new Dictionary<string, object?>
            {
                ["hmi-name"] = "HMI",
                ["table-path"] = "Group/Table",
                ["input-path"] = input,
                ["dry-run"] = false,
                ["confirm"] = true
            }), NullLogger.Instance);

            Assert.IsFalse(result.Success);
            StringAssert.Contains(result.Error, "External");
            StringAssert.Contains(result.Error, "PLC tag is invalid");
            Assert.IsNotNull(stage);
            Assert.IsFalse(Directory.Exists(stage.FullName));
        }
        finally
        {
            File.Delete(input);
        }
    }
}
