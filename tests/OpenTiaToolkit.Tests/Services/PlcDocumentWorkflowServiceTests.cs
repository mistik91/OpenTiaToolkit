using System;
using System.IO;
using System.Text;
using System.Text.Json;
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
    public void Validate_TypedInstructionWithoutTemplate_RequiresLiveProbe()
    {
        WriteUtf8Bom("Probe.s7dcl", "FUNCTION \"Probe\" : Void\r\n{ S7_Language := \"LAD\" } NETWORK RUNG wire#powerrail GT_Contact( in1 := #A, in2 := #B ) Coil( #Q ) END_RUNG END_NETWORK\r\nEND_FUNCTION");
        var result = _service.Validate(_directory, "Probe");
        Assert.IsTrue(result.Valid, string.Join("; ", result.Errors));
        Assert.AreEqual("unknown-requires-live-probe", result.Classification);
        StringAssert.Contains(string.Join("; ", result.Warnings), "no preceding S7_Templates SrcType");
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
    public void Validate_AttrDbWithExportedPins_IsVerified()
    {
        WriteUtf8Bom("Probe.s7dcl", "FUNCTION \"Probe\" : Void\r\nVAR_TEMP Ret : Int; Length : UDInt; Attr : Byte; END_VAR\r\n{ S7_Language := \"LAD\" } NETWORK RUNG wire#powerrail ATTR_DB( REQ := TRUE, DB_NUMBER := 1, RET_VAL => #Ret, DB_LENGTH => #Length, ATTRIB => #Attr ) END_RUNG END_NETWORK\r\nEND_FUNCTION");
        var result = _service.Validate(_directory, "Probe");
        Assert.IsTrue(result.Valid, string.Join("; ", result.Errors));
        Assert.AreEqual("verified", result.Instructions.Single(item => item.Instruction == "ATTR_DB").Classification);
    }

    [TestMethod]
    public void Validate_AttrDbMissingPin_IsRejected()
    {
        WriteUtf8Bom("Probe.s7dcl", "FUNCTION \"Probe\" : Void\r\n{ S7_Language := \"LAD\" } NETWORK RUNG wire#powerrail ATTR_DB( REQ := TRUE, DB_NUMBER := 1 ) END_RUNG END_NETWORK\r\nEND_FUNCTION");
        var result = _service.Validate(_directory, "Probe");
        Assert.IsFalse(result.Valid);
        StringAssert.Contains(string.Join("; ", result.Errors), "RET_VAL");
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
    public void Validate_GlobalDbDuplicateMemberAndUnknownStartTarget_Fails()
    {
        WriteUtf8Bom("Data.s7dcl", "DATA_BLOCK Data\r\nVAR\r\nA : Bool;\r\nA : Int;\r\nEND_VAR\r\nMissing := 1;\r\nEND_DATA_BLOCK");
        var result = _service.Validate(_directory, "Data");
        Assert.IsFalse(result.Valid);
        StringAssert.Contains(string.Join("; ", result.Errors), "declared more than once");
        StringAssert.Contains(string.Join("; ", result.Errors), "undeclared");
    }

    [TestMethod]
    public void Validate_GlobalDbInvalidArrayAndBoolStart_Fails()
    {
        WriteUtf8Bom("Data.s7dcl", "DATA_BLOCK Data\r\nVAR\r\nValues : Array[5..1] of Int;\r\nEnabled : Bool;\r\nEND_VAR\r\nEnabled := 1;\r\nEND_DATA_BLOCK");
        var result = _service.Validate(_directory, "Data");
        Assert.IsFalse(result.Valid);
        StringAssert.Contains(string.Join("; ", result.Errors), "descending array bounds");
        StringAssert.Contains(string.Join("; ", result.Errors), "TRUE or FALSE");
    }

    [TestMethod]
    public void Validate_GlobalDbQuotedMembersAndDuplicateStart_FailsPrecisely()
    {
        WriteUtf8Bom("Data.s7dcl", "{ S7_Optimized := \"TRUE\" }\r\nDATA_BLOCK Data\r\nVAR\r\n\"Line Enabled\" : Bool;\r\nEND_VAR\r\n\"Line Enabled\" := TRUE;\r\n\"Line Enabled\" := FALSE;\r\nEND_DATA_BLOCK");
        var result = _service.Validate(_directory, "Data");
        Assert.IsFalse(result.Valid);
        StringAssert.Contains(string.Join("; ", result.Errors), "more than one start-value");
        Assert.IsFalse(result.Errors.Any(error => error.Contains("undeclared")));
    }

    [TestMethod]
    public void Validate_GlobalDbInvalidStringAndStructBoundary_Fails()
    {
        WriteUtf8Bom("Data.s7dcl", "DATA_BLOCK Data\r\nVAR\r\nText : String[255];\r\nNested : Struct;\r\nValue : Int;\r\nEND_VAR\r\nEND_DATA_BLOCK");
        var result = _service.Validate(_directory, "Data");
        Assert.IsFalse(result.Valid);
        StringAssert.Contains(string.Join("; ", result.Errors), "STRUCT/END_STRUCT");
        StringAssert.Contains(string.Join("; ", result.Errors), "invalid String length");
        Assert.IsTrue(result.Warnings.Any(warning => warning.Contains("S7_Optimized")));
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

    [TestMethod]
    public async Task Transaction_TwoNewBlocks_AppliesOnceAndPersistsCanonicalEvidence()
    {
        WriteUtf8Bom("First.s7dcl", "{ S7_BlockNumber := \"61001\" }\r\nFUNCTION \"First\" : Void\r\nEND_FUNCTION");
        WriteUtf8Bom("Second.s7dcl", "{ S7_BlockNumber := \"61002\" }\r\nFUNCTION_BLOCK \"Second\"\r\nEND_FUNCTION_BLOCK");
        var context = ContextWithGroups();
        var software = (FakePlcSoftware)context.PlcSoftware!;
        var plc = OpenPlcMock();
        plc.Setup(service => service.ImportBlockFromDocumentsAsync(_directory, It.IsAny<string>(), "", "Override"))
            .ReturnsAsync((string _, string name, string _, string _) =>
            {
                var created = new FakeBlock { Name = name, Type = name == "First" ? "FC" : "FB", Number = name == "First" ? 61001 : 61002, ExportContent = File.ReadAllText(Path.Combine(_directory, name + ".s7dcl")) };
                created.OnDelete = () => software.BlockGroup.Blocks.Remove(created);
                software.BlockGroup.Blocks.Add(created);
                return true;
            });
        plc.Setup(service => service.CompileSoftwareAsync(null)).ReturnsAsync(new CompilationResult(true, 0, 0, "clean"));
        var service = new PlcDocumentWorkflowService(plc.Object, context);
        PlcCodeTransaction draft = await service.BeginTransactionAsync("PLC_1", "two-block change");
        service.AddTransactionOperation(draft.TransactionId, _directory, "First", null, "Override");
        service.AddTransactionOperation(draft.TransactionId, _directory, "Second", null, "Override");

        PlcCodeTransaction plan = await service.PlanTransactionAsync(draft.TransactionId);
        string transactionDirectory = Path.Combine(Path.GetTempPath(), "OpenTiaToolkit", "plc-code-transactions", plan.TransactionId);
        Assert.IsTrue(File.Exists(Path.Combine(transactionDirectory, "manifest.json")));
        Assert.IsTrue(File.Exists(Path.Combine(transactionDirectory, "journal.jsonl")));
        PlcCodeTransactionApplyResult result = await service.ApplyTransactionAsync(plan.TransactionId, plan.ApplyToken!);

        Assert.IsTrue(result.Applied, result.Message);
        Assert.IsTrue(result.Verified);
        Assert.AreEqual("Succeeded", result.State);
        Assert.AreEqual(2, result.Transaction.Operations.Count);
        Assert.IsTrue(result.Transaction.Operations.All(operation => operation.ApplyState == "Verified" && !string.IsNullOrWhiteSpace(operation.CanonicalResultHash)));
        Assert.AreEqual(2, software.BlockGroup.Blocks.Count);
        Assert.AreEqual("Succeeded", service.GetTransaction(plan.TransactionId).State);
        Assert.IsTrue(service.DiscardTransaction(plan.TransactionId));
    }

    [TestMethod]
    public async Task Transaction_FinalCompileFailure_RestoresAllTargetsInReverseWithHashProof()
    {
        WriteUtf8Bom("Existing.s7dcl", "{ S7_BlockNumber := \"62001\" }\r\nFUNCTION \"Existing\" : Void\r\nEND_FUNCTION");
        WriteUtf8Bom("Created.s7dcl", "{ S7_BlockNumber := \"62002\" }\r\nFUNCTION \"Created\" : Void\r\nEND_FUNCTION");
        const string baseline = "{ S7_BlockNumber := \"62001\" }\r\nFUNCTION \"Existing\" : Void\r\nEND_FUNCTION";
        var existing = new FakeBlock { Name = "Existing", Type = "FC", Number = 62001, ExportContent = baseline };
        var context = ContextWithGroups(("", existing));
        var software = (FakePlcSoftware)context.PlcSoftware!;
        var plc = OpenPlcMock();
        var importOrder = new List<string>();
        plc.Setup(service => service.ImportBlockFromDocumentsAsync(It.IsAny<string>(), It.IsAny<string>(), "", "Override"))
            .ReturnsAsync((string directory, string name, string _, string _) =>
            {
                importOrder.Add(name);
                if (name == "Existing") existing.ExportContent = File.ReadAllText(Path.Combine(directory, "Existing.s7dcl"));
                else
                {
                    var created = new FakeBlock { Name = "Created", Type = "FC", Number = 62002, ExportContent = File.ReadAllText(Path.Combine(directory, "Created.s7dcl")) };
                    created.OnDelete = () => software.BlockGroup.Blocks.Remove(created);
                    software.BlockGroup.Blocks.Add(created);
                }
                return true;
            });
        plc.SetupSequence(service => service.CompileSoftwareAsync(null))
            .ReturnsAsync(new CompilationResult(true, 0, 0, "baseline"))
            .ReturnsAsync(new CompilationResult(false, 1, 0, "forced"))
            .ReturnsAsync(new CompilationResult(true, 0, 0, "restored"));
        var service = new PlcDocumentWorkflowService(plc.Object, context);
        PlcCodeTransaction draft = await service.BeginTransactionAsync("PLC_1", "rollback all");
        service.AddTransactionOperation(draft.TransactionId, _directory, "Existing", null, "Override");
        service.AddTransactionOperation(draft.TransactionId, _directory, "Created", null, "Override");
        PlcCodeTransaction plan = await service.PlanTransactionAsync(draft.TransactionId);

        PlcCodeTransactionApplyResult result = await service.ApplyTransactionAsync(plan.TransactionId, plan.ApplyToken!);

        Assert.IsFalse(result.Applied);
        Assert.IsTrue(result.RolledBackAndVerified, result.Message);
        Assert.AreEqual("RolledBackAndVerified", result.State);
        Assert.AreEqual(baseline, existing.ExportContent.TrimStart('\uFEFF'));
        Assert.IsFalse(software.BlockGroup.Blocks.Any(block => block.Name == "Created"));
        Assert.IsTrue(result.Transaction.Operations.All(operation => operation.RestorationVerified));
        CollectionAssert.AreEqual(new[] { "Existing", "Created", "Existing" }, importOrder);
        Assert.IsTrue(service.DiscardTransaction(plan.TransactionId));
    }

    [TestMethod]
    public async Task Transaction_OneStaleTarget_RejectsBeforeAnyMutation()
    {
        WriteUtf8Bom("One.s7dcl", "FUNCTION \"One\" : Void\r\nEND_FUNCTION");
        WriteUtf8Bom("Two.s7dcl", "FUNCTION \"Two\" : Void\r\nEND_FUNCTION");
        var one = new FakeBlock { Name = "One", Type = "FC", ExportContent = "FUNCTION \"One\" : Void\r\nEND_FUNCTION" };
        var two = new FakeBlock { Name = "Two", Type = "FC", ExportContent = "FUNCTION \"Two\" : Void\r\nEND_FUNCTION" };
        var context = ContextWithGroups(("", one), ("", two));
        var plc = OpenPlcMock();
        plc.Setup(service => service.CompileSoftwareAsync(null)).ReturnsAsync(new CompilationResult(true, 0, 0, "baseline"));
        var service = new PlcDocumentWorkflowService(plc.Object, context);
        PlcCodeTransaction draft = await service.BeginTransactionAsync("PLC_1", "stale all-or-nothing");
        service.AddTransactionOperation(draft.TransactionId, _directory, "One", null, "Override");
        service.AddTransactionOperation(draft.TransactionId, _directory, "Two", null, "Override");
        PlcCodeTransaction plan = await service.PlanTransactionAsync(draft.TransactionId);
        one.ExportContent += "\r\n// external edit";

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => service.ApplyTransactionAsync(plan.TransactionId, plan.ApplyToken!));

        plc.Verify(service => service.ImportBlockFromDocumentsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        Assert.AreEqual("Stale", service.GetTransaction(plan.TransactionId).State);
        Assert.IsTrue(service.DiscardTransaction(plan.TransactionId));
    }

    [TestMethod]
    public async Task Transaction_TamperedPersistentPlan_IsRejectedBeforeMutation()
    {
        WriteUtf8Bom("Probe.s7dcl", "FUNCTION \"Probe\" : Void\r\nEND_FUNCTION");
        var context = ContextWithGroups();
        var plc = OpenPlcMock();
        plc.Setup(service => service.CompileSoftwareAsync(null)).ReturnsAsync(new CompilationResult(true, 0, 0, "baseline"));
        var service = new PlcDocumentWorkflowService(plc.Object, context);
        PlcCodeTransaction draft = await service.BeginTransactionAsync("PLC_1", "integrity");
        service.AddTransactionOperation(draft.TransactionId, _directory, "Probe", null, "Override");
        PlcCodeTransaction plan = await service.PlanTransactionAsync(draft.TransactionId);
        plan.Operations[0].Target!.Name = "DifferentTarget";
        string manifest = Path.Combine(Path.GetTempPath(), "OpenTiaToolkit", "plc-code-transactions", plan.TransactionId, "manifest.json");
        File.WriteAllText(manifest, JsonSerializer.Serialize(plan, new JsonSerializerOptions { WriteIndented = true }));

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => service.ApplyTransactionAsync(plan.TransactionId, plan.ApplyToken!));

        plc.Verify(service => service.ImportBlockFromDocumentsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        Assert.AreEqual("Stale", service.GetTransaction(plan.TransactionId).State);
        Assert.IsTrue(service.DiscardTransaction(plan.TransactionId));
    }

    [TestMethod]
    public async Task Transaction_InterruptedManifest_NewServiceRecoversConservatively()
    {
        WriteUtf8Bom("Existing.s7dcl", "FUNCTION \"Existing\" : Void\r\nEND_FUNCTION");
        WriteUtf8Bom("Created.s7dcl", "FUNCTION \"Created\" : Void\r\nEND_FUNCTION");
        const string baseline = "FUNCTION \"Existing\" : Void\r\nEND_FUNCTION";
        var existing = new FakeBlock { Name = "Existing", Type = "FC", ExportContent = baseline };
        var context = ContextWithGroups(("", existing));
        var software = (FakePlcSoftware)context.PlcSoftware!;
        var plc = OpenPlcMock();
        plc.Setup(service => service.CompileSoftwareAsync(null)).ReturnsAsync(new CompilationResult(true, 0, 0, "clean"));
        plc.Setup(service => service.ImportBlockFromDocumentsAsync(It.IsAny<string>(), "Existing", "", "Override"))
            .ReturnsAsync((string directory, string _, string _, string _) => { existing.ExportContent = File.ReadAllText(Path.Combine(directory, "Existing.s7dcl")); return true; });
        var firstService = new PlcDocumentWorkflowService(plc.Object, context);
        PlcCodeTransaction draft = await firstService.BeginTransactionAsync("PLC_1", "restart recovery");
        firstService.AddTransactionOperation(draft.TransactionId, _directory, "Existing", null, "Override");
        firstService.AddTransactionOperation(draft.TransactionId, _directory, "Created", null, "Override");
        PlcCodeTransaction plan = await firstService.PlanTransactionAsync(draft.TransactionId);
        existing.ExportContent = "FUNCTION \"Existing\" : Void\r\n// interrupted candidate\r\nEND_FUNCTION";
        var created = new FakeBlock { Name = "Created", Type = "FC", ExportContent = "FUNCTION \"Created\" : Void\r\nEND_FUNCTION" };
        created.OnDelete = () => software.BlockGroup.Blocks.Remove(created);
        software.BlockGroup.Blocks.Add(created);
        plan.State = "Applying";
        plan.ApplyToken = null;
        string manifest = Path.Combine(Path.GetTempPath(), "OpenTiaToolkit", "plc-code-transactions", plan.TransactionId, "manifest.json");
        File.WriteAllText(manifest, JsonSerializer.Serialize(plan, new JsonSerializerOptions { WriteIndented = true }));

        var restartedService = new PlcDocumentWorkflowService(plc.Object, context);
        PlcCodeTransactionApplyResult result = await restartedService.RecoverTransactionAsync(plan.TransactionId);

        Assert.IsTrue(result.RolledBackAndVerified, result.Message);
        Assert.AreEqual(baseline, existing.ExportContent.TrimStart('\uFEFF'));
        Assert.IsFalse(software.BlockGroup.Blocks.Any(block => block.Name == "Created"));
        Assert.IsTrue(restartedService.DiscardTransaction(plan.TransactionId));
    }

    [TestMethod]
    public async Task Transaction_UnprovenRecovery_RetainsPackageAndCannotBeDiscarded()
    {
        WriteUtf8Bom("Probe.s7dcl", "FUNCTION \"Probe\" : Void\r\nEND_FUNCTION");
        var existing = new FakeBlock { Name = "Probe", Type = "FC", ExportContent = "FUNCTION \"Probe\" : Void\r\nEND_FUNCTION" };
        var context = ContextWithGroups(("", existing));
        var plc = OpenPlcMock();
        plc.Setup(service => service.ImportBlockFromDocumentsAsync(It.IsAny<string>(), "Probe", "", "Override")).ReturnsAsync(false);
        plc.SetupSequence(service => service.CompileSoftwareAsync(null))
            .ReturnsAsync(new CompilationResult(true, 0, 0, "baseline"))
            .ReturnsAsync(new CompilationResult(false, 1, 0, "forced"))
            .ReturnsAsync(new CompilationResult(false, 1, 0, "not restored"));
        var service = new PlcDocumentWorkflowService(plc.Object, context);
        PlcCodeTransaction draft = await service.BeginTransactionAsync("PLC_1", "retain recovery");
        service.AddTransactionOperation(draft.TransactionId, _directory, "Probe", null, "Override");
        PlcCodeTransaction plan = await service.PlanTransactionAsync(draft.TransactionId);

        PlcCodeTransactionApplyResult result = await service.ApplyTransactionAsync(plan.TransactionId, plan.ApplyToken!);

        Assert.IsTrue(result.RecoveryRequired);
        Assert.AreEqual("RecoveryRequired", result.State);
        Assert.IsTrue(result.Transaction.RecoveryPackageRetained);
        Assert.ThrowsException<InvalidOperationException>(() => service.DiscardTransaction(plan.TransactionId));
        string transactionDirectory = Path.Combine(Path.GetTempPath(), "OpenTiaToolkit", "plc-code-transactions", plan.TransactionId);
        Assert.IsTrue(File.Exists(Path.Combine(transactionDirectory, "recovery.json")));
        if (Directory.Exists(transactionDirectory)) Directory.Delete(transactionDirectory, true);
    }

    [TestMethod]
    public async Task Transaction_SclSource_GeneratesMultipleExactBlocksAndCanonicalEvidence()
    {
        string sourcePath = Path.Combine(_directory, "UniqueSource.scl");
        File.WriteAllText(sourcePath, "FUNCTION \"SclA\" : Void\r\nBEGIN\r\nEND_FUNCTION\r\nFUNCTION_BLOCK \"SclB\"\r\nBEGIN\r\nEND_FUNCTION_BLOCK\r\n", new UTF8Encoding(true));
        var context = ContextWithGroups();
        var software = (FakePlcSoftware)context.PlcSoftware!;
        var plc = OpenPlcMock();
        plc.Setup(service => service.ListExternalSourcesAsync()).ReturnsAsync(Array.Empty<PlcSurfaceItem>());
        plc.Setup(service => service.ImportSourceAsync(sourcePath, false, true)).ReturnsAsync(new PlcSourceOperationResult(true, true, "imported", "UniqueSource"));
        plc.Setup(service => service.GenerateBlocksFromSourceAsync("UniqueSource", false, true)).ReturnsAsync(() =>
        {
            software.BlockGroup.Blocks.Add(new FakeBlock { Name = "SclA", Type = "FC", ExportContent = "FUNCTION \"SclA\" : Void\r\nEND_FUNCTION" });
            software.BlockGroup.Blocks.Add(new FakeBlock { Name = "SclB", Type = "FB", ExportContent = "FUNCTION_BLOCK \"SclB\"\r\nEND_FUNCTION_BLOCK" });
            return new PlcSourceOperationResult(true, true, "generated", "UniqueSource");
        });
        plc.Setup(service => service.CompileSoftwareAsync(null)).ReturnsAsync(new CompilationResult(true, 0, 0, "clean"));
        var service = new PlcDocumentWorkflowService(plc.Object, context);
        PlcCodeTransaction draft = await service.BeginTransactionAsync("PLC_1", "SCL multi-block");
        service.AddSclTransactionOperation(draft.TransactionId, sourcePath, null);
        PlcCodeTransaction plan = await service.PlanTransactionAsync(draft.TransactionId);

        PlcCodeTransactionApplyResult result = await service.ApplyTransactionAsync(plan.TransactionId, plan.ApplyToken!);

        Assert.IsTrue(result.Applied, result.Message);
        Assert.AreEqual(2, result.Transaction.Operations.Single().TargetSnapshots.Count);
        Assert.IsTrue(result.Transaction.Operations.Single().TargetSnapshots.All(target => !string.IsNullOrWhiteSpace(target.CanonicalResultHash)));
        Assert.IsTrue(service.DiscardTransaction(plan.TransactionId));
    }

    [TestMethod]
    public async Task Transaction_SclSourceWithType_IsRejectedBeforeMutation()
    {
        string sourcePath = Path.Combine(_directory, "TypeAndBlock.scl");
        File.WriteAllText(sourcePath, "TYPE \"UnsafeType\"\r\nSTRUCT\r\nValue : Int;\r\nEND_STRUCT;\r\nEND_TYPE\r\nFUNCTION \"SafeBlock\" : Void\r\nBEGIN\r\nEND_FUNCTION\r\n", new UTF8Encoding(true));
        var plc = OpenPlcMock();
        var service = new PlcDocumentWorkflowService(plc.Object, ContextWithGroups());
        PlcCodeTransaction draft = await service.BeginTransactionAsync("PLC_1", "reject unsnapshotted type");
        service.AddSclTransactionOperation(draft.TransactionId, sourcePath, null);

        InvalidOperationException error = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => service.PlanTransactionAsync(draft.TransactionId));

        StringAssert.Contains(error.Message, "PLC data type");
        plc.Verify(candidate => candidate.ImportSourceAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
        Assert.IsTrue(service.DiscardTransaction(draft.TransactionId));
    }

    [TestMethod]
    public async Task Transaction_SclCompileFailure_RestoresExistingDeletesNewAndSource()
    {
        string sourcePath = Path.Combine(_directory, "UniqueSource.scl");
        File.WriteAllText(sourcePath, "FUNCTION \"SclA\" : Void\r\nBEGIN\r\nEND_FUNCTION\r\nFUNCTION \"SclB\" : Void\r\nBEGIN\r\nEND_FUNCTION\r\n", new UTF8Encoding(true));
        const string baseline = "FUNCTION \"SclA\" : Void\r\nEND_FUNCTION";
        var existing = new FakeBlock { Name = "SclA", Type = "FC", ExportContent = baseline };
        var context = ContextWithGroups(("", existing));
        var software = (FakePlcSoftware)context.PlcSoftware!;
        var plc = OpenPlcMock();
        plc.Setup(service => service.ListExternalSourcesAsync()).ReturnsAsync(Array.Empty<PlcSurfaceItem>());
        plc.Setup(service => service.ImportSourceAsync(sourcePath, false, true)).ReturnsAsync(new PlcSourceOperationResult(true, true, "imported", "UniqueSource"));
        plc.Setup(service => service.GenerateBlocksFromSourceAsync("UniqueSource", false, true)).ReturnsAsync(() =>
        {
            existing.ExportContent = "FUNCTION \"SclA\" : Void\r\n// changed\r\nEND_FUNCTION";
            var created = new FakeBlock { Name = "SclB", Type = "FC", ExportContent = "FUNCTION \"SclB\" : Void\r\nEND_FUNCTION" };
            created.OnDelete = () => software.BlockGroup.Blocks.Remove(created);
            software.BlockGroup.Blocks.Add(created);
            return new PlcSourceOperationResult(true, true, "generated", "UniqueSource");
        });
        plc.Setup(service => service.ImportBlockFromDocumentsAsync(It.IsAny<string>(), "SclA", "", "Override")).ReturnsAsync((string directory, string _, string _, string _) => { existing.ExportContent = File.ReadAllText(Path.Combine(directory, "SclA.s7dcl")); return true; });
        plc.Setup(service => service.DeleteExternalSourceAsync("UniqueSource", true)).ReturnsAsync(new PlcSourceOperationResult(true, true, "deleted", "UniqueSource"));
        plc.SetupSequence(service => service.CompileSoftwareAsync(null))
            .ReturnsAsync(new CompilationResult(true, 0, 0, "baseline"))
            .ReturnsAsync(new CompilationResult(false, 1, 0, "forced"))
            .ReturnsAsync(new CompilationResult(true, 0, 0, "restored"));
        var service = new PlcDocumentWorkflowService(plc.Object, context);
        PlcCodeTransaction draft = await service.BeginTransactionAsync("PLC_1", "SCL rollback");
        service.AddSclTransactionOperation(draft.TransactionId, sourcePath, null);
        PlcCodeTransaction plan = await service.PlanTransactionAsync(draft.TransactionId);

        PlcCodeTransactionApplyResult result = await service.ApplyTransactionAsync(plan.TransactionId, plan.ApplyToken!);

        Assert.IsTrue(result.RolledBackAndVerified, result.Message);
        Assert.AreEqual(baseline, existing.ExportContent.TrimStart('\uFEFF'));
        Assert.IsFalse(software.BlockGroup.Blocks.Any(block => block.Name == "SclB"));
        Assert.IsTrue(service.DiscardTransaction(plan.TransactionId));
    }

    [TestMethod]
    public async Task Transaction_SimaticMlPatch_BindsBaselineAndVerifiesCanonicalBlock()
    {
        const string baselineXml = "<?xml version=\"1.0\"?><Document xmlns=\"urn:siemens:test\"><SW.Blocks.FC ID=\"1\"><AttributeList><Name>XmlProbe</Name><Title>Before</Title></AttributeList></SW.Blocks.FC></Document>";
        string baselinePath = Path.Combine(_directory, "baseline.xml");
        string candidatePath = Path.Combine(_directory, "candidate.xml");
        File.WriteAllText(baselinePath, baselineXml, new UTF8Encoding(true));
        File.WriteAllText(candidatePath, baselineXml.Replace("Before", "After"), new UTF8Encoding(true));
        var existing = new FakeBlock { Name = "XmlProbe", Type = "FC", Number = 42, ExportContent = "FUNCTION \"XmlProbe\" : Void\r\nEND_FUNCTION" };
        var plc = OpenPlcMock();
        plc.Setup(service => service.ExportBlockAsync("XmlProbe", It.IsAny<string>())).ReturnsAsync((string _, string path) => { File.Copy(baselinePath, path, true); return true; });
        plc.Setup(service => service.ImportBlockAsync(candidatePath, "")).ReturnsAsync(true);
        plc.Setup(service => service.CompileSoftwareAsync(null)).ReturnsAsync(new CompilationResult(true, 0, 0, "clean"));
        var service = new PlcDocumentWorkflowService(plc.Object, ContextWithGroups(("", existing)));
        PlcCodeTransaction draft = await service.BeginTransactionAsync("PLC_1", "XML patch");
        service.AddSimaticMlTransactionOperation(draft.TransactionId, candidatePath, baselinePath, null, false);

        PlcCodeTransaction plan = await service.PlanTransactionAsync(draft.TransactionId);
        PlcCodeTransactionApplyResult result = await service.ApplyTransactionAsync(plan.TransactionId, plan.ApplyToken!);

        Assert.IsTrue(result.Applied, result.Message);
        Assert.AreEqual("PatchSimaticMlBlock", result.Transaction.Operations.Single().Kind);
        Assert.AreEqual(result.Transaction.Operations.Single().BaselineHash, result.Transaction.Operations.Single().CanonicalResultHash);
        Assert.IsTrue(service.DiscardTransaction(plan.TransactionId));
    }

    [TestMethod]
    public async Task Transaction_SimaticMlBaselineMismatch_RejectsBeforeImport()
    {
        const string baselineXml = "<?xml version=\"1.0\"?><Document xmlns=\"urn:siemens:test\"><SW.Blocks.FC ID=\"1\"><AttributeList><Name>XmlProbe</Name></AttributeList></SW.Blocks.FC></Document>";
        string baselinePath = Path.Combine(_directory, "baseline.xml");
        string candidatePath = Path.Combine(_directory, "candidate.xml");
        File.WriteAllText(baselinePath, baselineXml, new UTF8Encoding(true));
        File.WriteAllText(candidatePath, baselineXml, new UTF8Encoding(true));
        var existing = new FakeBlock { Name = "XmlProbe", Type = "FC", ExportContent = "FUNCTION \"XmlProbe\" : Void\r\nEND_FUNCTION" };
        var plc = OpenPlcMock();
        plc.Setup(service => service.ExportBlockAsync("XmlProbe", It.IsAny<string>())).ReturnsAsync((string _, string path) => { File.WriteAllText(path, baselineXml.Replace("ID=\"1\"", "ID=\"2\""), new UTF8Encoding(true)); return true; });
        var service = new PlcDocumentWorkflowService(plc.Object, ContextWithGroups(("", existing)));
        PlcCodeTransaction draft = await service.BeginTransactionAsync("PLC_1", "stale XML baseline");
        service.AddSimaticMlTransactionOperation(draft.TransactionId, candidatePath, baselinePath, null, false);

        InvalidOperationException error = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => service.PlanTransactionAsync(draft.TransactionId));

        StringAssert.Contains(error.Message, "does not match the current TIA export");
        plc.Verify(candidate => candidate.ImportBlockAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        Assert.IsTrue(service.DiscardTransaction(draft.TransactionId));
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
