using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Core.Commands;
using System;

namespace OpenTiaToolkit.Tests.Commands
{
    /// <summary>
    /// Unit tests for CommandArgs argument parsing.
    /// </summary>
    [TestClass]
    public class CommandArgsTests
    {
        [TestMethod]
        public void GetString_ReturnsValue_WhenArgumentExists()
        {
            // Arrange
            var values = new System.Collections.Generic.Dictionary<string, object?>
            {
                { "name", "TestProject" }
            };
            var args = new CommandArgs(values);

            // Act
            var result = args.GetString("name");

            // Assert
            Assert.AreEqual("TestProject", result);
        }

        [TestMethod]
        public void GetString_ReturnsDefault_WhenArgumentNotExists()
        {
            // Arrange
            var values = new System.Collections.Generic.Dictionary<string, object?>();
            var args = new CommandArgs(values);

            // Act
            var result = args.GetString("name", "DefaultValue");

            // Assert
            Assert.AreEqual("DefaultValue", result);
        }

        [TestMethod]
        public void GetInt_ReturnsInteger_WhenArgumentIsInteger()
        {
            // Arrange
            var values = new System.Collections.Generic.Dictionary<string, object?>
            {
                { "count", 42 }
            };
            var args = new CommandArgs(values);

            // Act
            var result = args.GetInt("count");

            // Assert
            Assert.AreEqual(42, result);
        }

        [TestMethod]
        public void GetInt_ReturnsDefault_WhenArgumentNotExists()
        {
            // Arrange
            var values = new System.Collections.Generic.Dictionary<string, object?>();
            var args = new CommandArgs(values);

            // Act
            var result = args.GetInt("count", 99);

            // Assert
            Assert.AreEqual(99, result);
        }

        [TestMethod]
        public void GetInt_ParsesString_WhenArgumentIsString()
        {
            // Arrange
            var values = new System.Collections.Generic.Dictionary<string, object?>
            {
                { "count", "123" }
            };
            var args = new CommandArgs(values);

            // Act
            var result = args.GetInt("count");

            // Assert
            Assert.AreEqual(123, result);
        }

        [TestMethod]
        public void GetBool_ReturnsTrue_WhenArgumentIsTrue()
        {
            // Arrange
            var values = new System.Collections.Generic.Dictionary<string, object?>
            {
                { "save", true }
            };
            var args = new CommandArgs(values);

            // Act
            var result = args.GetBool("save");

            // Assert
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void GetBool_ParsesString_WhenArgumentIsString()
        {
            // Arrange
            var values = new System.Collections.Generic.Dictionary<string, object?>
            {
                { "save", "true" }
            };
            var args = new CommandArgs(values);

            // Act
            var result = args.GetBool("save");

            // Assert
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void GetStringArray_ReturnsArray_WhenArgumentIsArray()
        {
            // Arrange
            var values = new System.Collections.Generic.Dictionary<string, object?>
            {
                { "items", new string[] { "item1", "item2", "item3" } }
            };
            var args = new CommandArgs(values);

            // Act
            var result = args.GetStringArray("items");

            // Assert
            Assert.AreEqual(3, result.Length);
            Assert.AreEqual("item1", result[0]);
            Assert.AreEqual("item2", result[1]);
            Assert.AreEqual("item3", result[2]);
        }

        [TestMethod]
        public void Has_ReturnsTrue_WhenArgumentExists()
        {
            // Arrange
            var values = new System.Collections.Generic.Dictionary<string, object?>
            {
                { "name", "Test" }
            };
            var args = new CommandArgs(values);

            // Act
            var result = args.Has("name");

            // Assert
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void Has_ReturnsFalse_WhenArgumentNotExists()
        {
            // Arrange
            var values = new System.Collections.Generic.Dictionary<string, object?>();
            var args = new CommandArgs(values);

            // Act
            var result = args.Has("name");

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void CaseInsensitive_BothWork()
        {
            // Arrange
            var values = new System.Collections.Generic.Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                { "ProjectPath", "C:\\Test.ap19" }
            };
            var args = new CommandArgs(values);

            // Act
            var result1 = args.GetString("projectpath");
            var result2 = args.GetString("PROJECTPATH");

            // Assert
            // Both should return the value (case-insensitive lookup works)
            // The key lookup should work regardless of case
            Assert.IsNotNull(result1);
            Assert.IsNotNull(result2);
            // The values should be the same
            Assert.AreEqual(result1, result2);
            // And should contain our test value
            Assert.IsTrue(result1.Contains("Test.ap19"));
        }
    }
}
