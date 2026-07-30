using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Protocol;
using OpenTiaToolkit.Contracts.Commands;
using OpenTiaToolkit.MCP.Server;

namespace OpenTiaToolkit.Tests.Integration
{
    [TestClass]
    [TestCategory("Integration")]
    public class RemoteToolRegistryIntegrationTests
    {
        public TestContext TestContext { get; set; } = null!;

        [TestMethod]
        public void ListTools_IsWorkerBackedUniqueAndIncludesPolicyParameters()
        {
            using (ToolRegistry registry = CreateRegistry())
            {
                List<Tool> tools = registry.ListTools();
                List<Tool> repeated = registry.ListTools();
                string[] expectedNames = registry.CommandNames
                    .Concat(new[]
                    {
                        "operation-status",
                        "cancel-operation",
                        "reset-session"
                    })
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                CollectionAssert.AreEqual(
                    expectedNames,
                    tools.Select(tool => tool.Name).ToArray());
                CollectionAssert.AreEqual(
                    tools.Select(tool => tool.Name).ToArray(),
                    repeated.Select(tool => tool.Name).ToArray());
                Assert.AreEqual(tools.Count, tools.Select(tool => tool.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
                Tool createProject = tools.Single(tool => tool.Name == "create-project");
                string schema = createProject.InputSchema.GetRawText();
                StringAssert.Contains(schema, "confirm");
                CollectionAssert.IsSubsetOf(
                    new[] { "validate-blueprint", "plan-blueprint", "apply-blueprint", "describe-api", "diff-api-profile", "get-hardware-drive-parameters", "list-unified-alarms" },
                    tools.Select(tool => tool.Name).ToArray());

                foreach (CommandDescriptor descriptor in registry.CommandDescriptors)
                {
                    Tool tool = tools.Single(candidate => candidate.Name == descriptor.Name);
                    JsonElement inputSchema = tool.InputSchema;
                    Assert.AreEqual("object", inputSchema.GetProperty("type").GetString(), descriptor.Name);
                    Assert.IsFalse(inputSchema.GetProperty("additionalProperties").GetBoolean(), descriptor.Name);
                    JsonElement properties = inputSchema.GetProperty("properties");
                    string[] required = inputSchema.TryGetProperty("required", out JsonElement requiredElement)
                        ? requiredElement.EnumerateArray().Select(value => value.GetString()!).ToArray()
                        : Array.Empty<string>();

                    CollectionAssert.AreEquivalent(
                        descriptor.Parameters
                            .Where(parameter => parameter.Required)
                            .Select(parameter => parameter.Name)
                            .ToArray(),
                        required,
                        descriptor.Name);
                    foreach (CommandParameterContract parameter in descriptor.Parameters)
                    {
                        Assert.IsTrue(
                            properties.TryGetProperty(parameter.Name, out JsonElement parameterSchema),
                            $"{descriptor.Name}: missing schema for {parameter.Name}");
                        Assert.AreEqual(
                            ExpectedJsonType(parameter.Kind),
                            parameterSchema.GetProperty("type").GetString(),
                            $"{descriptor.Name}.{parameter.Name}");
                        if (parameter.Kind == CommandParameterKind.StringArray)
                            Assert.AreEqual("string", parameterSchema.GetProperty("items").GetProperty("type").GetString());
                        if (parameter.Kind == CommandParameterKind.ObjectArray)
                            Assert.AreEqual("object", parameterSchema.GetProperty("items").GetProperty("type").GetString());
                        if (parameter.DefaultValue.HasValue)
                        {
                            Assert.IsTrue(parameterSchema.TryGetProperty("default", out JsonElement defaultValue));
                            Assert.AreEqual(
                                parameter.DefaultValue.Value.GetRawText(),
                                defaultValue.GetRawText(),
                                $"{descriptor.Name}.{parameter.Name}");
                        }
                    }
                }

                TestContext.WriteLine(
                    $"Runtime commands: {registry.CommandNames.Count}; MCP tools: {tools.Count}; infrastructure tools: 3.");
            }
        }

        [TestMethod]
        public async Task MutationWithoutConfirmation_IsDeniedBeforeWorkerStateChangeAndAudited()
        {
            string root = Path.Combine(Path.GetTempPath(), "OpenTiaToolkit", "RemoteRegistryTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string projectPath = Path.Combine(root, "DeniedProject.ap21");
            string auditPath = Path.Combine(root, "audit.jsonl");
            try
            {
                using (ToolRegistry registry = CreateRegistry(auditPath, root))
                {
                    using JsonDocument path = JsonDocument.Parse(JsonSerializer.Serialize(projectPath));
                    CallToolResult result = await registry.CallToolAsync(
                        "create-project",
                        new Dictionary<string, JsonElement> { ["project-path"] = path.RootElement.Clone() },
                        CancellationToken.None);
                    Assert.IsTrue(result.IsError);
                    StringAssert.Contains(result.StructuredContent!.Value.GetRawText(), "PolicyDenied");
                }

                Assert.IsFalse(Directory.Exists(Path.Combine(root, "DeniedProject")));
                Assert.IsTrue(File.Exists(auditPath));
                StringAssert.Contains(File.ReadAllText(auditPath), "PolicyDenied");
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
        }

        [TestMethod]
        public async Task ResetSession_ReplacesWorkerAndRemainsUsable()
        {
            using (ToolRegistry registry = CreateRegistry())
            {
                CallToolResult reset = await registry.CallToolAsync("reset-session", null, CancellationToken.None);
                Assert.IsFalse(reset.IsError);
                CallToolResult status = await registry.CallToolAsync("operation-status", null, CancellationToken.None);
                Assert.IsFalse(status.IsError);
                StringAssert.Contains(status.StructuredContent!.Value.GetRawText(), "No operation is active");
            }
        }

        private static string ExpectedJsonType(CommandParameterKind kind)
        {
            switch (kind)
            {
                case CommandParameterKind.Integer:
                    return "integer";
                case CommandParameterKind.Boolean:
                    return "boolean";
                case CommandParameterKind.StringArray:
                case CommandParameterKind.ObjectArray:
                    return "array";
                case CommandParameterKind.Object:
                    return "object";
                default:
                    return "string";
            }
        }

        private static ToolRegistry CreateRegistry(string? auditPath = null, string? allowedRoot = null)
        {
            string worker = ResolveWorkerPath();
            return new ToolRegistry(
                worker,
                auditPath ?? Path.Combine(Path.GetTempPath(), "OpenTiaToolkit", "RemoteRegistryTests", "audit.jsonl"),
                new McpRuntimeOptions
                {
                    AllowedFileRoots = allowedRoot == null ? Array.Empty<string>() : new[] { allowedRoot },
                    ToolTimeout = TimeSpan.FromSeconds(30)
                },
                NullLogger<ToolRegistry>.Instance);
        }

        private static string ResolveWorkerPath()
        {
            string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name ?? "Debug";
            return Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "..",
                "src", "OpenTiaToolkit.Worker.V21", "bin", configuration, "net48",
                "OpenTiaToolkit.Worker.V21.exe"));
        }
    }
}
