using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Services;
using System.Linq;

namespace OpenTiaToolkit.Tests.Services;

[TestClass]
public sealed class HmiTrendBatchRulesTests
{
    [DataTestMethod]
    [DataRow("size")]
    [DataRow("retention")]
    [DataRow("segment")]
    public void ConflictingArchiveSettings_AreRejectedInEitherOrder(string field)
    {
        var first = new HmiTrendCreateRequest { DataLogName = "Shared" };
        var second = new HmiTrendCreateRequest { DataLogName = "shared" };
        if (field == "size") second.LogMaxSize++;
        if (field == "retention") second.DurationDays++;
        if (field == "segment") second.SegmentMaxSize++;
        foreach (var requests in new[] { new[] { first, second }, new[] { second, first } })
        {
            var conflicts = HmiTrendBatchRules.ConflictingDataLogs(requests);
            Assert.AreEqual(1, conflicts.Count);
            Assert.AreEqual("shared", conflicts.Single().ToLowerInvariant());
        }
    }

    [TestMethod]
    public void IndependentTagSettingsAndReadOnlyFolder_DoNotConflict()
    {
        var first = new HmiTrendCreateRequest { DataLogName = "Shared", TagPath = "A", Cyclic = true, Cycle = "T1s", StorageFolder = "/A" };
        var second = new HmiTrendCreateRequest { DataLogName = "Shared", TagPath = "B", Cyclic = false, Cycle = null, StorageFolder = "/B" };
        Assert.AreEqual(0, HmiTrendBatchRules.ConflictingDataLogs(new[] { first, second }).Count);
    }

    [TestMethod]
    public void EffectiveNormalizedValues_AreComparedInsteadOfRawInput()
    {
        var first = new HmiTrendCreateRequest { DataLogName = "Shared", LogMaxSize = -5, DurationDays = 0, SegmentMaxSize = -2 };
        var second = new HmiTrendCreateRequest { DataLogName = "Shared", LogMaxSize = 1, DurationDays = 1, SegmentMaxSize = 1 };
        Assert.AreEqual(0, HmiTrendBatchRules.ConflictingDataLogs(new[] { first, second }).Count);
    }

    [TestMethod]
    public void OmittedFieldsRetainDefaultsAndIndependentArchivesMayDiffer()
    {
        var defaults = new HmiTrendCreateRequest { DataLogName = "A" };
        var explicitDefaults = new HmiTrendCreateRequest { DataLogName = "A", LogMaxSize = 2000, DurationDays = 365, SegmentMaxSize = 500 };
        var independent = new HmiTrendCreateRequest { DataLogName = "B", DurationDays = 7 };
        Assert.AreEqual(0, HmiTrendBatchRules.ConflictingDataLogs(new[] { defaults, explicitDefaults, independent }).Count);
        independent.DataLogName = "A";
        Assert.AreEqual("A", HmiTrendBatchRules.ConflictingDataLogs(new[] { defaults, independent }).Single());
    }

    [TestMethod]
    public void ValidationIsFreshForEachBatch()
    {
        var first = new HmiTrendCreateRequest { DataLogName = "A" };
        var second = new HmiTrendCreateRequest { DataLogName = "A" };
        Assert.AreEqual(0, HmiTrendBatchRules.ConflictingDataLogs(new[] { first, second }).Count);
        second.DurationDays = 7;
        Assert.AreEqual(1, HmiTrendBatchRules.ConflictingDataLogs(new[] { first, second }).Count);
        first.DurationDays = 7;
        Assert.AreEqual(0, HmiTrendBatchRules.ConflictingDataLogs(new[] { first, second }).Count);
    }
}
