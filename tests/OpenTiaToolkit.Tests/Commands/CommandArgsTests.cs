using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Core.Commands;
using System;
using System.Text.Json;

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
        public void GetStringArray_SplitsCommaSeparatedString_WhenArgumentIsString()
        {
            // Arrange
            var values = new System.Collections.Generic.Dictionary<string, object?>
            {
                { "items", "StartAddress, IoType,Length" }
            };
            var args = new CommandArgs(values);

            // Act
            var result = args.GetStringArray("items");

            // Assert
            CollectionAssert.AreEqual(new[] { "StartAddress", "IoType", "Length" }, result);
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
            var values = new System.Collections.Generic.Dictionary<string, object?>()
            {
                { "ProjectPath", "C:\\Test.ap21" }
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
            Assert.IsTrue(result1.Contains("Test.ap21"));
        }

        [TestMethod]
        public void KebabSnakeAndCamelCase_BothWork()
        {
            // Arrange
            var values = new System.Collections.Generic.Dictionary<string, object?>()
            {
                { "projectPath", "C:\\Test.ap21" }
            };
            var args = new CommandArgs(values);

            // Act
            var snakeCase = args.GetString("project_path");
            var kebabCase = args.GetString("project-path");

            // Assert
            Assert.AreEqual("C:\\Test.ap21", snakeCase);
            Assert.AreEqual("C:\\Test.ap21", kebabCase);
        }

        [TestMethod]
        public void GetInt_ReadsJsonElementNumber_WhenArgumentsComeFromMcp()
        {
            using var document = JsonDocument.Parse("{\"count\":7}");
            var args = new CommandArgs(new System.Collections.Generic.Dictionary<string, object?>
            {
                { "count", document.RootElement.GetProperty("count").Clone() }
            });

            Assert.AreEqual(7, args.GetInt("count"));
        }

        [TestMethod]
        public void GetBool_ReadsJsonElementBoolean_WhenArgumentsComeFromMcp()
        {
            using var document = JsonDocument.Parse("{\"save\":true}");
            var args = new CommandArgs(new System.Collections.Generic.Dictionary<string, object?>
            {
                { "save", document.RootElement.GetProperty("save").Clone() }
            });

            Assert.IsTrue(args.GetBool("save"));
        }

        [TestMethod]
        public void GetStringArray_ReadsJsonElementArray_WhenArgumentsComeFromMcp()
        {
            using var document = JsonDocument.Parse("{\"items\":[\"item1\",\"item2\"]}");
            var args = new CommandArgs(new System.Collections.Generic.Dictionary<string, object?>
            {
                { "items", document.RootElement.GetProperty("items").Clone() }
            });

            CollectionAssert.AreEqual(new[] { "item1", "item2" }, args.GetStringArray("items"));
        }
    }
}
