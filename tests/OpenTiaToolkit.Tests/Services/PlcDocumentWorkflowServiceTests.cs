using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Moq;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Services;

namespace OpenTiaToolkit.Tests.Services;

[TestClass]
public class PlcDocumentWorkflowServiceTests
{
    private string _directory = null!;
    private PlcDocumentWorkflowService _service = null!;

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), "OpenTiaToolkit.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _service = new PlcDocumentWorkflowService(new Mock<IPlcService>().Object);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [TestMethod]
    public void Validate_LadWithMatchingResources_ReturnsMetadata()
    {
        WriteUtf8Bom("Probe.s7dcl", "{ S7_BlockNumber := \"59001\"; S7_PreferredLanguage := \"LAD\" }\r\nFUNCTION \"Probe\" : Void\r\n{ S7_Language := \"LAD\"; S7_NetworkTitle := \"MLC_Title\" }\r\nNETWORK\r\nRUNG wire#powerrail END_RUNG\r\nEND_NETWORK\r\nEND_FUNCTION");
        WriteUtf8Bom("Probe.s7res", "MultiLingualTexts:\r\n  - id: MLC_Title\r\n    en-US: Probe");

        var result = _service.Validate(_directory, "Probe");

        Assert.IsTrue(result.Valid, string.Join("; ", result.Errors));
        Assert.AreEqual("Probe", result.BlockName);
        Assert.AreEqual("FC", result.BlockType);
        Assert.AreEqual("LAD", result.PreferredLanguage);
        Assert.AreEqual("59001", result.BlockNumber);
        Assert.IsTrue(result.ResourceRequired);
        Assert.IsTrue(result.HasUtf8Bom);
    }

    [TestMethod]
    public void Validate_MissingReferencedResource_Fails()
    {
        WriteUtf8Bom("Probe.s7dcl", "FUNCTION \"Probe\" : Void\r\n{ S7_NetworkTitle := \"MLC_Missing\" }\r\nEND_FUNCTION");
        var result = _service.Validate(_directory, "Probe");
        Assert.IsFalse(result.Valid);
        StringAssert.Contains(string.Join("; ", result.Errors), "S7RES file is missing");
    }

    [TestMethod]
    public void Validate_GlobalDbWithoutResources_IsValid()
    {
        WriteUtf8Bom("DataProbe.s7dcl", "{ S7_Optimized := \"TRUE\" }\r\nDATA_BLOCK DataProbe\r\nVAR Enabled : Bool; END_VAR\r\nEnabled := FALSE;\r\nEND_DATA_BLOCK");
        var result = _service.Validate(_directory, "DataProbe");
        Assert.IsTrue(result.Valid, string.Join("; ", result.Errors));
        Assert.AreEqual("GlobalDB", result.BlockType);
        Assert.IsFalse(result.ResourceRequired);
        Assert.IsNull(result.S7resPath);
    }

    [TestMethod]
    public void Validate_BlockAndFileNameMismatch_Fails()
    {
        WriteUtf8Bom("Probe.s7dcl", "FUNCTION \"Different\" : Void\r\nEND_FUNCTION");
        var result = _service.Validate(_directory, "Probe");
        Assert.IsFalse(result.Valid);
        StringAssert.Contains(string.Join("; ", result.Errors), "does not match file-name");
    }

    [TestMethod]
    public async Task Apply_WhenImportFails_RestoresExistingBlockAndCompiles()
    {
        WriteUtf8Bom("Probe.s7dcl", "FUNCTION \"Probe\" : Void\r\nEND_FUNCTION");
        var existing = new BlockInfoDetail("Probe", "FC", "LAD", "59001", false, null, null, null, null, 0, "LAD");
        var plc = new Mock<IPlcService>();
        plc.SetupGet(service => service.IsSoftwareOpen).Returns(true);
        plc.SetupGet(service => service.CurrentPlcName).Returns("PLC_1");
        plc.Setup(service => service.GetBlockInfoAsync("Probe")).ReturnsAsync(existing);
        plc.Setup(service => service.ExportBlockAsDocumentsAsync("Probe", It.IsAny<string>()))
            .ReturnsAsync((string _, string path) =>
            {
                Directory.CreateDirectory(path);
                File.WriteAllText(Path.Combine(path, "Probe.s7dcl"), "FUNCTION \"Probe\" : Void\r\nEND_FUNCTION", new UTF8Encoding(true));
                return true;
            });
        plc.SetupSequence(service => service.ImportBlockFromDocumentsAsync(It.IsAny<string>(), "Probe", null, "Override"))
            .ReturnsAsync(false)
            .ReturnsAsync(true);
        plc.Setup(service => service.CompileSoftwareAsync(null)).ReturnsAsync(new CompilationResult(true, 0, 0, "ok"));
        var service = new PlcDocumentWorkflowService(plc.Object);

        var plan = await service.PlanAsync("PLC_1", _directory, "Probe", null, "Override");
        var result = await service.ApplyAsync(plan.ApplyToken);

        Assert.IsFalse(result.Applied);
        Assert.IsTrue(result.RollbackAttempted);
        Assert.IsTrue(result.RolledBack);
        plc.Verify(service => service.ImportBlockFromDocumentsAsync(It.IsAny<string>(), "Probe", null, "Override"), Times.Exactly(2));
    }

    private void WriteUtf8Bom(string fileName, string content)
    {
        File.WriteAllText(Path.Combine(_directory, fileName), content, new UTF8Encoding(true));
    }
}
