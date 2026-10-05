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
    public void ValidateBehaviorPackage_AcceptsMixedSclAndS7dclDisposableObjects()
    {
        WriteUtf8Bom("Behavior.scl", "FUNCTION \"Behavior\" : DInt\r\nBEGIN\r\nBehavior := 1;\r\nEND_FUNCTION\r\nORGANIZATION_BLOCK \"BehaviorCycle\"\r\nBEGIN\r\nEND_ORGANIZATION_BLOCK");
        WriteUtf8Bom("BehaviorIO.s7dcl", "{ S7_Optimized := \"TRUE\" }\r\nDATA_BLOCK \"BehaviorIO\"\r\nVAR Value : DInt; END_VAR\r\nValue := 0;\r\nEND_DATA_BLOCK");
        string package = Path.Combine(_directory, "package.json");
        File.WriteAllText(package, "{\"schemaVersion\":\"1.0\",\"packageId\":\"behavior\",\"artifacts\":[{\"codeFormat\":\"scl\",\"inputPath\":\"Behavior.scl\"},{\"codeFormat\":\"s7dcl\",\"inputPath\":\"BehaviorIO.s7dcl\"}]}");

        PlcCodeArtifactValidationResult result = _service.ValidateBehaviorPackageArtifact(package);

        Assert.IsTrue(result.Valid, string.Join("; ", result.Errors));
        Assert.AreEqual("BehaviorPackage", result.Format);
        CollectionAssert.AreEquivalent(new[] { "Behavior", "BehaviorCycle", "BehaviorIO" }, result.DeclaredObjects.Select(item => item.Name).ToArray());
    }

    [TestMethod]
    public void ValidateBehaviorPackage_RejectsPackageWithoutSchedulerOb()
    {
        WriteUtf8Bom("Behavior.scl", "FUNCTION \"Behavior\" : DInt\r\nBEGIN\r\nBehavior := 1;\r\nEND_FUNCTION");
        WriteUtf8Bom("BehaviorIO.s7dcl", "DATA_BLOCK \"BehaviorIO\"\r\nVAR Value : DInt; END_VAR\r\nEND_DATA_BLOCK");
        string package = Path.Combine(_directory, "package.json");
        File.WriteAllText(package, "{\"schemaVersion\":\"1.0\",\"packageId\":\"behavior\",\"artifacts\":[{\"codeFormat\":\"scl\",\"inputPath\":\"Behavior.scl\"},{\"codeFormat\":\"s7dcl\",\"inputPath\":\"BehaviorIO.s7dcl\"}]}");

        InvalidDataException error = Assert.ThrowsException<InvalidDataException>(() => _service.ValidateBehaviorPackageArtifact(package));

        StringAssert.Contains(error.Message, "ORGANIZATION_BLOCK");
    }

    [TestMethod]
    public void ValidateBehaviorPackage_InstanceDbDoesNotReplaceGlobalContractDb()
    {
        WriteUtf8Bom("Stateful.scl", "FUNCTION_BLOCK \"Engine\"\r\nBEGIN\r\nEND_FUNCTION_BLOCK\r\nDATA_BLOCK \"State\"\r\n\"Engine\"\r\nBEGIN\r\nEND_DATA_BLOCK");
        WriteUtf8Bom("Cycle.scl", "ORGANIZATION_BLOCK \"Cycle\"\r\nBEGIN\r\nEND_ORGANIZATION_BLOCK");
        string package = Path.Combine(_directory, "package.json");
        File.WriteAllText(package, "{\"schemaVersion\":\"1.0\",\"packageId\":\"stateful\",\"artifacts\":[{\"codeFormat\":\"scl\",\"inputPath\":\"Stateful.scl\"},{\"codeFormat\":\"scl\",\"inputPath\":\"Cycle.scl\"}]}");
        InvalidDataException error = Assert.ThrowsException<InvalidDataException>(() => _service.ValidateBehaviorPackageArtifact(package));
        StringAssert.Contains(error.Message, "global DATA_BLOCK");
    }

    [TestMethod]
    public void ValidateBehaviorPackage_RejectsArtifactOutsidePackageDirectory()
    {
        string package = Path.Combine(_directory, "package.json");
        File.WriteAllText(package, "{\"schemaVersion\":\"1.0\",\"packageId\":\"behavior\",\"artifacts\":[{\"codeFormat\":\"scl\",\"inputPath\":\"..\\\\outside.scl\"},{\"codeFormat\":\"s7dcl\",\"inputPath\":\"inside.s7dcl\"}]}");

        InvalidDataException error = Assert.ThrowsException<InvalidDataException>(() => _service.ValidateBehaviorPackageArtifact(package));

        StringAssert.Contains(error.Message, "cannot escape");
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
    public void Validate_NormalizeScaleAndEdgeContacts_WithExportedPins_AreVerified()
    {
        WriteUtf8Bom("Probe.s7dcl", "FUNCTION \"Probe\" : Void\r\nVAR_IN_OUT EdgeP : Bool; EdgeN : Bool; END_VAR\r\nVAR_TEMP R : Real; END_VAR\r\n{ S7_Language := \"LAD\" } NETWORK RUNG wire#powerrail { S7_Templates := \"[SrcType := Int, DestType := Real]\" } Normalize( min := 0, value := 10, max := 100, out => #R ) { S7_Templates := \"[SrcType := Real, DestType := Int]\" } Scale( min := 0.0, value := #R, max := 100.0, out => #Out ) END_RUNG END_NETWORK\r\n{ S7_Language := \"LAD\" } NETWORK RUNG wire#powerrail P_Contact( operand := #In, bit := #EdgeP ) N_Contact( operand := #In, bit := #EdgeN ) Coil( #Q ) END_RUNG END_NETWORK\r\nEND_FUNCTION");

        var result = _service.Validate(_directory, "Probe");

        Assert.IsTrue(result.Valid, string.Join("; ", result.Errors));
        Assert.AreEqual("verified", result.Classification);
        Assert.IsTrue(result.Instructions.Any(item => item.Instruction == "Normalize" && item.Classification == "verified"));
        Assert.IsTrue(result.Instructions.Any(item => item.Instruction == "N_Contact" && item.Classification == "verified"));
    }

    [TestMethod]
    public void Validate_NormalizeWithoutDestTypeAndTimerCoilWithoutTimer_AreRejectedOrProbed()
    {
        WriteUtf8Bom("Probe.s7dcl", "FUNCTION \"Probe\" : Void\r\n{ S7_Language := \"LAD\" } NETWORK RUNG wire#powerrail { S7_Templates := \"SrcType := Int\" } Normalize( min := 0, value := 10, max := 100, out => #R ) { S7_Templates := \"time_type := Time\" } TON_Coil( pt := T#1s ) END_RUNG END_NETWORK\r\nEND_FUNCTION");

        var result = _service.Validate(_directory, "Probe");

        Assert.IsFalse(result.Valid);
        Assert.AreEqual("unknown-requires-live-probe", result.Classification);
        StringAssert.Contains(string.Join("; ", result.Errors), "requires named 'timer'");
        StringAssert.Contains(string.Join("; ", result.Warnings), "no preceding S7_Templates");
    }

    [TestMethod]
    public void Validate_EdgeContactWithTemporaryMemory_IsRejected()
    {
        WriteUtf8Bom("Probe.s7dcl", "FUNCTION \"Probe\" : Void\r\nVAR_TEMP Edge : Bool; END_VAR\r\n{ S7_Language := \"LAD\" } NETWORK RUNG wire#powerrail P_Contact( operand := #In, bit := #Edge ) Coil( #Q ) END_RUNG END_NETWORK\r\nEND_FUNCTION");

        var result = _service.Validate(_directory, "Probe");

        Assert.IsFalse(result.Valid);
        StringAssert.Contains(string.Join("; ", result.Errors), "not VAR_TEMP");
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
    public void Validate_SclNetworkIecCounter_IsVerified()
    {
        WriteUtf8Bom("Probe.s7dcl", "{ S7_PreferredLanguage := \"LAD\" }\r\nFUNCTION_BLOCK \"Probe\"\r\nVAR counter : IEC_COUNTER; END_VAR\r\n{ S7_Language := \"SCL\" } NETWORK #counter.CTU( CU := #In, R := #Reset, PV := #Preset, Q => #Q, CV => #Value ); END_NETWORK\r\nEND_FUNCTION_BLOCK");

        var result = _service.Validate(_directory, "Probe");

        Assert.IsTrue(result.Valid, string.Join("; ", result.Errors));
        Assert.AreEqual("verified", result.Classification);
        Assert.AreEqual("verified", result.Instructions.Single(item => item.Instruction == "CTU").Classification);
    }

    [TestMethod]
    public void Validate_GraphicalLadIecCounter_RemainsProbeOnly()
    {
        WriteUtf8Bom("Probe.s7dcl", "{ S7_PreferredLanguage := \"LAD\" }\r\nFUNCTION_BLOCK \"Probe\"\r\nVAR counter : IEC_COUNTER; END_VAR\r\n{ S7_Language := \"LAD\" } NETWORK RUNG wire#powerrail #counter.CTU( CU := #In, R := #Reset, PV := #Preset, Q => #Q, CV => #Value ) END_RUNG END_NETWORK\r\nEND_FUNCTION_BLOCK");

        var result = _service.Validate(_directory, "Probe");

        Assert.IsTrue(result.Valid, string.Join("; ", result.Errors));
        Assert.AreEqual("unknown-requires-live-probe", result.Classification);
        Assert.AreEqual("unknown-requires-live-probe", result.Instructions.Single(item => item.Instruction == "CTU").Classification);
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
            (Directory: Path.Combine(root, "skills", "s7dcl-lad-authoring", "assets"), Name: "Codex_LAD_Analog_Edge_Probe"),
            (Directory: Path.Combine(root, "skills", "s7dcl-lad-authoring", "assets"), Name: "Codex_LAD_Branch_Timer_Probe"),
            (Directory: Path.Combine(root, "skills", "s7dcl-lad-authoring", "assets"), Name: "Codex_LAD_Counter_Probe"),
            (Directory: Path.Combine(root, "skills", "s7dcl-lad-authoring", "assets"), Name: "Codex_Mixed_Probe"),
            (Directory: Path.Combine(root, "skills", "s7dcl-data-authoring", "assets"), Name: "Codex_Data_Probe"),
            (Directory: Path.Combine(root, "skills", "s7dcl-data-authoring", "assets"), Name: "Codex_Data_Struct_Probe")
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
    public void Validate_GlobalDbAuthoredMemberRetainAttribute_IsRejected()
    {
        WriteUtf8Bom("Data.s7dcl", "{ S7_Optimized := \"TRUE\"; S7_StandardRetain := \"FALSE\" }\r\nDATA_BLOCK Data\r\nVAR\r\n{ S7_Retain := \"Retain\" }\r\nCounter : DInt;\r\nEND_VAR\r\nCounter := 0;\r\nEND_DATA_BLOCK");

        var result = _service.Validate(_directory, "Data");

        Assert.IsFalse(result.Valid);
        Assert.AreEqual("known-unsupported", result.Classification);
        StringAssert.Contains(string.Join("; ", result.Errors), "S7_Retain");
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
            .ReturnsAsync(new CompilationResult(false, 1, 0, "forced", new[] { new CompilationDiagnostic("Error", "Program/Created", "Created", "forced diagnostic", "E-1") }))
            .ReturnsAsync(new CompilationResult(true, 0, 0, "restored", new[] { new CompilationDiagnostic("Information", null, null, "rollback diagnostic", null) }));
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
        var reloaded = new PlcDocumentWorkflowService(plc.Object, context).GetTransaction(plan.TransactionId);
        Assert.AreEqual("forced diagnostic", reloaded.FinalCompilation!.Diagnostics.Single().Description);
        Assert.AreEqual("rollback diagnostic", reloaded.RollbackCompilation!.Diagnostics.Single().Description);
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
            .ReturnsAsync(new CompilationResult(false, 1, 0, "not restored", new[] { new CompilationDiagnostic("Error", "Program/Probe", "Probe", "cleanup remains broken", "E-2") }));
        var service = new PlcDocumentWorkflowService(plc.Object, context);
        PlcCodeTransaction draft = await service.BeginTransactionAsync("PLC_1", "retain recovery");
        service.AddTransactionOperation(draft.TransactionId, _directory, "Probe", null, "Override");
        PlcCodeTransaction plan = await service.PlanTransactionAsync(draft.TransactionId);

        PlcCodeTransactionApplyResult result = await service.ApplyTransactionAsync(plan.TransactionId, plan.ApplyToken!);

        Assert.IsTrue(result.RecoveryRequired);
        Assert.AreEqual("RecoveryRequired", result.State);
        Assert.IsTrue(result.Transaction.RecoveryPackageRetained);
        Assert.AreEqual("cleanup remains broken", service.GetTransaction(plan.TransactionId).RollbackCompilation!.Diagnostics.Single().Description);
        Assert.ThrowsException<InvalidOperationException>(() => service.DiscardTransaction(plan.TransactionId));
        string transactionDirectory = Path.Combine(Path.GetTempPath(), "OpenTiaToolkit", "plc-code-transactions", plan.TransactionId);
        Assert.IsTrue(File.Exists(Path.Combine(transactionDirectory, "recovery.json")));
        if (Directory.Exists(transactionDirectory)) Directory.Delete(transactionDirectory, true);
    }

    [TestMethod]
    public async Task Transaction_InterruptedProbeDisposal_IsProtectedAndRecoverable()
    {
        WriteUtf8Bom("Probe.s7dcl", "FUNCTION \"Probe\" : Void\r\nEND_FUNCTION");
        var context = ContextWithGroups();
        var plc = OpenPlcMock();
        plc.Setup(service => service.CompileSoftwareAsync(null)).ReturnsAsync(new CompilationResult(true, 0, 0, "clean"));
        var service = new PlcDocumentWorkflowService(plc.Object, context);
        PlcCodeTransaction draft = await service.BeginTransactionAsync("PLC_1", "interrupted probe disposal");
        service.AddTransactionOperation(draft.TransactionId, _directory, "Probe", null, "Override");
        PlcCodeTransaction plan = await service.PlanTransactionAsync(draft.TransactionId);
        plan.State = "DisposingProbe";
        plan.ApplyToken = null;
        string manifest = Path.Combine(Path.GetTempPath(), "OpenTiaToolkit", "plc-code-transactions", plan.TransactionId, "manifest.json");
        File.WriteAllText(manifest, JsonSerializer.Serialize(plan, new JsonSerializerOptions { WriteIndented = true }));

        Assert.ThrowsException<InvalidOperationException>(() => service.DiscardTransaction(plan.TransactionId));
        PlcCodeTransactionApplyResult result = await service.RecoverTransactionAsync(plan.TransactionId);

        Assert.IsTrue(result.RolledBackAndVerified, result.Message);
        Assert.IsTrue(service.DiscardTransaction(plan.TransactionId));
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
    public async Task Transaction_SclSourceWithType_GeneratesAndCanonicallyVerifiesType()
    {
        string sourcePath = Path.Combine(_directory, "TypeAndBlock.scl");
        File.WriteAllText(sourcePath, "TYPE \"SafeType\"\r\nSTRUCT\r\nValue : Int;\r\nEND_STRUCT;\r\nEND_TYPE\r\nFUNCTION \"SafeBlock\" : Void\r\nBEGIN\r\nEND_FUNCTION\r\n", new UTF8Encoding(true));
        var plc = OpenPlcMock();
        var context = ContextWithGroups();
        var software = (FakePlcSoftware)context.PlcSoftware!;
        plc.Setup(service => service.ListExternalSourcesAsync()).ReturnsAsync(Array.Empty<PlcSurfaceItem>());
        plc.Setup(service => service.ImportSourceAsync(sourcePath, false, true)).ReturnsAsync(new PlcSourceOperationResult(true, true, "imported", "TypeAndBlock"));
        plc.Setup(service => service.GenerateBlocksFromSourceAsync("TypeAndBlock", false, true)).ReturnsAsync(() =>
        {
            software.TypeGroup.Types.Add(new FakeType { Name = "SafeType", ExportContent = TypeXml("SafeType", "generated") });
            software.BlockGroup.Blocks.Add(new FakeBlock { Name = "SafeBlock", Type = "FC", ExportContent = "FUNCTION \"SafeBlock\" : Void\r\nEND_FUNCTION" });
            return new PlcSourceOperationResult(true, true, "generated", "TypeAndBlock");
        });
        plc.Setup(service => service.CompileSoftwareAsync(null)).ReturnsAsync(new CompilationResult(true, 0, 0, "clean"));
        var service = new PlcDocumentWorkflowService(plc.Object, context);
        PlcCodeTransaction draft = await service.BeginTransactionAsync("PLC_1", "transactional type generation");
        service.AddSclTransactionOperation(draft.TransactionId, sourcePath, "PLC_1/Blocks", "PLC_1/Types");
        PlcCodeTransaction plan = await service.PlanTransactionAsync(draft.TransactionId);
        PlcCodeTransactionApplyResult result = await service.ApplyTransactionAsync(plan.TransactionId, plan.ApplyToken!);

        Assert.IsTrue(result.Applied, result.Message);
        Assert.AreEqual(string.Empty, result.Transaction.Operations.Single().GroupPath);
        Assert.AreEqual(string.Empty, result.Transaction.Operations.Single().TypeGroupPath);
        Assert.IsTrue(result.Transaction.Operations.Single().TargetSnapshots.Any(snapshot => snapshot.Target.BlockType == "TYPE" && snapshot.CanonicalResultHash != null));
        Assert.IsTrue(service.DiscardTransaction(plan.TransactionId));
    }

    [TestMethod]
    public async Task Transaction_SclInstanceDb_UsesExactXmlCanonicalExport()
    {
        var fixture = await RunInstanceDbTransactionAsync();
        Assert.IsTrue(fixture.Result.Verified, fixture.Result.Message);
        var snapshot = fixture.Result.Transaction.Operations.Single().TargetSnapshots.Single(item => item.Target.Name == "State");
        Assert.AreEqual("InstanceDB", snapshot.Target.BlockType);
        Assert.IsFalse(string.IsNullOrWhiteSpace(snapshot.CanonicalResultHash));
        var instance = fixture.Software.BlockGroup.Blocks.Single(item => item.Name == "State");
        Assert.AreEqual(0, instance.DocumentExports);
        Assert.IsTrue(instance.XmlExports > 0);
        Assert.IsTrue(fixture.Service.DiscardTransaction(fixture.Result.TransactionId));
    }

    [TestMethod]
    public async Task Transaction_SclInstanceDbReplacement_IsRejectedBeforeImport()
    {
        string sourcePath = Path.Combine(_directory, "Replacement.scl");
        File.WriteAllText(sourcePath, "FUNCTION_BLOCK \"Engine\"\r\nBEGIN\r\nEND_FUNCTION_BLOCK\r\nDATA_BLOCK \"State\"\r\n\"Engine\"\r\nBEGIN\r\nEND_DATA_BLOCK", new UTF8Encoding(true));
        var plc = OpenPlcMock();
        plc.Setup(item => item.ListExternalSourcesAsync()).ReturnsAsync(Array.Empty<PlcSurfaceItem>());
        var context = ContextWithGroups(("", new FakeBlock { Name = "State", Type = "InstanceDB" }));
        var service = new PlcDocumentWorkflowService(plc.Object, context);
        var draft = await service.BeginTransactionAsync("PLC_1", "unsupported replacement guard");
        service.AddSclTransactionOperation(draft.TransactionId, sourcePath, null);
        var error = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => service.PlanTransactionAsync(draft.TransactionId));
        StringAssert.Contains(error.Message, "lossless instance DB rollback");
        plc.Verify(item => item.ImportSourceAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
        Assert.IsTrue(service.DiscardTransaction(draft.TransactionId));
    }

    [DataTestMethod]
    [DataRow(true, false)]
    [DataRow(false, true)]
    public async Task Transaction_SclInstanceDb_FailedCompileOrXmlExport_CleansInstanceBeforeFb(bool failCompile, bool failXml)
    {
        var fixture = await RunInstanceDbTransactionAsync(failCompile: failCompile, failXml: failXml);
        Assert.IsTrue(fixture.Result.RolledBackAndVerified, fixture.Result.Message);
        CollectionAssert.AreEqual(new[] { "State", "Engine" }, fixture.Deletions.ToArray());
        Assert.AreEqual(0, fixture.Software.BlockGroup.Blocks.Count);
        Assert.IsTrue(fixture.Result.Transaction.Operations.Single().TargetSnapshots.All(item => item.RestorationVerified));
        Assert.IsTrue(fixture.Service.DiscardTransaction(fixture.Result.TransactionId));
    }

    [TestMethod]
    public async Task Transaction_SclWrongSubtype_RequiresRecoveryEvenWhenCompileIsClean()
    {
        var fixture = await RunInstanceDbTransactionAsync(wrongSubtype: true);
        Assert.IsTrue(fixture.Result.RecoveryRequired, fixture.Result.Message);
        Assert.IsFalse(fixture.Result.RolledBackAndVerified);
        StringAssert.Contains(fixture.Result.Transaction.Failure!, "wrong-subtype");
        Assert.IsFalse(fixture.Result.Transaction.Operations.Single().RestorationVerified);
        Assert.IsTrue(fixture.Software.BlockGroup.Blocks.Any(item => item.Name == "State"));
        Assert.IsFalse(fixture.Deletions.Contains("State"), "Never delete an object whose exact type is not proven.");
        // Manual removal is only a fake test fixture action. Supported recovery
        // must still freshly prove every planned target and compilation condition.
        fixture.Software.BlockGroup.Blocks.RemoveAll(item => item.Name == "State");
        var recovered = await fixture.Service.RecoverTransactionAsync(fixture.Result.TransactionId);
        Assert.IsTrue(recovered.RolledBackAndVerified, recovered.Message);
        Assert.AreEqual(0, fixture.Software.BlockGroup.Blocks.Count);
        Assert.IsTrue(fixture.Service.DiscardTransaction(fixture.Result.TransactionId));
    }

    private async Task<(PlcDocumentWorkflowService Service, FakePlcSoftware Software, PlcCodeTransactionApplyResult Result, List<string> Deletions)> RunInstanceDbTransactionAsync(bool failCompile = false, bool failXml = false, bool wrongSubtype = false)
    {
        string sourcePath = Path.Combine(_directory, "Stateful.scl");
        File.WriteAllText(sourcePath, "FUNCTION_BLOCK \"Engine\"\r\nVAR Value : Int; END_VAR\r\nBEGIN\r\nEND_FUNCTION_BLOCK\r\nDATA_BLOCK \"State\"\r\n\"Engine\"\r\nBEGIN\r\nEND_DATA_BLOCK", new UTF8Encoding(true));
        var context = ContextWithGroups();
        var software = (FakePlcSoftware)context.PlcSoftware!;
        var deletions = new List<string>();
        var plc = OpenPlcMock();
        plc.Setup(item => item.ListExternalSourcesAsync()).ReturnsAsync(Array.Empty<PlcSurfaceItem>());
        plc.Setup(item => item.ImportSourceAsync(sourcePath, false, true)).ReturnsAsync(new PlcSourceOperationResult(true, true, "imported", "Stateful"));
        plc.Setup(item => item.GenerateBlocksFromSourceAsync("Stateful", false, true)).ReturnsAsync(() =>
        {
            var fb = new FakeBlock { Name = "Engine", Type = "FB", ExportContent = "FUNCTION_BLOCK \"Engine\"\r\nVAR Value : Int; END_VAR\r\nEND_FUNCTION_BLOCK" };
            var db = new FakeBlock
            {
                Name = "State",
                Type = wrongSubtype ? "GlobalDB" : "InstanceDB",
                XmlExportFails = failXml,
                ExportContent = "<?xml version=\"1.0\"?><Document><SW.Blocks.InstanceDB ID=\"0\"><AttributeList><Name>State</Name><Interface xmlns=\"urn:siemens:test\"/></AttributeList></SW.Blocks.InstanceDB></Document>"
            };
            foreach (var block in new[] { fb, db })
            {
                block.OnDelete = () => { deletions.Add(block.Name); software.BlockGroup.Blocks.Remove(block); };
                software.BlockGroup.Blocks.Add(block);
            }
            return new PlcSourceOperationResult(true, true, "generated", "Stateful");
        });
        plc.Setup(item => item.DeleteExternalSourceAsync("Stateful", true)).ReturnsAsync(new PlcSourceOperationResult(true, true, "deleted", "Stateful"));
        int compile = 0;
        plc.Setup(item => item.CompileSoftwareAsync(null)).ReturnsAsync(() => ++compile == 2 && failCompile
            ? new CompilationResult(false, 1, 0, "forced mid-transaction compile failure") : new CompilationResult(true, 0, 0, "clean"));
        var service = new PlcDocumentWorkflowService(plc.Object, context);
        var draft = await service.BeginTransactionAsync("PLC_1", "generic source-owned instance DB");
        service.AddSclTransactionOperation(draft.TransactionId, sourcePath, null);
        var plan = await service.PlanTransactionAsync(draft.TransactionId);
        var result = await service.ApplyTransactionAsync(plan.TransactionId, plan.ApplyToken!);
        return (service, software, result, deletions);
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
        service.AddSimaticMlTransactionOperation(draft.TransactionId, candidatePath, baselinePath, null, false, allowedChangePaths: "/Document/SW.Blocks.FC[@ID='1']/AttributeList/Title");

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
        File.WriteAllText(candidatePath, baselineXml.Replace("</AttributeList>", "<Title>After</Title></AttributeList>"), new UTF8Encoding(true));
        var existing = new FakeBlock { Name = "XmlProbe", Type = "FC", ExportContent = "FUNCTION \"XmlProbe\" : Void\r\nEND_FUNCTION" };
        var plc = OpenPlcMock();
        plc.Setup(service => service.ExportBlockAsync("XmlProbe", It.IsAny<string>())).ReturnsAsync((string _, string path) => { File.WriteAllText(path, baselineXml.Replace("ID=\"1\"", "ID=\"2\""), new UTF8Encoding(true)); return true; });
        var service = new PlcDocumentWorkflowService(plc.Object, ContextWithGroups(("", existing)));
        PlcCodeTransaction draft = await service.BeginTransactionAsync("PLC_1", "stale XML baseline");
        service.AddSimaticMlTransactionOperation(draft.TransactionId, candidatePath, baselinePath, null, false, allowedChangePaths: "/Document/SW.Blocks.FC[@ID='1']/AttributeList/Title");

        InvalidOperationException error = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => service.PlanTransactionAsync(draft.TransactionId));

        StringAssert.Contains(error.Message, "does not match the current TIA export");
        plc.Verify(candidate => candidate.ImportBlockAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        Assert.IsTrue(service.DiscardTransaction(draft.TransactionId));
    }

    [TestMethod]
    public async Task Transaction_SimaticMlTypePatch_BindsBaselineAndVerifiesCanonicalType()
    {
        string baselinePath = Path.Combine(_directory, "type-baseline.xml");
        string candidatePath = Path.Combine(_directory, "type-candidate.xml");
        File.WriteAllText(baselinePath, TypeXml("XmlType", "before"), new UTF8Encoding(true));
        File.WriteAllText(candidatePath, TypeXml("XmlType", "after"), new UTF8Encoding(true));
        var type = new FakeType { Name = "XmlType", ExportContent = File.ReadAllText(baselinePath) };
        var context = ContextWithGroups();
        ((FakePlcSoftware)context.PlcSoftware!).TypeGroup.Types.Add(type);
        var plc = OpenPlcMock();
        plc.Setup(service => service.ImportTypeAsync(candidatePath, "")).ReturnsAsync(() => { type.ExportContent = File.ReadAllText(candidatePath); return true; });
        plc.Setup(service => service.CompileSoftwareAsync(null)).ReturnsAsync(new CompilationResult(true, 0, 0, "clean"));
        var service = new PlcDocumentWorkflowService(plc.Object, context);
        PlcCodeTransaction draft = await service.BeginTransactionAsync("PLC_1", "XML type patch");
        service.AddSimaticMlTransactionOperation(draft.TransactionId, candidatePath, baselinePath, null, false, allowedChangePaths: "/Document/SW.Types.PlcStruct[@ID='0']/AttributeList/Interface/Marker");

        PlcCodeTransaction plan = await service.PlanTransactionAsync(draft.TransactionId);
        PlcCodeTransactionApplyResult result = await service.ApplyTransactionAsync(plan.TransactionId, plan.ApplyToken!);

        Assert.IsTrue(result.Applied, result.Message);
        Assert.AreEqual("PatchSimaticMlType", result.Transaction.Operations.Single().Kind);
        Assert.AreNotEqual(result.Transaction.Operations.Single().BaselineHash, result.Transaction.Operations.Single().CanonicalResultHash);
        Assert.IsTrue(service.DiscardTransaction(plan.TransactionId));
    }

    [TestMethod]
    public async Task Transaction_SimaticMlTypeCompileFailure_RestoresBaselineHash()
    {
        string baselinePath = Path.Combine(_directory, "type-baseline.xml");
        string candidatePath = Path.Combine(_directory, "type-candidate.xml");
        File.WriteAllText(baselinePath, TypeXml("XmlType", "before"), new UTF8Encoding(true));
        File.WriteAllText(candidatePath, TypeXml("XmlType", "after"), new UTF8Encoding(true));
        var type = new FakeType { Name = "XmlType", ExportContent = File.ReadAllText(baselinePath) };
        var context = ContextWithGroups();
        ((FakePlcSoftware)context.PlcSoftware!).TypeGroup.Types.Add(type);
        var plc = OpenPlcMock();
        plc.Setup(service => service.ImportTypeAsync(It.IsAny<string>(), "")).ReturnsAsync((string path, string _) => { type.ExportContent = File.ReadAllText(path); return true; });
        plc.SetupSequence(service => service.CompileSoftwareAsync(null))
            .ReturnsAsync(new CompilationResult(true, 0, 0, "baseline"))
            .ReturnsAsync(new CompilationResult(false, 1, 0, "forced"))
            .ReturnsAsync(new CompilationResult(true, 0, 0, "restored"));
        var service = new PlcDocumentWorkflowService(plc.Object, context);
        PlcCodeTransaction draft = await service.BeginTransactionAsync("PLC_1", "XML type rollback");
        service.AddSimaticMlTransactionOperation(draft.TransactionId, candidatePath, baselinePath, null, false, allowedChangePaths: "/Document/SW.Types.PlcStruct[@ID='0']/AttributeList/Interface/Marker");
        PlcCodeTransaction plan = await service.PlanTransactionAsync(draft.TransactionId);

        PlcCodeTransactionApplyResult result = await service.ApplyTransactionAsync(plan.TransactionId, plan.ApplyToken!);

        Assert.IsTrue(result.RolledBackAndVerified, result.Message);
        StringAssert.Contains(type.ExportContent, "before");
        Assert.IsTrue(result.Transaction.Operations.Single().RestorationVerified);
        Assert.IsTrue(service.DiscardTransaction(plan.TransactionId));
    }

    [TestMethod]
    public async Task Transaction_DeletePlcType_CompileFailureRestoresExactType()
    {
        var context = ContextWithGroups();
        var software = (FakePlcSoftware)context.PlcSoftware!;
        var type = new FakeType { Name = "DeleteProbe", ExportContent = TypeXml("DeleteProbe", "baseline") };
        type.OnDelete = () => software.TypeGroup.Types.Remove(type);
        software.TypeGroup.Types.Add(type);
        var plc = OpenPlcMock();
        plc.Setup(service => service.GetCrossReferencesAsync("DeleteProbe"))
            .ReturnsAsync(new PlcCrossReferenceResult(true, "DeleteProbe", "No PLC consumers were found."));
        plc.Setup(service => service.ImportTypeAsync(It.IsAny<string>(), "")).ReturnsAsync((string path, string _) =>
        {
            type.ExportContent = File.ReadAllText(path);
            if (!software.TypeGroup.Types.Contains(type)) software.TypeGroup.Types.Add(type);
            return true;
        });
        plc.SetupSequence(service => service.CompileSoftwareAsync(null))
            .ReturnsAsync(new CompilationResult(true, 0, 0, "baseline"))
            .ReturnsAsync(new CompilationResult(false, 1, 0, "forced"))
            .ReturnsAsync(new CompilationResult(true, 0, 0, "restored"));
        var service = new PlcDocumentWorkflowService(plc.Object, context);
        PlcCodeTransaction draft = await service.BeginTransactionAsync("PLC_1", "type delete rollback");
        service.AddDeleteTypeTransactionOperation(draft.TransactionId, "DeleteProbe", null);
        PlcCodeTransaction plan = await service.PlanTransactionAsync(draft.TransactionId);
        Assert.AreEqual("ImpactReviewRequired", plan.State);
        plan = await service.ApproveTransactionImpactAsync(plan.TransactionId, plan.ImpactAnalysis!.Fingerprint, acceptUnknown: false);

        PlcCodeTransactionApplyResult result = await service.ApplyTransactionAsync(plan.TransactionId, plan.ApplyToken!);

        Assert.IsTrue(result.RolledBackAndVerified, result.Message);
        Assert.AreSame(type, software.TypeGroup.Types.Single());
        Assert.IsTrue(result.Transaction.Operations.Single().RestorationVerified);
        Assert.IsTrue(service.DiscardTransaction(plan.TransactionId));
    }

    [TestMethod]
    public async Task Transaction_AutomaticDependencyOrdering_AppliesProviderBeforeConsumer()
    {
        WriteUtf8Bom("Consumer.s7dcl", "FUNCTION \"Consumer\" : Void\r\n{ S7_Language := \"LAD\" } NETWORK RUNG wire#powerrail \"Provider\"() END_RUNG END_NETWORK\r\nEND_FUNCTION");
        WriteUtf8Bom("Provider.s7dcl", "FUNCTION \"Provider\" : Void\r\nEND_FUNCTION");
        var context = ContextWithGroups();
        var software = (FakePlcSoftware)context.PlcSoftware!;
        var plc = OpenPlcMock();
        var importOrder = new List<string>();
        plc.Setup(service => service.ImportBlockFromDocumentsAsync(_directory, It.IsAny<string>(), "", "Override")).ReturnsAsync((string directory, string name, string _, string _) =>
        {
            importOrder.Add(name);
            var created = new FakeBlock { Name = name, Type = "FC", ExportContent = File.ReadAllText(Path.Combine(directory, name + ".s7dcl")) };
            created.OnDelete = () => software.BlockGroup.Blocks.Remove(created);
            software.BlockGroup.Blocks.Add(created);
            return true;
        });
        plc.Setup(service => service.CompileSoftwareAsync(null)).ReturnsAsync(new CompilationResult(true, 0, 0, "clean"));
        var service = new PlcDocumentWorkflowService(plc.Object, context);
        PlcCodeTransaction draft = await service.BeginTransactionAsync("PLC_1", "dependency order");
        service.AddTransactionOperation(draft.TransactionId, _directory, "Consumer", null, "Override");
        service.AddTransactionOperation(draft.TransactionId, _directory, "Provider", null, "Override");

        PlcCodeTransaction plan = await service.PlanTransactionAsync(draft.TransactionId);
        PlcCodeTransactionApplyResult result = await service.ApplyTransactionAsync(plan.TransactionId, plan.ApplyToken!);

        Assert.IsTrue(result.Applied, result.Message);
        CollectionAssert.AreEqual(new[] { "Provider", "Consumer" }, importOrder);
        Assert.AreEqual(2, result.Transaction.Operations.Single(operation => operation.Target!.Name == "Consumer").ExecutionOrder);
        Assert.IsTrue(service.DiscardTransaction(plan.TransactionId));
    }

    [TestMethod]
    public async Task Transaction_DependencyCycle_IsRejectedBeforeMutation()
    {
        WriteUtf8Bom("A.s7dcl", "FUNCTION \"A\" : Void\r\n{ S7_Language := \"LAD\" } NETWORK RUNG wire#powerrail \"B\"() END_RUNG END_NETWORK\r\nEND_FUNCTION");
        WriteUtf8Bom("B.s7dcl", "FUNCTION \"B\" : Void\r\n{ S7_Language := \"LAD\" } NETWORK RUNG wire#powerrail \"A\"() END_RUNG END_NETWORK\r\nEND_FUNCTION");
        var plc = OpenPlcMock();
        var service = new PlcDocumentWorkflowService(plc.Object, ContextWithGroups());
        PlcCodeTransaction draft = await service.BeginTransactionAsync("PLC_1", "dependency cycle");
        service.AddTransactionOperation(draft.TransactionId, _directory, "A", null, "Override");
        service.AddTransactionOperation(draft.TransactionId, _directory, "B", null, "Override");

        InvalidOperationException error = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => service.PlanTransactionAsync(draft.TransactionId));

        StringAssert.Contains(error.Message, "dependency cycle");
        plc.Verify(service => service.ImportBlockFromDocumentsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        Assert.IsTrue(service.DiscardTransaction(draft.TransactionId));
    }

    [TestMethod]
    public async Task Transaction_SecondApplyFailure_RollsBackFirstAppliedTarget()
    {
        WriteUtf8Bom("First.s7dcl", "FUNCTION \"First\" : Void\r\nEND_FUNCTION");
        WriteUtf8Bom("Second.s7dcl", "FUNCTION \"Second\" : Void\r\nEND_FUNCTION");
        var context = ContextWithGroups();
        var software = (FakePlcSoftware)context.PlcSoftware!;
        var plc = OpenPlcMock();
        plc.Setup(service => service.ImportBlockFromDocumentsAsync(_directory, "First", "", "Override")).ReturnsAsync(() =>
        {
            var created = new FakeBlock { Name = "First", Type = "FC", ExportContent = File.ReadAllText(Path.Combine(_directory, "First.s7dcl")) };
            created.OnDelete = () => software.BlockGroup.Blocks.Remove(created);
            software.BlockGroup.Blocks.Add(created);
            return true;
        });
        plc.Setup(service => service.ImportBlockFromDocumentsAsync(_directory, "Second", "", "Override")).ReturnsAsync(false);
        plc.SetupSequence(service => service.CompileSoftwareAsync(null))
            .ReturnsAsync(new CompilationResult(true, 0, 0, "baseline"))
            .ReturnsAsync(new CompilationResult(true, 0, 0, "restored"));
        var service = new PlcDocumentWorkflowService(plc.Object, context);
        PlcCodeTransaction draft = await service.BeginTransactionAsync("PLC_1", "mid-apply failure");
        service.AddTransactionOperation(draft.TransactionId, _directory, "First", null, "Override");
        service.AddTransactionOperation(draft.TransactionId, _directory, "Second", null, "Override");
        PlcCodeTransaction plan = await service.PlanTransactionAsync(draft.TransactionId);

        PlcCodeTransactionApplyResult result = await service.ApplyTransactionAsync(plan.TransactionId, plan.ApplyToken!);

        Assert.IsTrue(result.RolledBackAndVerified, result.Message);
        Assert.AreEqual(0, software.BlockGroup.Blocks.Count);
        Assert.IsTrue(result.Transaction.Operations.All(operation => operation.RestorationVerified));
        Assert.IsTrue(service.DiscardTransaction(plan.TransactionId));
    }

    [TestMethod]
    public async Task Transaction_CanonicalExportFailure_RollsBackCreatedTarget()
    {
        WriteUtf8Bom("Probe.s7dcl", "FUNCTION \"Probe\" : Void\r\nEND_FUNCTION");
        var context = ContextWithGroups();
        var software = (FakePlcSoftware)context.PlcSoftware!;
        var plc = OpenPlcMock();
        plc.Setup(service => service.ImportBlockFromDocumentsAsync(_directory, "Probe", "", "Override")).ReturnsAsync(() =>
        {
            var created = new FakeBlock { Name = "Probe", Type = "FC", ExportContent = File.ReadAllText(Path.Combine(_directory, "Probe.s7dcl")), ExportState = "Failed" };
            created.OnDelete = () => software.BlockGroup.Blocks.Remove(created);
            software.BlockGroup.Blocks.Add(created);
            return true;
        });
        plc.SetupSequence(service => service.CompileSoftwareAsync(null))
            .ReturnsAsync(new CompilationResult(true, 0, 0, "baseline"))
            .ReturnsAsync(new CompilationResult(true, 0, 0, "candidate"))
            .ReturnsAsync(new CompilationResult(true, 0, 0, "restored"));
        var service = new PlcDocumentWorkflowService(plc.Object, context);
        PlcCodeTransaction draft = await service.BeginTransactionAsync("PLC_1", "canonical failure");
        service.AddTransactionOperation(draft.TransactionId, _directory, "Probe", null, "Override");
        PlcCodeTransaction plan = await service.PlanTransactionAsync(draft.TransactionId);

        PlcCodeTransactionApplyResult result = await service.ApplyTransactionAsync(plan.TransactionId, plan.ApplyToken!);

        Assert.IsTrue(result.RolledBackAndVerified, result.Message);
        StringAssert.Contains(result.Transaction.Failure!, "canonical export failed");
        Assert.AreEqual(0, software.BlockGroup.Blocks.Count);
        Assert.IsTrue(service.DiscardTransaction(plan.TransactionId));
    }

    [TestMethod]
    public async Task Transaction_InterfaceChange_RequiresFingerprintBoundImpactApproval()
    {
        const string baseline = "FUNCTION \"ImpactProbe\" : Void\r\nVAR_INPUT\r\n    Enable : Bool;\r\nEND_VAR\r\nEND_FUNCTION";
        const string candidate = "FUNCTION \"ImpactProbe\" : Void\r\nVAR_INPUT\r\n    Enable : Bool;\r\n    Reset : Bool;\r\nEND_VAR\r\nEND_FUNCTION";
        WriteUtf8Bom("ImpactProbe.s7dcl", candidate);
        var existing = new FakeBlock { Name = "ImpactProbe", Type = "FC", Number = 62010, ExportContent = baseline };
        var context = ContextWithGroups(("", existing));
        var plc = OpenPlcMock();
        plc.Setup(service => service.GetCrossReferencesAsync("ImpactProbe")).ReturnsAsync(new PlcCrossReferenceResult(true, "ImpactProbe", "One PLC consumer.", new[]
        {
            new PlcCrossReferenceEntry("Caller", "Program blocks/Caller", "Call", "Block", "Calls ImpactProbe"),
            new PlcCrossReferenceEntry("Enable", "PLC_1/Program blocks/ImpactProbe", null, "Member", "source=ImpactProbe; target=ImpactProbe")
        }));
        plc.Setup(service => service.ImportBlockFromDocumentsAsync(_directory, "ImpactProbe", "", "Override")).ReturnsAsync(() =>
        {
            existing.ExportContent = File.ReadAllText(Path.Combine(_directory, "ImpactProbe.s7dcl"));
            return true;
        });
        plc.Setup(service => service.CompileSoftwareAsync(null)).ReturnsAsync(new CompilationResult(true, 0, 0, "clean"));
        var service = new PlcDocumentWorkflowService(plc.Object, context);
        PlcCodeTransaction draft = await service.BeginTransactionAsync("PLC_1", "interface impact");
        service.AddTransactionOperation(draft.TransactionId, _directory, "ImpactProbe", null, "Override");

        PlcCodeTransaction reviewed = await service.PlanTransactionAsync(draft.TransactionId);

        Assert.AreEqual("ImpactReviewRequired", reviewed.State);
        Assert.IsNull(reviewed.ApplyToken);
        Assert.AreEqual("ReviewRequired", reviewed.ImpactAnalysis!.Status);
        Assert.AreEqual("InterfaceChange", reviewed.ImpactAnalysis.Targets.Single().ChangeKind);
        Assert.AreEqual(1, reviewed.ImpactAnalysis.Targets.Single().Consumers.Count);
        Assert.AreEqual(1, reviewed.ImpactAnalysis.Targets.Single().ExcludedSelfReferenceCount);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => service.ApproveTransactionImpactAsync(reviewed.TransactionId, "wrong", false));

        PlcCodeTransaction plan = await service.ApproveTransactionImpactAsync(reviewed.TransactionId, reviewed.ImpactAnalysis.Fingerprint, false);
        PlcCodeTransactionApplyResult result = await service.ApplyTransactionAsync(plan.TransactionId, plan.ApplyToken!);

        Assert.AreEqual("Planned", plan.State);
        Assert.IsTrue(plan.ImpactAcknowledged);
        Assert.IsTrue(result.Applied, result.Message);
        Assert.IsTrue(service.DiscardTransaction(plan.TransactionId));
    }

    [TestMethod]
    public async Task Transaction_ImpactConsumerChange_MarksAcknowledgementStale()
    {
        WriteUtf8Bom("ImpactProbe.s7dcl", "FUNCTION \"ImpactProbe\" : Void\r\nVAR_INPUT\r\n    Changed : Bool;\r\nEND_VAR\r\nEND_FUNCTION");
        var existing = new FakeBlock { Name = "ImpactProbe", Type = "FC", ExportContent = "FUNCTION \"ImpactProbe\" : Void\r\nEND_FUNCTION" };
        var plc = OpenPlcMock();
        plc.SetupSequence(service => service.GetCrossReferencesAsync("ImpactProbe"))
            .ReturnsAsync(new PlcCrossReferenceResult(true, "ImpactProbe", "first", new[] { new PlcCrossReferenceEntry("CallerA", "CallerA", "Call", "Block", null) }))
            .ReturnsAsync(new PlcCrossReferenceResult(true, "ImpactProbe", "second", new[] { new PlcCrossReferenceEntry("CallerB", "CallerB", "Call", "Block", null) }));
        plc.Setup(service => service.CompileSoftwareAsync(null)).ReturnsAsync(new CompilationResult(true, 0, 0, "clean"));
        var service = new PlcDocumentWorkflowService(plc.Object, ContextWithGroups(("", existing)));
        PlcCodeTransaction draft = await service.BeginTransactionAsync("PLC_1", "stale impact");
        service.AddTransactionOperation(draft.TransactionId, _directory, "ImpactProbe", null, "Override");
        PlcCodeTransaction reviewed = await service.PlanTransactionAsync(draft.TransactionId);

        InvalidOperationException error = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => service.ApproveTransactionImpactAsync(reviewed.TransactionId, reviewed.ImpactAnalysis!.Fingerprint, false));

        StringAssert.Contains(error.Message, "changed after planning");
        Assert.AreEqual("Stale", service.GetTransaction(reviewed.TransactionId).State);
        Assert.IsTrue(service.DiscardTransaction(reviewed.TransactionId));
    }

    [TestMethod]
    public async Task Transaction_ImpactConsumerChangeAfterApproval_RejectsApplyBeforeMutation()
    {
        WriteUtf8Bom("ImpactProbe.s7dcl", "FUNCTION \"ImpactProbe\" : Void\r\nVAR_INPUT\r\n    Changed : Bool;\r\nEND_VAR\r\nEND_FUNCTION");
        var existing = new FakeBlock { Name = "ImpactProbe", Type = "FC", ExportContent = "FUNCTION \"ImpactProbe\" : Void\r\nEND_FUNCTION" };
        var firstEvidence = new PlcCrossReferenceResult(true, "ImpactProbe", "first", new[] { new PlcCrossReferenceEntry("CallerA", "CallerA", "Call", "Block", null) });
        var changedEvidence = new PlcCrossReferenceResult(true, "ImpactProbe", "changed", new[] { new PlcCrossReferenceEntry("CallerB", "CallerB", "Call", "Block", null) });
        var plc = OpenPlcMock();
        plc.SetupSequence(service => service.GetCrossReferencesAsync("ImpactProbe"))
            .ReturnsAsync(firstEvidence)
            .ReturnsAsync(firstEvidence)
            .ReturnsAsync(changedEvidence);
        plc.Setup(service => service.CompileSoftwareAsync(null)).ReturnsAsync(new CompilationResult(true, 0, 0, "clean"));
        var service = new PlcDocumentWorkflowService(plc.Object, ContextWithGroups(("", existing)));
        PlcCodeTransaction draft = await service.BeginTransactionAsync("PLC_1", "apply stale impact");
        service.AddTransactionOperation(draft.TransactionId, _directory, "ImpactProbe", null, "Override");
        PlcCodeTransaction reviewed = await service.PlanTransactionAsync(draft.TransactionId);
        PlcCodeTransaction plan = await service.ApproveTransactionImpactAsync(reviewed.TransactionId, reviewed.ImpactAnalysis!.Fingerprint, false);

        InvalidOperationException error = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => service.ApplyTransactionAsync(plan.TransactionId, plan.ApplyToken!));

        StringAssert.Contains(error.Message, "changed after planning");
        Assert.AreEqual("Stale", service.GetTransaction(plan.TransactionId).State);
        plc.Verify(service => service.ImportBlockFromDocumentsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        Assert.IsTrue(service.DiscardTransaction(plan.TransactionId));
    }

    [TestMethod]
    public async Task Transaction_DeleteWithPlcConsumers_IsImpactBlocked()
    {
        var context = ContextWithGroups();
        var software = (FakePlcSoftware)context.PlcSoftware!;
        var type = new FakeType { Name = "UsedType", ExportContent = TypeXml("UsedType", "baseline") };
        software.TypeGroup.Types.Add(type);
        var plc = OpenPlcMock();
        plc.Setup(service => service.GetCrossReferencesAsync("UsedType")).ReturnsAsync(new PlcCrossReferenceResult(true, "UsedType", "used", new[]
        {
            new PlcCrossReferenceEntry("Data", "PLC data types/Data", "DataType", "Type", null)
        }));
        plc.Setup(service => service.CompileSoftwareAsync(null)).ReturnsAsync(new CompilationResult(true, 0, 0, "clean"));
        var service = new PlcDocumentWorkflowService(plc.Object, context);
        PlcCodeTransaction draft = await service.BeginTransactionAsync("PLC_1", "blocked delete");
        service.AddDeleteTypeTransactionOperation(draft.TransactionId, "UsedType", null);

        PlcCodeTransaction plan = await service.PlanTransactionAsync(draft.TransactionId);

        Assert.AreEqual("ImpactBlocked", plan.State);
        Assert.AreEqual("Blocked", plan.ImpactAnalysis!.Status);
        Assert.IsNull(plan.ApplyToken);
        StringAssert.Contains(plan.ImpactAnalysis.Targets.Single().Reasons.Last(), "blocked");
        Assert.IsTrue(service.DiscardTransaction(plan.TransactionId));
    }

    [TestMethod]
    public async Task Transaction_UnknownImpact_RequiresExplicitUnknownAcceptance()
    {
        WriteUtf8Bom("ImpactProbe.s7dcl", "FUNCTION \"ImpactProbe\" : Void\r\nVAR_INPUT\r\n    Changed : Bool;\r\nEND_VAR\r\nEND_FUNCTION");
        var existing = new FakeBlock { Name = "ImpactProbe", Type = "FC", ExportContent = "FUNCTION \"ImpactProbe\" : Void\r\nEND_FUNCTION" };
        var plc = OpenPlcMock();
        plc.Setup(service => service.GetCrossReferencesAsync("ImpactProbe")).ReturnsAsync(new PlcCrossReferenceResult(false, "ImpactProbe", "CrossReference service unavailable."));
        plc.Setup(service => service.CompileSoftwareAsync(null)).ReturnsAsync(new CompilationResult(true, 0, 0, "clean"));
        var service = new PlcDocumentWorkflowService(plc.Object, ContextWithGroups(("", existing)));
        PlcCodeTransaction draft = await service.BeginTransactionAsync("PLC_1", "unknown impact");
        service.AddTransactionOperation(draft.TransactionId, _directory, "ImpactProbe", null, "Override");
        PlcCodeTransaction reviewed = await service.PlanTransactionAsync(draft.TransactionId);

        Assert.AreEqual("Unknown", reviewed.ImpactAnalysis!.Status);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => service.ApproveTransactionImpactAsync(reviewed.TransactionId, reviewed.ImpactAnalysis.Fingerprint, false));

        PlcCodeTransaction plan = await service.ApproveTransactionImpactAsync(reviewed.TransactionId, reviewed.ImpactAnalysis.Fingerprint, true);

        Assert.AreEqual("Planned", plan.State);
        Assert.IsNotNull(plan.ApplyToken);
        Assert.IsTrue(service.DiscardTransaction(plan.TransactionId));
    }

    [TestMethod]
    public async Task Transaction_UnresolvedExternalDependency_IsReportedAsUnknownImpact()
    {
        WriteUtf8Bom("Consumer.s7dcl", "FUNCTION \"Consumer\" : Void\r\n{ S7_Language := \"LAD\" } NETWORK RUNG wire#powerrail \"MissingProvider\"() END_RUNG END_NETWORK\r\nEND_FUNCTION");
        var plc = OpenPlcMock();
        plc.Setup(service => service.CompileSoftwareAsync(null)).ReturnsAsync(new CompilationResult(true, 0, 0, "clean"));
        var service = new PlcDocumentWorkflowService(plc.Object, ContextWithGroups());
        PlcCodeTransaction draft = await service.BeginTransactionAsync("PLC_1", "unknown dependency");
        service.AddTransactionOperation(draft.TransactionId, _directory, "Consumer", null, "Override");

        PlcCodeTransaction plan = await service.PlanTransactionAsync(draft.TransactionId);

        Assert.AreEqual("ImpactReviewRequired", plan.State);
        Assert.AreEqual("Unknown", plan.ImpactAnalysis!.Status);
        Assert.AreEqual("MissingProvider", plan.ImpactAnalysis.Dependencies.Single().Name);
        Assert.AreEqual("Unknown", plan.ImpactAnalysis.Dependencies.Single().Status);
        Assert.IsTrue(service.DiscardTransaction(plan.TransactionId));
    }

    [TestMethod]
    public async Task Transaction_DataLayoutAttributeChange_RequiresNonPlcConsumerReview()
    {
        string asset = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "skills", "s7dcl-data-authoring", "assets", "Codex_Data_Probe.s7dcl"))
            .Replace("Codex_Data_Probe", "DataImpact")
            .Replace("59994", "63994");
        WriteUtf8Bom("DataImpact.s7dcl", asset.Replace("S7_Optimized := \"TRUE\"", "S7_Optimized := \"FALSE\""));
        var existing = new FakeBlock { Name = "DataImpact", Type = "GlobalDB", Number = 63994, ExportContent = asset };
        var plc = OpenPlcMock();
        plc.Setup(service => service.GetCrossReferencesAsync("DataImpact")).ReturnsAsync(new PlcCrossReferenceResult(true, "DataImpact", "No external PLC consumers."));
        plc.Setup(service => service.CompileSoftwareAsync(null)).ReturnsAsync(new CompilationResult(true, 0, 0, "clean"));
        var service = new PlcDocumentWorkflowService(plc.Object, ContextWithGroups(("", existing)));
        PlcCodeTransaction draft = await service.BeginTransactionAsync("PLC_1", "optimized layout impact");
        service.AddTransactionOperation(draft.TransactionId, _directory, "DataImpact", null, "Override");

        PlcCodeTransaction plan = await service.PlanTransactionAsync(draft.TransactionId);

        Assert.AreEqual("ImpactReviewRequired", plan.State);
        Assert.AreEqual("LayoutChange", plan.ImpactAnalysis!.Targets.Single().ChangeKind);
        Assert.IsTrue(plan.ImpactAnalysis.Warnings.Any(warning => warning.Contains("HMI")));
        Assert.IsTrue(service.DiscardTransaction(plan.TransactionId));
    }

    [TestMethod]
    public async Task Transaction_BlockNumberChange_IsClassifiedAsIdentityImpact()
    {
        const string baseline = "{ S7_BlockNumber := \"64001\"; }\r\nFUNCTION \"IdentityImpact\" : Void\r\nEND_FUNCTION";
        WriteUtf8Bom("IdentityImpact.s7dcl", baseline.Replace("64001", "64002"));
        var existing = new FakeBlock { Name = "IdentityImpact", Type = "FC", Number = 64001, ExportContent = baseline };
        var plc = OpenPlcMock();
        plc.Setup(service => service.GetCrossReferencesAsync("IdentityImpact")).ReturnsAsync(new PlcCrossReferenceResult(true, "IdentityImpact", "No external PLC consumers."));
        plc.Setup(service => service.CompileSoftwareAsync(null)).ReturnsAsync(new CompilationResult(true, 0, 0, "clean"));
        var service = new PlcDocumentWorkflowService(plc.Object, ContextWithGroups(("", existing)));
        PlcCodeTransaction draft = await service.BeginTransactionAsync("PLC_1", "identity impact");
        service.AddTransactionOperation(draft.TransactionId, _directory, "IdentityImpact", null, "Override");

        PlcCodeTransaction plan = await service.PlanTransactionAsync(draft.TransactionId);

        Assert.AreEqual("ImpactReviewRequired", plan.State);
        Assert.AreEqual("IdentityChange", plan.ImpactAnalysis!.Targets.Single().ChangeKind);
        Assert.IsTrue(service.DiscardTransaction(plan.TransactionId));
    }

    private void WriteUtf8Bom(string fileName, string content)
    {
        File.WriteAllText(Path.Combine(_directory, fileName), content, new UTF8Encoding(true));
    }

    [DataTestMethod]
    [DataRow("route")]
    [DataRow("group")]
    [DataRow("type")]
    [DataRow("assembly")]
    [DataRow("planning")]
    [DataRow("review")]
    public async Task BehaviorProbe_PreApplyFailure_DoesNotQuarantineUntouchedDraft(string scenario)
    {
        WriteUtf8Bom("PreIO.s7dcl", "DATA_BLOCK \"PreIO\"\r\nVAR Value : DInt; END_VAR\r\nValue := 0;\r\nEND_DATA_BLOCK");
        WriteUtf8Bom("PreCycle.s7dcl", "ORGANIZATION_BLOCK \"PreCycle\"\r\nBEGIN\r\nEND_ORGANIZATION_BLOCK");
        if (scenario == "review") WriteUtf8Bom("PreCycle.s7dcl", "ORGANIZATION_BLOCK \"PreCycle\"\r\nBEGIN\r\n\"UnresolvedConsumer\"();\r\nEND_ORGANIZATION_BLOCK");
        string package = Path.Combine(_directory, "package.json");
        File.WriteAllText(package, "{\"schemaVersion\":\"1.0\",\"packageId\":\"pre-apply\",\"artifacts\":[{\"codeFormat\":\"s7dcl\",\"inputPath\":\"PreIO.s7dcl\"},{\"codeFormat\":\"s7dcl\",\"inputPath\":\"PreCycle.s7dcl\"}]}");
        string vector = Path.Combine(_directory, "vector.json");
        File.WriteAllText(vector, "{\"schemaVersion\":\"1.0\",\"vectorId\":\"pre-apply\",\"reset\":[{\"tag\":\"PreIO.Value\",\"type\":\"Int32\",\"value\":0}],\"steps\":[{\"name\":\"check\",\"assertions\":[{\"tag\":\"PreIO.Value\",\"type\":\"Int32\",\"expected\":0}]}],\"cleanup\":[{\"tag\":\"PreIO.Value\",\"type\":\"Int32\",\"value\":0}]}");
        var context = ContextWithGroups();
        context.AttachedPortalProcessId = 42;
        var plc = OpenPlcMock();
        plc.Setup(item => item.GetBlocksAsync(It.IsAny<string>())).ReturnsAsync(Array.Empty<BlockInfo>());
        plc.Setup(item => item.GetCrossReferencesAsync(It.IsAny<string>())).ReturnsAsync(new PlcCrossReferenceResult(false, "pre-apply", "Unknown consumers"));
        if (scenario == "planning") plc.Setup(item => item.CompileSoftwareAsync(null)).ThrowsAsync(new InvalidOperationException("Planning compile unavailable"));
        else plc.Setup(item => item.CompileSoftwareAsync(null)).ReturnsAsync(new CompilationResult(true, 0, 0, "clean"));
        plc.Setup(item => item.DownloadSoftwareAsync(true, true, false, true)).ReturnsAsync(new PlcSoftwareDownloadResult(true, true, true, false, "preflight"));
        plc.Setup(item => item.GetOnlineStatusAsync(true)).ReturnsAsync(new PlcOnlineStatusResult(true, "Online", true, "online"));
        plc.Setup(item => item.CompareSoftwareToOnlineAsync()).ReturnsAsync(new PlcSoftwareCompareResult(true, true, "baseline"));
        plc.Setup(item => item.GoOfflineAsync()).ReturnsAsync(() =>
        {
            if (scenario == "assembly") File.Delete(Path.Combine(_directory, "PreCycle.s7dcl"));
            return new PlcOnlineStatusResult(true, "Offline", false, "offline");
        });
        var discovery = new PlcCodeCapabilityDiscoveryService(plc.Object, new Mock<IVciService>().Object, context,
            environmentProvider: name => Task.FromResult(new PlcCodeEnvironmentEvidence { TiaProcessId = 42, TiaVersion = "V21 Update 2", TiaBuild = "21.0.2", PlcName = name, PlcFamily = "S7-1500", PlcTypeIdentifier = "CPU 1511" }),
            safetyProvider: target => new PlcCodeSafetyEvidence());
        var ids = new List<string>();
        foreach (var target in new[] { (Name: "PreIO", Type: "GlobalDB"), (Name: "PreCycle", Type: "OB") })
        {
            ids.Add((await discovery.DiscoverAsync(new PlcCodeCapabilityRequest
            {
                PlcName = "PLC_1",
                ObjectKind = "Block",
                ObjectName = target.Name,
                Operation = "Create",
                BlockType = scenario == "type" ? "FB" : target.Type,
                RequestedFormat = scenario == "route" ? "SCL" : "S7DCL",
                GroupPath = scenario == "group" ? "WrongGroup" : ""
            })).CapabilityId);
        }
        string registryPath = Path.Combine(_directory, "pre-evidence");
        var registry = new PlcCodeEvidenceRegistry(registryPath);
        var runtime = new Mock<IPlcSimulationRuntime>(MockBehavior.Strict);
        runtime.Setup(item => item.Discover("PLC_1", "TestSim", false)).Returns(new PlcSimulatorCapabilityDiscovery
        {
            Available = true,
            SelectedInstance = new PlcSimulatorInstanceCapability { Name = "TestSim", ControllerName = "PLC_1", OperatingState = "Run", Fingerprint = "test-simulator" }
        });
        var service = new PlcDocumentWorkflowService(plc.Object, context, discovery, registry, runtime.Object);
        if (scenario == "route" || scenario == "group" || scenario == "type")
        {
            var error = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => service.RunBehaviorProbeAsync("PLC_1", string.Join(",", ids), package, vector, "TestSim", "CleanCompile", 0, false, true, true));
            StringAssert.Contains(error.Message, scenario == "route" ? "DirectDocuments" : scenario == "group" ? "group" : "object type");
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => service.QueryBehaviorEvidenceAsync("PLC_1", string.Join(",", ids), package, vector, "TestSim"));
            Assert.IsFalse(Directory.Exists(registryPath));
            plc.Verify(item => item.DownloadSoftwareAsync(It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
            runtime.Verify(item => item.Discover(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
        }
        else
        {
            var result = await service.RunBehaviorProbeAsync("PLC_1", string.Join(",", ids), package, vector, "TestSim", "CleanCompile", 0, false, true, true);
            Assert.AreEqual("Failed", result.Evidence.Status, result.Evidence.Failure);
            Assert.IsTrue(result.Evidence.CleanupProven, result.Evidence.Failure);
            Assert.IsNull(result.ApplyResult);
            Assert.IsNull(result.CleanupResult); // No fictitious rollback or cleanup compilation.
            StringAssert.Contains(result.Evidence.Failure, scenario == "assembly" ? "invalid" : scenario == "planning" ? "Planning compile unavailable" : "impact is Unknown");
            Assert.IsTrue(new PlcCodeEvidenceRegistry(registryPath).Get(result.Evidence.EvidenceId).CleanupProven);
            Assert.IsFalse(Directory.Exists(Path.Combine(Path.GetTempPath(), "OpenTiaToolkit", "plc-code-transactions", result.Evidence.TransactionId)));
        }
        plc.Verify(item => item.ImportBlockFromDocumentsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        plc.Verify(item => item.DownloadSoftwareAsync(true, false, true, It.IsAny<bool>()), Times.Never);
        runtime.Verify(item => item.Execute(It.IsAny<PlcBehaviorVector>(), It.IsAny<PlcSimulatorCapabilityDiscovery>(), It.IsAny<IReadOnlyList<string>>()), Times.Never);
        Assert.AreEqual(0, ((FakePlcSoftware)context.PlcSoftware!).BlockGroup.Blocks.Count);
    }

    [DataTestMethod]
    [DataRow("Draft", true)]
    [DataRow("ImpactReviewRequired", true)]
    [DataRow("ImpactBlocked", true)]
    [DataRow("Expired", true)]
    [DataRow("missing", false)]
    [DataRow("inconsistent", false)]
    [DataRow("journal-ahead", false)]
    public async Task BehaviorRecovery_NeverAppliedProofOrQuarantine_NoSimulatorWrites(string scenario, bool clean)
    {
        var plc = OpenPlcMock();
        plc.Setup(item => item.GoOfflineAsync()).ReturnsAsync(new PlcOnlineStatusResult(true, "Offline", false, "offline"));
        var context = ContextWithGroups();
        var registryPath = Path.Combine(_directory, "recover-evidence");
        var registry = new PlcCodeEvidenceRegistry(registryPath);
        var runtime = new Mock<IPlcSimulationRuntime>(MockBehavior.Strict);
        var service = new PlcDocumentWorkflowService(plc.Object, context, capabilityDiscovery: null, evidenceRegistry: registry, simulationRuntime: runtime.Object);
        var draft = await service.BeginTransactionAsync("PLC_1", "interrupted assembly");
        var store = new PlcCodeTransactionStore();
        if (clean) { draft.State = scenario; store.Save(draft); }
        else if (scenario == "missing") service.DiscardTransaction(draft.TransactionId);
        else if (scenario == "inconsistent") { draft.Operations = new[] { new PlcCodeTransactionOperation { ApplyState = "Applied" } }; store.Save(draft); }
        else store.AddJournal(draft, null, "Apply", "Starting", "write-ahead crash");
        var evidence = new PlcCodeEvidenceRecord
        {
            EvidenceId = Guid.NewGuid().ToString("N"),
            EvidenceKind = "Behavior",
            Status = "Quarantined",
            TransactionId = draft.TransactionId,
            Behavior = new PlcCodeBehaviorEvidence { PlcName = "PLC_1", Simulator = new PlcSimulatorCapabilityDiscovery { SelectedInstance = new PlcSimulatorInstanceCapability { Name = "TestSim" } } }
        };
        registry.Save(evidence);
        var result = await service.RecoverBehaviorProbeAsync(evidence.EvidenceId, true);
        Assert.AreEqual(clean ? "Failed" : "Quarantined", result.Evidence.Status, result.Evidence.Failure);
        Assert.AreEqual(clean, result.Evidence.CleanupProven);
        Assert.AreEqual(clean, new PlcCodeEvidenceRegistry(registryPath).Get(evidence.EvidenceId).CleanupProven);
        Assert.IsNull(result.CleanupResult);
        plc.Verify(item => item.DownloadSoftwareAsync(It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
        plc.Verify(item => item.CompileSoftwareAsync(It.IsAny<string>()), Times.Never);
        if (!clean && scenario != "missing")
        {
            Assert.IsTrue(File.Exists(store.ManifestPath(draft.TransactionId)));
            // Test-owned retained evidence only; do not turn this into a public recovery bypass.
            Directory.Delete(store.DirectoryPath(draft.TransactionId), true);
        }
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task BehaviorProbe_RejectedCompile_RetainsAllPhaseDiagnosticsAfterRollbackAndDiscard(bool compilerError)
    {
        WriteUtf8Bom("DiagnosticIO.s7dcl", "DATA_BLOCK \"DiagnosticIO\"\r\nVAR Value : DInt; END_VAR\r\nValue := 0;\r\nEND_DATA_BLOCK");
        WriteUtf8Bom("DiagnosticCycle.s7dcl", "ORGANIZATION_BLOCK \"DiagnosticCycle\"\r\nBEGIN\r\nEND_ORGANIZATION_BLOCK");
        string package = Path.Combine(_directory, "package.json");
        File.WriteAllText(package, "{\"schemaVersion\":\"1.0\",\"packageId\":\"diagnostics\",\"artifacts\":[{\"codeFormat\":\"s7dcl\",\"inputPath\":\"DiagnosticIO.s7dcl\"},{\"codeFormat\":\"s7dcl\",\"inputPath\":\"DiagnosticCycle.s7dcl\"}]}");
        string vector = Path.Combine(_directory, "vector.json");
        File.WriteAllText(vector, "{\"schemaVersion\":\"1.0\",\"vectorId\":\"diagnostics\",\"reset\":[{\"tag\":\"DiagnosticIO.Value\",\"type\":\"Int32\",\"value\":0}],\"steps\":[{\"name\":\"check\",\"assertions\":[{\"tag\":\"DiagnosticIO.Value\",\"type\":\"Int32\",\"expected\":0}]}],\"cleanup\":[{\"tag\":\"DiagnosticIO.Value\",\"type\":\"Int32\",\"value\":0}]}");
        var context = ContextWithGroups();
        context.AttachedPortalProcessId = 42;
        var software = (FakePlcSoftware)context.PlcSoftware!;
        var plc = OpenPlcMock();
        plc.Setup(service => service.GetBlocksAsync(It.IsAny<string>())).ReturnsAsync(Array.Empty<BlockInfo>());
        plc.Setup(service => service.GetCrossReferencesAsync(It.IsAny<string>())).ReturnsAsync(new PlcCrossReferenceResult(true, "diagnostics", "No external consumers."));
        plc.Setup(service => service.ImportBlockFromDocumentsAsync(It.IsAny<string>(), It.IsAny<string>(), "", "Override"))
            .ReturnsAsync((string directory, string name, string group, string option) =>
            {
                var block = new FakeBlock { Name = name, Type = name == "DiagnosticIO" ? "GlobalDB" : "OB", ExportContent = File.ReadAllText(Path.Combine(directory, name + ".s7dcl")) };
                block.OnDelete = () => software.BlockGroup.Blocks.Remove(block);
                software.BlockGroup.Blocks.Add(block);
                return true;
            });
        var baseline = new CompilationDiagnostic("Information", "PLC/Baseline", "Main", "baseline detail", null);
        var candidate = new CompilationDiagnostic(compilerError ? "Error" : "Warning", "PLC/DiagnosticCycle", "DiagnosticCycle", "candidate detail", "C-17");
        var rollback = new CompilationDiagnostic("Information", "PLC/Cleanup", "Main", "rollback detail", null);
        plc.SetupSequence(service => service.CompileSoftwareAsync(null))
            .ReturnsAsync(new CompilationResult(true, 0, 0, "baseline", new[] { baseline }))
            .ReturnsAsync(new CompilationResult(!compilerError, compilerError ? 1 : 0, compilerError ? 0 : 1, "candidate", new[] { candidate }))
            .ReturnsAsync(new CompilationResult(true, 0, 0, "rollback", new[] { rollback }));
        plc.Setup(service => service.DownloadSoftwareAsync(true, true, false, true)).ReturnsAsync(new PlcSoftwareDownloadResult(true, true, true, false, "preflight"));
        plc.Setup(service => service.GetOnlineStatusAsync(true)).ReturnsAsync(new PlcOnlineStatusResult(true, "Online", true, "online"));
        plc.Setup(service => service.GoOfflineAsync()).ReturnsAsync(new PlcOnlineStatusResult(true, "Offline", false, "offline"));
        plc.Setup(service => service.CompareSoftwareToOnlineAsync()).ReturnsAsync(new PlcSoftwareCompareResult(true, true, "baseline"));
        var discovery = new PlcCodeCapabilityDiscoveryService(plc.Object, new Mock<IVciService>().Object, context,
            environmentProvider: name => Task.FromResult(new PlcCodeEnvironmentEvidence { TiaProcessId = 42, TiaVersion = "V21 Update 2", TiaBuild = "21.0.2", PlcName = name, PlcFamily = "S7-1500", PlcTypeIdentifier = "CPU 1511" }),
            safetyProvider: target => new PlcCodeSafetyEvidence());
        var capabilities = new List<string>();
        foreach (var target in new[] { (Name: "DiagnosticIO", Type: "GlobalDB"), (Name: "DiagnosticCycle", Type: "OB") })
        {
            var capability = await discovery.DiscoverAsync(new PlcCodeCapabilityRequest { PlcName = "PLC_1", ObjectKind = "Block", ObjectName = target.Name, Operation = "Create", BlockType = target.Type, RequestedFormat = "S7DCL" });
            capabilities.Add(capability.CapabilityId);
        }
        string registryPath = Path.Combine(_directory, "behavior-evidence");
        var registry = new PlcCodeEvidenceRegistry(registryPath);
        var runtime = new Mock<IPlcSimulationRuntime>(MockBehavior.Strict);
        runtime.Setup(service => service.Discover("PLC_1", "TestSim", false)).Returns(new PlcSimulatorCapabilityDiscovery
        {
            Available = true,
            SelectedInstance = new PlcSimulatorInstanceCapability { Name = "TestSim", ControllerName = "PLC_1", OperatingState = "Run", Fingerprint = "test-simulator" }
        });
        var service = new PlcDocumentWorkflowService(plc.Object, context, discovery, registry, runtime.Object);

        PlcCodeBehaviorProbeRunResult result = await service.RunBehaviorProbeAsync("PLC_1", string.Join(",", capabilities), package, vector, "TestSim", "CleanCompile", 0, true, true, true);

        Assert.AreEqual("Failed", result.Evidence.Status, result.Evidence.Failure);
        Assert.IsTrue(result.ApplyResult?.RolledBackAndVerified, result.Evidence.Failure);
        Assert.IsTrue(result.Evidence.CleanupProven);
        Assert.AreEqual(0, software.BlockGroup.Blocks.Count);
        Assert.IsFalse(Directory.Exists(Path.Combine(Path.GetTempPath(), "OpenTiaToolkit", "plc-code-transactions", result.Evidence.TransactionId)));
        var persisted = new PlcCodeEvidenceRegistry(registryPath).Get(result.Evidence.EvidenceId);
        Assert.AreEqual("baseline detail", persisted.BaselineCompilation!.Diagnostics.Single().Description);
        Assert.AreEqual("candidate detail", persisted.ProbeCompilation!.Diagnostics.Single().Description);
        Assert.AreEqual(candidate.Severity, persisted.ProbeCompilation.Diagnostics.Single().Severity);
        Assert.AreEqual("PLC/DiagnosticCycle", persisted.ProbeCompilation.Diagnostics.Single().Path);
        Assert.AreEqual("C-17", persisted.ProbeCompilation.Diagnostics.Single().ErrorCode);
        Assert.AreEqual("rollback detail", persisted.CleanupCompilation!.Diagnostics.Single().Description);
        plc.Verify(service => service.DownloadSoftwareAsync(true, false, true, It.IsAny<bool>()), Times.Never);
        runtime.Verify(service => service.Execute(It.IsAny<PlcBehaviorVector>(), It.IsAny<PlcSimulatorCapabilityDiscovery>(), It.IsAny<IReadOnlyList<string>>()), Times.Never);
    }

    [TestMethod]
    public async Task ProbeRunner_VerifiesThenRemovesExactTargetAndPromotesEvidence()
    {
        WriteUtf8Bom("DisposableProbe.s7dcl", "{ S7_BlockNumber := \"65001\"; S7_PreferredLanguage := \"LAD\" }\r\nFUNCTION \"DisposableProbe\" : Void\r\n{ S7_Language := \"LAD\" }\r\nNETWORK\r\nRUNG wire#powerrail END_RUNG\r\nEND_NETWORK\r\nEND_FUNCTION");
        var context = ContextWithGroups();
        context.AttachedPortalProcessId = 42;
        var software = (FakePlcSoftware)context.PlcSoftware!;
        var plc = OpenPlcMock();
        plc.Setup(service => service.GetBlocksAsync(It.IsAny<string>())).ReturnsAsync(Array.Empty<BlockInfo>());
        plc.Setup(service => service.CompileSoftwareAsync(null)).ReturnsAsync(new CompilationResult(true, 0, 0, "clean"));
        plc.Setup(service => service.GetSoftwareTreeAsync(null)).ReturnsAsync((SoftwareTreeNode?)null);
        plc.Setup(service => service.GetCrossReferencesAsync("DisposableProbe")).ReturnsAsync(new PlcCrossReferenceResult(true, "DisposableProbe", "No external PLC consumers."));
        plc.Setup(service => service.ImportBlockFromDocumentsAsync(_directory, "DisposableProbe", "", "Override"))
            .ReturnsAsync(() =>
            {
                var created = new FakeBlock { Name = "DisposableProbe", Type = "FC", Number = 65001, ExportContent = File.ReadAllText(Path.Combine(_directory, "DisposableProbe.s7dcl")) };
                created.OnDelete = () => software.BlockGroup.Blocks.Remove(created);
                software.BlockGroup.Blocks.Add(created);
                return true;
            });
        var discovery = new PlcCodeCapabilityDiscoveryService(
            plc.Object,
            new Mock<IVciService>().Object,
            context,
            environmentProvider: name => Task.FromResult(new PlcCodeEnvironmentEvidence { TiaProcessId = 42, TiaVersion = "V21 Update 2", TiaBuild = "21.0.2", PlcName = name, PlcFamily = "S7-1500", PlcTypeIdentifier = "CPU 1511" }),
            safetyProvider: target => new PlcCodeSafetyEvidence());
        PlcCodeCapabilityDiscovery capability = await discovery.DiscoverAsync(new PlcCodeCapabilityRequest { PlcName = "PLC_1", ObjectKind = "Block", ObjectName = "DisposableProbe", Operation = "Create", BlockType = "FC", RequestedLanguage = "LAD" });
        var registry = new PlcCodeEvidenceRegistry(Path.Combine(_directory, "evidence"));
        var service = new PlcDocumentWorkflowService(plc.Object, context, discovery, registry);
        plc.SetupSequence(item => item.CompileSoftwareAsync(null))
            .ReturnsAsync(new CompilationResult(true, 0, 0, "baseline", new[] { new CompilationDiagnostic("Information", null, null, "baseline diagnostic", null) }))
            .ReturnsAsync(new CompilationResult(true, 0, 0, "candidate", new[] { new CompilationDiagnostic("Information", null, null, "candidate diagnostic", null) }))
            .ReturnsAsync(new CompilationResult(true, 0, 0, "cleanup", new[] { new CompilationDiagnostic("Information", null, null, "cleanup diagnostic", null) }));

        PlcCodeProbeRunResult result = await service.RunProbeAsync("PLC_1", capability.CapabilityId, "s7dcl", Path.Combine(_directory, "DisposableProbe.s7dcl"), null, null, "NoNewErrors", null, false, true);

        Assert.AreEqual("Verified", result.Evidence.Status, result.Evidence.Failure);
        Assert.IsTrue(result.ProbeApplied);
        Assert.IsTrue(result.CleanupProven);
        Assert.AreEqual(0, software.BlockGroup.Blocks.Count);
        Assert.IsTrue(result.Evidence.CleanupCompilation?.Success);
        var persisted = registry.Get(result.Evidence.EvidenceId);
        Assert.AreEqual("baseline diagnostic", persisted.BaselineCompilation!.Diagnostics.Single().Description);
        Assert.AreEqual("candidate diagnostic", persisted.ProbeCompilation!.Diagnostics.Single().Description);
        Assert.AreEqual("cleanup diagnostic", persisted.CleanupCompilation!.Diagnostics.Single().Description);
        Assert.AreEqual(1, registry.FindVerified(result.Evidence.Compatibility.Fingerprint, result.Evidence.QualificationSignature).Count);
        Assert.IsFalse(Directory.Exists(Path.Combine(Path.GetTempPath(), "OpenTiaToolkit", "plc-code-transactions", result.Evidence.TransactionId)));
    }

    [TestMethod]
    public async Task Promotion_BindsExactEvidenceAndRejectsArtifactDriftBeforePlanning()
    {
        const string production = "{ S7_BlockNumber := \"65101\"; S7_PreferredLanguage := \"LAD\" }\r\nFUNCTION \"ProductionShape\" : Void\r\n{ S7_Language := \"LAD\" }\r\nNETWORK\r\nRUNG wire#powerrail END_RUNG\r\nEND_NETWORK\r\nEND_FUNCTION";
        WriteUtf8Bom("ProductionShape.s7dcl", production);
        var context = ContextWithGroups();
        context.AttachedPortalProcessId = 42;
        var plc = OpenPlcMock();
        plc.Setup(service => service.GetBlocksAsync(It.IsAny<string>())).ReturnsAsync(Array.Empty<BlockInfo>());
        plc.Setup(service => service.CompileSoftwareAsync(null)).ReturnsAsync(new CompilationResult(true, 0, 0, "clean"));
        var discovery = new PlcCodeCapabilityDiscoveryService(
            plc.Object,
            new Mock<IVciService>().Object,
            context,
            environmentProvider: name => Task.FromResult(new PlcCodeEnvironmentEvidence { TiaProcessId = 42, TiaVersion = "V21 Update 2", TiaBuild = "21.0.2", PlcName = name, PlcFamily = "S7-1500", PlcTypeIdentifier = "CPU 1511" }),
            safetyProvider: target => new PlcCodeSafetyEvidence());
        PlcCodeCapabilityDiscovery capability = await discovery.DiscoverAsync(new PlcCodeCapabilityRequest { PlcName = "PLC_1", ObjectKind = "Block", ObjectName = "ProductionShape", Operation = "Create", BlockType = "FC", RequestedLanguage = "LAD", RequestedFormat = "S7DCL" });
        var registry = new PlcCodeEvidenceRegistry(Path.Combine(_directory, "promotion-evidence"));
        var service = new PlcDocumentWorkflowService(plc.Object, context, discovery, registry);
        PlcCodeArtifactValidationResult validation = new PlcCodeFormatService(service).Validate("s7dcl", Path.Combine(_directory, "ProductionShape.s7dcl"));
        PlcCodeEvidenceCompatibility compatibility = PlcCodeEvidenceRegistry.BuildCompatibility(new[] { capability }, validation);
        var evidence = new PlcCodeEvidenceRecord
        {
            EvidenceId = Guid.NewGuid().ToString("N"),
            Status = "Verified",
            StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            CompletedAtUtc = DateTimeOffset.UtcNow,
            CodeFormat = validation.Format,
            ValidatorCatalogVersion = PlcCodeFormatService.ValidatorCatalogVersion,
            QualificationSignature = validation.QualificationSignature,
            InputHash = validation.InputHash,
            Compatibility = compatibility,
            CleanupProven = true
        };
        registry.Save(evidence);

        StringAssert.Contains((await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => service.BeginPromotedTransactionAsync("PLC_1", evidence.EvidenceId, capability.CapabilityId, "s7dcl", validation.InputPath, "invalid", null, null, "AutoSave", "None"))).Message, "save-decision");
        StringAssert.Contains((await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => service.BeginPromotedTransactionAsync("PLC_1", evidence.EvidenceId, capability.CapabilityId, "s7dcl", validation.InputPath, "invalid", null, null, "Defer", "CommitDirectly"))).Message, "source-control-decision");

        PlcCodeTransaction transaction = await service.BeginPromotedTransactionAsync("PLC_1", evidence.EvidenceId, capability.CapabilityId, "s7dcl", validation.InputPath, "production promotion", null, null, "Defer", "None");
        service.AddTransactionOperation(transaction.TransactionId, _directory, "ProductionShape", null, "Override", targetMode: "create-only");
        WriteUtf8Bom("ProductionShape.s7dcl", production.Replace("RUNG wire#powerrail END_RUNG", "RUNG wire#powerrail Coil( #Changed ) END_RUNG"));

        InvalidOperationException error = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => service.PlanTransactionAsync(transaction.TransactionId));

        StringAssert.Contains(error.Message, "artifact changed");
        Assert.IsTrue(service.DiscardTransaction(transaction.TransactionId));

        WriteUtf8Bom("ProductionShape.s7dcl", production);
        PlcCodeTransaction injected = await service.BeginPromotedTransactionAsync("PLC_1", evidence.EvidenceId, capability.CapabilityId, "s7dcl", validation.InputPath, "operation injection", null, null, "Defer", "None");
        service.AddTransactionOperation(injected.TransactionId, _directory, "ProductionShape", null, "Override", targetMode: "create-only");
        service.AddTransactionOperation(injected.TransactionId, _directory, "ProductionShape", null, "Override", targetMode: "create-only");

        StringAssert.Contains((await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => service.PlanTransactionAsync(injected.TransactionId))).Message, "exactly one operation");
        Assert.IsTrue(service.DiscardTransaction(injected.TransactionId));

        PlcCodeTransaction revoked = await service.BeginPromotedTransactionAsync("PLC_1", evidence.EvidenceId, capability.CapabilityId, "s7dcl", validation.InputPath, "revoked before apply", null, null, "Defer", "None");
        service.AddTransactionOperation(revoked.TransactionId, _directory, "ProductionShape", null, "Override", targetMode: "create-only");
        PlcCodeTransaction revokedPlan = await service.PlanTransactionAsync(revoked.TransactionId);
        evidence.CleanupProven = false;
        registry.Save(evidence);

        StringAssert.Contains((await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => service.ApplyTransactionAsync(revokedPlan.TransactionId, revokedPlan.ApplyToken!))).Message, "cleanup");
        Assert.AreEqual("Stale", service.GetTransaction(revokedPlan.TransactionId).State);
        Assert.IsTrue(service.DiscardTransaction(revokedPlan.TransactionId));
    }

    [TestMethod]
    public async Task Promotion_RejectsEvidenceWithoutCleanupProof()
    {
        WriteUtf8Bom("ProductionShape.s7dcl", "FUNCTION \"ProductionShape\" : Void\r\nEND_FUNCTION");
        var context = ContextWithGroups();
        context.AttachedPortalProcessId = 42;
        var plc = OpenPlcMock();
        plc.Setup(service => service.GetBlocksAsync(It.IsAny<string>())).ReturnsAsync(Array.Empty<BlockInfo>());
        var discovery = new PlcCodeCapabilityDiscoveryService(
            plc.Object,
            new Mock<IVciService>().Object,
            context,
            environmentProvider: name => Task.FromResult(new PlcCodeEnvironmentEvidence { TiaProcessId = 42, TiaVersion = "V21 Update 2", TiaBuild = "21.0.2", PlcName = name, PlcFamily = "S7-1500", PlcTypeIdentifier = "CPU 1511" }),
            safetyProvider: target => new PlcCodeSafetyEvidence());
        PlcCodeCapabilityDiscovery capability = await discovery.DiscoverAsync(new PlcCodeCapabilityRequest { PlcName = "PLC_1", ObjectKind = "Block", ObjectName = "ProductionShape", Operation = "Create", BlockType = "FC", RequestedLanguage = "LAD", RequestedFormat = "S7DCL" });
        var registry = new PlcCodeEvidenceRegistry(Path.Combine(_directory, "promotion-evidence"));
        var service = new PlcDocumentWorkflowService(plc.Object, context, discovery, registry);
        PlcCodeArtifactValidationResult validation = new PlcCodeFormatService(service).Validate("s7dcl", Path.Combine(_directory, "ProductionShape.s7dcl"));
        var evidence = new PlcCodeEvidenceRecord
        {
            EvidenceId = Guid.NewGuid().ToString("N"),
            Status = "Verified",
            StartedAtUtc = DateTimeOffset.UtcNow,
            CodeFormat = validation.Format,
            ValidatorCatalogVersion = PlcCodeFormatService.ValidatorCatalogVersion,
            QualificationSignature = validation.QualificationSignature,
            Compatibility = PlcCodeEvidenceRegistry.BuildCompatibility(new[] { capability }, validation),
            CleanupProven = false
        };
        registry.Save(evidence);

        InvalidOperationException error = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => service.BeginPromotedTransactionAsync("PLC_1", evidence.EvidenceId, capability.CapabilityId, "s7dcl", validation.InputPath, "unsafe", null, null, "Defer", "None"));

        StringAssert.Contains(error.Message, "cleanup");
    }

    [TestMethod]
    public async Task Promotion_RejectsFailSafeTargetBeforeEvidenceLookup()
    {
        WriteUtf8Bom("SafetyShape.s7dcl", "{ S7_PreferredLanguage := \"LAD\" }\r\nFUNCTION \"SafetyShape\" : Void\r\nEND_FUNCTION");
        var context = ContextWithGroups();
        context.AttachedPortalProcessId = 42;
        var plc = OpenPlcMock();
        plc.Setup(service => service.GetBlocksAsync(It.IsAny<string>())).ReturnsAsync(Array.Empty<BlockInfo>());
        var discovery = new PlcCodeCapabilityDiscoveryService(
            plc.Object,
            new Mock<IVciService>().Object,
            context,
            environmentProvider: name => Task.FromResult(new PlcCodeEnvironmentEvidence { TiaProcessId = 42, TiaVersion = "V21 Update 2", TiaBuild = "21.0.2", PlcName = name, PlcFamily = "S7-1500", PlcTypeIdentifier = "CPU 1511" }),
            safetyProvider: target => new PlcCodeSafetyEvidence { IsFailSafeTarget = true, MutationBlocked = true, Reason = "Safety authorization is unavailable." });
        PlcCodeCapabilityDiscovery capability = await discovery.DiscoverAsync(new PlcCodeCapabilityRequest { PlcName = "PLC_1", ObjectKind = "Block", ObjectName = "SafetyShape", Operation = "Create", BlockType = "FC", RequestedLanguage = "LAD", RequestedFormat = "S7DCL" });
        var service = new PlcDocumentWorkflowService(plc.Object, context, discovery, new PlcCodeEvidenceRegistry(Path.Combine(_directory, "promotion-evidence")));

        InvalidOperationException error = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => service.BeginPromotedTransactionAsync("PLC_1", Guid.NewGuid().ToString("N"), capability.CapabilityId, "s7dcl", Path.Combine(_directory, "SafetyShape.s7dcl"), "safety", null, null, "Defer", "None"));

        StringAssert.Contains(error.Message, "fail-safe");
    }

    [TestMethod]
    public async Task Transaction_CreateOnlyAndBaselineBoundModes_FailClosedBeforeMutation()
    {
        const string baseline = "{ S7_BlockNumber := \"62001\" }\r\nFUNCTION \"Guarded\" : Void\r\nEND_FUNCTION";
        WriteUtf8Bom("Guarded.s7dcl", baseline.Replace("END_FUNCTION", "// reviewed implementation\r\nEND_FUNCTION"));
        string baselineDirectory = Path.Combine(_directory, "untouched");
        Directory.CreateDirectory(baselineDirectory);
        File.WriteAllText(Path.Combine(baselineDirectory, "Guarded.s7dcl"), baseline, new UTF8Encoding(true));
        var existing = new FakeBlock { Name = "Guarded", Type = "FC", Number = 62001, ExportContent = baseline };
        var context = ContextWithGroups(("", existing));
        var plc = OpenPlcMock();
        var service = new PlcDocumentWorkflowService(plc.Object, context);

        PlcCodeTransaction createDraft = await service.BeginTransactionAsync("PLC_1", "must not override");
        service.AddTransactionOperation(createDraft.TransactionId, _directory, "Guarded", null, "Override", targetMode: "create-only");
        StringAssert.Contains((await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => service.PlanTransactionAsync(createDraft.TransactionId))).Message, "create-only");
        Assert.IsTrue(service.DiscardTransaction(createDraft.TransactionId));

        File.WriteAllText(Path.Combine(baselineDirectory, "Guarded.s7dcl"), baseline.Replace("62001", "62002"), new UTF8Encoding(true));
        PlcCodeTransaction modifyDraft = await service.BeginTransactionAsync("PLC_1", "must bind exact baseline");
        Assert.ThrowsException<InvalidDataException>(() => service.AddTransactionOperation(modifyDraft.TransactionId, _directory, "Guarded", null, "Override", targetMode: "baseline-bound", baselineDirectory: baselineDirectory, baselineFileName: "Guarded", allowedChanges: "Implementation"));
        plc.Verify(item => item.ImportBlockFromDocumentsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        Assert.IsTrue(service.DiscardTransaction(modifyDraft.TransactionId));
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
        public FakeTypeGroup TypeGroup { get; } = new FakeTypeGroup();
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
        public string ExportState { get; set; } = "Success";
        public int DocumentExports { get; private set; }
        public int XmlExports { get; private set; }
        public bool XmlExportFails { get; set; }
        public FakeDocumentExportResult ExportAsDocuments(DirectoryInfo directory, string baseName)
        {
            DocumentExports++;
            directory.Create();
            File.WriteAllText(Path.Combine(directory.FullName, baseName + ".s7dcl"), ExportContent, new UTF8Encoding(true));
            return new FakeDocumentExportResult { State = ExportState };
        }
        public void Export(FileInfo file)
        {
            XmlExports++;
            if (XmlExportFails) throw new InvalidOperationException("forced XML export failure");
            file.Directory?.Create();
            File.WriteAllText(file.FullName, ExportContent, new UTF8Encoding(true));
        }
        public void Delete() => OnDelete?.Invoke();
    }

    private sealed class FakeTypeGroup
    {
        public string Name { get; set; } = "PLC data types";
        public List<FakeType> Types { get; } = new List<FakeType>();
        public List<FakeTypeGroup> Groups { get; } = new List<FakeTypeGroup>();
    }

    private sealed class FakeType
    {
        public string Name { get; set; } = string.Empty;
        public string ExportContent { get; set; } = string.Empty;
        public Action? OnDelete { get; set; }
        public void Export(FileInfo file, FakeExportOptions options)
        {
            file.Directory?.Create();
            File.WriteAllText(file.FullName, ExportContent, new UTF8Encoding(true));
        }
        public void Export(FileInfo file) => Export(file, FakeExportOptions.None);
        public void Delete() => OnDelete?.Invoke();
    }

    private enum FakeExportOptions { None }

    private sealed class FakeDocumentExportResult
    {
        public string State { get; set; } = string.Empty;
    }

    private static string TypeXml(string name, string marker) => $"<?xml version=\"1.0\"?><Document><Engineering version=\"V21\"/><SW.Types.PlcStruct ID=\"0\"><AttributeList><Interface xmlns=\"urn:siemens:test\"><Marker>{marker}</Marker></Interface><Name>{name}</Name></AttributeList></SW.Types.PlcStruct></Document>";
}
