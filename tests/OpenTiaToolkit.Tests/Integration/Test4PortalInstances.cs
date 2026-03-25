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
    /// Integration tests for TIA Portal instance management operations.
    /// Tests the OpenTiaPortalCommand, CloseTiaPortalCommand, ListInstancesCommand, and ConnectInstanceCommand.
    /// </summary>
    [TestClass]
    [DoNotParallelize]
    public sealed class Test4PortalInstances
    {
        private ILoggerFactory _loggerFactory = null!;
        private ILogger _logger = null!;
        private IPortalService _portalService = null!;

        [TestInitialize]
        public void TestInitialize()
        {
            // Create logger factory
            _loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Information);
            });

            _logger = _loggerFactory.CreateLogger<Test4PortalInstances>();

            // Create portal service
            _portalService = TestHelpers.CreatePortalService(_loggerFactory);
        }

        [TestCleanup]
        public void TestCleanup()
        {
            // Ensure we disconnect and close after each test
            if (_portalService?.IsConnected == true)
            {
                try
                {
                    _portalService.CloseTiaPortalAsync().Wait();
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
        public async Task Test_401_OpenTiaPortal_WithoutUI_ReturnsSuccess()
        {
            // Arrange
            var command = new OpenTiaPortalCommand(_portalService!);
            var args = TestHelpers.CreateArgs("with-ui", false);

            // Act
            var result = await command.ExecuteAsync(args, _logger!);

            // Assert
            Assert.IsTrue(result.Success, $"Failed to open TIA Portal: {result.Error}");
            Assert.IsNotNull(result.Data, "Result data should not be null");
            Assert.IsTrue(_portalService!.IsConnected, "Should be connected after opening");

            _logger!.LogInformation("Opened TIA Portal without UI: {Data}", result.Data);
        }

        [TestMethod]
        public async Task Test_402_ListInstances_ReturnsRunningInstances()
        {
            // Arrange
            // First open a portal instance
            var openCommand = new OpenTiaPortalCommand(_portalService!);
            await openCommand.ExecuteAsync(TestHelpers.CreateArgs("with-ui", false), _logger!);

            var listCommand = new ListInstancesCommand(_portalService);
            var args = TestHelpers.CreateEmptyArgs();

            // Act
            var result = await listCommand.ExecuteAsync(args, _logger!);

            // Assert
            Assert.IsTrue(result.Success, $"Failed to list instances: {result.Error}");
            Assert.IsNotNull(result.Data, "Result data should not be null");

            _logger!.LogInformation("Found instances: {Data}", result.Data);
        }

        [TestMethod]
        public async Task Test_403_ConnectToInstance_ById_ReturnsSuccess()
        {
            // Arrange
            // First open a portal instance
            var openCommand = new OpenTiaPortalCommand(_portalService!);
            await openCommand.ExecuteAsync(TestHelpers.CreateArgs("with-ui", false), _logger!);

            // Get the process ID
            var instances = await _portalService!.GetInstancesAsync();
            Assert.IsTrue(instances.Count > 0, "Should have at least one instance");

            // Close the current connection
            await _portalService.CloseTiaPortalAsync();

            // Now connect to the specific instance
            var connectCommand = new ConnectInstanceCommand(_portalService);
            var args = TestHelpers.CreateArgs("instance-id", instances[0].ProcessId);

            // Act
            var result = await connectCommand.ExecuteAsync(args, _logger!);

            // Assert
            Assert.IsTrue(result.Success, $"Failed to connect to instance: {result.Error}");
            Assert.IsTrue(_portalService.IsConnected, "Should be connected after attaching");

            _logger!.LogInformation("Connected to instance: {Data}", result.Data);
        }

        [TestMethod]
        public async Task Test_404_CloseTiaPortal_ClosesInstance_ReturnsSuccess()
        {
            // Arrange
            var openCommand = new OpenTiaPortalCommand(_portalService!);
            await openCommand.ExecuteAsync(TestHelpers.CreateArgs("with-ui", false), _logger!);

            Assert.IsTrue(_portalService!.IsConnected, "Should be connected before closing");

            var closeCommand = new CloseTiaPortalCommand(_portalService);
            var args = TestHelpers.CreateEmptyArgs();

            // Act
            var result = await closeCommand.ExecuteAsync(args, _logger!);

            // Assert
            Assert.IsTrue(result.Success, $"Failed to close TIA Portal: {result.Error}");
            Assert.IsFalse(_portalService.IsConnected, "Should not be connected after closing");

            _logger!.LogInformation("Closed TIA Portal: {Data}", result.Data);
        }

        [TestMethod]
        public async Task Test_405_OpenClose_MultipleSequence_WorksCorrectly()
        {
            // Arrange
            var openCommand = new OpenTiaPortalCommand(_portalService!);
            var closeCommand = new CloseTiaPortalCommand(_portalService);
            var argsOpen = TestHelpers.CreateArgs("with-ui", false);
            var argsEmpty = TestHelpers.CreateEmptyArgs();

            // Act - First open/close cycle
            var result1 = await openCommand.ExecuteAsync(argsOpen, _logger!);
            Assert.IsTrue(result1.Success, $"First open failed: {result1.Error}");

            var result2 = await closeCommand.ExecuteAsync(argsEmpty, _logger);
            Assert.IsTrue(result2.Success, $"First close failed: {result2.Error}");

            // Act - Second open/close cycle
            var result3 = await openCommand.ExecuteAsync(argsOpen, _logger);
            Assert.IsTrue(result3.Success, $"Second open failed: {result3.Error}");

            var result4 = await closeCommand.ExecuteAsync(argsEmpty, _logger);
            Assert.IsTrue(result4.Success, $"Second close failed: {result4.Error}");

            // Assert
            Assert.IsFalse(_portalService!.IsConnected, "Should not be connected after test");

            _logger!.LogInformation("Multiple open/close cycles completed successfully");
        }

        [TestMethod]
        public async Task Test_406_ConnectToInstance_InvalidId_ReturnsFailure()
        {
            // Arrange
            var connectCommand = new ConnectInstanceCommand(_portalService!);
            var args = TestHelpers.CreateArgs("instance-id", 99999); // Invalid process ID

            // Act
            var result = await connectCommand.ExecuteAsync(args, _logger!);

            // Assert
            Assert.IsFalse(result.Success, "Should fail when connecting to invalid instance ID");
            Assert.IsNotNull(result.Error, "Error message should not be null");

            _logger!.LogInformation("Correctly failed to connect to invalid instance: {Error}", result.Error);
        }
    }
}
