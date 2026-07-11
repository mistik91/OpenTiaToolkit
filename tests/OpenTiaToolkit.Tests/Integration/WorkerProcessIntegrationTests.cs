using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Application.Execution;
using OpenTiaToolkit.Contracts.Commands;
using System.Text.Json;

namespace OpenTiaToolkit.Tests.Integration
{
    [TestClass]
    [TestCategory("Integration")]
    public class WorkerProcessIntegrationTests
    {
        [TestMethod]
        public async Task Worker_HealthAndCatalog_RoundTripAcrossProcessBoundary()
        {
            string workerPath = ResolveWorkerPath();

            using (WorkerProcessTransport transport = await WorkerProcessTransport.StartAsync(
                workerPath,
                TimeSpan.FromSeconds(20),
                CancellationToken.None))
            {
                CommandDescriptor[] commands = await transport.GetCommandsAsync(CancellationToken.None);

                Assert.IsTrue(commands.Length >= 124, $"Expected the shared command catalog, got {commands.Length} commands.");
                Assert.AreEqual(commands.Length, commands.Select(command => command.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
                Assert.IsTrue(commands.Any(command => command.Name == "list-instances" && command.Risk == CommandRisk.ReadOnly));
                Assert.IsTrue(commands.Any(command => command.Name == "modify-tags" && command.Risk == CommandRisk.Mutating));
                Assert.IsTrue(commands.Any(command => command.Name == "delete-blocks" && command.Risk == CommandRisk.Destructive));
            }
        }

        [TestMethod]
        public async Task Worker_InjectedCrash_IsObservedAsUnavailableWithoutCrashingHost()
        {
            string workerPath = ResolveWorkerPath();
            using (WorkerProcessTransport transport = await WorkerProcessTransport.StartAsync(
                workerPath,
                TimeSpan.FromSeconds(20),
                CancellationToken.None,
                new Dictionary<string, string> { ["OPEN_TIA_WORKER_FAILURE_INJECTION"] = "crash:before-execute" }))
            {
                await Assert.ThrowsExceptionAsync<EndOfStreamException>(() => transport.ExecuteAsync(
                    new CommandInvocation { CommandName = "list-instances" },
                    CancellationToken.None));
            }
        }

        [TestMethod]
        public async Task Worker_BlueprintPlanAndApply_UsesOneTimeStateBoundToken()
        {
            string blueprintPath = Path.Combine(Path.GetTempPath(), "opentia-blueprint-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(blueprintPath, "{\"schemaVersion\":\"1.0\",\"name\":\"disconnect\",\"requiredWorker\":\"V21\",\"steps\":[{\"id\":\"disconnect\",\"command\":\"disconnect-portal\",\"arguments\":{}}]}");
            try
            {
                using (ToolkitSession transport = await ToolkitSession.StartAsync(
                    ResolveWorkerPath(),
                    Path.Combine(Path.GetTempPath(), "opentia-blueprint-audit-" + Guid.NewGuid().ToString("N") + ".jsonl"),
                    CancellationToken.None))
                {
                    var plan = await transport.ExecuteAsync(new CommandInvocation
                    {
                        CommandName = "plan-blueprint",
                        Arguments = new Dictionary<string, JsonElement> { ["blueprint-path"] = JsonSerializer.SerializeToElement(blueprintPath) }
                    }, CancellationToken.None);
                    Assert.IsTrue(plan.Success, plan.Message);
                    string token = plan.Data!.Value.GetProperty("ApplyToken").GetString()!;
                    var denied = await transport.ExecuteAsync(new CommandInvocation
                    {
                        CommandName = "apply-blueprint",
                        Arguments = new Dictionary<string, JsonElement> { ["apply-token"] = JsonSerializer.SerializeToElement(token) }
                    }, CancellationToken.None);
                    Assert.IsFalse(denied.Success);
                    Assert.AreEqual("PolicyDenied", denied.Error!.Code.ToString());
                    var apply = await transport.ExecuteAsync(new CommandInvocation
                    {
                        CommandName = "apply-blueprint",
                        Arguments = new Dictionary<string, JsonElement>
                        {
                            ["apply-token"] = JsonSerializer.SerializeToElement(token),
                            ["confirm"] = JsonSerializer.SerializeToElement(true)
                        }
                    }, CancellationToken.None);
                    Assert.IsTrue(apply.Success, apply.Message);
                    var replay = await transport.ExecuteAsync(new CommandInvocation
                    {
                        CommandName = "apply-blueprint",
                        Arguments = new Dictionary<string, JsonElement>
                        {
                            ["apply-token"] = JsonSerializer.SerializeToElement(token),
                            ["confirm"] = JsonSerializer.SerializeToElement(true)
                        }
                    }, CancellationToken.None);
                    Assert.IsFalse(replay.Success);
                    StringAssert.Contains(replay.Message, "already consumed");
                }
            }
            finally { File.Delete(blueprintPath); }
        }

        [TestMethod]
        public async Task Worker_ApiDescriptionMatchesCommittedMigrationProfile()
        {
            using (WorkerProcessTransport transport = await WorkerProcessTransport.StartAsync(ResolveWorkerPath(), TimeSpan.FromSeconds(30), CancellationToken.None))
            {
                var describe = await transport.ExecuteAsync(new CommandInvocation { CommandName = "describe-api" }, CancellationToken.None);
                Assert.IsTrue(describe.Success, describe.Message);
                Assert.AreEqual("V21", describe.Data!.Value.GetProperty("TiaVersion").GetString());
                var diff = await transport.ExecuteAsync(new CommandInvocation { CommandName = "diff-api-profile" }, CancellationToken.None);
                Assert.IsTrue(diff.Success, diff.Message);
                Assert.IsTrue(diff.Data!.Value.GetProperty("Compatible").GetBoolean());
            }
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
