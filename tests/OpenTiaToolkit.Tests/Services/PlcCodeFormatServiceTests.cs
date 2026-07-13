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
    public void ValidateSimaticMl_Dtd_IsRejected()
    {
        string path = Write("bad.xml", "<!DOCTYPE x [<!ENTITY y SYSTEM 'file:///c:/windows/win.ini'>]><Document xmlns=\"urn:test\">&y;</Document>");
        var result = _service.ValidateSimaticMl(path, null, false);
        Assert.IsFalse(result.Valid);
        StringAssert.Contains(string.Join("; ", result.Errors), "unsafe");
    }

    private string Write(string name, string content)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllText(path, content, new UTF8Encoding(true));
        return path;
    }

    private static string Xml(string id, string uid) => $"<?xml version=\"1.0\"?><Document xmlns=\"urn:siemens:test\"><SW.Blocks.FC ID=\"{id}\"><AttributeList><Name>Probe</Name><Original>Original</Original></AttributeList><ObjectList><SW.Blocks.CompileUnit ID=\"cu\"><AttributeList><NetworkSource><FlgNet><Parts><Part UId=\"{uid}\" /></Parts></FlgNet></NetworkSource></AttributeList></SW.Blocks.CompileUnit></ObjectList></SW.Blocks.FC></Document>";

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
