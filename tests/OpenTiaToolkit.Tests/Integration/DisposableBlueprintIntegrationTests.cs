using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Application.Execution;
using OpenTiaToolkit.Contracts.Commands;

namespace OpenTiaToolkit.Tests.Integration;

[TestClass]
[TestCategory("Integration")]
public class DisposableBlueprintIntegrationTests
{
    [TestMethod]
    public async Task Blueprint_CreatesArchivesAndClosesDisposableProject()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("OPEN_TIA_RUN_DISPOSABLE_BLUEPRINT"), "true", StringComparison.OrdinalIgnoreCase))
            Assert.Inconclusive("Set OPEN_TIA_RUN_DISPOSABLE_BLUEPRINT=true to run the live disposable blueprint lifecycle.");

        string id = Guid.NewGuid().ToString("N").Substring(0, 8);
        string root = Path.Combine(Path.GetTempPath(), "OTT", "B", id);
        Directory.CreateDirectory(root);
        string projectPath = Path.Combine(root, "Blueprint.ap21");
        string archivePath = Path.Combine(root, "Blueprint.zap21");
        string blueprintPath = Path.Combine(root, "blueprint.json");
        string auditPath = Path.Combine(root, "audit.jsonl");
        File.WriteAllText(blueprintPath, JsonSerializer.Serialize(new
        {
            schemaVersion = "1.0",
            name = "Disposable lifecycle",
            requiredWorker = "V21",
            steps = new object[]
            {
                new { id = "open", command = "open-tia-portal", arguments = new Dictionary<string, object> { ["with-ui"] = false } },
                new { id = "create", command = "create-project", arguments = new Dictionary<string, object> { ["project-path"] = projectPath } },
                new { id = "archive", command = "archive-project", arguments = new Dictionary<string, object> { ["archive-path"] = archivePath, ["mode"] = "Compressed" } },
                new { id = "close", command = "close-tia-portal", arguments = new Dictionary<string, object>() }
            }
        }));

        try
        {
            using ToolkitSession session = await ToolkitSession.StartAsync(ToolkitSession.ResolveWorkerPath(typeof(DisposableBlueprintIntegrationTests).Assembly.Location), auditPath, CancellationToken.None);
            var plan = await session.ExecuteAsync(Invocation("plan-blueprint", new Dictionary<string, object> { ["blueprint-path"] = blueprintPath }), CancellationToken.None);
            Assert.IsTrue(plan.Success, plan.Message);
            string token = plan.Data!.Value.GetProperty("ApplyToken").GetString()!;
            var apply = await session.ExecuteAsync(Invocation("apply-blueprint", new Dictionary<string, object> { ["apply-token"] = token, ["confirm"] = true }), CancellationToken.None);
            Assert.IsTrue(apply.Success, apply.Message);
            Assert.IsTrue(File.Exists(archivePath), "Blueprint did not produce the archive.");
            Assert.IsTrue(new FileInfo(archivePath).Length > 0, "Blueprint archive was empty.");
            var replay = await session.ExecuteAsync(Invocation("apply-blueprint", new Dictionary<string, object> { ["apply-token"] = token, ["confirm"] = true }), CancellationToken.None);
            Assert.IsFalse(replay.Success, "Blueprint apply token unexpectedly replayed.");
            StringAssert.Contains(File.ReadAllText(auditPath), "[REDACTED]");
        }
        finally
        {
            DeleteDirectoryWithRetry(root);
        }
    }

    private static CommandInvocation Invocation(string command, IReadOnlyDictionary<string, object> arguments)
    {
        var serialized = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, object> pair in arguments) serialized[pair.Key] = JsonSerializer.SerializeToElement(pair.Value);
        return new CommandInvocation { CommandName = command, Arguments = serialized };
    }

    private static void DeleteDirectoryWithRetry(string path)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            if (!Directory.Exists(path)) return;
            try { Directory.Delete(path, true); return; }
            catch (IOException) when (attempt < 19) { Thread.Sleep(500); }
            catch (UnauthorizedAccessException) when (attempt < 19) { Thread.Sleep(500); }
        }
    }
}
