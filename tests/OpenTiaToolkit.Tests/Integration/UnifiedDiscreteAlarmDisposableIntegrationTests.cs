using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Tests.Integration;

/// <summary>
/// Opt-in Tier 4 campaign. The dedicated path and disposable marker prevent this test from
/// ever falling back to a developer's normal HMI project.
/// </summary>
[TestClass]
[TestCategory("Integration")]
[DoNotParallelize]
public sealed class UnifiedDiscreteAlarmDisposableIntegrationTests
{
    private ILoggerFactory? _loggerFactory;
    private IPortalService? _portal;
    private IHmiService? _hmi;
    private string _projectPath = string.Empty;
    private string _hmiName = string.Empty;
    private string _alarmClass = string.Empty;
    private string _raisedTag = string.Empty;
    private string _culture1 = string.Empty;
    private string _culture2 = string.Empty;
    private uint _idBase;

    [TestInitialize]
    public async Task Initialize()
    {
        _projectPath = Environment.GetEnvironmentVariable("OPEN_TIA_TEST_HMI_DISCRETE_ALARM_PROJECT_PATH") ?? string.Empty;
        if (!bool.TryParse(Environment.GetEnvironmentVariable("OPEN_TIA_TEST_HMI_DISCRETE_ALARM_DISPOSABLE"), out var disposable) || !disposable)
            Assert.Inconclusive("Set OPEN_TIA_TEST_HMI_DISCRETE_ALARM_DISPOSABLE=true only for an independently retrieved disposable project.");
        if (string.IsNullOrWhiteSpace(_projectPath) || !File.Exists(_projectPath))
            Assert.Inconclusive("Set OPEN_TIA_TEST_HMI_DISCRETE_ALARM_PROJECT_PATH to an existing disposable .ap21 project.");
        var allowedRoot = Path.GetFullPath(Environment.GetEnvironmentVariable("OPEN_TIA_TEST_HMI_DISCRETE_ALARM_DISPOSABLE_ROOT") ?? @"C:\OTT\Live");
        var fullProjectPath = Path.GetFullPath(_projectPath);
        if (!fullProjectPath.StartsWith(allowedRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            Assert.Inconclusive($"Disposable project must be below the explicit live-test root '{allowedRoot}'.");

        _hmiName = Required("OPEN_TIA_TEST_HMI_DISCRETE_ALARM_HMI_NAME");
        _alarmClass = Required("OPEN_TIA_TEST_HMI_DISCRETE_ALARM_CLASS");
        _raisedTag = Required("OPEN_TIA_TEST_HMI_DISCRETE_ALARM_RAISED_TAG");
        _culture1 = Required("OPEN_TIA_TEST_HMI_DISCRETE_ALARM_CULTURE_1");
        _culture2 = Required("OPEN_TIA_TEST_HMI_DISCRETE_ALARM_CULTURE_2");
        if (!uint.TryParse(Required("OPEN_TIA_TEST_HMI_DISCRETE_ALARM_ID_BASE"), NumberStyles.None, CultureInfo.InvariantCulture, out _idBase) || _idBase > uint.MaxValue - 20)
            Assert.Inconclusive("OPEN_TIA_TEST_HMI_DISCRETE_ALARM_ID_BASE must leave room for 20 test IDs.");

        _loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        _portal = TestHelpers.CreatePortalService(_loggerFactory);
        _hmi = new HmiService(_portal, _loggerFactory.CreateLogger<HmiService>());
        Assert.IsTrue(await _portal.OpenTiaPortalAsync(withUi: false), "Failed to start a toolkit-owned hidden TIA Portal instance for the disposable campaign.");
        Assert.IsTrue(await TestHelpers.OpenProjectAsync(_portal, fullProjectPath, _loggerFactory.CreateLogger<UnifiedDiscreteAlarmDisposableIntegrationTests>()));
        var projectInfo = await _portal.GetProjectInfoAsync();
        Assert.IsNotNull(projectInfo, "The disposable project was opened but its verified project information is unavailable.");
        Assert.IsTrue(projectInfo!.IsToolkitOwned, "Refusing to mutate a project whose lifecycle is not owned by this test.");
        Assert.IsFalse(projectInfo.IsPortalVisible, "The disposable campaign must run in a dedicated hidden TIA Portal instance.");
        Assert.IsTrue(
            string.Equals(fullProjectPath, Path.GetFullPath(projectInfo.Path), StringComparison.OrdinalIgnoreCase),
            $"Refusing to mutate unexpected project '{projectInfo.Path}'; expected '{fullProjectPath}'.");
        Assert.IsTrue(await _hmi.OpenUnifiedHmiAsync(_hmiName), $"Unified HMI '{_hmiName}' was not found in the disposable project.");
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_portal?.IsConnected == true)
        {
            try { await _portal.CloseProjectAsync(saveChanges: false); } catch { }
            try { await _portal.CloseTiaPortalAsync(); } catch { }
        }
        _portal?.Dispose();
        _loggerFactory?.Dispose();
    }

    [TestMethod]
    public async Task CreateUpdateIdempotenceBatchRollbackAndGuardedDelete_RoundTrip()
    {
        var suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
        var primary = "OTT_Discrete_" + suffix;
        var batchA = primary + "_A";
        var batchB = primary + "_B";
        var rollbackA = primary + "_RollbackA";
        var rollbackB = primary + "_RollbackB";
        var baseline = await _hmi!.ListDiscreteAlarmsAsync(false);
        Assert.IsFalse(baseline.Any(x => x.Name.StartsWith(primary, StringComparison.Ordinal)));
        try
        {
            using (var create = Document(new[] { Alarm(primary, _idBase, "absent", null, "Created", _culture1, _culture2) }))
            {
                var dryRun = await _hmi.ApplyDiscreteAlarmsAsync(create.Path, true, true);
                Assert.IsTrue(dryRun.Success, dryRun.Message);
                Assert.IsTrue(dryRun.Transaction.RolledBack);
                Assert.IsNull(await _hmi.GetDiscreteAlarmAsync(primary));
                var applied = await _hmi.ApplyDiscreteAlarmsAsync(create.Path, false, true);
                Assert.IsTrue(applied.Success, applied.Message);
            }

            var created = await _hmi.GetDiscreteAlarmAsync(primary, true);
            Assert.IsNotNull(created);
            Assert.AreEqual("Created " + _culture1, created!.Texts[_culture1].EventText);
            Assert.AreEqual("Created " + _culture2, created.Texts[_culture2].EventText);

            using (var update = Document(new[] { Alarm(primary, null, "present", created.ContentHash, "Updated", _culture1, _culture2) }))
            {
                var updatedResult = await _hmi.ApplyDiscreteAlarmsAsync(update.Path, false, true);
                Assert.IsTrue(updatedResult.Success, updatedResult.Message);
            }
            var updated = await _hmi.GetDiscreteAlarmAsync(primary, true);
            Assert.IsNotNull(updated);
            Assert.AreEqual("Updated " + _culture2, updated!.Texts[_culture2].EventText);

            using (var unchanged = Document(new[] { Alarm(primary, null, "present", updated.ContentHash, "Updated", _culture1, _culture2) }))
            {
                var unchangedResult = await _hmi.ApplyDiscreteAlarmsAsync(unchanged.Path, false, true);
                Assert.IsTrue(unchangedResult.Success, unchangedResult.Message);
                Assert.AreEqual("Unchanged", unchangedResult.Alarms.Single().Status);
            }

            using (var batch = Document(new[]
            {
                Alarm(batchA, _idBase + 1, "absent", null, "Batch A", _culture1, _culture2),
                Alarm(batchB, _idBase + 2, "absent", null, "Batch B", _culture1, _culture2)
            }))
            {
                var batchResult = await _hmi.ApplyDiscreteAlarmsAsync(batch.Path, false, true);
                Assert.IsTrue(batchResult.Success, batchResult.Message);
            }
            Assert.IsNotNull(await _hmi.GetDiscreteAlarmAsync(batchA));
            Assert.IsNotNull(await _hmi.GetDiscreteAlarmAsync(batchB));

            var failingSecond = Alarm(rollbackB, _idBase + 4, "absent", null, "Rollback B", _culture1, _culture2);
            failingSecond["raisedStateTagBitNumber"] = uint.MaxValue;
            failingSecond["acknowledgmentStateTag"] = _raisedTag;
            failingSecond["acknowledgmentStateTagBitNumber"] = int.MinValue;
            using (var rollback = Document(new[]
            {
                Alarm(rollbackA, _idBase + 3, "absent", null, "Rollback A", _culture1, _culture2),
                failingSecond
            }))
            {
                var rollbackResult = await _hmi.ApplyDiscreteAlarmsAsync(rollback.Path, false, true);
                Assert.IsFalse(rollbackResult.Success);
                Assert.IsTrue(rollbackResult.Transaction.RolledBack);
            }
            Assert.IsNull(await _hmi.GetDiscreteAlarmAsync(rollbackA));
            Assert.IsNull(await _hmi.GetDiscreteAlarmAsync(rollbackB));

            foreach (var name in new[] { primary, batchA, batchB })
            {
                var alarm = await _hmi.GetDiscreteAlarmAsync(name, true);
                Assert.IsNotNull(alarm);
                var deleteDryRun = await _hmi.DeleteDiscreteAlarmAsync(name, alarm!.ContentHash, true, false, false);
                Assert.IsTrue(deleteDryRun.Success, deleteDryRun.Message);
                Assert.IsNotNull(await _hmi.GetDiscreteAlarmAsync(name));
                var deleted = await _hmi.DeleteDiscreteAlarmAsync(name, alarm.ContentHash, false, true, false);
                Assert.IsTrue(deleted.Deleted, deleted.Message);
                Assert.IsNull(await _hmi.GetDiscreteAlarmAsync(name));
            }

            Assert.IsTrue(await _portal!.SaveProjectAsync(), "Failed to save the cleaned disposable project before reopen verification.");
            Assert.IsTrue(await _portal.CloseProjectAsync(saveChanges: false));
            Assert.IsTrue(await TestHelpers.OpenProjectAsync(_portal, _projectPath, _loggerFactory!.CreateLogger<UnifiedDiscreteAlarmDisposableIntegrationTests>()));
            Assert.IsTrue(await _hmi.OpenUnifiedHmiAsync(_hmiName));
            Assert.IsNull(await _hmi.GetDiscreteAlarmAsync(primary));
            Assert.IsNull(await _hmi.GetDiscreteAlarmAsync(batchA));
            Assert.IsNull(await _hmi.GetDiscreteAlarmAsync(batchB));
            var finalInventory = await _hmi.ListDiscreteAlarmsAsync(false);
            CollectionAssert.AreEquivalent(baseline.Select(x => x.ContentHash).ToArray(), finalInventory.Select(x => x.ContentHash).ToArray());
        }
        finally
        {
            foreach (var name in new[] { primary, batchA, batchB, rollbackA, rollbackB })
            {
                var alarm = await _hmi.GetDiscreteAlarmAsync(name, false);
                if (alarm != null) await _hmi.DeleteDiscreteAlarmAsync(name, alarm.ContentHash, false, true, true);
            }
        }
    }

    private Dictionary<string, object?> Alarm(string name, uint? id, string expectedState, string? expectedHash, string text, string culture1, string culture2)
    {
        var alarm = new Dictionary<string, object?>
        {
            ["name"] = name,
            ["expectedState"] = expectedState,
            ["expectedHash"] = expectedHash,
            ["raisedStateTag"] = _raisedTag,
            ["raisedStateTagBitNumber"] = 0,
            ["triggerMode"] = "OnRisingEdge",
            ["alarmClass"] = _alarmClass,
            ["texts"] = new Dictionary<string, object>
            {
                [culture1] = new { eventText = text + " " + culture1, infoText = "OpenTiaToolkit disposable integration test" },
                [culture2] = new { eventText = text + " " + culture2, infoText = "OpenTiaToolkit disposable integration test" }
            }
        };
        if (id.HasValue) alarm["id"] = id.Value;
        return alarm;
    }

    private static TempDocument Document(object alarms) => new(JsonSerializer.Serialize(new { schemaVersion = "1.0", alarms }));
    private static string Required(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value)) Assert.Inconclusive($"Set {name} for the disposable discrete-alarm campaign.");
        return value!;
    }

    private sealed class TempDocument : IDisposable
    {
        public TempDocument(string json) { Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ott-discrete-live-" + Guid.NewGuid().ToString("N") + ".json"); File.WriteAllText(Path, json); }
        public string Path { get; }
        public void Dispose() { if (File.Exists(Path)) File.Delete(Path); }
    }
}
