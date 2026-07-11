using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Application.Execution;
using OpenTiaToolkit.Contracts.Commands;

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
