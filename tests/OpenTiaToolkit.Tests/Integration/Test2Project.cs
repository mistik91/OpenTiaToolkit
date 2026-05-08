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
    /// Integration tests for TIA Portal project operations.
    /// Tests the OpenProjectCommand, CloseProjectCommand, ListProjectsCommand, and GetProjectInfoCommand.
    /// </summary>
    [TestClass]
    [TestCategory("Integration")]
    [DoNotParallelize]
    public sealed class Test2Project
    {
        private ILoggerFactory _loggerFactory = null!;
        private ILogger _logger = null!;
        private IPortalService _portalService = null!;

        [TestInitialize]
        public async Task TestInitialize()
        {
            // Create logger factory
            _loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Information);
            });

            _logger = _loggerFactory.CreateLogger<Test2Project>();

            // Create portal service and connect
            _portalService = TestHelpers.CreatePortalService(_loggerFactory);
            await _portalService.ConnectAsync();
        }

        [TestCleanup]
        public async Task TestCleanup()
        {
            // Close any open project and disconnect
            if (_portalService?.IsConnected == true)
            {
                try
                {
                    await _portalService.CloseProjectAsync(false);
                    await _portalService.DisconnectAsync();
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
        public async Task Test_201_OpenProject_ReturnsSuccess()
        {
            // Arrange
            var command = new OpenProjectCommand(_portalService!);
            var args = TestHelpers.CreateArgs("project-path", Settings.TestProjectPath);

            // Act
            var result = await command.ExecuteAsync(args, _logger!);

            // Assert
            Assert.IsTrue(result.Success, $"Failed to open project: {result.Error}");
            Assert.IsNotNull(result.Data, "Result data should not be null");

            _logger.LogInformation("Project opened successfully: {Data}", result.Data);
        }

        [TestMethod]
        public async Task Test_202_CloseProject_WithoutSave_ReturnsSuccess()
        {
            // Arrange
            var openCmd = new OpenProjectCommand(_portalService!);
            await openCmd.ExecuteAsync(TestHelpers.CreateArgs("project-path", Settings.TestProjectPath), _logger!);

            var closeCmd = new CloseProjectCommand(_portalService);
            var args = TestHelpers.CreateArgs("save", false);

            // Act
            var result = await closeCmd.ExecuteAsync(args, _logger!);

            // Assert
            Assert.IsTrue(result.Success, $"Failed to close project: {result.Error}");

            _logger.LogInformation("Project closed successfully (not saved)");
        }

        [TestMethod]
        public async Task Test_203_GetProjectInfo_ReturnsValidInfo()
        {
            // Arrange
            var openCmd = new OpenProjectCommand(_portalService!);
            await openCmd.ExecuteAsync(TestHelpers.CreateArgs("project-path", Settings.TestProjectPath), _logger!);

            var command = new GetProjectInfoCommand(_portalService);
            var args = TestHelpers.CreateEmptyArgs();

            // Act
            var result = await command.ExecuteAsync(args, _logger!);

            // Assert
            Assert.IsTrue(result.Success, $"Failed to get project info: {result.Error}");
            Assert.IsNotNull(result.Data, "Project info data should not be null");

            _logger.LogInformation("Project info: {Data}", result.Data);
        }

        [TestMethod]
        public async Task Test_204_ListProjects_ReturnsProjectList()
        {
            // Arrange
            var openCmd = new OpenProjectCommand(_portalService!);
            await openCmd.ExecuteAsync(TestHelpers.CreateArgs("project-path", Settings.TestProjectPath), _logger!);

            var command = new ListProjectsCommand(_portalService);
            var args = TestHelpers.CreateEmptyArgs();

            // Act
            var result = await command.ExecuteAsync(args, _logger!);

            // Assert
            Assert.IsTrue(result.Success, $"Failed to list projects: {result.Error}");
            Assert.IsNotNull(result.Data, "Projects list should not be null");

            _logger.LogInformation("Found {Count} project(s)", result.Data);
        }

        [TestMethod]
        public async Task Test_205_OpenClose_Sequence()
        {
            // Arrange
            var openCmd = new OpenProjectCommand(_portalService!);
            var closeCmd = new CloseProjectCommand(_portalService);
            var args = TestHelpers.CreateArgs("project-path", Settings.TestProjectPath);

            // Act - Open
            var openResult = await openCmd.ExecuteAsync(args, _logger!);
            Assert.IsTrue(openResult.Success, $"Failed to open project: {openResult.Error}");

            // Act - Get info while open
            var infoCmd = new GetProjectInfoCommand(_portalService);
            var infoResult = await infoCmd.ExecuteAsync(TestHelpers.CreateEmptyArgs(), _logger);
            Assert.IsTrue(infoResult.Success, "Failed to get project info");

            // Act - Close
            var closeResult = await closeCmd.ExecuteAsync(TestHelpers.CreateArgs("save", false), _logger);
            Assert.IsTrue(closeResult.Success, $"Failed to close project: {closeResult.Error}");

            _logger.LogInformation("Open-Close sequence completed successfully");
        }

        [TestMethod]
        public async Task Test_206_OpenProject_InvalidPath_ReturnsFailure()
        {
            // Arrange
            var command = new OpenProjectCommand(_portalService!);
            var args = TestHelpers.CreateArgs("project-path", "C:\\Invalid\\Path\\Nonexistent.ap21");

            // Act
            var result = await command.ExecuteAsync(args, _logger!);

            // Assert
            Assert.IsFalse(result.Success, "Should fail to open non-existent project");
            Assert.IsNotNull(result.Error, "Error message should be provided");

            _logger.LogInformation("Correctly failed to open invalid project: {Error}", result.Error);
        }

        [TestMethod]
        public async Task Test_207_OpenProject_AlreadyOpen_ReturnsSuccess()
        {
            // Arrange
            var command = new OpenProjectCommand(_portalService!);
            var args = TestHelpers.CreateArgs("project-path", Settings.TestProjectPath);

            // Act
            var firstResult = await command.ExecuteAsync(args, _logger!);
            var secondResult = await command.ExecuteAsync(args, _logger!);

            // Assert
            Assert.IsTrue(firstResult.Success, $"Initial open should succeed: {firstResult.Error}");
            Assert.IsTrue(secondResult.Success, $"Opening an already open project should succeed: {secondResult.Error}");
        }
    }
}
