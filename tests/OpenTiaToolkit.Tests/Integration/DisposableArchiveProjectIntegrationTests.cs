using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Application.Execution;
using OpenTiaToolkit.Contracts.Commands;
using OpenTiaToolkit.Contracts.Outcomes;

namespace OpenTiaToolkit.Tests.Integration
{
    [TestClass]
    [TestCategory("Integration")]
    public class DisposableArchiveProjectIntegrationTests
    {
        [TestMethod]
        public async Task ArchivedProject_IsRetrievedIntoDisposableDirectoryAndInspected()
        {
            string? archive = Environment.GetEnvironmentVariable("OPEN_TIA_TEST_ARCHIVE_PATH");
            if (string.IsNullOrWhiteSpace(archive) || !File.Exists(archive))
                Assert.Inconclusive("Set OPEN_TIA_TEST_ARCHIVE_PATH to a disposable V21 project archive.");

            // TIA V21 limits the full retrieved project path to 143 characters.
            string target = Path.Combine(Path.GetTempPath(), "OTT", "A", Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(target);
            string worker = ToolkitSession.ResolveWorkerPath(typeof(DisposableArchiveProjectIntegrationTests).Assembly.Location);
            try
            {
                using (ToolkitSession session = await ToolkitSession.StartAsync(
                    worker,
                    Path.Combine(target, "audit.jsonl"),
                    CancellationToken.None))
                {
                    AssertSuccess(await Execute(session, "open-tia-portal", new Dictionary<string, object>
                    {
                        ["with-ui"] = false
                    }));
                    AssertSuccess(await Execute(session, "retrieve-project", new Dictionary<string, object>
                    {
                        ["archive-path"] = archive!,
                        ["target-directory"] = target,
                        ["upgrade"] = false,
                        ["confirm"] = true
                    }));
                    AssertSuccess(await Execute(session, "get-project-info"));
                    AssertSuccess(await Execute(session, "close-project", new Dictionary<string, object>
                    {
                        ["confirm"] = true
                    }));
                    AssertSuccess(await Execute(session, "close-tia-portal", new Dictionary<string, object>
                    {
                        ["confirm"] = true
                    }));
                }
            }
            finally
            {
                if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
            }
        }

        private static Task<CommandOutcome> Execute(
            ToolkitSession session,
            string command,
            IReadOnlyDictionary<string, object>? arguments = null)
        {
            var serialized = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            if (arguments != null)
                foreach (KeyValuePair<string, object> pair in arguments)
                    serialized[pair.Key] = JsonSerializer.SerializeToElement(pair.Value);
            return session.ExecuteAsync(new CommandInvocation { CommandName = command, Arguments = serialized }, CancellationToken.None);
        }

        private static void AssertSuccess(CommandOutcome outcome)
        {
            Assert.IsTrue(outcome.Success, $"{outcome.Error?.Code}: {outcome.Message}");
        }
    }
}
