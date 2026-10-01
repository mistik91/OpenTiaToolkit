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

            Assert.IsTrue(await service.ExportBlockAsync(block.Name, singlePath));
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
            CollectionAssert.AreEqual(new[] { "Failed to export: Rejected" }, result.Errors.ToArray());
            Assert.AreEqual(1, before.ExportCalls);
            Assert.AreEqual(1, after.ExportCalls);
            if (rejected is ExportableBlock exportable) Assert.AreEqual(1, exportable.ExportCalls);
            Assert.IsFalse(File.Exists(Path.Combine(_exportRoot, "Rejected.s7dcl")));
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

        private static PlcDocumentTransferService CreateService(params Block[] blocks) => CreateService("V21", blocks);

        private static PlcDocumentTransferService CreateService(string version, params Block[] blocks)
        {
            var software = new Software();
            software.BlockGroup.Blocks.AddRange(blocks);
            var context = new EngineeringSessionContext { PlcSoftware = software, CurrentPlcName = "PLC" };
            return new PlcDocumentTransferService(context,
                Mock.Of<IPortalService>(portal => portal.Version == version),
                new PlcBrowseService(context, null), null,
                new V21DocumentAccess(new V21TypeServiceResolver()));
        }

        private sealed class Software { public BlockGroup BlockGroup { get; } = new BlockGroup(); }
        private sealed class BlockGroup { public List<Block> Blocks { get; } = new List<Block>(); }
        private class Block
        {
            public string Name { get; set; } = string.Empty;
            public string Type { get; set; } = "FC";
        }
        private sealed class ExportableBlock : Block
        {
            public int ExportCalls { get; private set; }
            public string State { get; set; } = "Success";
            public bool ThrowOnExport { get; set; }
            public ExportState ExportAsDocuments(DirectoryInfo directory, string baseName)
            {
                ExportCalls++;
                if (ThrowOnExport) throw new InvalidOperationException("Siemens rejected this target");
                if (State == "Success") File.WriteAllText(Path.Combine(directory.FullName, baseName + ".s7dcl"), "stable documents");
                return new ExportState { State = State };
            }
        }
        private sealed class ExportState { public string State { get; set; } = string.Empty; }
    }
}
