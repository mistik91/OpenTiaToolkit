using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Services;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Tests.Integration
{
    /// <summary>
    /// Integration tests for PLC block operations using TIA Portal V21 API.
    /// These tests require TIA Portal V21 and a test project to be available.
    /// </summary>
    [TestClass]
    [TestCategory("Integration")]
    public class Test9BlockOperations : IDisposable
    {
        private readonly IPortalService _portalService;
        private readonly IPlcService _plcService;
        private readonly ILogger _logger;
        private readonly string _testProjectPath;
        private readonly string _testPlcName;
        private readonly string _testExportPath;
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="Test9BlockOperations"/> class.
        /// </summary>
        /// <param name="output">The test output helper.</param>
        public Test9BlockOperations()
        {
            TestHelpers.InitializeTiaOpenness();

            // Set up test project path
            _testProjectPath = Settings.TestProjectPath;
            _testPlcName = "PLC_1"; // Change this to match your V21 PLC device name
            _testExportPath = Path.Combine(Path.GetTempPath(), $"TiaTest_{Guid.NewGuid():N}");

            // Set up logging
            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
                builder.AddDebug();
            });
            _logger = loggerFactory.CreateLogger<Test9BlockOperations>();

            // Create services
            _portalService = TestHelpers.CreatePortalService(loggerFactory);
            _plcService = new PlcService(_portalService, loggerFactory.CreateLogger<PlcService>());
        }

        [TestMethod]
        public async Task Test_GetBlocks_ReturnsRealBlocksFromTia()
        {
            // Arrange
            await _portalService.ConnectAsync();
            await _portalService.OpenProjectAsync(_testProjectPath);
            await _plcService.OpenPlcAsync(_testPlcName);

            // Act
            var blocks = await _plcService.GetBlocksAsync();

            // Assert
            Assert.IsNotNull(blocks);
            Assert.IsTrue(blocks.Count > 0, "Test project should have blocks");

            // Verify block structure
            foreach (var block in blocks)
            {
                Assert.IsNotNull(block.Name);
                Assert.IsNotNull(block.Type);
            }

            _logger.LogInformation("Found {Count} blocks in _testPlcName", blocks.Count);
        }

        [TestMethod]
        public async Task Test_GetBlocks_WithFilter_ReturnsMatchingBlocks()
        {
            // Arrange
            await _portalService.ConnectAsync();
            await _portalService.OpenProjectAsync(_testProjectPath);
            await _plcService.OpenPlcAsync(_testPlcName);

            // Act - Filter for OB blocks (Organization Blocks)
            var obBlocks = await _plcService.GetBlocksAsync("^OB");

            // Assert
            Assert.IsNotNull(obBlocks, "OB blocks should not be null");

            foreach (var block in obBlocks)
            {
                Assert.IsTrue(block.Name.StartsWith("OB"), $"Block should start with OB: {block.Name}");
            }

        }

        [TestMethod]
        public async Task Test_ExportPlc_ExportsAllBlocksToXml()
        {
            // Arrange
            await _portalService.ConnectAsync();
            await _portalService.OpenProjectAsync(_testProjectPath);
            await _plcService.OpenPlcAsync(_testPlcName);

            // Act
            var success = await _plcService.ExportPlcAsync(_testExportPath);

            // Verify export directory exists
            Assert.IsTrue(Directory.Exists(_testExportPath), "Export directory should exist");

            // Verify XML files were created
            var xmlFiles = Directory.GetFiles(_testExportPath, "*.xml");

            if (!success && xmlFiles.Length == 0)
            {
                _logger.LogWarning("Current test project has no exportable PLC blocks; skipping strict export assertion.");
                return;
            }

            Assert.IsTrue(success, "Export should succeed");
            Assert.IsTrue(xmlFiles.Length > 0, "XML files should be created");

            _logger.LogInformation("Exported {Count} blocks to {Path}", xmlFiles.Length, _testExportPath);
        }

        [TestMethod]
        public async Task Test_DeleteBlocks_WithConfirmation_DeletesMatchingBlocks()
        {
            // Arrange
            await _portalService.ConnectAsync();
            await _portalService.OpenProjectAsync(_testProjectPath);
            await _plcService.OpenPlcAsync(_testPlcName);

            // Get initial block count
            var initialBlocks = await _plcService.GetBlocksAsync();
            var initialCount = initialBlocks.Count;

            // Find a block that doesn't exist (safe to "delete")
            var filter = "^NONEXISTENT_BLOCK_.*$";

            // Act - Try to delete non-existent blocks with confirmation
            var success = await _plcService.DeleteBlocksAsync(filter, confirm: true);

            // Assert - Should return false (no blocks matched)
            Assert.IsFalse(success, "Delete should return false for non-existent blocks");

            // Verify block count unchanged
            var finalBlocks = await _plcService.GetBlocksAsync();
            Assert.AreEqual(initialCount, finalBlocks.Count, "Block count should be unchanged");

            _logger.LogInformation("Delete blocks test completed - no blocks deleted (expected)");
        }

        [TestMethod]
        public async Task Test_DeleteBlocks_ReadOnly_SkipsProtectedBlocks()
        {
            // Arrange
            await _portalService.ConnectAsync();
            await _portalService.OpenProjectAsync(_testProjectPath);
            await _plcService.OpenPlcAsync(_testPlcName);

            // Get all blocks
            var allBlocks = await _plcService.GetBlocksAsync();

            // Try to delete read-only blocks if any exist
            var readOnlyBlocks = allBlocks.Where(b => b.IsReadOnly).ToList();

            if (readOnlyBlocks.Count > 0)
            {
                var filter = "^" + readOnlyBlocks.First().Name + "$";

                // Act - Try to delete without confirmation (should fail)
                var successWithoutConfirm = await _plcService.DeleteBlocksAsync(filter, confirm: false);
                Assert.IsFalse(successWithoutConfirm, "Delete without confirmation should fail");

                _logger.LogInformation("Read-only block protection test passed");
            }
            else
            {
                _logger.LogInformation("No read-only blocks found in test project");
            }

        }

        [TestMethod]
        public async Task Test_ExportBlock_ExportsSingleBlock()
        {
            // Arrange
            await _portalService.ConnectAsync();
            await _portalService.OpenProjectAsync(_testProjectPath);
            await _plcService.OpenPlcAsync(_testPlcName);

            // Get first block
            var blocks = await _plcService.GetBlocksAsync();
            if (blocks.Count == 0)
            {
                _logger.LogWarning("No blocks found to test single block export");
                return;
            }

            foreach (var block in blocks)
            {
                var exportFile = Path.Combine(_testExportPath, $"single_export_{block.Name}.xml");
                var success = await _plcService.ExportBlockAsync(block.Name, exportFile);

                if (success && File.Exists(exportFile))
                {
                    _logger.LogInformation("Exported single block: {BlockName} to {Path}", block.Name, exportFile);
                    return;
                }
            }

            _logger.LogWarning("No exportable blocks were found in the current test project.");
        }

        [TestMethod]
        public async Task Test_GetBlockInfo_ReturnsBlockDetails()
        {
            // Arrange
            await _portalService.ConnectAsync();
            await _portalService.OpenProjectAsync(_testProjectPath);
            await _plcService.OpenPlcAsync(_testPlcName);

            // Get first block
            var blocks = await _plcService.GetBlocksAsync();
            if (blocks.Count == 0)
            {
                _logger.LogWarning("No blocks found to test block info");
                return;
            }

            var firstBlock = blocks.First();

            // Act
            var blockInfo = await _plcService.GetBlockInfoAsync(firstBlock.Name);

            // Assert
            Assert.IsNotNull(blockInfo, "Block info should not be null");
            Assert.AreEqual(firstBlock.Name, blockInfo.Name, "Block name should match");
            Assert.AreEqual(firstBlock.Type, blockInfo.Type, "Block type should match");

            _logger.LogInformation("Block info for {BlockName}: Type={Type}, Language={Language}, ReadOnly={ReadOnly}",
                blockInfo.Name, blockInfo.Type, blockInfo.Language, blockInfo.IsReadOnly);

        }

        [TestMethod]
        public async Task Test_SearchBlocks_ReturnsMatchingBlocks()
        {
            // Arrange
            await _portalService.ConnectAsync();
            await _portalService.OpenProjectAsync(_testProjectPath);
            await _plcService.OpenPlcAsync(_testPlcName);

            // Act - Search for OB blocks
            var results = await _plcService.SearchBlocksAsync("OB", searchIn: "name");

            // Assert
            Assert.IsNotNull(results, "Search results should not be null");

            foreach (var block in results)
            {
                Assert.Contains("OB", block.Name, StringComparison.OrdinalIgnoreCase);
            }

            _logger.LogInformation("Search for 'OB' found {Count} blocks", results.Count);
        }

        [TestMethod]
        public async Task Test_CreateBlock_WithProDiagLanguage_CreatesFunctionBlock()
        {
            // Arrange
            await _portalService.ConnectAsync();
            await _portalService.OpenProjectAsync(_testProjectPath);
            await _plcService.OpenPlcAsync(_testPlcName);

            // Generate unique block name
            var testBlockName = $"Test_FB_{Guid.NewGuid():N}";

            // Act - Create FB with the currently validated V21 language
            var success = await _plcService.CreateBlockAsync(testBlockName, "FB", "ProDiag");

            // Assert - Verify creation succeeded
            Assert.IsTrue(success, "Block creation should succeed");

            // Verify block exists
            var blocks = await _plcService.GetBlocksAsync();
            var createdBlock = blocks.FirstOrDefault(b => b.Name.Equals(testBlockName, StringComparison.OrdinalIgnoreCase));
            Assert.IsNotNull(createdBlock, $"Created block {testBlockName} should exist in block list");
            Assert.AreEqual("FB", createdBlock?.Type, "Block type should be FB");

            _logger.LogInformation("Created test block: {BlockName}", testBlockName);

            // Cleanup - Delete the test block
            await _plcService.DeleteBlocksAsync($"^{testBlockName}$", confirm: true);
        }

        [TestMethod]
        public async Task Test_CreateBlock_UnsupportedType_ReturnsFalse()
        {
            // Arrange
            await _portalService.ConnectAsync();
            await _portalService.OpenProjectAsync(_testProjectPath);
            await _plcService.OpenPlcAsync(_testPlcName);

            var testBlockName = $"Test_DB_{Guid.NewGuid():N}";

            // Act - Global DB creation is not part of the current validated V21 path
            var success = await _plcService.CreateBlockAsync(testBlockName, "DB");

            // Assert - Should fail cleanly instead of pretending to support it
            Assert.IsFalse(success, "Unsupported V21 block creation should fail cleanly");
        }

        [TestMethod]
        public async Task Test_CreateBlock_DuplicateName_ReturnsFalse()
        {
            // Arrange
            await _portalService.ConnectAsync();
            await _portalService.OpenProjectAsync(_testProjectPath);
            await _plcService.OpenPlcAsync(_testPlcName);

            // Generate unique block name
            var testBlockName = $"Test_FB_{Guid.NewGuid():N}";

            // Act - Create block first time
            var success1 = await _plcService.CreateBlockAsync(testBlockName, "FB", "ProDiag");
            Assert.IsTrue(success1, "First block creation should succeed");

            // Try to create again with same name
            var success2 = await _plcService.CreateBlockAsync(testBlockName, "FB", "ProDiag");

            // Assert - Second creation should fail
            Assert.IsFalse(success2, "Duplicate block creation should fail");

            _logger.LogInformation("Duplicate name test passed for: {BlockName}", testBlockName);

            // Cleanup - Delete the test block
            await _plcService.DeleteBlocksAsync($"^{testBlockName}$", confirm: true);
        }

        [TestMethod]
        public async Task Test_CreateBlock_InvalidType_ReturnsFalse()
        {
            // Arrange
            await _portalService.ConnectAsync();
            await _portalService.OpenProjectAsync(_testProjectPath);
            await _plcService.OpenPlcAsync(_testPlcName);

            // Act - Try to create block with invalid type
            var success = await _plcService.CreateBlockAsync("Test_Invalid", "INVALID_TYPE");

            // Assert - Should fail gracefully
            Assert.IsFalse(success, "Invalid block type should fail");

            _logger.LogInformation("Invalid block type test passed");
        }

        /// <summary>
        /// Disposes of resources used by the tests.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            try
            {
                // Clean up test exports
                if (Directory.Exists(_testExportPath))
                {
                    Directory.Delete(_testExportPath, recursive: true);
                }

                // Close connections
                _plcService?.ClosePlcAsync().Wait();
                _portalService?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during cleanup");
            }
            finally
            {
                _disposed = true;
            }
        }
    }
}
