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
        public async Task ExecuteAsync_ParsesNegativeIntegerValue_WhenValueStartsWithDash()
        {
            // Arrange
            _commandMock.Setup(c => c.Schema).Returns(new CommandSchema("Test command", new[]
            {
                new CommandParameter("limit", CommandParameterType.Integer, "Limit")
            }));

            CommandArgs? capturedArgs = null;
            _commandMock.Setup(c => c.ExecuteAsync(It.IsAny<CommandArgs>(), It.IsAny<ILogger>()))
                .Callback<CommandArgs, ILogger>((args, _) => capturedArgs = args)
                .ReturnsAsync(CommandResult.CreateSuccess("Executed"));

            var router = new CommandRouter(new List<ICommand> { _commandMock.Object }, _loggerMock.Object);

            // Act
            var result = await router.ExecuteAsync(new[] { "test-command", "--limit", "-5" });

            // Assert
            Assert.IsTrue(result.Success);
            Assert.IsNotNull(capturedArgs);
            Assert.AreEqual(-5, capturedArgs!.GetInt("limit"));
        }

        [TestMethod]
        public async Task ExecuteAsync_ReturnsError_WhenParameterIsUnknown()
        {
            // Arrange
            _commandMock.Setup(c => c.Schema).Returns(new CommandSchema("Test command", new[]
            {
                new CommandParameter("known", CommandParameterType.String, "Known")
            }));

            var router = new CommandRouter(new List<ICommand> { _commandMock.Object }, _loggerMock.Object);

            // Act
            var result = await router.ExecuteAsync(new[] { "test-command", "--unknwon", "value" });

            // Assert
            Assert.IsFalse(result.Success);
            StringAssert.Contains(result.Message, "Unknown parameter");
            _commandMock.Verify(c => c.ExecuteAsync(It.IsAny<CommandArgs>(), It.IsAny<ILogger>()), Times.Never);
        }

        [TestMethod]
        public async Task ExecuteAsync_ReturnsError_WhenRequiredParameterIsMissing()
        {
            // Arrange
            _commandMock.Setup(c => c.Schema).Returns(new CommandSchema("Test command", new[]
            {
                new CommandParameter("plc-name", CommandParameterType.String, "PLC name", required: true)
            }));

            var router = new CommandRouter(new List<ICommand> { _commandMock.Object }, _loggerMock.Object);

            // Act
            var result = await router.ExecuteAsync(new[] { "test-command" });

            // Assert
            Assert.IsFalse(result.Success);
            StringAssert.Contains(result.Message, "Missing required parameter: plc-name");
            _commandMock.Verify(c => c.ExecuteAsync(It.IsAny<CommandArgs>(), It.IsAny<ILogger>()), Times.Never);
        }

        [TestMethod]
        public async Task ExecuteAsync_ReturnsUnknownCommand_WhenCommandNameStartsWithDash()
        {
            // Arrange
            _commandMock.Setup(c => c.Name).Returns("version");
            var router = new CommandRouter(new List<ICommand> { _commandMock.Object }, _loggerMock.Object);

            // Act
            var result = await router.ExecuteAsync(new[] { "--version" });

            // Assert
            Assert.IsFalse(result.Success);
            StringAssert.Contains(result.Message, "Unknown command: '--version'");
            _commandMock.Verify(c => c.ExecuteAsync(It.IsAny<CommandArgs>(), It.IsAny<ILogger>()), Times.Never);
        }

        [TestMethod]
        public async Task ExecuteAsync_ReturnsError_WhenUnexpectedPositionalArgumentIsProvided()
        {
            // Arrange
            var router = new CommandRouter(new List<ICommand> { _commandMock.Object }, _loggerMock.Object);

            // Act
            var result = await router.ExecuteAsync(new[] { "test-command", "stray" });

            // Assert
            Assert.IsFalse(result.Success);
            StringAssert.Contains(result.Message, "Unexpected positional argument: stray");
            _commandMock.Verify(c => c.ExecuteAsync(It.IsAny<CommandArgs>(), It.IsAny<ILogger>()), Times.Never);
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
            StringAssert.Contains(help, "Test description");
            Assert.IsFalse(help.Contains("Unknown command"));
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
