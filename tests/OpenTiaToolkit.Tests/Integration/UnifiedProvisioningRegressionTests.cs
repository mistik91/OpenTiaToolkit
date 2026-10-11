using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenTiaToolkit.Application.Execution;
using OpenTiaToolkit.Contracts.Commands;
using OpenTiaToolkit.Contracts.Outcomes;

namespace OpenTiaToolkit.Tests.Integration;

[TestClass]
[TestCategory("Integration")]
[DoNotParallelize]
public class UnifiedProvisioningRegressionTests
{
    [TestMethod]
    public async Task UnifiedCreation_RepeatedIsolatedPreviewThenIdenticalApplyRetainsSourceSession()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("OPEN_TIA_RUN_UNIFIED_ACCEPTANCE"), "true", StringComparison.OrdinalIgnoreCase))
            Assert.Inconclusive("Set OPEN_TIA_RUN_UNIFIED_ACCEPTANCE=true with a pinned empty archive, registered worker and discovered Unified type identifier.");
        string archive = Required("OPEN_TIA_ACCEPTANCE_ARCHIVE");
        string archiveHash = Required("OPEN_TIA_ACCEPTANCE_ARCHIVE_SHA256");
        string worker = Required("OPEN_TIA_ACCEPTANCE_WORKER_EXE");
        string typeIdentifier = Required("OPEN_TIA_ACCEPTANCE_UNIFIED_TYPE_ID");
        Assert.AreEqual(archiveHash, Hash(archive), "Acceptance archive identity changed.");
        Assert.IsTrue((File.GetAttributes(archive) & FileAttributes.ReadOnly) != 0, "Use an immutable acceptance archive.");
        string root = Path.Combine(Path.GetTempPath(), "OpenTiaToolkit", "UnifiedAcceptance", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using ToolkitSession session = await ToolkitSession.StartAsync(worker, Path.Combine(root, "audit.jsonl"), CancellationToken.None);
        async Task<CommandOutcome> Call(string command, Dictionary<string, object>? arguments = null)
        {
            var values = new Dictionary<string, JsonElement>();
            foreach (var pair in arguments ?? new Dictionary<string, object>()) values[pair.Key] = JsonSerializer.SerializeToElement(pair.Value);
            var result = await session.ExecuteAsync(new CommandInvocation { CommandName = command, Arguments = values }, CancellationToken.None);
            File.WriteAllText(Path.Combine(root, Guid.NewGuid().ToString("N") + "-" + command + ".json"), JsonSerializer.Serialize(result));
            Assert.IsTrue(result.Success, result.Message + " " + result.Error?.Message);
            return result;
        }
        try
        {
            await Call("open-tia-portal", new() { ["with-ui"] = false, ["confirm"] = true });
            await Call("retrieve-project", new() { ["archive-path"] = archive, ["target-directory"] = Path.Combine(root, "Source"), ["confirm"] = true });
            var info = (await Call("get-project-info")).Data!.Value;
            string path = info.GetProperty("Path").GetString()!;
            Assert.IsTrue(info.GetProperty("IsToolkitOwned").GetBoolean());
            Assert.IsTrue(Path.GetFullPath(path).StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
            Assert.AreEqual(0, (await Call("list-devices")).Data!.Value.GetArrayLength(), "Refuse a non-empty acceptance fixture.");
            var operations = new { operations = new[] { new { op = "create-device-with-item", typeIdentifier, deviceName = "AcceptanceUnified", itemName = "AcceptanceUnified" } } };
            for (int repeat = 0; repeat < 2; repeat++)
            {
                var preview = (await Call("apply-hardware-patch", new() { ["operations"] = operations, ["dry-run"] = true })).Data!.Value;
                Assert.IsTrue(preview.GetProperty("PreviewIsolated").GetBoolean());
                Assert.IsTrue(preview.GetProperty("PreviewDiscarded").GetBoolean());
                Assert.IsFalse(preview.GetProperty("Applied").GetBoolean());
                Assert.IsFalse(preview.GetProperty("RolledBack").GetBoolean());
                Assert.AreEqual(0, (await Call("list-devices")).Data!.Value.GetArrayLength());
                Assert.AreEqual(path, (await Call("get-project-info")).Data!.Value.GetProperty("Path").GetString());
            }
            await Call("apply-hardware-patch", new() { ["operations"] = operations, ["dry-run"] = false, ["confirm"] = true });
            Assert.AreEqual(1, (await Call("list-hmis")).Data!.Value.GetArrayLength());
            await Call("get-hardware-tree", new() { ["device-name"] = "AcceptanceUnified" });
            await Call("save-project", new() { ["confirm"] = true });
            await Call("close-tia-portal", new() { ["confirm"] = true });
            await Call("open-tia-portal", new() { ["with-ui"] = false, ["confirm"] = true });
            await Call("open-project", new() { ["project-path"] = path, ["confirm"] = true });
            Assert.AreEqual(1, (await Call("list-hmis")).Data!.Value.GetArrayLength());
            Assert.AreEqual(1, (await Call("list-devices")).Data!.Value.GetArrayLength());
        }
        finally
        {
            try { await Call("close-tia-portal", new() { ["confirm"] = true }); }
            finally { Assert.AreEqual(archiveHash, Hash(archive), "Acceptance archive was modified."); }
        }
    }

    private static string Required(string name) => Environment.GetEnvironmentVariable(name) is string value && !string.IsNullOrWhiteSpace(value)
        ? value : throw new InvalidOperationException(name + " is required for explicit live acceptance.");
    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }
}
