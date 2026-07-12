using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
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
        plc.SetupSequence(service => service.ImportBlockFromDocumentsAsync(It.IsAny<string>(), "Probe", "", "Override"))
            .ReturnsAsync(false)
            .ReturnsAsync(true);
        plc.Setup(service => service.CompileSoftwareAsync(null)).ReturnsAsync(new CompilationResult(true, 0, 0, "ok"));
        var fakeBlock = new FakeBlock { Name = "Probe", Type = "FC", Number = 59001, ProgrammingLanguage = "LAD", ExportContent = "FUNCTION \"Probe\" : Void\r\nEND_FUNCTION" };
        var context = new EngineeringSessionContext { CurrentPlcName = "PLC_1", PlcSoftware = new FakePlcSoftware() };
        ((FakePlcSoftware)context.PlcSoftware).BlockGroup.Blocks.Add(fakeBlock);
        var service = new PlcDocumentWorkflowService(plc.Object, context);

        var plan = await service.PlanAsync("PLC_1", _directory, "Probe", null, "Override");
        var result = await service.ApplyAsync(plan.ApplyToken);

        Assert.IsFalse(result.Applied);
        Assert.IsTrue(result.RollbackAttempted);
        Assert.IsTrue(result.RolledBack);
        Assert.IsTrue(result.RestoredHashMatches);
        plc.Verify(service => service.ImportBlockFromDocumentsAsync(It.IsAny<string>(), "Probe", "", "Override"), Times.Exactly(2));
    }

    [TestMethod]
    public void Validate_KnownExportOnlyInstruction_IsRejected()
    {
        WriteUtf8Bom("Probe.s7dcl", "FUNCTION \"Probe\" : Void\r\n{ S7_Language := \"LAD\" } NETWORK RUNG wire#powerrail PBox( #M ) Coil( #Q ) END_RUNG END_NETWORK\r\nEND_FUNCTION");
        var result = _service.Validate(_directory, "Probe");
        Assert.IsFalse(result.Valid);
        Assert.AreEqual("known-unsupported", result.Classification);
        Assert.IsTrue(result.Instructions.Any(item => item.Instruction == "PBox" && item.Classification == "known-unsupported"));
    }

    [TestMethod]
    public void Validate_ProjectSpecificCall_IsUnknownButValid()
    {
        WriteUtf8Bom("Probe.s7dcl", "FUNCTION \"Probe\" : Void\r\n{ S7_Language := \"LAD\" } NETWORK RUNG wire#powerrail \"ProjectCall\"() END_RUNG END_NETWORK\r\nEND_FUNCTION");
        var result = _service.Validate(_directory, "Probe");
        Assert.IsTrue(result.Valid, string.Join("; ", result.Errors));
        Assert.AreEqual("unknown-requires-live-probe", result.Classification);
    }

    [TestMethod]
    public void Validate_TypedInstructionWithoutTemplate_IsRejected()
    {
        WriteUtf8Bom("Probe.s7dcl", "FUNCTION \"Probe\" : Void\r\n{ S7_Language := \"LAD\" } NETWORK RUNG wire#powerrail GT_Contact( in1 := #A, in2 := #B ) Coil( #Q ) END_RUNG END_NETWORK\r\nEND_FUNCTION");
        var result = _service.Validate(_directory, "Probe");
        Assert.IsFalse(result.Valid);
        StringAssert.Contains(string.Join("; ", result.Errors), "SrcType");
    }

    [TestMethod]
    public void Validate_MoveWithoutNamedOutput_IsRejected()
    {
        WriteUtf8Bom("Probe.s7dcl", "FUNCTION \"Probe\" : Void\r\n{ S7_Language := \"LAD\" } NETWORK RUNG wire#powerrail Move( in := 1 ) END_RUNG END_NETWORK\r\nEND_FUNCTION");
        var result = _service.Validate(_directory, "Probe");
        Assert.IsFalse(result.Valid);
        StringAssert.Contains(string.Join("; ", result.Errors), "out1");
    }

    [TestMethod]
    public void Validate_LocalTimerInFc_IsRejected()
    {
        WriteUtf8Bom("Probe.s7dcl", "FUNCTION \"Probe\" : Void\r\n{ S7_Language := \"LAD\" } NETWORK RUNG wire#powerrail #timer.TON( pt := T#1s, et => ) END_RUNG END_NETWORK\r\nEND_FUNCTION");
        var result = _service.Validate(_directory, "Probe");
        Assert.IsFalse(result.Valid);
        StringAssert.Contains(string.Join("; ", result.Errors), "local to an FC");
    }

    [TestMethod]
    public void Validate_TimerAndEdgeInFbStaticAndTemp_AreVerified()
    {
        WriteUtf8Bom("Probe.s7dcl", "{ S7_PreferredLanguage := \"LAD\" }\r\nFUNCTION_BLOCK \"Probe\"\r\nVAR timer : TON_TIME; END_VAR\r\nVAR_TEMP edge : Bool; END_VAR\r\n{ S7_Language := \"LAD\" } NETWORK RUNG wire#powerrail #timer.TON( pt := T#1s, et => ) P_Trig( #edge ) Coil( #Q ) END_RUNG END_NETWORK\r\nEND_FUNCTION_BLOCK");
        var result = _service.Validate(_directory, "Probe");
        Assert.IsTrue(result.Valid, string.Join("; ", result.Errors));
        Assert.AreEqual("verified", result.Classification);
    }

    [TestMethod]
    public void Validate_CounterMissingRequiredParameter_IsRejectedAsUnknownProbe()
    {
        WriteUtf8Bom("Probe.s7dcl", "FUNCTION_BLOCK \"Probe\"\r\nVAR counter : CTU_INT; END_VAR\r\n{ S7_Language := \"LAD\" } NETWORK RUNG wire#powerrail #counter.CTU( CU := #In, R := #Reset ) END_RUNG END_NETWORK\r\nEND_FUNCTION_BLOCK");
        var result = _service.Validate(_directory, "Probe");
        Assert.IsFalse(result.Valid);
        Assert.AreEqual("unknown-requires-live-probe", result.Classification);
        StringAssert.Contains(string.Join("; ", result.Errors), "PV");
    }

    [TestMethod]
    public void Validate_SafetyLanguage_IsRejected()
    {
        WriteUtf8Bom("Probe.s7dcl", "{ S7_PreferredLanguage := \"F_LAD\" }\r\nFUNCTION \"Probe\" : Void\r\nEND_FUNCTION");
        var result = _service.Validate(_directory, "Probe");
        Assert.IsFalse(result.Valid);
        Assert.AreEqual("known-unsupported", result.Classification);
    }

    [TestMethod]
    public void Validate_BundledSimaticSdAssets_AreVerified()
    {
        string root = FindRepositoryRoot();
        foreach (var item in new[]
        {
            (Directory: Path.Combine(root, "skills", "s7dcl-lad-authoring", "assets"), Name: "Codex_LAD_Probe"),
            (Directory: Path.Combine(root, "skills", "s7dcl-lad-authoring", "assets"), Name: "Codex_LAD_FB_Probe"),
            (Directory: Path.Combine(root, "skills", "s7dcl-lad-authoring", "assets"), Name: "Codex_Mixed_Probe"),
            (Directory: Path.Combine(root, "skills", "s7dcl-data-authoring", "assets"), Name: "Codex_Data_Probe")
        })
        {
            var result = _service.Validate(item.Directory, item.Name);
            Assert.IsTrue(result.Valid, item.Name + ": " + string.Join("; ", result.Errors));
            Assert.AreEqual("verified", result.Classification, item.Name);
        }
    }

    [TestMethod]
    public void Validate_DanglingWire_IsRejected()
    {
        WriteUtf8Bom("Probe.s7dcl", "FUNCTION \"Probe\" : Void\r\n{ S7_Language := \"LAD\" } NETWORK RUNG wire#powerrail Contact( #A ) wire#w1 Coil( #Q ) END_RUNG END_NETWORK\r\nEND_FUNCTION");
        var result = _service.Validate(_directory, "Probe");
        Assert.IsFalse(result.Valid);
        StringAssert.Contains(string.Join("; ", result.Errors), "wire#w1");
    }

    [TestMethod]
    public void Validate_DuplicateResourceDefinition_IsRejected()
    {
        WriteUtf8Bom("Probe.s7dcl", "FUNCTION \"Probe\" : Void\r\n{ S7_BlockTitle := \"MLC_Title\" }\r\nEND_FUNCTION");
        WriteUtf8Bom("Probe.s7res", "MultiLingualTexts:\r\n  - id: MLC_Title\r\n    en-US: One\r\n  - id: MLC_Title\r\n    en-US: Two");
        var result = _service.Validate(_directory, "Probe");
        Assert.IsFalse(result.Valid);
        StringAssert.Contains(string.Join("; ", result.Errors), "more than once");
    }

    [TestMethod]
    public async Task Plan_DuplicateNameOutsideRoot_RequiresExplicitGroup()
    {
        WriteUtf8Bom("Probe.s7dcl", "FUNCTION \"Probe\" : Void\r\nEND_FUNCTION");
        var context = ContextWithGroups(
            ("AreaA", new FakeBlock { Name = "Probe", Type = "FC", Number = 1, ExportContent = "FUNCTION \"Probe\" : Void\r\nEND_FUNCTION" }),
            ("AreaB", new FakeBlock { Name = "Probe", Type = "FC", Number = 2, ExportContent = "FUNCTION \"Probe\" : Void\r\nEND_FUNCTION" }));
        var plc = OpenPlcMock();
        var service = new PlcDocumentWorkflowService(plc.Object, context);

        var error = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => service.PlanAsync("PLC_1", _directory, "Probe", null, "Override"));

        StringAssert.Contains(error.Message, "Supply group-path explicitly");
        StringAssert.Contains(error.Message, "AreaA");
        StringAssert.Contains(error.Message, "AreaB");
    }

    [TestMethod]
    public async Task Plan_ExplicitGroupAndType_BindsExactIdentity()
    {
        WriteUtf8Bom("Probe.s7dcl", "{ S7_BlockNumber := \"2\" }\r\nFUNCTION \"Probe\" : Void\r\nEND_FUNCTION");
        var selected = new FakeBlock { Name = "Probe", Type = "FC", Number = 2, ExportContent = "{ S7_BlockNumber := \"2\" }\r\nFUNCTION \"Probe\" : Void\r\nEND_FUNCTION" };
        var context = ContextWithGroups(("AreaA", new FakeBlock { Name = "Probe", Type = "FC", Number = 1, ExportContent = "FUNCTION \"Probe\" : Void\r\nEND_FUNCTION" }), ("AreaB", selected));
        var service = new PlcDocumentWorkflowService(OpenPlcMock().Object, context);

        var plan = await service.PlanAsync("PLC_1", _directory, "Probe", "Program Blocks/AreaB", "Override");

        Assert.AreEqual("AreaB", plan.Target.GroupPath);
        Assert.AreEqual("FC", plan.Target.BlockType);
        Assert.AreEqual("2", plan.Target.Number);
    }

    [TestMethod]
    public async Task Plan_SameNameWrongTypeInGroup_IsRejected()
    {
        WriteUtf8Bom("Probe.s7dcl", "FUNCTION \"Probe\" : Void\r\nEND_FUNCTION");
        var context = ContextWithGroups(("Area", new FakeBlock { Name = "Probe", Type = "FB", Number = 1, ExportContent = "FUNCTION_BLOCK \"Probe\"\r\nEND_FUNCTION_BLOCK" }));
        var service = new PlcDocumentWorkflowService(OpenPlcMock().Object, context);

        var error = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => service.PlanAsync("PLC_1", _directory, "Probe", "Area", "Override"));

        StringAssert.Contains(error.Message, "document declares 'FC'");
    }

    [TestMethod]
    public async Task Apply_TargetContentChangedAfterPlan_RejectsStaleToken()
    {
        WriteUtf8Bom("Probe.s7dcl", "FUNCTION \"Probe\" : Void\r\nEND_FUNCTION");
        var block = new FakeBlock { Name = "Probe", Type = "FC", Number = 1, ExportContent = "FUNCTION \"Probe\" : Void\r\nEND_FUNCTION" };
        var context = ContextWithGroups(("Area", block));
        var service = new PlcDocumentWorkflowService(OpenPlcMock().Object, context);
        var plan = await service.PlanAsync("PLC_1", _directory, "Probe", "Area", "Override");
        block.ExportContent += "\r\n// changed";

        var error = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => service.ApplyAsync(plan.ApplyToken));

        StringAssert.Contains(error.Message, "token is stale");
        Assert.IsFalse(Directory.Exists(Path.Combine(Path.GetTempPath(), "OpenTiaToolkit", "plc-document-plans", plan.PlanId)), "Stale plans must remove their rollback backup immediately.");
    }

    [TestMethod]
    public void Constructor_RemovesExpiredOrphanedPlanBackup()
    {
        string orphan = Path.Combine(Path.GetTempPath(), "OpenTiaToolkit", "plc-document-plans", "expired-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(orphan);
        File.WriteAllText(Path.Combine(orphan, "backup.s7dcl"), "test");
        Directory.SetLastWriteTimeUtc(orphan, DateTime.UtcNow.AddHours(-1));

        _ = new PlcDocumentWorkflowService(new Mock<IPlcService>().Object);

        Assert.IsFalse(Directory.Exists(orphan));
    }

    [TestMethod]
    public async Task Apply_CompileFailure_RestoresAndHashVerifiesExactBaseline()
    {
        const string baseline = "FUNCTION \"Probe\" : Void\r\nEND_FUNCTION";
        WriteUtf8Bom("Probe.s7dcl", baseline);
        var block = new FakeBlock { Name = "Probe", Type = "FC", Number = 1, ExportContent = baseline };
        var context = ContextWithGroups(("Area", block));
        var plc = OpenPlcMock();
        int imports = 0;
        plc.Setup(service => service.ImportBlockFromDocumentsAsync(It.IsAny<string>(), "Probe", "Area", "Override"))
            .ReturnsAsync((string directory, string _, string? __, string ___) =>
            {
                imports++;
                block.ExportContent = imports == 1 ? baseline + "\r\n// compile-broken" : File.ReadAllText(Path.Combine(directory, "Probe.s7dcl"), Encoding.UTF8).TrimStart('\uFEFF');
                return true;
            });
        plc.SetupSequence(service => service.CompileSoftwareAsync(null))
            .ReturnsAsync(new CompilationResult(false, 1, 0, "forced failure"))
            .ReturnsAsync(new CompilationResult(true, 0, 0, "restored"));
        var service = new PlcDocumentWorkflowService(plc.Object, context);
        var plan = await service.PlanAsync("PLC_1", _directory, "Probe", "Area", "Override");

        var result = await service.ApplyAsync(plan.ApplyToken);

        Assert.IsFalse(result.Applied);
        Assert.IsTrue(result.RolledBack, result.Message);
        Assert.IsTrue(result.RestoredHashMatches);
        Assert.AreEqual(result.ExpectedRestoredHash, result.ActualRestoredHash);
        Assert.AreEqual(2, imports);
    }

    [TestMethod]
    public async Task Apply_NewBlockCompileFailure_DeletesOnlyExactPlannedTarget()
    {
        WriteUtf8Bom("Probe.s7dcl", "{ S7_BlockNumber := \"3\" }\r\nFUNCTION \"Probe\" : Void\r\nEND_FUNCTION");
        var context = ContextWithGroups();
        var software = (FakePlcSoftware)context.PlcSoftware!;
        var targetGroup = new FakeBlockGroup { Name = "Area" };
        software.BlockGroup.Groups.Add(targetGroup);
        var unrelatedGroup = new FakeBlockGroup { Name = "Other" };
        var unrelated = new FakeBlock { Name = "Probe", Type = "FB", Number = 99, ExportContent = "FUNCTION_BLOCK \"Probe\"\r\nEND_FUNCTION_BLOCK" };
        unrelatedGroup.Blocks.Add(unrelated);
        software.BlockGroup.Groups.Add(unrelatedGroup);
        var plc = OpenPlcMock();
        plc.Setup(service => service.ImportBlockFromDocumentsAsync(_directory, "Probe", "Area", "Override"))
            .ReturnsAsync(() =>
            {
                var created = new FakeBlock { Name = "Probe", Type = "FC", Number = 3, ExportContent = "FUNCTION \"Probe\" : Void\r\nEND_FUNCTION" };
                created.OnDelete = () => targetGroup.Blocks.Remove(created);
                targetGroup.Blocks.Add(created);
                return true;
            });
        plc.SetupSequence(service => service.CompileSoftwareAsync(null))
            .ReturnsAsync(new CompilationResult(false, 1, 0, "forced"))
            .ReturnsAsync(new CompilationResult(true, 0, 0, "clean"));
        var service = new PlcDocumentWorkflowService(plc.Object, context);
        var plan = await service.PlanAsync("PLC_1", _directory, "Probe", "Area", "Override");

        var result = await service.ApplyAsync(plan.ApplyToken);

        Assert.IsTrue(result.RolledBack, result.Message);
        Assert.AreEqual("missing", result.ExpectedRestoredHash);
        Assert.AreEqual("missing", result.ActualRestoredHash);
        Assert.AreEqual(0, targetGroup.Blocks.Count);
        Assert.AreSame(unrelated, unrelatedGroup.Blocks.Single());
    }

    private void WriteUtf8Bom(string fileName, string content)
    {
        File.WriteAllText(Path.Combine(_directory, fileName), content, new UTF8Encoding(true));
    }

    private static Mock<IPlcService> OpenPlcMock()
    {
        var plc = new Mock<IPlcService>();
        plc.SetupGet(service => service.IsSoftwareOpen).Returns(true);
        plc.SetupGet(service => service.CurrentPlcName).Returns("PLC_1");
        return plc;
    }

    private static EngineeringSessionContext ContextWithGroups(params (string Group, FakeBlock Block)[] blocks)
    {
        var software = new FakePlcSoftware();
        foreach (var entry in blocks)
        {
            FakeBlockGroup? group = software.BlockGroup.Groups.SingleOrDefault(candidate => candidate.Name == entry.Group);
            if (group == null) { group = new FakeBlockGroup { Name = entry.Group }; software.BlockGroup.Groups.Add(group); }
            group.Blocks.Add(entry.Block);
        }
        return new EngineeringSessionContext { CurrentPlcName = "PLC_1", PlcSoftware = software };
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "skills")) && File.Exists(Path.Combine(directory.FullName, "OpenTiaToolkit.sln"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Repository root was not found from the test output path.");
    }

    private sealed class FakePlcSoftware
    {
        public FakeBlockGroup BlockGroup { get; } = new FakeBlockGroup();
    }

    private sealed class FakeBlockGroup
    {
        public string Name { get; set; } = "Program blocks";
        public List<FakeBlock> Blocks { get; } = new List<FakeBlock>();
        public List<FakeBlockGroup> Groups { get; } = new List<FakeBlockGroup>();
    }

    private sealed class FakeBlock
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = "FC";
        public int Number { get; set; }
        public string ProgrammingLanguage { get; set; } = "LAD";
        public bool IsReadOnly { get; set; }
        public string ExportContent { get; set; } = string.Empty;
        public Action? OnDelete { get; set; }
        public FakeDocumentExportResult ExportAsDocuments(DirectoryInfo directory, string baseName)
        {
            directory.Create();
            File.WriteAllText(Path.Combine(directory.FullName, baseName + ".s7dcl"), ExportContent, new UTF8Encoding(true));
            return new FakeDocumentExportResult { State = "Success" };
        }
        public void Delete() => OnDelete?.Invoke();
    }

    private sealed class FakeDocumentExportResult
    {
        public string State { get; set; } = string.Empty;
    }
}
