using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Core.Services;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;

namespace OpenTiaToolkit.Tests.Commands;

[TestClass]
public class HmiTagExportTests
{
    private const string SimpleTags = "#Version: 2.0\nSimpleTags:\n  One:\n    DataType: Bool\n";

    [TestMethod]
    public void StandaloneExport_ReportsActualFileAndUsesImportRules()
    {
        WithDirectory(root =>
        {
            var output = Path.Combine(root, "Requested.hmi.yml");
            var result = HmiTagExport.Write("HMI", "Group/Table", output, false, stage =>
                File.WriteAllText(Path.Combine(stage.FullName, "Native.hmi.yml"), SimpleTags), CancellationToken.None);

            Assert.AreEqual(output, result.OutputPath);
            Assert.AreEqual(new FileInfo(output).Length, result.OutputBytes);
            Assert.AreEqual(SimpleTags, File.ReadAllText(output));
            Assert.IsTrue(result.SupportsToolkitImport);
            Assert.AreEqual(1, result.ImportableTagCount);
            Assert.IsEmpty(Directory.GetDirectories(root));
        });
    }

    [TestMethod]
    public void StandaloneExport_DoesNotReplaceAnExistingFileWhenDeniedOrWhenNativeOutputIsIncomplete()
    {
        WithDirectory(root =>
        {
            var output = Path.Combine(root, "Requested.hmi.yml");
            File.WriteAllText(output, "original");
            Assert.ThrowsException<IOException>(() => HmiTagExport.Write("HMI", "Table", output, false,
                _ => Assert.Fail("Overwrite should be blocked before invoking TIA."), CancellationToken.None));
            Assert.ThrowsException<InvalidDataException>(() => HmiTagExport.Write("HMI", "Table", output, true,
                _ => { }, CancellationToken.None));
            Assert.AreEqual("original", File.ReadAllText(output));
            Assert.IsEmpty(Directory.GetDirectories(root));
        });
    }

    [TestMethod]
    public void CompanionDefinitions_RequireZipAndArePreservedInIt()
    {
        WithDirectory(root =>
        {
            var output = Path.Combine(root, "Requested.hmi.yml");
            void NativeExport(DirectoryInfo stage)
            {
                File.WriteAllText(Path.Combine(stage.FullName, "Tags.hmi.yml"), SimpleTags);
                File.WriteAllText(Path.Combine(stage.FullName, "Tags.def.hmi.yml"), "TypeDefinitions: {}");
                File.WriteAllText(Path.Combine(stage.FullName, "NameData.yml"), "metadata");
            }

            Assert.ThrowsException<InvalidDataException>(() =>
                HmiTagExport.Write("HMI", "Table", output, false, NativeExport, CancellationToken.None));
            Assert.IsFalse(File.Exists(output));

            var zip = Path.Combine(root, "Requested.zip");
            var result = HmiTagExport.Write("HMI", "Table", zip, false, NativeExport, CancellationToken.None);
            Assert.IsFalse(result.SupportsToolkitImport);
            StringAssert.Contains(result.ImportLimitation, "companion definitions");
            using (var stream = File.OpenRead(zip))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
                CollectionAssert.AreEquivalent(new[] { "Tags.hmi.yml", "Tags.def.hmi.yml" },
                    archive.Entries.Select(entry => entry.FullName).ToArray());
            Assert.IsEmpty(Directory.GetDirectories(root));
        });
    }

    private static void WithDirectory(Action<string> test)
    {
        var root = Path.Combine(Path.GetTempPath(), "OttTagExportTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { test(root); }
        finally { Directory.Delete(root, recursive: true); }
    }
}
