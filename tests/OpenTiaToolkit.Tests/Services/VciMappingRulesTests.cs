using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Services;
using System;
using System.IO;

namespace OpenTiaToolkit.Tests.Services;

[TestClass]
public class VciMappingRulesTests
{
    [DataTestMethod]
    [DataRow("GlobalDB", "DB")]
    [DataRow("globaldb", "DB")]
    [DataRow("DB", "DB")]
    [DataRow("InstanceDB", "InstanceDB")]
    public void BlockTypeFilter_PreservesDbAliases(string input, string expected)
    {
        Assert.AreEqual(expected, VciService.NormalizeBlockTypeFilter(input));
    }

    [TestMethod]
    public void MappingDirectory_PassesRelativeSpellingToSiemensAndCreatesDirectoryUnderWorkspace()
    {
        string root = Path.Combine(Path.GetTempPath(), "ott-vci-rules-" + Guid.NewGuid().ToString("N"));
        string child = Path.Combine("PLC_1", "Program blocks");
        try
        {
            Assert.AreEqual(Path.DirectorySeparatorChar.ToString(), VciService.WorkspaceDirectory(root, ".").ToString());
            Assert.AreEqual(Path.DirectorySeparatorChar + child, VciService.WorkspaceDirectory(root, child).ToString());
            Assert.IsTrue(Directory.Exists(Path.Combine(root, child)));
            Assert.ThrowsException<ArgumentException>(() => VciService.WorkspaceDirectory(root, Path.Combine(root, child)));
            Assert.ThrowsException<ArgumentException>(() => VciService.WorkspaceDirectory(root, @"..\outside"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void SimaticSdComparison_DistinguishesMismatchFromMissingReadBack()
    {
        Assert.AreEqual(true, VciSimaticSdEvidence.Matches("same", "same"));
        Assert.AreEqual(false, VciSimaticSdEvidence.Matches("source", "different"));
        Assert.IsNull(VciSimaticSdEvidence.Matches("source", null));
    }

    [DataTestMethod]
    [DataRow("Equal", false, true, "Unequal", false)]
    [DataRow("Equal", null, true, "Unknown", false)]
    [DataRow("Equal", true, true, "Equal", true)]
    [DataRow("Equal", null, false, "Equal", true)]
    [DataRow("Unequal", true, true, "Unequal", false)]
    public void SyncOutcome_RequiresNativeEqualityAndApplicableDocumentEvidence(
        string siemensState, bool? verified, bool checkDocuments, string expectedState, bool success)
    {
        var item = new VciSyncItemResult { Status = siemensState, Success = true, Message = "Synchronized." };
        VciService.RecordSyncOutcome(item, siemensState, verified, checkDocuments);

        Assert.AreEqual(siemensState, item.SiemensStatus);
        Assert.AreEqual(expectedState, item.Status);
        Assert.AreEqual(success, item.Success);
    }
}
