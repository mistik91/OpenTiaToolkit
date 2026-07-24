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
    /// Integration tests for TIA Portal device operations.
    /// Tests the ListDevicesCommand.
    /// </summary>
    [TestClass]
    [TestCategory("Integration")]
    [DoNotParallelize]
    public sealed class Test3Devices
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

            _logger = _loggerFactory.CreateLogger<Test3Devices>();

            // Create portal service, connect, and open project
            _portalService = TestHelpers.CreatePortalService(_loggerFactory);
            await _portalService.ConnectAsync();

            var openCmd = new OpenProjectCommand(_portalService);
            await openCmd.ExecuteAsync(
                TestHelpers.CreateArgs("project-path", Settings.TestProjectPath),
                _logger);
        }

        [TestCleanup]
        public async Task TestCleanup()
        {
            // Disconnect without closing a project owned by an already-running TIA instance.
            if (_portalService?.IsConnected == true)
            {
                try
                {
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
        public async Task Test_301_ListDevices_ReturnsDeviceList()
        {
            // Arrange
            var command = new ListDevicesCommand(_portalService!);
            var args = TestHelpers.CreateEmptyArgs();

            // Act
            var result = await command.ExecuteAsync(args, _logger!);

            // Assert
            Assert.IsTrue(result.Success, $"Failed to list devices: {result.Error}");
            Assert.IsNotNull(result.Data, "Devices list should not be null");

            _logger.LogInformation("Found devices: {Data}", result.Data);
        }

        [TestMethod]
        public async Task Test_302_ListDevices_VerifyStructure()
        {
            // Arrange
            var command = new ListDevicesCommand(_portalService!);
            var args = TestHelpers.CreateEmptyArgs();

            // Act
            var result = await command.ExecuteAsync(args, _logger!);

            // Assert
            Assert.IsTrue(result.Success, $"Failed to list devices: {result.Error}");
            Assert.IsNotNull(result.Data, "Devices list should not be null");

            // Verify the data structure
            var json = result.ToJson();
            Assert.IsFalse(string.IsNullOrEmpty(json), "JSON output should not be empty");

            _logger.LogInformation("Devices JSON: {Json}", json);
        }

        [TestMethod]
        public async Task Test_303_ListDevices_MultipleCalls_ReturnsConsistentData()
        {
            // Arrange
            var command = new ListDevicesCommand(_portalService!);
            var args = TestHelpers.CreateEmptyArgs();

            // Act - First call
            var result1 = await command.ExecuteAsync(args, _logger!);
            Assert.IsTrue(result1.Success, "First call failed");

            // Act - Second call
            var result2 = await command.ExecuteAsync(args, _logger);
            Assert.IsTrue(result2.Success, "Second call failed");

            // Assert - Both should succeed
            Assert.IsNotNull(result1.Data, "First result data should not be null");
            Assert.IsNotNull(result2.Data, "Second result data should not be null");

            _logger.LogInformation("Multiple device list calls completed successfully");
        }
    }
}
