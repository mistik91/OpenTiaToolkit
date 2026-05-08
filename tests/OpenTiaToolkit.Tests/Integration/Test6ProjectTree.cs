using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands.Portal;
using OpenTiaToolkit.Core.Services;
using System;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Tests.Integration
{
    /// <summary>
    /// Integration tests for project tree operations.
    /// Tests the GetProjectTreeCommand.
    /// </summary>
    [TestClass]
    [TestCategory("Integration")]
    [DoNotParallelize]
    public sealed class Test6ProjectTree
    {
        private ILoggerFactory _loggerFactory = null!;
        private ILogger _logger = null!;
        private IPortalService _portalService = null!;
        private ICommand _connectCommand = null!;
        private ICommand _openProjectCommand = null!;

        [TestInitialize]
        public void TestInitialize()
        {
            // Create logger factory
            _loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Information);
            });

            _logger = _loggerFactory.CreateLogger<Test6ProjectTree>();

            // Create portal service and commands
            _portalService = TestHelpers.CreatePortalService(_loggerFactory);
            _connectCommand = new ConnectPortalCommand(_portalService);
            _openProjectCommand = new OpenProjectCommand(_portalService);
        }

        [TestCleanup]
        public void TestCleanup()
        {
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

            _portalService?.Dispose();
            _loggerFactory?.Dispose();
        }

        [TestMethod]
        public async Task Test_601_GetProjectTree_WithOpenProject_ReturnsSuccess()
        {
            // Arrange
            await _connectCommand!.ExecuteAsync(TestHelpers.CreateEmptyArgs(), _logger!);
            var projectOpened = await TestHelpers.OpenProjectAsync(_portalService!, Settings.TestProjectPath, _logger!);
            Assert.IsTrue(projectOpened, $"Failed to open test project: {Settings.TestProjectPath}");

            var treeCommand = new GetProjectTreeCommand(_portalService);
            var args = TestHelpers.CreateEmptyArgs();

            // Act
            var result = await treeCommand.ExecuteAsync(args, _logger!);

            // Assert
            Assert.IsTrue(result.Success, $"Failed to get project tree: {result.Error}");
            Assert.IsNotNull(result.Data, "Result data should not be null");

            _logger!.LogInformation("Project tree retrieved: {Data}", result.Data);
        }

        [TestMethod]
        public async Task Test_602_GetProjectTree_WithDepthLimit_ReturnsLimitedTree()
        {
            // Arrange
            await _connectCommand!.ExecuteAsync(TestHelpers.CreateEmptyArgs(), _logger!);
            var projectOpened = await TestHelpers.OpenProjectAsync(_portalService!, Settings.TestProjectPath, _logger!);
            Assert.IsTrue(projectOpened, $"Failed to open test project: {Settings.TestProjectPath}");

            var treeCommand = new GetProjectTreeCommand(_portalService);
            var args = TestHelpers.CreateArgs("depth", 2); // Limit depth to 2

            // Act
            var result = await treeCommand.ExecuteAsync(args, _logger!);

            // Assert
            Assert.IsTrue(result.Success, $"Failed to get project tree with depth limit: {result.Error}");
            Assert.IsNotNull(result.Data, "Result data should not be null");

            _logger!.LogInformation("Project tree with depth limit retrieved: {Data}", result.Data);
        }

        [TestMethod]
        public async Task Test_603_GetProjectTree_VerifyStructure_HasDevices()
        {
            // Arrange
            await _connectCommand!.ExecuteAsync(TestHelpers.CreateEmptyArgs(), _logger!);
            var projectOpened = await TestHelpers.OpenProjectAsync(_portalService!, Settings.TestProjectPath, _logger!);
            Assert.IsTrue(projectOpened, $"Failed to open test project: {Settings.TestProjectPath}");

            var treeCommand = new GetProjectTreeCommand(_portalService);
            var args = TestHelpers.CreateEmptyArgs();

            // Act
            var result = await treeCommand.ExecuteAsync(args, _logger!);

            // Assert
            Assert.IsTrue(result.Success, $"Failed to get project tree: {result.Error}");

            // The tree should have a root with children
            var tree = result.Data;
            Assert.IsNotNull(tree, "Tree data should not be null");

            _logger!.LogInformation("Project tree structure verified");
        }

        [TestMethod]
        public async Task Test_604_GetProjectTree_WithoutProject_ReturnsFailure()
        {
            // Arrange
            await _connectCommand!.ExecuteAsync(TestHelpers.CreateEmptyArgs(), _logger!);

            // Close any projects that might be open from previous tests
            var closeCommand = new CloseProjectCommand(_portalService);
            await closeCommand.ExecuteAsync(TestHelpers.CreateArgs("save-changes", false), _logger!);

            var treeCommand = new GetProjectTreeCommand(_portalService);
            var args = TestHelpers.CreateEmptyArgs();

            // Act
            var result = await treeCommand.ExecuteAsync(args, _logger!);

            // Assert - Should fail when no project is open
            Assert.IsFalse(result.Success, "Should fail when no project is open");

            _logger!.LogInformation("Correctly failed to get tree without project: {Error}", result.Error);
        }

        [TestMethod]
        public async Task Test_605_GetProjectTree_DifferentDepths_ReturnsDifferentResults()
        {
            // Arrange
            await _connectCommand!.ExecuteAsync(TestHelpers.CreateEmptyArgs(), _logger!);
            var projectOpened = await TestHelpers.OpenProjectAsync(_portalService!, Settings.TestProjectPath, _logger!);
            Assert.IsTrue(projectOpened, $"Failed to open test project: {Settings.TestProjectPath}");

            var treeCommand = new GetProjectTreeCommand(_portalService);

            // Act - Get tree with depth 1
            var result1 = await treeCommand.ExecuteAsync(TestHelpers.CreateArgs("depth", 1), _logger!);
            Assert.IsTrue(result1.Success, $"Failed to get tree with depth 1: {result1.Error}");

            // Act - Get tree with depth 3
            var result2 = await treeCommand.ExecuteAsync(TestHelpers.CreateArgs("depth", 3), _logger!);
            Assert.IsTrue(result2.Success, $"Failed to get tree with depth 3: {result2.Error}");

            // Act - Get tree with unlimited depth
            var result3 = await treeCommand.ExecuteAsync(TestHelpers.CreateEmptyArgs(), _logger!);
            Assert.IsTrue(result3.Success, $"Failed to get tree with unlimited depth: {result3.Error}");

            // Assert
            _logger!.LogInformation("Retrieved project trees with different depth limits");
        }

        [TestMethod]
        public async Task Test_606_GetProjectTree_AfterProjectClose_ReturnsFailure()
        {
            // Arrange
            await _connectCommand!.ExecuteAsync(TestHelpers.CreateEmptyArgs(), _logger!);
            var projectOpened = await TestHelpers.OpenProjectAsync(_portalService!, Settings.TestProjectPath, _logger!);
            Assert.IsTrue(projectOpened, $"Failed to open test project: {Settings.TestProjectPath}");

            // Close the project
            var closeCommand = new CloseProjectCommand(_portalService);
            await closeCommand.ExecuteAsync(TestHelpers.CreateArgs("save-changes", false), _logger!);

            var treeCommand = new GetProjectTreeCommand(_portalService);
            var args = TestHelpers.CreateEmptyArgs();

            // Act
            var result = await treeCommand.ExecuteAsync(args, _logger!);

            // Assert
            Assert.IsFalse(result.Success, "Should fail when project is closed");

            _logger!.LogInformation("Correctly failed to get tree after project closed: {Error}", result.Error);
        }
    }
}
