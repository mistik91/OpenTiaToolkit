using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands;
using OpenTiaToolkit.MCP.Server;
using System.Collections.Generic;
using System.Linq;

namespace OpenTiaToolkit.Tests.Server
{
    /// <summary>
    /// Unit tests for ToolRegistry.
    /// </summary>
    [TestClass]
    public class ToolRegistryTests
    {
        private Mock<ILogger<ToolRegistry>> _loggerMock = null!;

        [TestInitialize]
        public void Setup()
        {
            _loggerMock = new Mock<ILogger<ToolRegistry>>();
        }

        [TestMethod]
        public void Constructor_RegistersAllCommands()
        {
            // Arrange
            var commandMock1 = new Mock<ICommand>();
            commandMock1.Setup(c => c.Name).Returns("cmd1");
            commandMock1.Setup(c => c.Description).Returns("Command 1");
            commandMock1.Setup(c => c.Schema).Returns(new CommandSchema("Command 1"));

            var commandMock2 = new Mock<ICommand>();
            commandMock2.Setup(c => c.Name).Returns("cmd2");
            commandMock2.Setup(c => c.Description).Returns("Command 2");
            commandMock2.Setup(c => c.Schema).Returns(new CommandSchema("Command 2"));

            var commands = new List<ICommand> { commandMock1.Object, commandMock2.Object };

            // Act
            var registry = new ToolRegistry(commands, _loggerMock.Object);

            // Assert
            Assert.AreEqual(2, registry.Tools.Count);
            Assert.IsTrue(registry.Tools.ContainsKey("cmd1"));
            Assert.IsTrue(registry.Tools.ContainsKey("cmd2"));
        }

        [TestMethod]
        public void GetTool_ReturnsAdapter_WhenToolExists()
        {
            // Arrange
            var commandMock = new Mock<ICommand>();
            commandMock.Setup(c => c.Name).Returns("test-cmd");
            commandMock.Setup(c => c.Description).Returns("Test");
            commandMock.Setup(c => c.Schema).Returns(new CommandSchema("Test"));

            var commands = new List<ICommand> { commandMock.Object };
            var registry = new ToolRegistry(commands, _loggerMock.Object);

            // Act
            var tool = registry.GetTool("test-cmd");

            // Assert
            Assert.IsNotNull(tool);
            Assert.AreEqual("test-cmd", tool.Name);
            Assert.AreEqual("Test", tool.Description);
        }

        [TestMethod]
        public void GetTool_ReturnsNull_WhenToolNotExists()
        {
            // Arrange
            var commands = new List<ICommand>();
            var registry = new ToolRegistry(commands, _loggerMock.Object);

            // Act
            var tool = registry.GetTool("nonexistent");

            // Assert
            Assert.IsNull(tool);
        }

        [TestMethod]
        public void ListTools_ReturnsAllTools()
        {
            // Arrange
            var commandMock1 = new Mock<ICommand>();
            commandMock1.Setup(c => c.Name).Returns("cmd1");
            commandMock1.Setup(c => c.Description).Returns("Command 1");
            commandMock1.Setup(c => c.Schema).Returns(new CommandSchema("Command 1"));

            var commandMock2 = new Mock<ICommand>();
            commandMock2.Setup(c => c.Name).Returns("cmd2");
            commandMock2.Setup(c => c.Description).Returns("Command 2");
            commandMock2.Setup(c => c.Schema).Returns(new CommandSchema("Command 2"));

            var commands = new List<ICommand> { commandMock1.Object, commandMock2.Object };
            var registry = new ToolRegistry(commands, _loggerMock.Object);

            // Act
            var tools = registry.ListTools();

            // Assert
            Assert.AreEqual(2, tools.Count);
            Assert.AreEqual("cmd1", tools[0].Name);
            Assert.AreEqual("cmd2", tools[1].Name);
        }

        [TestMethod]
        public void ToolAdapter_CaseInsensitiveLookup()
        {
            // Arrange
            var commandMock = new Mock<ICommand>();
            commandMock.Setup(c => c.Name).Returns("Test-Cmd");
            commandMock.Setup(c => c.Description).Returns("Test");
            commandMock.Setup(c => c.Schema).Returns(new CommandSchema("Test"));

            var commands = new List<ICommand> { commandMock.Object };
            var registry = new ToolRegistry(commands, _loggerMock.Object);

            // Act & Assert
            Assert.IsNotNull(registry.GetTool("test-cmd"));
            Assert.IsNotNull(registry.GetTool("TEST-CMD"));
            Assert.IsNotNull(registry.GetTool("Test-Cmd"));
        }
    }
}
