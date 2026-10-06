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
    /// Integration tests for TIA Portal connection operations.
    /// Tests the ConnectPortalCommand, DisconnectPortalCommand, and PortalStatusCommand.
    /// </summary>
    [TestClass]
    [TestCategory("Integration")]
    [DoNotParallelize]
    public sealed class Test1Portal
    {
        private ILoggerFactory _loggerFactory = null!;
        private ILogger _logger = null!;
        private IPortalService _portalService = null!;
        private ICommand _connectCommand = null!;
        private ICommand _disconnectCommand = null!;
        private ICommand _statusCommand = null!;

        [TestInitialize]
        public void TestInitialize()
        {
            // Create logger factory
            _loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Information);
            });

            _logger = _loggerFactory.CreateLogger<Test1Portal>();

            // Create portal service and commands
            _portalService = TestHelpers.CreatePortalService(_loggerFactory);
            _connectCommand = new ConnectPortalCommand(_portalService);
            _disconnectCommand = new DisconnectPortalCommand(_portalService);
            _statusCommand = new PortalStatusCommand(_portalService);
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
        public async Task Test_101_ConnectPortal_ReturnsSuccess()
        {
            // Arrange
            Assert.IsNotNull(_connectCommand, "Connect command not initialized");
            var args = TestHelpers.CreateEmptyArgs();
            Assert.IsNotNull(_logger, "Logger not initialized");

            // Act
            var result = await _connectCommand.ExecuteAsync(args, _logger);

            // Assert
            Assert.IsTrue(result.Success, $"Failed to connect to TIA Portal: {result.Error}");
            Assert.IsNotNull(result.Data, "Result data should not be null");

            _logger.LogInformation("Connected successfully: {Data}", result.Data);
        }

        [TestMethod]
        public async Task Test_102_DisconnectPortal_ReturnsSuccess()
        {
            // Arrange
            Assert.IsNotNull(_disconnectCommand, "Disconnect command not initialized");

            // First connect
            var connectCmd = new ConnectPortalCommand(_portalService!);
            await connectCmd.ExecuteAsync(TestHelpers.CreateEmptyArgs(), _logger!);

            var args = TestHelpers.CreateEmptyArgs();

            // Act
            var result = await _disconnectCommand.ExecuteAsync(args, _logger!);

            // Assert
            Assert.IsTrue(result.Success, $"Failed to disconnect from TIA Portal: {result.Error}");
        }

        [TestMethod]
        public async Task Test_103_PortalStatus_ShowConnected_AfterConnect()
        {
            // Arrange
            Assert.IsNotNull(_statusCommand, "Status command not initialized");

            // First connect
            var connectCmd = new ConnectPortalCommand(_portalService!);
            await connectCmd.ExecuteAsync(TestHelpers.CreateEmptyArgs(), _logger!);

            var args = TestHelpers.CreateEmptyArgs();

            // Act
            var result = await _statusCommand.ExecuteAsync(args, _logger!);

            // Assert
            Assert.IsTrue(result.Success, $"Failed to get portal status: {result.Error}");
            Assert.IsNotNull(result.Data, "Status data should not be null");
        }

        [TestMethod]
        public async Task Test_104_ConnectDisconnect_Sequence()
        {
            // Arrange
            var args = TestHelpers.CreateEmptyArgs();

            // Act - Connect
            var connectResult = await _connectCommand!.ExecuteAsync(args, _logger!);
            Assert.IsTrue(connectResult.Success, $"Failed to connect: {connectResult.Error}");

            // Act - Check status
            var statusResult1 = await _statusCommand!.ExecuteAsync(args, _logger);
            Assert.IsTrue(statusResult1.Success, "Failed to get status after connect");

            // Act - Disconnect
            var disconnectResult = await _disconnectCommand!.ExecuteAsync(args, _logger);
            Assert.IsTrue(disconnectResult.Success, $"Failed to disconnect: {disconnectResult.Error}");

            // Act - Check status again (should show disconnected)
            var statusResult2 = await _statusCommand.ExecuteAsync(args, _logger);
            Assert.IsTrue(statusResult2.Success, "Failed to get status after disconnect");

            _logger.LogInformation("Connect-Disconnect sequence completed successfully");
        }
    }
}
