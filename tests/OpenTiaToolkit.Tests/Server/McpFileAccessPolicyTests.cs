using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands;
using OpenTiaToolkit.MCP.Server;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace OpenTiaToolkit.Tests.Server
{
    [TestClass]
    public class McpFileAccessPolicyTests
    {
        [TestMethod]
        public void Validate_RejectsPathOutsideAllowedRoots()
        {
            var allowedRoot = CreateTempDirectory();
            try
            {
                var policy = CreatePolicy(allowedRoot);
                var command = CreatePathCommand("input-path");

                var error = policy.Validate(command, Args(("input-path", @"C:\Windows\win.ini")));

                StringAssert.Contains(error, "Allowed roots");
            }
            finally
            {
                Directory.Delete(allowedRoot, recursive: true);
            }
        }

        [TestMethod]
        public void Validate_RejectsOversizedExistingInput()
        {
            var allowedRoot = CreateTempDirectory();
            var inputPath = Path.Combine(allowedRoot, "input.json");
            File.WriteAllBytes(inputPath, new byte[32]);
            try
            {
                var policy = CreatePolicy(allowedRoot, maxInputBytes: 16);

                var error = policy.Validate(CreatePathCommand("input-path"), Args(("input-path", inputPath)));

                StringAssert.Contains(error, "configured maximum");
            }
            finally
            {
                Directory.Delete(allowedRoot, recursive: true);
            }
        }

        [TestMethod]
        public void Validate_RejectsExistingOutputUnlessOverwriteEnabled()
        {
            var allowedRoot = CreateTempDirectory();
            var outputPath = Path.Combine(allowedRoot, "result.json");
            File.WriteAllText(outputPath, "existing");
            try
            {
                var denied = CreatePolicy(allowedRoot, allowOverwrite: false)
                    .Validate(CreatePathCommand("output-path"), Args(("output-path", outputPath)));
                var allowed = CreatePolicy(allowedRoot, allowOverwrite: true)
                    .Validate(CreatePathCommand("output-path"), Args(("output-path", outputPath)));

                StringAssert.Contains(denied, "rejected overwrite");
                Assert.IsNull(allowed);
            }
            finally
            {
                Directory.Delete(allowedRoot, recursive: true);
            }
        }

        private static McpFileAccessPolicy CreatePolicy(
            string allowedRoot,
            long maxInputBytes = 1024,
            bool allowOverwrite = false)
        {
            return new McpFileAccessPolicy(new McpRuntimeOptions
            {
                AllowedFileRoots = new[] { allowedRoot },
                MaxInputFileBytes = maxInputBytes,
                AllowFileOverwrite = allowOverwrite
            });
        }

        private static ICommand CreatePathCommand(string parameterName)
        {
            var command = new Mock<ICommand>();
            command.SetupGet(candidate => candidate.Name).Returns("path-test");
            command.SetupGet(candidate => candidate.Description).Returns("Path test");
            command.SetupGet(candidate => candidate.Schema).Returns(new CommandSchema(
                "Path test",
                new[] { new CommandParameter(parameterName, CommandParameterType.Path, "Test path") }));
            return command.Object;
        }

        private static IReadOnlyDictionary<string, JsonElement> Args(params (string Name, string Value)[] values)
        {
            var result = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var value in values)
            {
                using var document = JsonDocument.Parse(JsonSerializer.Serialize(value.Value));
                result[value.Name] = document.RootElement.Clone();
            }

            return result;
        }

        private static string CreateTempDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), "OpenTiaToolkit", "PolicyTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
