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
using OpenTiaToolkit.MCP.Server;

namespace OpenTiaToolkit.Tests.Integration
{
    [TestClass]
    [TestCategory("Integration")]
    public class RemoteToolRegistryIntegrationTests
    {
        [TestMethod]
        public void ListTools_IsWorkerBackedUniqueAndIncludesPolicyParameters()
        {
            using (ToolRegistry registry = CreateRegistry())
            {
                List<Tool> tools = registry.ListTools();
                Assert.IsTrue(tools.Count >= 145);
                Assert.AreEqual(tools.Count, tools.Select(tool => tool.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
                Tool createProject = tools.Single(tool => tool.Name == "create-project");
                string schema = createProject.InputSchema.GetRawText();
                StringAssert.Contains(schema, "confirm");
                CollectionAssert.IsSubsetOf(
                    new[] { "validate-blueprint", "plan-blueprint", "apply-blueprint", "describe-api", "diff-api-profile", "get-hardware-drive-parameters", "list-unified-alarms" },
                    tools.Select(tool => tool.Name).ToArray());
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
                    StringAssert.Contains(result.StructuredContent!.ToJsonString(), "PolicyDenied");
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
                StringAssert.Contains(status.StructuredContent!.ToJsonString(), "No operation is active");
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
