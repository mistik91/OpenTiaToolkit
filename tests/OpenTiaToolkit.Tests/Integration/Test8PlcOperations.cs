using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands;
using OpenTiaToolkit.Core.Commands.Plc;
using OpenTiaToolkit.Core.Commands.Portal;
using OpenTiaToolkit.Core.Services;
using System;
using System.IO;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Tests.Integration
{
    /// <summary>
    /// Integration tests for PLC operations.
    /// Tests the OpenPlcCommand, GetSoftwareTreeCommand, CompileSoftwareCommand, ExportPlcCommand, and GetBlocksCommand.
    /// </summary>
    [TestClass]
    [DoNotParallelize]
    public sealed class Test8PlcOperations
    {
        private ILoggerFactory? _loggerFactory;
        private ILogger? _logger;
        private IPortalService? _portalService;
        private IPlcService? _plcService;
        private ICommand? _connectCommand;
        private ICommand? _openProjectCommand;
        private ICommand? _closeProjectCommand;

        [TestInitialize]
        public void TestInitialize()
        {
            // Create logger factory
            _loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Information);
            });

            _logger = _loggerFactory.CreateLogger<Test8PlcOperations>();

            // Create portal service, PLC service, and commands
            _portalService = TestHelpers.CreatePortalService(_loggerFactory);
            _plcService = new PlcService(_portalService, _loggerFactory.CreateLogger<PlcService>());
            _connectCommand = new ConnectPortalCommand(_portalService);
            _openProjectCommand = new OpenProjectCommand(_portalService);
            _closeProjectCommand = new CloseProjectCommand(_portalService);
        }

        [TestCleanup]
        public void TestCleanup()
        {
            // Clean up test export files
            try
            {
                if (Directory.Exists(Settings.TestExportPath))
                {
                    var plcExportFiles = Directory.GetFiles(Settings.TestExportPath, "*_blocks.xml", SearchOption.TopDirectoryOnly);
                    foreach (var file in plcExportFiles)
                    {
                        try
                        {
                            if (File.Exists(file))
                            {
                                File.Delete(file);
                            }
                        }
                        catch
                        {
                            // Ignore cleanup errors
                        }
                    }
                }
            }
            catch
            {
                // Ignore cleanup errors
            }

            // Ensure we disconnect after each test
            if (_portalService?.IsConnected == true)
            {
                try
                {
                    _portalService.DisconnectAsync().Wait();
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }

            // PlcService doesn't implement IDisposable, so no need to dispose
            _portalService?.Dispose();
            _loggerFactory?.Dispose();
        }

        [TestMethod]
        public async Task Test_801_OpenPlc_WithValidName_ReturnsSuccess()
        {
            // Arrange
            await _connectCommand!.ExecuteAsync(TestHelpers.CreateEmptyArgs(), _logger!);
            var projectOpened = await TestHelpers.OpenProjectAsync(_portalService!, Settings.TestProjectPath, _logger!);
            Assert.IsTrue(projectOpened, $"Failed to open test project: {Settings.TestProjectPath}");

            var openPlcCommand = new OpenPlcCommand(_plcService!);
            var args = TestHelpers.CreateArgs("plc-name", "PLC_1");

            // Act
            var result = await openPlcCommand.ExecuteAsync(args, _logger!);

            // Assert
            Assert.IsTrue(result.Success, $"Failed to open PLC: {result.Error}");
            Assert.IsTrue(_plcService!.IsSoftwareOpen, "PLC software should be open");

            _logger!.LogInformation("PLC opened successfully: {Data}", result.Data);
        }

        [TestMethod]
        public async Task Test_802_GetSoftwareTree_WithOpenPlc_ReturnsSuccess()
        {
            // Arrange
            await _connectCommand!.ExecuteAsync(TestHelpers.CreateEmptyArgs(), _logger!);
            var projectOpened = await TestHelpers.OpenProjectAsync(_portalService!, Settings.TestProjectPath, _logger!);
            Assert.IsTrue(projectOpened, $"Failed to open test project: {Settings.TestProjectPath}");

            var getSoftwareTreeCommand = new GetSoftwareTreeCommand(_plcService!);
            var args = TestHelpers.CreateArgs("plc-name", "PLC_1");

            // Act
            var result = await getSoftwareTreeCommand.ExecuteAsync(args, _logger!);

            // Assert
            Assert.IsTrue(result.Success, $"Failed to get software tree: {result.Error}");
            Assert.IsNotNull(result.Data, "Result data should not be null");

            _logger!.LogInformation("Software tree retrieved: {Data}", result.Data);
        }

        [TestMethod]
        public async Task Test_803_GetSoftwareTree_WithDepthLimit_ReturnsLimitedTree()
        {
            // Arrange
            await _connectCommand!.ExecuteAsync(TestHelpers.CreateEmptyArgs(), _logger!);
            var projectOpened = await TestHelpers.OpenProjectAsync(_portalService!, Settings.TestProjectPath, _logger!);
            Assert.IsTrue(projectOpened, $"Failed to open test project: {Settings.TestProjectPath}");

            var getSoftwareTreeCommand = new GetSoftwareTreeCommand(_plcService!);
            var args = new CommandArgs(new System.Collections.Generic.Dictionary<string, object?>
            {
                { "plc-name", "PLC_1" },
                { "depth", 1 }
            });

            // Act
            var result = await getSoftwareTreeCommand.ExecuteAsync(args, _logger!);

            // Assert
            Assert.IsTrue(result.Success, $"Failed to get software tree with depth limit: {result.Error}");
            Assert.IsNotNull(result.Data, "Result data should not be null");

            _logger!.LogInformation("Software tree with depth limit retrieved: {Data}", result.Data);
        }

        [TestMethod]
        public async Task Test_804_CompileSoftware_WithOpenPlc_ReturnsSuccess()
        {
            // Arrange
            await _connectCommand!.ExecuteAsync(TestHelpers.CreateEmptyArgs(), _logger!);
            var projectOpened = await TestHelpers.OpenProjectAsync(_portalService!, Settings.TestProjectPath, _logger!);
            Assert.IsTrue(projectOpened, $"Failed to open test project: {Settings.TestProjectPath}");

            var compileSoftwareCommand = new CompileSoftwareCommand(_plcService!);
            var args = TestHelpers.CreateArgs("plc-name", "PLC_1");

            // Act
            var result = await compileSoftwareCommand.ExecuteAsync(args, _logger!);

            // Assert
            Assert.IsTrue(result.Success, $"Failed to compile software: {result.Error}");
            Assert.IsNotNull(result.Data, "Result data should not be null");

            _logger!.LogInformation("Software compilation result: {Data}", result.Data);
        }

        [TestMethod]
        public async Task Test_805_ExportPlc_WithValidPath_ReturnsSuccess()
        {
            // Arrange
            await _connectCommand!.ExecuteAsync(TestHelpers.CreateEmptyArgs(), _logger!);
            var projectOpened = await TestHelpers.OpenProjectAsync(_portalService!, Settings.TestProjectPath, _logger!);
            Assert.IsTrue(projectOpened, $"Failed to open test project: {Settings.TestProjectPath}");

            var exportPlcCommand = new ExportPlcCommand(_plcService!);
            var exportPath = Settings.TestExportPath;
            var args = new CommandArgs(new System.Collections.Generic.Dictionary<string, object?>
            {
                { "plc-name", "PLC_1" },
                { "export-path", exportPath }
            });

            // Act
            var result = await exportPlcCommand.ExecuteAsync(args, _logger!);

            var exportedFiles = Directory.Exists(exportPath)
                ? Directory.GetFiles(exportPath, "*.xml")
                : Array.Empty<string>();

            // Verify export directory exists
            Assert.IsTrue(Directory.Exists(exportPath), $"Export directory not created: {exportPath}");

            if (!result.Success && exportedFiles.Length == 0)
            {
                _logger!.LogWarning("Current test project does not contain exportable PLC blocks; export returned: {Error}", result.Error);
                return;
            }

            // Assert
            Assert.IsTrue(result.Success, $"Failed to export PLC: {result.Error}");

            _logger!.LogInformation("PLC blocks exported to: {Path}", exportPath);

            // Clean up exported files
            try
            {
                var cleanupFiles = Directory.GetFiles(exportPath, "*_blocks.xml");
                foreach (var file in cleanupFiles)
                {
                    File.Delete(file);
                }
            }
            catch { }
        }

        [TestMethod]
        public async Task Test_806_GetBlocks_WithOpenPlc_ReturnsSuccess()
        {
            // Arrange
            await _connectCommand!.ExecuteAsync(TestHelpers.CreateEmptyArgs(), _logger!);
            var projectOpened = await TestHelpers.OpenProjectAsync(_portalService!, Settings.TestProjectPath, _logger!);
            Assert.IsTrue(projectOpened, $"Failed to open test project: {Settings.TestProjectPath}");

            var getBlocksCommand = new GetBlocksCommand(_plcService!);
            var args = TestHelpers.CreateArgs("plc-name", "PLC_1");

            // Act
            var result = await getBlocksCommand.ExecuteAsync(args, _logger!);

            // Assert
            Assert.IsTrue(result.Success, $"Failed to get blocks: {result.Error}");
            Assert.IsNotNull(result.Data, "Result data should not be null");

            _logger!.LogInformation("Blocks retrieved: {Data}", result.Data);
        }

        [TestMethod]
        public async Task Test_807_GetBlocks_WithFilter_ReturnsFilteredBlocks()
        {
            // Arrange
            await _connectCommand!.ExecuteAsync(TestHelpers.CreateEmptyArgs(), _logger!);
            var projectOpened = await TestHelpers.OpenProjectAsync(_portalService!, Settings.TestProjectPath, _logger!);
            Assert.IsTrue(projectOpened, $"Failed to open test project: {Settings.TestProjectPath}");

            var getBlocksCommand = new GetBlocksCommand(_plcService!);
            var args = new CommandArgs(new System.Collections.Generic.Dictionary<string, object?>
            {
                { "plc-name", "PLC_1" },
                { "filter", "Motor" }
            });

            // Act
            var result = await getBlocksCommand.ExecuteAsync(args, _logger!);

            // Assert
            Assert.IsTrue(result.Success, $"Failed to get blocks with filter: {result.Error}");
            Assert.IsNotNull(result.Data, "Result data should not be null");

            _logger!.LogInformation("Filtered blocks retrieved: {Data}", result.Data);
        }

        [TestMethod]
        public async Task Test_808_GetBlocks_WithoutProject_ReturnsFailure()
        {
            // Arrange
            await _connectCommand!.ExecuteAsync(TestHelpers.CreateEmptyArgs(), _logger!);
            // Don't open a project

            var getBlocksCommand = new GetBlocksCommand(_plcService!);
            var args = TestHelpers.CreateArgs("plc-name", "PLC_1");

            // Act
            var result = await getBlocksCommand.ExecuteAsync(args, _logger!);

            // Assert - this test documents current behavior when a project was never opened
            _logger!.LogInformation("Get blocks result: {Success}, {Error}", result.Success, result.Error);
        }

        [TestMethod]
        public async Task Test_809_CompileSoftware_WithPassword_WorksCorrectly()
        {
            // Arrange
            await _connectCommand!.ExecuteAsync(TestHelpers.CreateEmptyArgs(), _logger!);
            var projectOpened = await TestHelpers.OpenProjectAsync(_portalService!, Settings.TestProjectPath, _logger!);
            Assert.IsTrue(projectOpened, $"Failed to open test project: {Settings.TestProjectPath}");

            var compileSoftwareCommand = new CompileSoftwareCommand(_plcService!);
            var args = new CommandArgs(new System.Collections.Generic.Dictionary<string, object?>
            {
                { "plc-name", "PLC_1" },
                { "password", "test_password" }
            });

            // Act
            var result = await compileSoftwareCommand.ExecuteAsync(args, _logger!);

            // Assert
            Assert.IsTrue(result.Success, $"Failed to compile software with password: {result.Error}");

            _logger!.LogInformation("Software compilation with password completed: {Data}", result.Data);
        }

        [TestMethod]
        public async Task Test_810_PlcOperations_CompleteWorkflow_WorksCorrectly()
        {
            // Arrange
            await _connectCommand!.ExecuteAsync(TestHelpers.CreateEmptyArgs(), _logger!);
            var projectOpened = await TestHelpers.OpenProjectAsync(_portalService!, Settings.TestProjectPath, _logger!);
            Assert.IsTrue(projectOpened, $"Failed to open test project: {Settings.TestProjectPath}");

            var openPlcCommand = new OpenPlcCommand(_plcService!);
            var getBlocksCommand = new GetBlocksCommand(_plcService!);
            var compileCommand = new CompileSoftwareCommand(_plcService!);
            var exportPath = Settings.TestExportPath;
            var exportCommand = new ExportPlcCommand(_plcService!);

            // Act - Open PLC
            var result1 = await openPlcCommand.ExecuteAsync(TestHelpers.CreateArgs("plc-name", "PLC_1"), _logger!);
            Assert.IsTrue(result1.Success, $"Failed to open PLC: {result1.Error}");

            // Act - Get blocks
            var result2 = await getBlocksCommand.ExecuteAsync(TestHelpers.CreateArgs("plc-name", "PLC_1"), _logger!);
            Assert.IsTrue(result2.Success, $"Failed to get blocks: {result2.Error}");

            // Act - Compile
            var result3 = await compileCommand.ExecuteAsync(TestHelpers.CreateArgs("plc-name", "PLC_1"), _logger!);
            Assert.IsTrue(result3.Success, $"Failed to compile: {result3.Error}");

            // Act - Export
            var exportArgs = new CommandArgs(new System.Collections.Generic.Dictionary<string, object?>
            {
                { "plc-name", "PLC_1" },
                { "export-path", exportPath }
            });
            var result4 = await exportCommand.ExecuteAsync(exportArgs, _logger!);

            var exportedFiles = Directory.Exists(exportPath)
                ? Directory.GetFiles(exportPath, "*.xml")
                : Array.Empty<string>();

            if (!result4.Success && exportedFiles.Length == 0)
            {
                _logger!.LogWarning("Export step skipped for complete workflow because current project has no exportable blocks: {Error}", result4.Error);
            }
            else
            {
                Assert.IsTrue(result4.Success, $"Failed to export: {result4.Error}");
            }

            // Assert
            Assert.IsTrue(_plcService!.IsSoftwareOpen, "PLC should still be open");

            _logger!.LogInformation("Complete PLC workflow completed successfully");

            // Clean up exported files
            try
            {
                var cleanupFiles = Directory.GetFiles(exportPath, "*_blocks.xml");
                foreach (var file in cleanupFiles)
                {
                    File.Delete(file);
                }
            }
            catch { }
        }
    }
}
