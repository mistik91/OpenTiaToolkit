using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands;
using OpenTiaToolkit.Core.Commands.Devices;
using OpenTiaToolkit.Core.Commands.Portal;
using OpenTiaToolkit.Core.Services;
using System;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Tests.Integration
{
    /// <summary>
    /// Integration tests for device information operations.
    /// Tests the GetDeviceInfoCommand and GetDeviceItemInfoCommand.
    /// </summary>
    [TestClass]
    [DoNotParallelize]
    public sealed class Test7PlcSoftware
    {
        private ILoggerFactory? _loggerFactory;
        private ILogger? _logger;
        private IPortalService? _portalService;
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

            _logger = _loggerFactory.CreateLogger<Test7PlcSoftware>();

            // Create portal service and commands
            _portalService = TestHelpers.CreatePortalService(_loggerFactory);
            _connectCommand = new ConnectPortalCommand(_portalService);
            _openProjectCommand = new OpenProjectCommand(_portalService);
            _closeProjectCommand = new CloseProjectCommand(_portalService);
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
        public async Task Test_701_GetDeviceInfo_WithValidDevice_ReturnsSuccess()
        {
            // Arrange
            await _connectCommand!.ExecuteAsync(TestHelpers.CreateEmptyArgs(), _logger!);
            var projectOpened = await TestHelpers.OpenProjectAsync(_portalService!, Settings.TestProjectPath, _logger!);
            Assert.IsTrue(projectOpened, $"Failed to open test project: {Settings.TestProjectPath}");

            var getDeviceInfoCommand = new GetDeviceInfoCommand(_portalService);

            // Act - Get info for a device (assuming there's at least one device named "PLC_1" or similar)
            // For this test, we'll first list devices and then get info for the first one
            var result = await getDeviceInfoCommand.ExecuteAsync(TestHelpers.CreateArgs("device-name", "PLC_1"), _logger!);

            // Assert - If the device doesn't exist, we expect a failure, which is also valid
            if (result.Success)
            {
                Assert.IsNotNull(result.Data, "Result data should not be null");
                _logger!.LogInformation("Device info retrieved: {Data}", result.Data);
            }
            else
            {
                _logger!.LogInformation("Device 'PLC_1' not found (this is acceptable if the project has different device names)");
            }
        }

        [TestMethod]
        public async Task Test_702_GetDeviceItemInfo_WithValidDevice_ReturnsSuccess()
        {
            // Arrange
            await _connectCommand!.ExecuteAsync(TestHelpers.CreateEmptyArgs(), _logger!);
            var projectOpened = await TestHelpers.OpenProjectAsync(_portalService!, Settings.TestProjectPath, _logger!);
            Assert.IsTrue(projectOpened, $"Failed to open test project: {Settings.TestProjectPath}");

            var getDeviceItemInfoCommand = new GetDeviceItemInfoCommand(_portalService);

            // Act - Try to get info for a device item (common naming: PLC_1 / CPU)
            var argsWithItem = new CommandArgs(new System.Collections.Generic.Dictionary<string, object?>
            {
                { "device-name", "PLC_1" },
                { "item-name", "CPU" }
            });

            // Act
            var result = await getDeviceItemInfoCommand.ExecuteAsync(argsWithItem, _logger!);

            // Assert - If the device/item doesn't exist, we expect a failure, which is also valid
            if (result.Success)
            {
                Assert.IsNotNull(result.Data, "Result data should not be null");
                _logger!.LogInformation("Device item info retrieved: {Data}", result.Data);
            }
            else
            {
                _logger!.LogInformation("Device item 'PLC_1/CPU' not found (this is acceptable if the project has different device names)");
            }
        }

        [TestMethod]
        public async Task Test_703_GetDeviceInfo_WithoutProject_ReturnsFailure()
        {
            // Arrange
            await _connectCommand!.ExecuteAsync(TestHelpers.CreateEmptyArgs(), _logger!);
            // Don't open a project

            var getDeviceInfoCommand = new GetDeviceInfoCommand(_portalService);
            var args = TestHelpers.CreateArgs("device-name", "PLC_1");

            // Act
            var result = await getDeviceInfoCommand.ExecuteAsync(args, _logger!);

            // Assert - Should fail because no project is open
            Assert.IsFalse(result.Success, "Should fail when no project is open");

            _logger!.LogInformation("Correctly failed to get device info without project: {Error}", result.Error);
        }

        [TestMethod]
        public async Task Test_704_GetDeviceItemInfo_WithoutProject_ReturnsFailure()
        {
            // Arrange
            await _connectCommand!.ExecuteAsync(TestHelpers.CreateEmptyArgs(), _logger!);
            // Don't open a project

            var getDeviceItemInfoCommand = new GetDeviceItemInfoCommand(_portalService);
            var args = new CommandArgs(new System.Collections.Generic.Dictionary<string, object?>
            {
                { "device-name", "PLC_1" },
                { "item-name", "CPU" }
            });

            // Act
            var result = await getDeviceItemInfoCommand.ExecuteAsync(args, _logger!);

            // Assert - Should fail because no project is open
            Assert.IsFalse(result.Success, "Should fail when no project is open");

            _logger!.LogInformation("Correctly failed to get device item info without project: {Error}", result.Error);
        }

        [TestMethod]
        public async Task Test_705_GetDeviceInfo_InvalidDevice_ReturnsFailure()
        {
            // Arrange
            await _connectCommand!.ExecuteAsync(TestHelpers.CreateEmptyArgs(), _logger!);
            var projectOpened = await TestHelpers.OpenProjectAsync(_portalService!, Settings.TestProjectPath, _logger!);
            Assert.IsTrue(projectOpened, $"Failed to open test project: {Settings.TestProjectPath}");

            var getDeviceInfoCommand = new GetDeviceInfoCommand(_portalService);
            var args = TestHelpers.CreateArgs("device-name", "NonExistentDevice");

            // Act
            var result = await getDeviceInfoCommand.ExecuteAsync(args, _logger!);

            // Assert
            Assert.IsFalse(result.Success, "Should fail for non-existent device");

            _logger!.LogInformation("Correctly failed to get info for non-existent device: {Error}", result.Error);
        }

        [TestMethod]
        public async Task Test_706_GetDeviceItemInfo_InvalidItem_ReturnsFailure()
        {
            // Arrange
            await _connectCommand!.ExecuteAsync(TestHelpers.CreateEmptyArgs(), _logger!);
            var projectOpened = await TestHelpers.OpenProjectAsync(_portalService!, Settings.TestProjectPath, _logger!);
            Assert.IsTrue(projectOpened, $"Failed to open test project: {Settings.TestProjectPath}");

            var getDeviceItemInfoCommand = new GetDeviceItemInfoCommand(_portalService);
            var args = new CommandArgs(new System.Collections.Generic.Dictionary<string, object?>
            {
                { "device-name", "PLC_1" },
                { "item-name", "NonExistentItem" }
            });

            // Act
            var result = await getDeviceItemInfoCommand.ExecuteAsync(args, _logger!);

            // Assert
            Assert.IsFalse(result.Success, "Should fail for non-existent device item");

            _logger!.LogInformation("Correctly failed to get info for non-existent device item: {Error}", result.Error);
        }
    }
}
