using OpenTiaToolkit.Application.Execution;
using OpenTiaToolkit.Contracts.Commands;
using OpenTiaToolkit.Contracts.Outcomes;
using OpenTiaToolkit.Core.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Tests.Integration;

/// <summary>Opt-in cache qualification through the production host policy and isolated worker.</summary>
[TestClass]
[TestCategory("Integration")]
[DoNotParallelize]
public sealed class UnifiedDiscreteAlarmWorkerCacheIntegrationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    private ToolkitSession? _session;
    private string _runRoot = string.Empty;
    private string _hmiName = string.Empty;
    private int _sequence;
    private bool _ownsPortal;

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task CachedBatch_DryRunApplyStaleHashInverseAndFreshSessionReadback()
    {
        if (!bool.TryParse(Environment.GetEnvironmentVariable("OPEN_TIA_TEST_ALARM_CACHE_DISPOSABLE"), out var disposable) || !disposable)
            Assert.Inconclusive("Set OPEN_TIA_TEST_ALARM_CACHE_DISPOSABLE=true to authorize independent archive retrieval for this campaign.");
        var archive = Path.GetFullPath(Required("OPEN_TIA_TEST_ALARM_CACHE_ARCHIVE_PATH"));
        var worker = Path.GetFullPath(Required("OPEN_TIA_TEST_ALARM_CACHE_WORKER_PATH"));
        _hmiName = Required("OPEN_TIA_TEST_ALARM_CACHE_HMI_NAME");
        Assert.IsTrue(File.Exists(archive), "The baseline archive must exist.");
        Assert.IsTrue(File.Exists(worker), "Register the exact rebuilt worker before running this test.");
        var archiveHash = FileHash(archive);
        var liveRoot = Path.GetFullPath(Environment.GetEnvironmentVariable("OPEN_TIA_TEST_HMI_DISCRETE_ALARM_DISPOSABLE_ROOT") ?? @"C:\OTT\Live");
        _runRoot = Path.Combine(liveRoot, "alarm-cache-" + Guid.NewGuid().ToString("N"));
        Assert.IsFalse(Directory.Exists(_runRoot), "The campaign directory must be new.");
        Directory.CreateDirectory(_runRoot);
        File.WriteAllText(Path.Combine(_runRoot, "environment.json"), JsonSerializer.Serialize(new
        {
            worker,
            workerHash = FileHash(worker),
            archive,
            archiveHash,
            hmiName = _hmiName
        }, JsonOptions));

        HmiDiscreteAlarmSnapshot[] baseline = Array.Empty<HmiDiscreteAlarmSnapshot>();
        string projectPath = string.Empty;
        try
        {
            await StartAsync(worker);
            await SendAsync("retrieve-project", ("archive-path", archive), ("target-directory", Path.Combine(_runRoot, "retrieved")), ("confirm", true));
            var project = Read<ProjectInfo>(await SendAsync("get-project-info"));
            projectPath = Path.GetFullPath(project.Path);
            Assert.IsTrue(project.IsToolkitOwned);
            Assert.IsFalse(project.IsPortalVisible);
            Assert.IsTrue(projectPath.StartsWith(_runRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Refusing to mutate outside the campaign retrieval.");
            await SendAsync("open-unified-hmi", ("hmi-name", _hmiName));
            baseline = await InventoryAsync();
            var targets = baseline.Where(x => x.Validation.All(v => v.Errors.Count == 0 && v.Warnings.Count == 0))
                .Where(x => !string.IsNullOrWhiteSpace(x.AlarmClass) && !string.IsNullOrWhiteSpace(x.RaisedStateTag))
                .GroupBy(x => new { x.AlarmClass, x.RaisedStateTag })
                .Where(x => x.Count() >= 2).Select(x => x.Take(2).ToArray()).FirstOrDefault();
            if (targets == null) Assert.Inconclusive("The disposable fixture needs two valid alarms sharing a class and trigger tag.");
            var culture = targets![0].Texts.Keys.Intersect(targets[1].Texts.Keys, StringComparer.Ordinal).FirstOrDefault();
            if (culture == null) Assert.Inconclusive("The fixture alarms must share an existing alarm-text culture.");

            await CheckLargePreflightAsync(baseline, targets[0], culture!);

            var marker = "OpenTiaToolkit cache qualification " + Guid.NewGuid().ToString("N");
            var patch = WriteDocument("update", targets.Select(x => Request(x, culture!, marker, marker)).ToArray());
            var dryRun = Read<HmiDiscreteAlarmApplyResult>(await ApplyAsync(patch, true));
            Assert.IsTrue(dryRun.Transaction.Created);
            Assert.IsTrue(dryRun.Transaction.RolledBack);
            Assert.IsFalse(dryRun.Transaction.Committed);
            AssertInventory(baseline, await InventoryAsync());

            var applied = Read<HmiDiscreteAlarmApplyResult>(await ApplyAsync(patch, false));
            Assert.IsTrue(applied.Transaction.Committed);
            var after = await InventoryAsync();
            foreach (var target in targets)
            {
                var persisted = after.Single(x => x.Name == target.Name);
                Assert.AreEqual(marker, persisted.Origin);
                Assert.AreEqual(marker, persisted.Texts[culture!].EventText);
                Assert.AreEqual(target.Id, persisted.Id);
            }
            AssertInventory(baseline.Where(x => !targets.Any(t => t.Name == x.Name)).ToArray(), after.Where(x => !targets.Any(t => t.Name == x.Name)).ToArray());

            var stale = Read<HmiDiscreteAlarmApplyResult>(await SendAsync("apply-unified-discrete-alarms", false,
                ("hmi-name", _hmiName), ("input-path", patch), ("dry-run", false), ("apply", true), ("confirm", true)));
            Assert.AreEqual("preflight-blocked", stale.ErrorCode);
            Assert.IsFalse(stale.Transaction.Created);
            AssertInventory(after, await InventoryAsync());

            var collisionRequest = Request(after.Single(x => x.Name == targets[0].Name), culture!, marker, marker);
            collisionRequest["id"] = targets[1].Id;
            var collisionPath = WriteDocument("collision", new[] { collisionRequest });
            var collision = Read<HmiDiscreteAlarmApplyResult>(await SendAsync("apply-unified-discrete-alarms", false,
                ("hmi-name", _hmiName), ("input-path", collisionPath), ("dry-run", true)));
            Assert.AreEqual("preflight-blocked", collision.ErrorCode);
            Assert.IsFalse(collision.Transaction.Created);
            Assert.IsTrue(collision.Alarms.Single().Errors.Any(x => x.Contains("already in use")));
            AssertInventory(after, await InventoryAsync());

            var inverse = WriteDocument("inverse", targets.Select(original => Request(
                after.Single(x => x.Name == original.Name), culture!, original.Origin, original.Texts[culture!].EventText)).ToArray());
            Assert.IsTrue(Read<HmiDiscreteAlarmApplyResult>(await ApplyAsync(inverse, true)).Transaction.RolledBack);
            Assert.IsTrue(Read<HmiDiscreteAlarmApplyResult>(await ApplyAsync(inverse, false)).Transaction.Committed);
            AssertInventory(baseline, await InventoryAsync());
        }
        finally
        {
            await CloseAsync();
            Assert.AreEqual(archiveHash, FileHash(archive), "The baseline archive changed.");
        }

        try
        {
            await StartAsync(worker);
            await SendAsync("open-project", ("project-path", projectPath));
            var reopened = Read<ProjectInfo>(await SendAsync("get-project-info"));
            Assert.AreEqual(projectPath, Path.GetFullPath(reopened.Path), true);
            Assert.IsTrue(reopened.IsToolkitOwned);
            await SendAsync("open-unified-hmi", ("hmi-name", _hmiName));
            AssertInventory(baseline, await InventoryAsync());
        }
        finally
        {
            await CloseAsync();
            Assert.AreEqual(archiveHash, FileHash(archive));
        }
    }

    private async Task StartAsync(string worker)
    {
        _session = await ToolkitSession.StartAsync(worker, Path.Combine(_runRoot, "audit.jsonl"), CancellationToken.None);
        await SendAsync("version");
        await SendAsync("list-instances");
        await SendAsync("open-tia-portal", ("with-ui", false));
        _ownsPortal = true;
    }

    private async Task CloseAsync()
    {
        if (_session == null) return;
        try
        {
            if (_ownsPortal)
            {
                try { await SendAsync("close-project", ("save", false), ("confirm", true)); }
                finally { await SendAsync("close-tia-portal", ("confirm", true)); }
            }
        }
        finally { _session.Dispose(); _session = null; _ownsPortal = false; }
    }

    private Task<CommandOutcome> ApplyAsync(string path, bool dryRun) => SendAsync("apply-unified-discrete-alarms",
        ("hmi-name", _hmiName), ("input-path", path), ("dry-run", dryRun), ("apply", !dryRun), ("confirm", !dryRun), ("fail-on-warning", true));

    private async Task CheckLargePreflightAsync(HmiDiscreteAlarmSnapshot[] baseline, HmiDiscreteAlarmSnapshot template, string culture)
    {
        const int count = 1936;
        var prefix = "OTT_Cache_Preflight_" + Guid.NewGuid().ToString("N") + "_";
        var usedIds = new HashSet<uint>(baseline.Select(x => x.Id));
        var ids = Enumerable.Range(1, count + usedIds.Count).Select(x => (uint)x).Where(x => !usedIds.Contains(x)).Take(count).ToArray();
        var parameters = baseline.SelectMany(x => x.AlarmParameterTags).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).Take(2).ToArray();
        var requests = Enumerable.Range(0, count).Select(index =>
        {
            var request = Request(template, culture, template.Origin, template.Texts[culture].EventText);
            request["name"] = prefix + index;
            request["id"] = ids[index];
            request["raisedStateTagBitNumber"] = template.RaisedStateTagBitNumber;
            request["triggerMode"] = template.TriggerMode;
            request["alarmParameterTags"] = parameters;
            request.Remove("expectedHash");
            return request;
        }).ToArray();
        // Real reference reads still run for every request, but missing expected
        // targets reject the entire preflight before any Siemens transaction.
        var path = WriteDocument("bulk-preflight", requests);
        var result = Read<HmiDiscreteAlarmApplyResult>(await SendAsync("apply-unified-discrete-alarms", false,
            ("hmi-name", _hmiName), ("input-path", path), ("dry-run", true), ("fail-on-warning", true)));
        Assert.AreEqual("preflight-blocked", result.ErrorCode);
        Assert.IsFalse(result.Transaction.Created);
        Assert.AreEqual(count, result.Alarms.Count);
        Assert.IsTrue(result.Alarms.All(x => x.Errors.Count == 1 && x.Errors[0].Contains("expectedState=present")));
        AssertInventory(baseline, await InventoryAsync());
    }

    private async Task<HmiDiscreteAlarmSnapshot[]> InventoryAsync() => Read<HmiDiscreteAlarmSnapshot[]>(
        await SendAsync("list-unified-discrete-alarms", ("hmi-name", _hmiName), ("include-validation", true)));

    private Task<CommandOutcome> SendAsync(string command, params (string Name, object Value)[] args) => SendAsync(command, true, args);

    private async Task<CommandOutcome> SendAsync(string command, bool expectedSuccess, params (string Name, object Value)[] args)
    {
        var timer = Stopwatch.StartNew();
        var outcome = await _session!.ExecuteAsync(new CommandInvocation
        {
            CommandName = command,
            Arguments = args.ToDictionary(x => x.Name, x => JsonSerializer.SerializeToElement(x.Value))
        }, CancellationToken.None);
        timer.Stop();
        var path = Path.Combine(_runRoot, $"{++_sequence:D3}-{command}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new { command, elapsedMilliseconds = timer.Elapsed.TotalMilliseconds, outcome }, JsonOptions));
        TestContext.AddResultFile(path);
        TestContext.WriteLine($"{command}: success={outcome.Success}, {timer.Elapsed.TotalMilliseconds:F0} ms");
        Assert.AreEqual(expectedSuccess, outcome.Success, outcome.Message);
        return outcome;
    }

    private static T Read<T>(CommandOutcome outcome) => JsonSerializer.Deserialize<T>(outcome.Data!.Value.GetRawText(), JsonOptions)!;

    private static Dictionary<string, object?> Request(HmiDiscreteAlarmSnapshot current, string culture, string origin, string text) => new()
    {
        ["name"] = current.Name,
        ["expectedState"] = "present",
        ["expectedHash"] = current.ContentHash,
        ["id"] = current.Id,
        ["alarmClass"] = current.AlarmClass,
        ["raisedStateTag"] = current.RaisedStateTag,
        ["origin"] = origin,
        ["texts"] = new Dictionary<string, object> { [culture] = new { eventText = text } }
    };

    private string WriteDocument(string name, Dictionary<string, object?>[] alarms)
    {
        var path = Path.Combine(_runRoot, name + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(new { schemaVersion = "1.0", alarms }, JsonOptions));
        return path;
    }

    private static void AssertInventory(HmiDiscreteAlarmSnapshot[] expected, HmiDiscreteAlarmSnapshot[] actual) =>
        CollectionAssert.AreEquivalent(expected.Select(x => x.Name + ":" + x.ContentHash).ToArray(), actual.Select(x => x.Name + ":" + x.ContentHash).ToArray());

    private static string FileHash(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
    }

    private static string Required(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value)) Assert.Inconclusive($"Set {name} for the disposable worker cache campaign.");
        return value!;
    }
}
