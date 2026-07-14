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
