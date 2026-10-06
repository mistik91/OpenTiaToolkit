using System;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Services;

namespace OpenTiaToolkit.Tests.Services;

[TestClass]
public sealed class PlcCodeFormatServiceTests
{
    private string _root = null!;
    private PlcCodeFormatService _service = null!;

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "OpenTiaToolkit.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _service = new PlcCodeFormatService(new PlcDocumentWorkflowService(new Mock<IPlcService>().Object));
    }

    [TestCleanup]
    public void Cleanup() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [TestMethod]
    public void DescribeFormats_DeclaresSafetyAndTransactionRouteForEveryFormat()
    {
        var formats = _service.DescribeFormats();
        Assert.AreEqual(5, formats.Count);
        Assert.IsTrue(formats.All(item => item.RequiredVerification.Length > 0 && item.KnownLimitations.Length > 0));
        Assert.IsTrue(formats.Single(item => item.Format == "SCL").TransactionalApply);
        Assert.IsFalse(formats.Single(item => item.Format == "VCI").TransactionalApply);
    }

    [TestMethod]
    public void ValidateScl_LiveAsset_IsVerified()
    {
        string path = Path.Combine(FindRepositoryRoot(), "skills", "scl-source-authoring", "assets", "Codex_SCL_Probe.scl");
        var result = _service.ValidateScl(path);
        Assert.IsTrue(result.Valid, string.Join("; ", result.Errors));
        Assert.AreEqual("verified", result.Classification);
        Assert.AreEqual("Codex_SCL_Probe", result.DeclaredObjects.Single().Name);
    }

    [TestMethod]
    public void ValidateScl_BundledStatefulAndTypeAssets_AreVerified()
    {
        string root = FindRepositoryRoot();
        foreach (string name in new[] { "Codex_SCL_Stateful_Probe", "Codex_SCL_Type_Probe" })
        {
            string path = Path.Combine(root, "skills", "scl-source-authoring", "assets", name + ".scl");
            var result = _service.ValidateScl(path);
            Assert.IsTrue(result.Valid, name + ": " + string.Join("; ", result.Errors));
            Assert.AreEqual("verified", result.Classification, name + ": " + string.Join("; ", result.Warnings));
            Assert.AreEqual(0, result.Dependencies.Count, name);
        }
    }

    [TestMethod]
    public void ValidateScl_BundledInstanceAsset_ProvesExactFbAndInstanceDb()
    {
        string path = Path.Combine(FindRepositoryRoot(), "skills", "scl-source-authoring", "assets", "Codex_SCL_Instance_Probe.scl");
        var result = _service.ValidateScl(path);

        Assert.IsTrue(result.Valid, string.Join("; ", result.Errors));
        Assert.IsTrue(result.HasUtf8Bom, "The shipped source must retain its Siemens import BOM.");
        Assert.AreEqual("verified", result.Classification);
        Assert.AreEqual(0, result.Dependencies.Count);
        Assert.AreEqual(2, result.DeclaredObjects.Count);
        Assert.AreEqual("FB", result.DeclaredObjects.Single(item => item.Name == "Codex_SCL_Instance_Probe_FB").ObjectType);
        Assert.AreEqual("InstanceDB", result.DeclaredObjects.Single(item => item.Name == "Codex_SCL_Instance_Probe_DB").ObjectType);
    }

    [TestMethod]
    public void ValidateScl_BundledInstanceAssetWithoutItsFb_IsRejected()
    {
        string asset = Path.Combine(FindRepositoryRoot(), "skills", "scl-source-authoring", "assets", "Codex_SCL_Instance_Probe.scl");
        string source = File.ReadAllText(asset);
        string instanceOnly = Write("InstanceOnly.scl", source.Substring(source.IndexOf("DATA_BLOCK", StringComparison.Ordinal)));
        var result = _service.ValidateScl(instanceOnly);

        Assert.IsFalse(result.Valid);
        Assert.AreEqual("known-unsupported", result.Classification);
        Assert.AreEqual(1, result.DeclaredObjects.Count);
        CollectionAssert.Contains(result.Dependencies.ToArray(), "Codex_SCL_Instance_Probe_FB");
        StringAssert.Contains(string.Join("; ", result.Errors), "same source");
    }

    [TestMethod]
    public void ValidateScl_StatefulTempAndMissingBoundary_AreRejected()
    {
        string path = Write("Bad.scl", "FUNCTION \"Bad\" : Void\r\nVAR_TEMP T : TON; END_VAR\r\nBEGIN\r\n#T(IN := TRUE, PT := T#1S);\r\n");
        var result = _service.ValidateScl(path);
        Assert.IsFalse(result.Valid);
        StringAssert.Contains(string.Join("; ", result.Errors), "VAR_TEMP");
        StringAssert.Contains(string.Join("; ", result.Errors), "END_FUNCTION");
    }

    [TestMethod]
    public void ValidateScl_ProjectCallAndWhile_RequireLiveProbe()
    {
        string path = Write("Probe.scl", "FUNCTION \"Probe\" : Void\r\nBEGIN\r\nWHILE #Run DO\r\n  \"ProjectFC\"();\r\nEND_WHILE;\r\nEND_FUNCTION\r\n");
        var result = _service.ValidateScl(path);
        Assert.IsTrue(result.Valid);
        Assert.AreEqual("unknown-requires-live-probe", result.Classification);
        CollectionAssert.Contains(result.Dependencies.ToArray(), "ProjectFC");
    }

    [TestMethod]
    public void QualificationSignature_NormalizesDisposableIdentityButNotCodeShape()
    {
        string first = Write("First.scl", "FUNCTION \"First\" : Void\r\nBEGIN\r\n#Result := ABS(#Value);\r\nEND_FUNCTION\r\n");
        string second = Write("Second.scl", "FUNCTION \"Second\" : Void\r\nBEGIN\r\n#Result := ABS(#Value);\r\nEND_FUNCTION\r\n");
        string changed = Write("Changed.scl", "FUNCTION \"Changed\" : Void\r\nBEGIN\r\n#Result := MAX(#Value, 0);\r\nEND_FUNCTION\r\n");

        string firstSignature = _service.Validate("scl", first).QualificationSignature;
        string secondSignature = _service.Validate("scl", second).QualificationSignature;
        string changedSignature = _service.Validate("scl", changed).QualificationSignature;

        Assert.AreEqual(firstSignature, secondSignature);
        Assert.AreNotEqual(firstSignature, changedSignature);
    }

    [TestMethod]
    public void ValidateScl_LocalFbInstances_AreNotProjectDependencies()
    {
        string path = Write("Stateful.scl", "FUNCTION_BLOCK \"Stateful\"\r\nVAR_INPUT Enable : Bool; END_VAR\r\nVAR_OUTPUT Active : Bool; Rising : Bool; END_VAR\r\nVAR DelayTimer : TON_TIME; RisingEdge : R_TRIG; END_VAR\r\nBEGIN\r\n#DelayTimer.TON(IN := #Enable, PT := T#1s);\r\n#RisingEdge(CLK := #Enable);\r\n#Active := #DelayTimer.Q;\r\n#Rising := #RisingEdge.Q;\r\nEND_FUNCTION_BLOCK\r\n");

        var result = _service.ValidateScl(path);

        Assert.IsTrue(result.Valid, string.Join("; ", result.Errors));
        Assert.AreEqual("verified", result.Classification);
        Assert.AreEqual(0, result.Dependencies.Count);
    }

    [DataTestMethod]
    [DataRow("\"Reusable control\"")]
    [DataRow("ReusableControl")]
    [DataRow("\"VAR motor\"")]
    public void ValidateScl_SourceBoundInstanceDb_PreservesExactSubtype(string binding)
    {
        string path = Write("Instance.scl", $"FUNCTION_BLOCK {binding}\r\nVAR Value : Int; END_VAR\r\nBEGIN\r\nEND_FUNCTION_BLOCK\r\nDATA_BLOCK \"Instance\"\r\n{{ S7_Optimized_Access := 'TRUE' }}\r\nVERSION : 0.1\r\nNON_RETAIN\r\n(* FB binding, not an executable call *)\r\n{binding}\r\nBEGIN\r\nEND_DATA_BLOCK\r\n");
        var result = _service.ValidateScl(path);
        Assert.IsTrue(result.Valid, string.Join("; ", result.Errors));
        Assert.AreEqual("InstanceDB", result.DeclaredObjects.Single(item => item.Name == "Instance").ObjectType);
        Assert.AreEqual(0, result.Dependencies.Count);
        Assert.IsTrue(_service.DescribeFormats().Single(item => item.Format == "SCL").SupportedObjects.Contains("InstanceDB"));
    }

    [TestMethod]
    public void ValidateScl_TypeBoundGlobalDb_IsNotAnInstanceDb()
    {
        string path = Write("TypeDb.scl", "TYPE \"Record\"\r\nSTRUCT Value : Int; END_STRUCT;\r\nEND_TYPE\r\nDATA_BLOCK \"Records\"\r\n\"Record\"\r\nBEGIN\r\nEND_DATA_BLOCK\r\n");
        var result = _service.ValidateScl(path);
        Assert.IsTrue(result.Valid, string.Join("; ", result.Errors));
        Assert.AreEqual("GlobalDB", result.DeclaredObjects.Single(item => item.Name == "Records").ObjectType);
    }

    [TestMethod]
    public void ValidateScl_ExternalNamedDbBinding_FailsClosedAndRecordsDependency()
    {
        string path = Write("External.scl", "DATA_BLOCK \"External\"\r\n\"ProjectDefinition\"\r\nBEGIN\r\nEND_DATA_BLOCK\r\n");
        var result = _service.ValidateScl(path);
        Assert.IsFalse(result.Valid);
        Assert.AreEqual("known-unsupported", result.Classification);
        CollectionAssert.Contains(result.Dependencies.ToArray(), "ProjectDefinition");
        StringAssert.Contains(string.Join("; ", result.Errors), "does not distinguish");
    }

    [TestMethod]
    public void ValidateScl_CommentDeclarations_DoNotProveDbBinding()
    {
        string path = Write("Commented.scl", "(*\r\nFUNCTION_BLOCK \"NotARealFB\"\r\nBEGIN\r\nEND_FUNCTION_BLOCK\r\n*)\r\nDATA_BLOCK \"Instance\"\r\n\"NotARealFB\"\r\nBEGIN\r\nEND_DATA_BLOCK\r\n");
        var result = _service.ValidateScl(path);
        Assert.IsFalse(result.Valid);
        Assert.AreEqual(1, result.DeclaredObjects.Count);
        Assert.AreEqual(6, result.DeclaredObjects.Single().Line);
    }

    [TestMethod]
    public void ValidateScl_StructGlobalDbAndQuotedInitialValue_AreNotFbBindings()
    {
        string path = Write("Global.scl", "DATA_BLOCK \"Global\"\r\nVERSION : 0.1\r\nSTRUCT Text : String[30]; END_STRUCT;\r\nBEGIN\r\nText := '// not a comment';\r\nEND_DATA_BLOCK\r\n");
        var result = _service.ValidateScl(path);
        Assert.IsTrue(result.Valid, string.Join("; ", result.Errors));
        Assert.AreEqual("GlobalDB", result.DeclaredObjects.Single().ObjectType);
    }

    [TestMethod]
    public void ValidateScl_AmbiguousDbHeaderAndCrossSubtypeDuplicate_AreRejected()
    {
        string path = Write("Ambiguous.scl", "FUNCTION_BLOCK \"Duplicate\"\r\nBEGIN\r\nEND_FUNCTION_BLOCK\r\nDATA_BLOCK \"Duplicate\"\r\nFB 123\r\nBEGIN\r\nEND_DATA_BLOCK\r\n");
        var result = _service.ValidateScl(path);
        Assert.IsFalse(result.Valid);
        StringAssert.Contains(string.Join("; ", result.Errors), "ambiguous header");
        StringAssert.Contains(string.Join("; ", result.Errors), "more than once");
    }

    [TestMethod]
    public void ValidateScl_ParenthesizedBooleanExpressions_AreNotProjectDependencies()
    {
        string path = Write("BooleanExpressions.scl", "FUNCTION \"BooleanExpressions\" : Bool\r\nVAR_INPUT A : Real; B : Real; C : Bool; END_VAR\r\nBEGIN\r\nIF (#A < #B) OR (#A > 10.0) THEN\r\n  #BooleanExpressions := (#C AND (#A > 0.0));\r\nELSE\r\n  #BooleanExpressions := NOT (#C);\r\nEND_IF;\r\nEND_FUNCTION\r\n");

        var result = _service.ValidateScl(path);

        Assert.IsTrue(result.Valid, string.Join("; ", result.Errors));
        Assert.AreEqual("verified", result.Classification, string.Join("; ", result.Warnings));
        Assert.AreEqual(0, result.Dependencies.Count);
    }

    [TestMethod]
    public void ValidateScl_TimerStorageAliasInTemp_IsRejected()
    {
        string path = Write("BadTimer.scl", "FUNCTION_BLOCK \"BadTimer\"\r\nVAR_TEMP DelayTimer : TON_TIME; END_VAR\r\nBEGIN\r\nEND_FUNCTION_BLOCK\r\n");

        var result = _service.ValidateScl(path);

        Assert.IsFalse(result.Valid);
        StringAssert.Contains(string.Join("; ", result.Errors), "VAR_TEMP");
    }

    [TestMethod]
    public void ValidateSimaticMl_MatchingBaseline_IsVerified()
    {
        string xml = Xml("1", "10");
        string baseline = Write("baseline.xml", xml);
        string candidate = Write("candidate.xml", xml.Replace("Original", "Changed"));
        var result = _service.ValidateSimaticMl(candidate, baseline, false);
        Assert.IsTrue(result.Valid, string.Join("; ", result.Errors));
        Assert.AreEqual("verified", result.Classification);
        Assert.AreEqual("Probe", result.DeclaredObjects.Single().Name);
    }

    [TestMethod]
    public void ValidateSimaticMl_IdOrTopologyChange_FailsClosed()
    {
        string baseline = Write("baseline.xml", Xml("1", "10"));
        string candidate = Write("candidate.xml", Xml("2", "11"));
        var result = _service.ValidateSimaticMl(candidate, baseline, false);
        Assert.IsFalse(result.Valid);
        StringAssert.Contains(string.Join("; ", result.Errors), "IDs");
        StringAssert.Contains(string.Join("; ", result.Errors), "topology");
    }

    [TestMethod]
    public void ValidateSimaticMl_ConnectionReferencesMayReuseDefinitionUids()
    {
        const string flgNet = "<?xml version=\"1.0\"?><Document><SW.Blocks.FC ID=\"1\"><AttributeList><Name>Probe</Name></AttributeList><ObjectList><SW.Blocks.CompileUnit ID=\"2\"><AttributeList><NetworkSource><FlgNet xmlns=\"urn:siemens:test\"><Parts><Access UId=\"21\"/><Part UId=\"24\"/></Parts><Wires><Wire UId=\"27\"><NameCon UId=\"24\"/><IdentCon UId=\"21\"/></Wire></Wires></FlgNet></NetworkSource></AttributeList></SW.Blocks.CompileUnit></ObjectList></SW.Blocks.FC></Document>";
        string baseline = Write("flgnet-baseline.xml", flgNet);
        string candidate = Write("flgnet-candidate.xml", flgNet.Replace("<Name>Probe</Name>", "<Name>Probe</Name><Title>Reviewed</Title>"));

        var result = _service.ValidateSimaticMl(candidate, baseline, false, "/Document/SW.Blocks.FC[@ID='1']/AttributeList/Title");

        Assert.IsTrue(result.Valid, string.Join("; ", result.Errors));
        string invalid = Write("flgnet-duplicate.xml", flgNet.Replace("<Part UId=\"24\"/>", "<Part UId=\"24\"/><Part UId=\"24\"/>"));
        StringAssert.Contains(string.Join("; ", _service.ValidateSimaticMl(invalid, baseline, false).Errors), "definition UId");
    }

    [TestMethod]
    public void AnalyzeSimaticMlChange_ReportsPathsAndRejectsUnrelatedLeaves()
    {
        string baseline = Write("baseline-path.xml", Xml("1", "10"));
        string candidate = Write("candidate-path.xml", Xml("1", "10").Replace(">Original<", ">Changed<"));
        const string titlePath = "/Document/SW.Blocks.FC[@ID='1']/AttributeList/Original";

        var contract = _service.AnalyzeSimaticMlChange(candidate, baseline, false, titlePath);

        Assert.IsTrue(contract.ChangedSections.Single().StartsWith(titlePath, StringComparison.Ordinal));
        Assert.ThrowsException<InvalidDataException>(() => _service.AnalyzeSimaticMlChange(candidate, baseline, false, "/Document/SW.Blocks.FC[@ID='1']/AttributeList/Name"));
    }

    [TestMethod]
    public void ValidateSimaticMl_PlcStructBaseline_IsVerifiedAsType()
    {
        string baseline = Write("type-baseline.xml", TypeXml("ProbeType", "before"));
        string candidate = Write("type-candidate.xml", TypeXml("ProbeType", "after"));

        var result = _service.ValidateSimaticMl(candidate, baseline, false);

        Assert.IsTrue(result.Valid, string.Join("; ", result.Errors));
        Assert.AreEqual("verified", result.Classification);
        Assert.AreEqual("TYPE", result.DeclaredObjects.Single().ObjectType);
        Assert.AreEqual("ProbeType", result.DeclaredObjects.Single().Name);
    }

    [TestMethod]
    public void ValidateSimaticMl_Dtd_IsRejected()
    {
        string path = Write("bad.xml", "<!DOCTYPE x [<!ENTITY y SYSTEM 'file:///c:/windows/win.ini'>]><Document xmlns=\"urn:test\">&y;</Document>");
        var result = _service.ValidateSimaticMl(path, null, false);
        Assert.IsFalse(result.Valid);
        StringAssert.Contains(string.Join("; ", result.Errors), "unsafe");
    }

    [TestMethod]
    public void AnalyzeS7DclChange_ReportsOnlyAuthorizedSemanticSections()
    {
        string baselineDirectory = Path.Combine(_root, "baseline");
        string candidateDirectory = Path.Combine(_root, "candidate");
        Directory.CreateDirectory(baselineDirectory);
        Directory.CreateDirectory(candidateDirectory);
        const string baseline = "{ S7_BlockNumber := \"62001\"; S7_PreferredLanguage := \"LAD\" }\r\nFUNCTION \"Probe\" : Void\r\nVAR_INPUT\r\n  Enable : Bool;\r\nEND_VAR\r\nNETWORK\r\nEND_NETWORK\r\nEND_FUNCTION";
        File.WriteAllText(Path.Combine(baselineDirectory, "Probe.s7dcl"), baseline, new UTF8Encoding(true));
        File.WriteAllText(Path.Combine(candidateDirectory, "Probe.s7dcl"), baseline.Replace("END_NETWORK", "// reviewed body\r\nEND_NETWORK"), new UTF8Encoding(true));

        var contract = _service.AnalyzeS7DclChange(candidateDirectory, "Probe", baselineDirectory, "Probe", "Implementation");

        CollectionAssert.AreEqual(new[] { "Implementation" }, contract.ChangedSections.ToArray());
        Assert.IsFalse(string.IsNullOrWhiteSpace(contract.Fingerprint));
        Assert.AreEqual("BaselineBound", contract.Mode);
    }

    [TestMethod]
    public void AnalyzeS7DclChange_UnauthorizedOrIdentityChange_FailsClosed()
    {
        string baselineDirectory = Path.Combine(_root, "baseline");
        string candidateDirectory = Path.Combine(_root, "candidate");
        Directory.CreateDirectory(baselineDirectory);
        Directory.CreateDirectory(candidateDirectory);
        const string baseline = "{ S7_BlockNumber := \"62001\" }\r\nFUNCTION \"Probe\" : Void\r\nVAR_INPUT\r\n  Enable : Bool;\r\nEND_VAR\r\nEND_FUNCTION";
        File.WriteAllText(Path.Combine(baselineDirectory, "Probe.s7dcl"), baseline, new UTF8Encoding(true));
        File.WriteAllText(Path.Combine(candidateDirectory, "Probe.s7dcl"), baseline.Replace("Enable : Bool", "Enable : Bool;\r\n  Mode : Int").Replace(";;", ";"), new UTF8Encoding(true));

        StringAssert.Contains(Assert.ThrowsException<InvalidDataException>(() => _service.AnalyzeS7DclChange(candidateDirectory, "Probe", baselineDirectory, "Probe", "Implementation")).Message, "unauthorized");
        File.WriteAllText(Path.Combine(candidateDirectory, "Probe.s7dcl"), baseline.Replace("62001", "62002"), new UTF8Encoding(true));
        StringAssert.Contains(Assert.ThrowsException<InvalidDataException>(() => _service.AnalyzeS7DclChange(candidateDirectory, "Probe", baselineDirectory, "Probe", "Attributes")).Message, "name, type, and number");
    }

    private string Write(string name, string content)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllText(path, content, new UTF8Encoding(true));
        return path;
    }

    private static string Xml(string id, string uid) => $"<?xml version=\"1.0\"?><Document xmlns=\"urn:siemens:test\"><SW.Blocks.FC ID=\"{id}\"><AttributeList><Name>Probe</Name><Original>Original</Original></AttributeList><ObjectList><SW.Blocks.CompileUnit ID=\"cu\"><AttributeList><NetworkSource><FlgNet><Parts><Part UId=\"{uid}\" /></Parts></FlgNet></NetworkSource></AttributeList></SW.Blocks.CompileUnit></ObjectList></SW.Blocks.FC></Document>";
    private static string TypeXml(string name, string marker) => $"<?xml version=\"1.0\"?><Document><SW.Types.PlcStruct ID=\"0\"><AttributeList><Interface xmlns=\"urn:siemens:test\"><Marker>{marker}</Marker></Interface><Name>{name}</Name></AttributeList></SW.Types.PlcStruct></Document>";

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "skills")) && File.Exists(Path.Combine(directory.FullName, "OpenTiaToolkit.sln"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException();
    }
}
