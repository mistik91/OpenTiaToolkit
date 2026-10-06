using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Services;

namespace OpenTiaToolkit.Tests.Services;

[TestClass]
public sealed class PlcBehaviorVectorServiceTests
{
    private string _root = null!;

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "OpenTiaToolkit.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [TestMethod]
    public void CompareGuard_AcceptsRemovedBaselineDifferencesButRejectsNewOnes()
    {
        var known = new PlcSoftwareCompareEntry("Program/Old", "Different", "Old", "Old", "known", true);
        var unexpected = new PlcSoftwareCompareEntry("Program/New", "Different", "New", "New", "unexpected", true);
        var baseline = new PlcSoftwareCompareResult(true, true, "baseline", new[] { known });

        Assert.IsTrue(PlcDocumentWorkflowService.HasNoNewCompareDifferences(baseline, new PlcSoftwareCompareResult(true, true, "synchronized")));
        Assert.IsTrue(PlcDocumentWorkflowService.HasNoNewCompareDifferences(baseline, new PlcSoftwareCompareResult(true, true, "unchanged", new[] { known })));
        Assert.IsFalse(PlcDocumentWorkflowService.HasNoNewCompareDifferences(baseline, new PlcSoftwareCompareResult(true, true, "new", new[] { known, unexpected })));
    }

    [TestMethod]
    public void Validate_AcceptsDeterministicOwnedVector()
    {
        string path = Write("valid.json", Vector("Codex_Behavior_IO.Seed", includeCleanup: true));
        var result = new PlcBehaviorVectorService().Validate(path, new[] { "Codex_Behavior_IO" });

        Assert.IsTrue(result.Valid, string.Join("; ", result.Errors));
        Assert.AreEqual(2, result.ReferencedTags.Count);
        Assert.AreEqual(64, result.VectorHash.Length);
    }

    [TestMethod]
    public void Validate_BlocksProductionTagOutsideDisposableOwner()
    {
        string path = Write("outside.json", Vector("Production_DB.Seed", includeCleanup: true));
        var result = new PlcBehaviorVectorService().Validate(path, new[] { "Codex_Behavior_IO" });

        Assert.IsFalse(result.Valid);
        StringAssert.Contains(string.Join("; ", result.Errors), "outside the declared disposable data-block owners");
    }

    [TestMethod]
    public void Validate_RequiresCleanupForEveryStimulatedTag()
    {
        string path = Write("cleanup.json", Vector("Codex_Behavior_IO.Seed", includeCleanup: false));
        var result = new PlcBehaviorVectorService().Validate(path, new[] { "Codex_Behavior_IO" });

        Assert.IsFalse(result.Valid);
        StringAssert.Contains(string.Join("; ", result.Errors), "Cleanup must restore every stimulated tag");
    }

    [TestMethod]
    public void Validate_RejectsUnsupportedTypeAndUnboundedTiming()
    {
        string json = Vector("Codex_Behavior_IO.Seed", includeCleanup: true)
            .Replace("\"Int32\"", "\"DateAndTime\"")
            .Replace("\"timeoutMilliseconds\": 1000", "\"timeoutMilliseconds\": 400000");
        var result = new PlcBehaviorVectorService().Validate(Write("invalid.json", json), new[] { "Codex_Behavior_IO" });

        Assert.IsFalse(result.Valid);
        StringAssert.Contains(string.Join("; ", result.Errors), "unsupported type");
        StringAssert.Contains(string.Join("; ", result.Errors), "timeoutMilliseconds");
    }

    [TestMethod]
    public void Validate_HashBindsExactVectorBytes()
    {
        var service = new PlcBehaviorVectorService();
        string first = Write("first.json", Vector("Codex_Behavior_IO.Seed", includeCleanup: true));
        string second = Write("second.json", Vector("Codex_Behavior_IO.Seed", includeCleanup: true).Replace("behavior-vector", "behavior-vector-2"));

        Assert.AreNotEqual(service.Validate(first).VectorHash, service.Validate(second).VectorHash);
    }

    private string Write(string name, string content) { string path = Path.Combine(_root, name); File.WriteAllText(path, content); return path; }

    private static string Vector(string writeTag, bool includeCleanup) => @"
{
  ""schemaVersion"": ""1.0"",
  ""vectorId"": ""behavior-vector"",
  ""reset"": [ { ""tag"": ""Codex_Behavior_IO.Seed"", ""type"": ""Int32"", ""value"": 0 } ],
  ""steps"": [
    {
      ""name"": ""calculate"",
      ""writes"": [ { ""tag"": """ + writeTag + @""", ""type"": ""Int32"", ""value"": 7 } ],
      ""settleMilliseconds"": 25,
      ""assertions"": [ { ""tag"": ""Codex_Behavior_IO.Result"", ""type"": ""Int32"", ""operator"": ""Equal"", ""expected"": 13, ""timeoutMilliseconds"": 1000, ""pollMilliseconds"": 25 } ]
    }
  ],
  ""cleanup"": [ " + (!includeCleanup ? string.Empty : @"{ ""tag"": ""Codex_Behavior_IO.Seed"", ""type"": ""Int32"", ""value"": 0 }" + (writeTag.Equals("Codex_Behavior_IO.Seed", StringComparison.OrdinalIgnoreCase) ? string.Empty : @", { ""tag"": """ + writeTag + @""", ""type"": ""Int32"", ""value"": 0 }")) + @" ]
}";
}
