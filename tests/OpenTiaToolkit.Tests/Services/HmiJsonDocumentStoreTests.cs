using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Services;
using System;
using System.IO;

namespace OpenTiaToolkit.Tests.Services
{
    [TestClass]
    public class HmiJsonDocumentStoreTests
    {
        [TestMethod]
        public void ResolveImportFilePath_PrefersExportFileThenPatchFileInsideDirectory()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "OpenTiaToolkit", "HmiJsonDocumentStore", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            try
            {
                var exportFile = Path.Combine(tempRoot, "unified_hmi_export.json");
                var patchFile = Path.Combine(tempRoot, "unified_hmi_patch.json");

                File.WriteAllText(patchFile, "{}");
                Assert.AreEqual(patchFile, HmiJsonDocumentStore.ResolveImportFilePath(tempRoot));

                File.WriteAllText(exportFile, "{}");
                Assert.AreEqual(exportFile, HmiJsonDocumentStore.ResolveImportFilePath(tempRoot));
            }
            finally
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        [TestMethod]
        public void SaveDocument_CreatesParentDirectoryAndRoundTripsDocument()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "OpenTiaToolkit", "HmiJsonDocumentStore", Guid.NewGuid().ToString("N"));
            var outputPath = Path.Combine(tempRoot, "nested", "unified_hmi_export.json");

            try
            {
                HmiJsonDocumentStore.SaveDocument(
                    new HmiJsonDocument
                    {
                        HmiName = "HMI_1",
                        ExportedAtUtc = "2026-06-13T00:00:00.0000000Z",
                        Screens =
                        {
                            new HmiJsonScreen { Path = "Screens/Main", Name = "Main" }
                        }
                    },
                    outputPath);

                var loaded = HmiJsonDocumentStore.LoadDocument(outputPath);

                Assert.AreEqual("HMI_1", loaded.HmiName);
                Assert.AreEqual(1, loaded.Screens.Count);
                Assert.AreEqual("Screens/Main", loaded.Screens[0].Path);
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
        }
    }
}
