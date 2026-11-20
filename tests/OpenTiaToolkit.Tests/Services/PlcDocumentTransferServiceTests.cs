using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Services;
using OpenTiaToolkit.Core.Services.V21;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Tests.Services
{
    [TestClass]
    public class PlcDocumentTransferServiceTests
    {
        private string _exportRoot = null!;

        [TestInitialize]
        public void Initialize() => _exportRoot = Path.Combine(Path.GetTempPath(), "ott-doc-transfer-" + Guid.NewGuid().ToString("N"));

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_exportRoot)) Directory.Delete(_exportRoot, true);
        }

        [DataTestMethod]
        [DataRow("OB")]
        [DataRow("FOB")]
        public async Task BulkExport_UsesSameDocumentApiAsSingleExportRegardlessOfBlockType(string type)
        {
            // This proves delegation, not that every real OB/FOB supports SD export.
            var block = new ExportableBlock { Name = "Cycle", Type = type };
            var service = CreateService(block);
            var singlePath = Path.Combine(_exportRoot, "single");
            var bulkPath = Path.Combine(_exportRoot, "bulk");

            Assert.IsTrue((await service.ExportBlockAsync(block.Name, singlePath)).Success);
            var result = await service.ExportBlocksAsync(null, bulkPath);

            Assert.AreEqual(1, result.TotalCount);
            Assert.AreEqual(1, result.SuccessCount);
            Assert.AreEqual(0, result.FailedCount);
            Assert.AreEqual(0, result.Errors.Count);
            CollectionAssert.AreEqual(new[] { "Cycle" }, result.ExportedItems.ToArray());
            Assert.AreEqual(2, block.ExportCalls);
            CollectionAssert.AreEqual(
                File.ReadAllBytes(Path.Combine(singlePath, "Cycle.s7dcl")),
                File.ReadAllBytes(Path.Combine(bulkPath, "Cycle.s7dcl")));
        }

        [DataTestMethod]
        [DataRow("state")]
        [DataRow("exception")]
        [DataRow("missing-api")]
        public async Task BulkExport_IsolatesActualTargetFailureAndContinues(string failure)
        {
            Block rejected = failure == "missing-api"
                ? new Block { Name = "Rejected", Type = "FOB" }
                : new ExportableBlock { Name = "Rejected", Type = "OB", State = "NotSupported", ThrowOnExport = failure == "exception" };
            var before = new ExportableBlock { Name = "Before", Type = "FB" };
            var after = new ExportableBlock { Name = "After", Type = "FC" };
            var service = CreateService(before, rejected, after);

            var result = await service.ExportBlocksAsync(null, _exportRoot);

            Assert.AreEqual(3, result.TotalCount);
            Assert.AreEqual(2, result.SuccessCount);
            Assert.AreEqual(1, result.FailedCount);
            CollectionAssert.AreEqual(new[] { "Before", "After" }, result.ExportedItems.ToArray());
            CollectionAssert.AreEqual(new[] { "Rejected" }, result.FailedItems.ToArray());
            string diagnostic = failure == "state" ? "Document export rejected."
                : failure == "exception" ? "Siemens rejected this target" : "no result state";
            StringAssert.Contains(result.Errors.Single(), diagnostic);
            Assert.AreEqual(1, before.ExportCalls);
            Assert.AreEqual(1, after.ExportCalls);
            if (rejected is ExportableBlock exportable) Assert.AreEqual(1, exportable.ExportCalls);
            Assert.IsFalse(File.Exists(Path.Combine(_exportRoot, "Rejected.s7dcl")));
        }

        [TestMethod]
        public async Task SingleExport_PreservesNativeFailureStateAndMessages()
        {
            var block = new ExportableBlock { Name = "Alarm", State = "Failure" };
            var result = await CreateService(block).ExportBlockAsync(block.Name, _exportRoot);

            Assert.IsFalse(result.Success);
            Assert.AreEqual("Failure", result.SiemensState);
            CollectionAssert.AreEqual(new[] { "Document export rejected." }, result.Diagnostics.ToArray());
            StringAssert.Contains(result.Message, "Document export rejected.");
        }

        [TestMethod]
        public async Task BulkExport_FilterCountsOnlySelectedTargetsIncludingOb()
        {
            var cycle = new ExportableBlock { Name = "Cycle", Type = "OB" };
            var excluded = new ExportableBlock { Name = "Helper", Type = "FC", ThrowOnExport = true };
            var result = await CreateService(cycle, excluded).ExportBlocksAsync("^cycle$", _exportRoot);

            Assert.AreEqual(1, result.TotalCount);
            Assert.AreEqual(1, result.SuccessCount);
            Assert.AreEqual(0, result.FailedCount);
            Assert.AreEqual(1, cycle.ExportCalls);
            Assert.AreEqual(0, excluded.ExportCalls);
        }

        [TestMethod]
        public async Task BulkExport_NoMatchesMakesNoExportCalls()
        {
            var block = new ExportableBlock { Name = "Cycle", Type = "OB" };
            var result = await CreateService(block).ExportBlocksAsync("^Missing$", _exportRoot);

            Assert.AreEqual(0, result.TotalCount);
            Assert.AreEqual(0, result.SuccessCount);
            Assert.AreEqual(0, result.FailedCount);
            Assert.AreEqual(0, result.Errors.Count);
            Assert.AreEqual(0, block.ExportCalls);
        }

        [TestMethod]
        public async Task BulkExport_InvalidFilterFailsBeforeDirectoryOrExport()
        {
            var block = new ExportableBlock { Name = "Cycle", Type = "OB" };
            var result = await CreateService(block).ExportBlocksAsync("[", _exportRoot);

            Assert.AreEqual(0, result.TotalCount);
            CollectionAssert.AreEqual(new[] { "Invalid regex filter: [" }, result.Errors.ToArray());
            Assert.AreEqual(0, block.ExportCalls);
            Assert.IsFalse(Directory.Exists(_exportRoot));
        }

        [TestMethod]
        public async Task BulkExport_UnsupportedPortalVersionStillFailsBeforeExport()
        {
            var block = new ExportableBlock { Name = "Cycle", Type = "OB" };
            var result = await CreateService("V20", block).ExportBlocksAsync(null, _exportRoot);

            Assert.AreEqual(0, result.TotalCount);
            Assert.AreEqual(1, result.Errors.Count);
            StringAssert.Contains(result.Errors.Single(), "requires a supported TIA Portal version");
            Assert.AreEqual(0, block.ExportCalls);
            Assert.IsFalse(Directory.Exists(_exportRoot));
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task BulkExport_ReadsEachNameOnceWithoutBrowseMetadata(bool filtered)
        {
            var blocks = Enumerable.Range(0, 1000).Select(i => new ExportableBlock
            {
                Name = "Block_" + i.ToString("D4"),
                ThrowOnMetadata = true
            }).ToArray();
            var result = await CreateService(blocks).ExportBlocksAsync(filtered ? "^block_0999$" : null, _exportRoot);

            Assert.AreEqual(filtered ? 1 : 1000, result.TotalCount);
            Assert.AreEqual(result.TotalCount, result.SuccessCount);
            Assert.AreEqual(0, result.FailedCount);
            Assert.IsTrue(blocks.All(block => block.NameReads == 1));
            Assert.IsTrue(blocks.All(block => block.MetadataReads == 0));
            Assert.AreEqual(result.TotalCount, blocks.Sum(block => block.ExportCalls));
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task BulkExport_IncludesNestedGroupsInTraversalOrder(bool filtered)
        {
            var software = new Software();
            software.BlockGroup.Blocks.Add(new ExportableBlock { Name = "Root" });
            var child = new BlockGroup();
            child.Blocks.Add(new ExportableBlock { Name = "Nested" });
            var deep = new BlockGroup();
            deep.Blocks.Add(new ExportableBlock { Name = "Deep" });
            child.Groups.Add(deep);
            software.BlockGroup.Groups.Add(child);
            var result = await CreateService("V21", software).ExportBlocksAsync(filtered ? "^(nested|deep)$" : null, _exportRoot);

            CollectionAssert.AreEqual(filtered ? new[] { "Nested", "Deep" } : new[] { "Root", "Nested", "Deep" }, result.ExportedItems.ToArray());
            Assert.AreEqual(filtered ? 2 : 3, result.TotalCount);
            Assert.AreEqual(result.TotalCount, result.SuccessCount);
            Assert.AreEqual(0, result.FailedCount);
        }

        [TestMethod]
        public async Task BulkExport_RejectsCaseInsensitiveFilenameCollisionsWithoutExportingEitherTarget()
        {
            var first = new ExportableBlock { Name = "Duplicate" };
            var second = new ExportableBlock { Name = "duplicate" };
            var other = new ExportableBlock { Name = "Unique" };
            var software = new Software();
            software.BlockGroup.Blocks.AddRange(new[] { first, other });
            var child = new BlockGroup(); child.Blocks.Add(second); software.BlockGroup.Groups.Add(child);
            var service = CreateService("V21", software);
            var result = await service.ExportBlocksAsync(null, _exportRoot);

            Assert.AreEqual(3, result.TotalCount);
            Assert.AreEqual(1, result.SuccessCount);
            Assert.AreEqual(2, result.FailedCount);
            Assert.AreEqual(0, first.ExportCalls);
            Assert.AreEqual(0, second.ExportCalls);
            CollectionAssert.AreEqual(new[] { "Unique" }, result.ExportedItems.ToArray());
            Assert.IsTrue(result.Errors.All(error => error.Contains("share the export filename")));
            Assert.IsFalse(File.Exists(Path.Combine(_exportRoot, "Duplicate.s7dcl")));
            var filtered = await service.ExportBlocksAsync("^Unique$", Path.Combine(_exportRoot, "filtered"));
            Assert.AreEqual(1, filtered.SuccessCount);
            Assert.AreEqual(0, filtered.FailedCount);
        }

        [TestMethod]
        public async Task BulkExport_ReobservesNewNestedTargetsOnTheNextInvocation()
        {
            var first = new ExportableBlock { Name = "First" };
            var software = new Software(); software.BlockGroup.Blocks.Add(first);
            var service = CreateService("V21", software);
            Assert.AreEqual(1, (await service.ExportBlocksAsync(null, _exportRoot)).SuccessCount);
            var added = new ExportableBlock { Name = "Added" };
            var child = new BlockGroup(); child.Blocks.Add(added); software.BlockGroup.Groups.Add(child);
            var next = await service.ExportBlocksAsync(null, Path.Combine(_exportRoot, "next"));

            CollectionAssert.AreEqual(new[] { "First", "Added" }, next.ExportedItems.ToArray());
            Assert.AreEqual(2, first.NameReads);
            Assert.AreEqual(1, added.NameReads);
        }

        [TestMethod]
        public async Task Import_RejectsNativePartialAndFailedResultsAndKeepsTheirDiagnostics()
        {
            var software = new Software();
            var service = CreateService("V21", software);
            Directory.CreateDirectory(_exportRoot);
            File.WriteAllText(Path.Combine(_exportRoot, "Probe.s7dcl"), "input");
            var blocks = software.BlockGroup.Blocks;
            blocks.ImportState = "PartialSuccess";

            var partial = await service.ImportBlockAsync(_exportRoot, "Probe", null, "Override", verify: true);

            Assert.IsFalse(partial.Success);
            Assert.AreEqual("PartialSuccess", partial.SiemensState);
            CollectionAssert.AreEqual(new[] { "Siemens diagnostic" }, partial.Diagnostics.ToArray());
            Assert.IsFalse(partial.Verified);

            blocks.ImportState = "Failure";
            Assert.IsFalse((await service.ImportBlockAsync(_exportRoot, "Probe", null, "Override", verify: false)).Success);
            var bulk = await service.ImportBlocksAsync(_exportRoot);
            Assert.AreEqual(0, bulk.SuccessCount);
            Assert.AreEqual(1, bulk.FailedCount);
            StringAssert.Contains(bulk.Errors.Single(), "Siemens diagnostic");
        }

        [TestMethod]
        public async Task Import_ReportsVerifiedChangeAlreadyMatchingAndUnverifiedSuccessSeparately()
        {
            var block = new ExportableBlock { Name = "Probe", Content = "baseline" };
            var software = new Software();
            software.BlockGroup.Blocks.Add(block);
            var service = CreateService("V21", software);
            Directory.CreateDirectory(_exportRoot);
            File.WriteAllText(Path.Combine(_exportRoot, "Probe.s7dcl"), "input\r\n");
            software.BlockGroup.Blocks.OnImport = () => block.Content = "input\n";

            var applied = await service.ImportBlockAsync(_exportRoot, "Probe", null, "Override", verify: true);
            var unchanged = await service.ImportBlockAsync(_exportRoot, "Probe", null, "Override", verify: true);
            software.BlockGroup.Blocks.OnImport = null;
            File.WriteAllText(Path.Combine(_exportRoot, "Probe.s7dcl"), "different input");
            var unverified = await service.ImportBlockAsync(_exportRoot, "Probe", null, "Override", verify: true);

            Assert.IsTrue(applied.Success, applied.Message);
            Assert.AreEqual(6, block.ExportCalls, "Each read-back comparison should export before and after import.");
            Assert.IsTrue(applied.Verified, applied.Message);
            Assert.IsFalse(applied.AlreadyMatching);
            Assert.IsTrue(unchanged.Success && unchanged.Verified && unchanged.AlreadyMatching);
            Assert.IsTrue(unverified.Success);
            Assert.IsFalse(unverified.Verified);
            Assert.IsFalse(unverified.AlreadyMatching);
        }

        [TestMethod]
        public async Task BulkExport_MissingRootFailsWithoutCreatingDirectory()
        {
            var context = new EngineeringSessionContext { PlcSoftware = new object(), CurrentPlcName = "PLC" };
            var service = new PlcDocumentTransferService(context, Mock.Of<IPortalService>(p => p.Version == "V21"), null,
                new V21DocumentAccess(new V21TypeServiceResolver()));
            var result = await service.ExportBlocksAsync(null, _exportRoot);
            CollectionAssert.AreEqual(new[] { "Could not get BlockGroup" }, result.Errors.ToArray());
            Assert.IsFalse(Directory.Exists(_exportRoot));
        }

        private static PlcDocumentTransferService CreateService(params Block[] blocks) => CreateService("V21", blocks);

        private static PlcDocumentTransferService CreateService(string version, params Block[] blocks)
        {
            var software = new Software();
            software.BlockGroup.Blocks.AddRange(blocks);
            return CreateService(version, software);
        }

        private static PlcDocumentTransferService CreateService(string version, Software software)
        {
            var context = new EngineeringSessionContext { PlcSoftware = software, CurrentPlcName = "PLC" };
            return new PlcDocumentTransferService(context,
                Mock.Of<IPortalService>(portal => portal.Version == version),
                null,
                new V21DocumentAccess(new V21TypeServiceResolver()));
        }

        private sealed class Software { public BlockGroup BlockGroup { get; } = new BlockGroup(); }
        private sealed class BlockGroup
        {
            public BlockCollection Blocks { get; } = new BlockCollection();
            public List<BlockGroup> Groups { get; } = new List<BlockGroup>();
        }
        private sealed class BlockCollection : List<Block>
        {
            public string ImportState { get; set; } = "Success";
            public Action? OnImport { get; set; }

            public ImportStateResult ImportFromDocuments(DirectoryInfo directory, string name, int options)
            {
                OnImport?.Invoke();
                return new ImportStateResult
                {
                    State = ImportState,
                    Messages = ImportState == "Success" ? Array.Empty<ImportMessage>() : new[] { new ImportMessage() }
                };
            }
        }
        private sealed class ImportStateResult
        {
            public string State { get; set; } = string.Empty;
            public ImportMessage[] Messages { get; set; } = Array.Empty<ImportMessage>();
        }
        private sealed class ImportMessage { public string Message => "Siemens diagnostic"; }
        private class Block
        {
            private string _name = string.Empty;
            private string _type = "FC";
            public int NameReads { get; private set; }
            public int MetadataReads { get; private set; }
            public bool ThrowOnMetadata { get; set; }
            public string Name { get { NameReads++; return _name; } set => _name = value; }
            public string Type { get => ReadMetadata(_type); set => _type = value; }
            public int Number => ReadMetadata(1);
            public string ProgrammingLanguage => ReadMetadata("SCL");
            public bool IsReadOnly => ReadMetadata(false);
            public string Comment => ReadMetadata("Comment");
            private T ReadMetadata<T>(T value)
            {
                MetadataReads++;
                if (ThrowOnMetadata) throw new InvalidOperationException("Export must not read browse metadata.");
                return value;
            }
        }
        private sealed class ExportableBlock : Block
        {
            public int ExportCalls { get; private set; }
            public string State { get; set; } = "Success";
            public string Content { get; set; } = "stable documents";
            public bool ThrowOnExport { get; set; }
            public ExportState ExportAsDocuments(DirectoryInfo directory, string baseName)
            {
                ExportCalls++;
                // Siemens V21 checks DirectoryInfo's string representation, not just FullName.
                if (!Path.IsPathRooted(directory.ToString())) throw new ArgumentException("Siemens requires an absolute directory argument.");
                if (ThrowOnExport) throw new InvalidOperationException("Siemens rejected this target");
                if (State == "Success") File.WriteAllText(Path.Combine(directory.FullName, baseName + ".s7dcl"), Content);
                return new ExportState
                {
                    State = State,
                    Messages = State == "Success" ? Array.Empty<ExportMessage>() : new[] { new ExportMessage() }
                };
            }
        }
        private sealed class ExportState
        {
            public string State { get; set; } = string.Empty;
            public ExportMessage[] Messages { get; set; } = Array.Empty<ExportMessage>();
        }
        private sealed class ExportMessage { public string Message => "Document export rejected."; }
    }
}
