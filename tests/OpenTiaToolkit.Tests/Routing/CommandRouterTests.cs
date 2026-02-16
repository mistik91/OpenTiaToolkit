using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OpenTiaToolkit.CLI.Routing;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Tests.Routing
{
    /// <summary>
    /// Unit tests for CommandRouter argument parsing and routing.
    /// </summary>
    [TestClass]
    public class CommandRouterTests
    {
        private Mock<ILogger<CommandRouter>> _loggerMock = null!;
        private Mock<ICommand> _commandMock = null!;

        [TestInitialize]
        public void Setup()
        {
            _loggerMock = new Mock<ILogger<CommandRouter>>();
            _commandMock = new Mock<ICommand>();
            _commandMock.Setup(c => c.Name).Returns("test-command");
            _commandMock.Setup(c => c.Description).Returns("Test command");
            _commandMock.Setup(c => c.Schema).Returns(new CommandSchema("Test command"));
            _commandMock.Setup(c => c.ExecuteAsync(It.IsAny<CommandArgs>(), It.IsAny<ILogger>()))
                .ReturnsAsync(CommandResult.CreateSuccess("Executed"));
        }

        [TestMethod]
        public async Task ExecuteAsync_RoutesToCommand_WhenCommandExists()
        {
            // Arrange
            var commands = new List<ICommand> { _commandMock.Object };
            var router = new CommandRouter(commands, _loggerMock.Object);

            // Act
            var result = await router.ExecuteAsync(new[] { "test-command" });

            // Assert
            Assert.IsTrue(result.Success);
            _commandMock.Verify(c => c.ExecuteAsync(It.IsAny<CommandArgs>(), It.IsAny<ILogger>()), Times.Once);
        }

        [TestMethod]
        public async Task ExecuteAsync_ReturnsError_WhenCommandNotExists()
        {
            // Arrange
            var commands = new List<ICommand> { _commandMock.Object };
            var router = new CommandRouter(commands, _loggerMock.Object);

            // Act
            var result = await router.ExecuteAsync(new[] { "unknown-command" });

            // Assert
            Assert.IsFalse(result.Success);
            StringAssert.Contains(result.Message, "Unknown command");
        }

        [TestMethod]
        public async Task ExecuteAsync_ReturnsError_WhenNoCommandSpecified()
        {
            // Arrange
            var commands = new List<ICommand> { _commandMock.Object };
            var router = new CommandRouter(commands, _loggerMock.Object);

            // Act
            var result = await router.ExecuteAsync(Array.Empty<string>());

            // Assert
            Assert.IsFalse(result.Success);
            StringAssert.Contains(result.Message, "No command specified");
        }

        [TestMethod]
        public void GetHelp_ReturnsAllCommands_WhenNoCommandSpecified()
        {
            // Arrange
            _commandMock.Setup(c => c.Name).Returns("cmd1");
            var commands = new List<ICommand> { _commandMock.Object };
            var router = new CommandRouter(commands, _loggerMock.Object);

            // Act
            var help = router.GetHelp();

            // Assert
            StringAssert.Contains(help, "cmd1");
            StringAssert.Contains(help, "Available commands");
        }

        [TestMethod]
        public void GetHelp_ReturnsCommandHelp_WhenCommandSpecified()
        {
            // Arrange
            _commandMock.Setup(c => c.Name).Returns("test-command");
            _commandMock.Setup(c => c.Description).Returns("Test description");
            _commandMock.Setup(c => c.Schema).Returns(new CommandSchema("Test description"));
            var commands = new List<ICommand> { _commandMock.Object };
            var router = new CommandRouter(commands, _loggerMock.Object);

            // Act
            var help = router.GetHelp("test-command");

            // Assert
            StringAssert.Contains(help, "test-command");
            // The help should contain either the command name or description
            Assert.IsTrue(help.Contains("Test description") || help.Contains("test-command"));
        }

        [TestMethod]
        public void GetHelp_ReturnsError_WhenUnknownCommandSpecified()
        {
            // Arrange
            var commands = new List<ICommand> { _commandMock.Object };
            var router = new CommandRouter(commands, _loggerMock.Object);

            // Act
            var help = router.GetHelp("unknown");

            // Assert
            StringAssert.Contains(help, "Unknown command");
        }
    }
}
