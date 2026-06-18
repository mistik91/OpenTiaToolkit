using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.CLI.Output;
using OpenTiaToolkit.Core.Commands;
using System.Linq;
using System.Text.Json;

namespace OpenTiaToolkit.Tests.Output
{
    [TestClass]
    public class CliOutputOptionsTests
    {
        [TestMethod]
        public void TryParse_DefaultsToTable_WhenFormatIsNotProvided()
        {
            var parsed = CliOutputOptions.TryParse(
                new[] { "list-devices" },
                out var options,
                out var error);

            Assert.IsTrue(parsed);
            Assert.IsNull(error);
            Assert.AreEqual(CliOutputFormat.Table, options.Format);
            CollectionAssert.AreEqual(new[] { "list-devices" }, options.CommandArgs);
        }

        [TestMethod]
        public void TryParse_StripsFormatEqualsJson_FromCommandArguments()
        {
            var parsed = CliOutputOptions.TryParse(
                new[] { "list-devices", "--format=json" },
                out var options,
                out var error);

            Assert.IsTrue(parsed);
            Assert.IsNull(error);
            Assert.AreEqual(CliOutputFormat.Json, options.Format);
            CollectionAssert.AreEqual(new[] { "list-devices" }, options.CommandArgs);
        }

        [TestMethod]
        public void TryParse_StripsFormatJsonPair_FromCommandArguments()
        {
            var parsed = CliOutputOptions.TryParse(
                new[] { "--format", "json", "list-devices" },
                out var options,
                out var error);

            Assert.IsTrue(parsed);
            Assert.IsNull(error);
            Assert.AreEqual(CliOutputFormat.Json, options.Format);
            CollectionAssert.AreEqual(new[] { "list-devices" }, options.CommandArgs);
        }

        [TestMethod]
        public void TryParse_ReturnsError_WhenFormatValueIsInvalid()
        {
            var parsed = CliOutputOptions.TryParse(
                new[] { "list-devices", "--format", "yaml" },
                out _,
                out var error);

            Assert.IsFalse(parsed);
            StringAssert.Contains(error, "Invalid value");
            StringAssert.Contains(error, "table, json");
        }

        [TestMethod]
        public void TryParse_ReturnsError_WhenFormatValueIsMissing()
        {
            var parsed = CliOutputOptions.TryParse(
                new[] { "list-devices", "--format" },
                out _,
                out var error);

            Assert.IsFalse(parsed);
            StringAssert.Contains(error, "Missing value");
        }

        [TestMethod]
        public void Format_ReturnsJsonEnvelope_WhenJsonIsRequested()
        {
            var result = CommandResult.CreateSuccess(
                new { items = new[] { "PLC_1", "HMI_1" } },
                "Found devices");

            var output = CliOutputFormatter.Format(result, CliOutputFormat.Json);
            using var document = JsonDocument.Parse(output);

            Assert.IsTrue(document.RootElement.GetProperty("success").GetBoolean());
            Assert.AreEqual("Found devices", document.RootElement.GetProperty("message").GetString());
            Assert.AreEqual("PLC_1", document.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().First().GetString());
        }

        [TestMethod]
        public void Format_ReturnsTableOutput_WhenTableIsRequested()
        {
            var result = CommandResult.CreateSuccess("Plain text", "Completed");

            var output = CliOutputFormatter.Format(result, CliOutputFormat.Table);

            Assert.AreEqual("Plain text", output);
        }
    }
}
