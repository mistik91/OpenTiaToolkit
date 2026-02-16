using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Core.Commands;

namespace OpenTiaToolkit.Tests.Commands
{
    /// <summary>
    /// Unit tests for CommandResult creation and formatting.
    /// </summary>
    [TestClass]
    public class CommandResultTests
    {
        [TestMethod]
        public void CreateSuccess_ReturnsSuccessfulResult()
        {
            // Act
            var result = CommandResult.CreateSuccess();

            // Assert
            Assert.IsTrue(result.Success);
            Assert.AreEqual("Command completed successfully", result.Message);
        }

        [TestMethod]
        public void CreateSuccess_WithData_ReturnsSuccessfulResultWithData()
        {
            // Arrange
            var data = new { Name = "Test", Value = 42 };

            // Act
            var result = CommandResult.CreateSuccess(data, "Custom message");

            // Assert
            Assert.IsTrue(result.Success);
            Assert.AreEqual("Custom message", result.Message);
            Assert.IsNotNull(result.Data);
        }

        [TestMethod]
        public void CreateFailure_ReturnsFailedResult()
        {
            // Act
            var result = CommandResult.CreateFailure("Something went wrong");

            // Assert
            Assert.IsFalse(result.Success);
            Assert.AreEqual("Something went wrong", result.Message);
        }

        [TestMethod]
        public void CreateFailure_WithException_IncludesErrorMessage()
        {
            // Arrange
            var exception = new System.Exception("Database error");

            // Act
            var result = CommandResult.CreateFailure("Failed to save", exception);

            // Assert
            Assert.IsFalse(result.Success);
            Assert.AreEqual("Failed to save", result.Message);
            Assert.AreEqual("Database error", result.Error);
        }

        [TestMethod]
        public void ToTable_ReturnsMessage_WhenDataIsNull()
        {
            // Arrange
            var result = CommandResult.CreateSuccess(message: "Just a message");

            // Act
            var table = result.ToTable();

            // Assert
            Assert.AreEqual("Just a message", table);
        }

        [TestMethod]
        public void ToTable_ReturnsError_WhenFailed()
        {
            // Arrange
            var result = CommandResult.CreateFailure("Error occurred");

            // Act
            var table = result.ToTable();

            // Assert
            Assert.IsTrue(table.Contains("Error:"));
            Assert.IsTrue(table.Contains("Error occurred"));
        }

        [TestMethod]
        public void ToTable_ReturnsString_WhenDataIsString()
        {
            // Arrange
            var result = CommandResult.CreateSuccess(data: "Simple string");

            // Act
            var table = result.ToTable();

            // Assert
            Assert.AreEqual("Simple string", table);
        }

        [TestMethod]
        public void ToTable_JoinsStringArray_WhenDataIsStringArray()
        {
            // Arrange
            var result = CommandResult.CreateSuccess(data: new string[] { "line1", "line2", "line3" });

            // Act
            var table = result.ToTable();

            // Assert
            StringAssert.Contains(table, "line1");
            StringAssert.Contains(table, "line2");
            StringAssert.Contains(table, "line3");
        }

        [TestMethod]
        public void ToJson_ReturnsValidJson_WhenSuccess()
        {
            // Arrange
            var result = CommandResult.CreateSuccess(data: new { name = "test" });

            // Act
            var json = result.ToJson();

            // Assert
            // Check for success (may be formatted differently)
            StringAssert.Contains(json, "\"success\"");
            StringAssert.Contains(json, "true");
            StringAssert.Contains(json, "\"name\"");
            StringAssert.Contains(json, "\"test\"");
        }

        [TestMethod]
        public void ToJson_IncludesError_WhenFailed()
        {
            // Arrange
            var result = CommandResult.CreateFailure("Error message");

            // Act
            var json = result.ToJson();

            // Assert
            // Check for success false (may be formatted differently)
            StringAssert.Contains(json, "\"success\"");
            StringAssert.Contains(json, "false");
            StringAssert.Contains(json, "\"Error message\"");
        }
    }
}
