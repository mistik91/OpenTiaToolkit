using OpenTiaToolkit.Application.Execution;
using OpenTiaToolkit.Contracts.Commands;
using OpenTiaToolkit.Contracts.Outcomes;
using OpenTiaToolkit.Core.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Tests.Integration;

[TestClass]
[TestCategory("Integration")]
[DoNotParallelize]
public sealed class PlcBrowseWorkerIntegrationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task FilteredBrowseAndSearch_PreserveUnfilteredMetadataThroughWorker()
    {
        if (!bool.TryParse(Environment.GetEnvironmentVariable("OPEN_TIA_TEST_ALARM_CACHE_DISPOSABLE"), out var disposable) || !disposable)
            Assert.Inconclusive("Opt in to the disposable worker qualification campaign first.");
        var archive = Path.GetFullPath(Required("OPEN_TIA_TEST_ALARM_CACHE_ARCHIVE_PATH"));
        var worker = Path.GetFullPath(Required("OPEN_TIA_TEST_ALARM_CACHE_WORKER_PATH"));
        Assert.IsTrue(File.Exists(archive));
        Assert.IsTrue(File.Exists(worker));
        var archiveHash = Hash(archive);
        var parent = Path.GetFullPath(Environment.GetEnvironmentVariable("OPEN_TIA_TEST_HMI_DISCRETE_ALARM_DISPOSABLE_ROOT") ?? @"C:\OTT\Live");
        var root = Path.Combine(parent, "plc-read-" + Guid.NewGuid().ToString("N"));
        Assert.IsFalse(Directory.Exists(root));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "environment.json"), JsonSerializer.Serialize(new { worker, archive, archiveHash }, JsonOptions));
        using var session = await ToolkitSession.StartAsync(worker, Path.Combine(root, "audit.jsonl"), CancellationToken.None);
        var ownsPortal = false;
        var sequence = 0;

        async Task<CommandOutcome> Send(string command, params (string Name, object Value)[] args)
        {
            var timer = Stopwatch.StartNew();
            var outcome = await session.ExecuteAsync(new CommandInvocation
            {
                CommandName = command,
                Arguments = args.ToDictionary(x => x.Name, x => JsonSerializer.SerializeToElement(x.Value))
            }, CancellationToken.None);
            timer.Stop();
            var path = Path.Combine(root, $"{++sequence:D3}-{command}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(new { command, elapsedMilliseconds = timer.Elapsed.TotalMilliseconds, outcome }, JsonOptions));
            TestContext.AddResultFile(path);
            Assert.IsTrue(outcome.Success, outcome.Message);
            return outcome;
        }

        try
        {
            await Send("version");
            await Send("list-instances");
            await Send("open-tia-portal", ("with-ui", false));
            ownsPortal = true;
            await Send("retrieve-project", ("archive-path", archive), ("target-directory", Path.Combine(root, "retrieved")), ("confirm", true));
            var project = Read<ProjectInfo>(await Send("get-project-info"));
            Assert.IsTrue(project.IsToolkitOwned);
            Assert.IsFalse(project.IsPortalVisible);
            Assert.IsTrue(Path.GetFullPath(project.Path).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
            var plcs = Read<PlcInfo[]>(await Send("list-plcs"));
            if (plcs.Length != 1) Assert.Inconclusive("This fixture must contain exactly one PLC for unambiguous read qualification.");
            var plc = plcs[0].Name;
            await Send("open-plc", ("plc-name", plc));
            var all = Read<BlockInfo[]>(await Send("get-blocks", ("plc-name", plc)));
            if (all.Length == 0) Assert.Inconclusive("The fixture must contain a readable PLC block.");
            var target = all[0];
            var missing = "OTT_NoBlock_" + Guid.NewGuid().ToString("N");

            Assert.AreEqual(0, Read<BlockInfo[]>(await Send("get-blocks", ("plc-name", plc), ("filter", "^" + missing + "$"))).Length);
            Same(all.Where(x => string.Equals(x.Name, target.Name, StringComparison.OrdinalIgnoreCase)).ToArray(),
                Read<BlockInfo[]>(await Send("get-blocks", ("plc-name", plc), ("filter", "^" + Regex.Escape(target.Name) + "$"))));
            Same(all, Read<BlockInfo[]>(await Send("get-blocks", ("plc-name", plc), ("filter", "["))));
            Assert.AreEqual(0, Read<BlockInfo[]>(await Send("search-blocks", ("plc-name", plc), ("pattern", missing), ("search-in", "name"))).Length);

            foreach (var mode in new[] { "name", "comment", "both" })
            {
                var expected = all.Where(x =>
                    (mode != "comment" && x.Name.IndexOf(target.Name, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (mode != "name" && x.Comment?.IndexOf(target.Name, StringComparison.OrdinalIgnoreCase) >= 0)).ToArray();
                Same(expected, Read<BlockInfo[]>(await Send("search-blocks", ("plc-name", plc), ("pattern", target.Name), ("search-in", mode))));
            }
            Same(all.Where(x => x.Name.IndexOf(target.Name, StringComparison.OrdinalIgnoreCase) >= 0 && string.Equals(x.Type, target.Type, StringComparison.OrdinalIgnoreCase)).ToArray(),
                Read<BlockInfo[]>(await Send("search-blocks", ("plc-name", plc), ("pattern", target.Name), ("search-in", "name"), ("filter-type", target.Type))));
            Same(all, Read<BlockInfo[]>(await Send("get-blocks", ("plc-name", plc))));
        }
        finally
        {
            try
            {
                if (ownsPortal)
                {
                    try { await Send("close-project", ("save", false), ("confirm", true)); }
                    finally { await Send("close-tia-portal", ("confirm", true)); }
                }
            }
            finally { Assert.AreEqual(archiveHash, Hash(archive)); }
        }
    }

    private static T Read<T>(CommandOutcome outcome) => JsonSerializer.Deserialize<T>(outcome.Data!.Value.GetRawText(), JsonOptions)!;
    private static void Same(BlockInfo[] expected, BlockInfo[] actual) =>
        Assert.AreEqual(JsonSerializer.Serialize(expected, JsonOptions), JsonSerializer.Serialize(actual, JsonOptions));
    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(stream));
    }
    private static string Required(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value)) Assert.Inconclusive($"Set {name} for disposable worker qualification.");
        return value!;
    }
}
