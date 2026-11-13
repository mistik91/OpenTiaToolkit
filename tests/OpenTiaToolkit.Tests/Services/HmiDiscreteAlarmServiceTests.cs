using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Tests.Services;

[TestClass]
public sealed class HmiDiscreteAlarmServiceTests
{
    [TestMethod]
    public async Task LargeBatch_ReusesClassCultureParameterReadsAndSnapshotIds()
    {
        const int count = 1936;
        var access = new FakeAccess();
        using var input = TempDocument(JsonSerializer.Serialize(new
        {
            schemaVersion = "1.0",
            alarms = Enumerable.Range(0, count).Select(i => new
            {
                name = $"Alarm_{i}",
                expectedState = "absent",
                id = 1000 + i,
                alarmClass = "Error",
                raisedStateTag = $"Motor1_Status_{i}",
                raisedStateTagBitNumber = 0,
                triggerMode = "OnRisingEdge",
                alarmParameterTags = new[] { "Motor1_Speed", "Motor1_Current" },
                texts = new Dictionary<string, object>
                {
                    ["en-US"] = new { eventText = "Alarm" },
                    ["it-IT"] = new { eventText = "Allarme" }
                }
            })
        }));
        var result = await new HmiDiscreteAlarmService(access).ApplyAsync(input.Path, true, true);
        Assert.IsTrue(result.Success, result.Message);
        Assert.IsTrue(result.Transaction.RolledBack);
        Assert.AreEqual(count, result.Alarms.Count);
        Assert.AreEqual(1, access.ClassReads);
        Assert.AreEqual(2, access.CultureReads);
        Assert.AreEqual(count + 2, access.TagReads);
        Assert.AreEqual(0, access.IdReads);
        Assert.AreEqual(0, access.List(false).Count);
    }

    [DataTestMethod]
    [DataRow("alarmClass")]
    [DataRow("tag")]
    [DataRow("culture")]
    public async Task LookupCache_RefreshesHitsAndMissesForEveryBatch(string reference)
    {
        var access = new FakeAccess();
        var service = new HmiDiscreteAlarmService(access);
        using var input = Batch(CreateRequest("A", 1001), CreateRequest("B", 1002));
        foreach (var available in new[] { false, true, false })
        {
            access.SetReferenceAvailability(reference, available);
            var result = await service.ApplyAsync(input.Path, true, true);
            Assert.AreEqual(available, result.Success, result.Message);
            Assert.AreEqual(available, result.Transaction.Created);
            if (available) Assert.IsTrue(result.Transaction.RolledBack);
            else
            {
                Assert.AreEqual("preflight-blocked", result.ErrorCode);
                Assert.IsTrue(result.Alarms.All(x => x.Errors.Count == 1));
            }
            Assert.AreEqual(0, access.List(false).Count);
        }
        Assert.AreEqual(3, access.ClassReads);
        Assert.AreEqual(3, access.TagReads);
        Assert.AreEqual(3, access.CultureReads);
    }

    [TestMethod]
    public async Task SnapshotIdIndex_RejectsAnUnrelatedExistingAlarm()
    {
        var existing = Snapshot("Unrelated"); existing.Id = 1001;
        var access = new FakeAccess(existing);
        using var input = TempDocument(CreateJson("A"));
        var collision = await new HmiDiscreteAlarmService(access).ApplyAsync(input.Path, true, true);
        Assert.IsFalse(collision.Success);
        Assert.IsFalse(collision.Transaction.Created);
        StringAssert.Contains(string.Join(" ", collision.Alarms.SelectMany(x => x.Errors)), "already in use");
        Assert.AreEqual(0, access.IdReads);
        Assert.AreEqual(HmiDiscreteAlarmService.Hash(existing), HmiDiscreteAlarmService.Hash(access.FindExact(existing.Name, false)!));
    }

    [DataTestMethod]
    [DataRow("alarmClass", "error")]
    [DataRow("raisedStateTag", "motor1_Status")]
    [DataRow("texts", "en-us")]
    public async Task LookupCache_DoesNotMergeCaseDifferentNames(string property, string alternate)
    {
        // The cache must preserve the access boundary's answer for each exact key.
        var second = CreateRequest("B", 1002);
        second[property] = property == "texts"
            ? new Dictionary<string, object> { [alternate] = new { eventText = "Alarm" } }
            : alternate;
        var access = new FakeAccess();
        using var input = Batch(CreateRequest("A", 1001), second);

        var result = await new HmiDiscreteAlarmService(access).ApplyAsync(input.Path, true, true);

        Assert.AreEqual("preflight-blocked", result.ErrorCode);
        Assert.IsFalse(result.Transaction.Created);
        Assert.AreEqual(0, result.Alarms[0].Errors.Count);
        StringAssert.Contains(result.Alarms[1].Errors.Single(), alternate);
        Assert.AreEqual(property == "alarmClass" ? 2 : 1, access.ClassReads);
        Assert.AreEqual(property == "raisedStateTag" ? 2 : 1, access.TagReads);
        Assert.AreEqual(property == "texts" ? 2 : 1, access.CultureReads);
    }

    [DataTestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task LookupCache_SharesTagReadsAcrossAllRolesAndPreservesErrors(bool available)
    {
        var requests = new[] { CreateRequest("A", 1001), CreateRequest("B", 1002) };
        foreach (var request in requests)
        {
            request["acknowledgmentStateTag"] = "Motor1_Status";
            request["acknowledgmentControlTag"] = "Motor1_Status";
            request["alarmParameterTags"] = new[] { "Motor1_Status" };
        }
        var access = new FakeAccess { TagAvailable = available };
        using var input = Batch(requests);

        var result = await new HmiDiscreteAlarmService(access).ApplyAsync(input.Path, true, true);

        Assert.AreEqual(available, result.Success, result.Message);
        Assert.AreEqual(1, access.TagReads);
        Assert.AreEqual(available, result.Transaction.Created);
        if (available) Assert.IsTrue(result.Transaction.RolledBack);
        else
        {
            foreach (var plan in result.Alarms)
            {
                Assert.AreEqual(4, plan.Errors.Count);
                foreach (var role in new[] { "raisedStateTag", "acknowledgmentStateTag", "acknowledgmentControlTag", "parameter" })
                    Assert.IsTrue(plan.Errors.Any(error => error.Contains(role)), role);
            }
        }
        Assert.AreEqual(0, access.List(false).Count);
    }

    [DataTestMethod]
    [DataRow(0u)]
    [DataRow(uint.MaxValue)]
    public async Task SnapshotIdIndex_ExcludesOnlyTheExactExistingName(uint id)
    {
        var existing = Snapshot("Existing"); existing.Id = id;
        var access = new FakeAccess(existing);
        var service = new HmiDiscreteAlarmService(access);
        using var self = Batch(new Dictionary<string, object?>
        {
            ["name"] = existing.Name,
            ["expectedState"] = "present",
            ["expectedHash"] = HmiDiscreteAlarmService.Hash(existing),
            ["id"] = id
        });
        var sameName = await service.ApplyAsync(self.Path, true, true);
        Assert.IsTrue(sameName.Success, sameName.Message);
        Assert.IsTrue(sameName.Transaction.RolledBack);

        using var other = Batch(CreateRequest("existing", id));
        var otherName = await service.ApplyAsync(other.Path, true, true);
        Assert.AreEqual("preflight-blocked", otherName.ErrorCode);
        Assert.IsFalse(otherName.Transaction.Created);
        StringAssert.Contains(otherName.Alarms.Single().Errors.Single(), "already in use");
        Assert.AreEqual(0, access.IdReads);
    }

    [DataTestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task CachedPreflight_LaterSetterFailureRestoresAnUpdatedAlarmAndUnrelatedInventory(bool dryRun)
    {
        var existing = Snapshot("Existing");
        var unrelated = Snapshot("Unrelated");
        var access = new FakeAccess(existing, unrelated) { FailOnName = "New" };
        var service = new HmiDiscreteAlarmService(access);
        var before = (await service.ListAsync()).Select(x => x.ContentHash).ToArray();
        using var input = Batch(new Dictionary<string, object?>
        {
            ["name"] = existing.Name,
            ["expectedState"] = "present",
            ["expectedHash"] = HmiDiscreteAlarmService.Hash(existing),
            ["origin"] = "Changed",
            ["alarmClass"] = "Error",
            ["raisedStateTag"] = "Motor1_Status"
        }, CreateRequest("New", 1001));

        var result = await service.ApplyAsync(input.Path, dryRun, true);

        Assert.AreEqual("setter-failed", result.ErrorCode);
        Assert.IsTrue(result.Transaction.Created);
        Assert.IsTrue(result.Transaction.RolledBack);
        Assert.IsFalse(result.Transaction.Committed);
        Assert.AreEqual("Changed", result.Alarms[0].ProposedAfter!.Origin);
        CollectionAssert.AreEquivalent(before, (await service.ListAsync()).Select(x => x.ContentHash).ToArray());
        Assert.AreEqual(1, access.ClassReads);
        Assert.AreEqual(1, access.TagReads);
    }

    [TestMethod]
    public async Task SnapshotIdIndex_RefreshesAfterCommitAndDeletion()
    {
        var access = new FakeAccess();
        var service = new HmiDiscreteAlarmService(access);
        using var first = Batch(CreateRequest("A", 1001));
        Assert.IsTrue((await service.ApplyAsync(first.Path, false, true)).Success);

        using var next = Batch(CreateRequest("B", 1001));
        var collision = await service.ApplyAsync(next.Path, true, true);
        Assert.AreEqual("preflight-blocked", collision.ErrorCode);
        Assert.IsFalse(collision.Transaction.Created);

        var created = await service.GetAsync("A");
        Assert.IsTrue((await service.DeleteAsync("A", created!.ContentHash, false, true, false)).Deleted);
        var reused = await service.ApplyAsync(next.Path, true, true);
        Assert.IsTrue(reused.Success, reused.Message);
        Assert.IsTrue(reused.Transaction.RolledBack);
        Assert.AreEqual(0, access.List(false).Count);
    }

    [TestMethod]
    public void CanonicalHash_IsStableAndKeepsParameterOrderSignificant()
    {
        var first = Snapshot("Alarm_1");
        first.Texts = new Dictionary<string, HmiDiscreteAlarmLocalizedText>
        {
            ["it-IT"] = new() { EventText = "Italiano" },
            ["en-US"] = new() { EventText = "English" }
        };
        first.Validation = new[] { new HmiDiscreteAlarmValidationDiagnostic { PropertyName = "X", Warnings = new[] { "volatile" } } };
        var second = Clone(first);
        second.Texts = first.Texts.Reverse().ToDictionary(x => x.Key, x => x.Value);
        second.Validation = Array.Empty<HmiDiscreteAlarmValidationDiagnostic>();

        Assert.AreEqual(HmiDiscreteAlarmService.Hash(first), HmiDiscreteAlarmService.Hash(second));
        second.AlarmParameterTags = second.AlarmParameterTags.Reverse().ToArray();
        Assert.AreNotEqual(HmiDiscreteAlarmService.Hash(first), HmiDiscreteAlarmService.Hash(second));
    }

    [TestMethod]
    public async Task CreateDryRun_UsesTransactionAndRollsBack()
    {
        var access = new FakeAccess();
        var service = new HmiDiscreteAlarmService(access);
        using var input = TempDocument(CreateJson("Alarm_New"));

        var result = await service.ApplyAsync(input.Path, dryRun: true, failOnWarning: true);

        Assert.IsTrue(result.Success, result.Message);
        Assert.IsTrue(result.Transaction.Created);
        Assert.IsTrue(result.Transaction.RolledBack);
        Assert.IsFalse(result.Transaction.Committed);
        Assert.AreEqual("Created", result.Alarms.Single().Status);
        Assert.IsNotNull(result.Alarms.Single().ProposedAfter);
        Assert.IsNull(access.FindExact("Alarm_New", false));
    }

    [TestMethod]
    public async Task ApplyCreate_ReturnsFreshPersistedSnapshot()
    {
        var access = new FakeAccess();
        var service = new HmiDiscreteAlarmService(access);
        using var input = TempDocument(CreateJson("Alarm_New"));

        var result = await service.ApplyAsync(input.Path, dryRun: false, failOnWarning: true);

        Assert.IsTrue(result.Success, result.Message);
        Assert.IsTrue(result.Transaction.Committed);
        Assert.IsTrue(result.ProjectHasUnsavedChanges);
        Assert.AreEqual("Alarm_New", result.Alarms.Single().Persisted?.Name);
        Assert.AreEqual("Motor overload", result.Alarms.Single().Persisted?.Texts["en-US"].EventText);
        CollectionAssert.AreEqual(new[] { "Motor1_Speed", "Motor1_Current" }, result.Alarms.Single().Persisted?.AlarmParameterTags.ToArray());
    }

    [TestMethod]
    public async Task Update_OmittedFieldsArePreservedAndExplicitEmptyClears()
    {
        var access = new FakeAccess(Snapshot("Alarm_1"));
        var before = HmiDiscreteAlarmService.WithHash(access.FindExact("Alarm_1", false)!);
        var service = new HmiDiscreteAlarmService(access);
        using var input = TempDocument("""
        {"schemaVersion":"1.0","alarms":[{"name":"Alarm_1","expectedState":"present","expectedHash":"__HASH__","origin":"","texts":{"en-US":{"infoText":"Changed"}}}]}
        """.Replace("__HASH__", before.ContentHash));

        var result = await service.ApplyAsync(input.Path, dryRun: false, failOnWarning: true);

        Assert.IsTrue(result.Success, result.Message);
        var persisted = result.Alarms.Single().Persisted!;
        Assert.AreEqual(string.Empty, persisted.Origin);
        Assert.AreEqual("Area", persisted.Area);
        Assert.AreEqual("Changed", persisted.Texts["en-US"].InfoText);
        Assert.AreEqual("Event", persisted.Texts["en-US"].EventText);
    }

    [TestMethod]
    public async Task Update_AllDiscreteScalarsAndAllTextSlotsRoundTrip()
    {
        var access = new FakeAccess(Snapshot("Alarm_1"));
        var current = HmiDiscreteAlarmService.WithHash(access.FindExact("Alarm_1", false)!);
        var textFields = string.Join(",", Enumerable.Range(1, 9).Select(x => $"\"eventText{x}\":\"E{x}\""));
        using var input = TempDocument("""
        {"schemaVersion":"1.0","alarms":[{"name":"Alarm_1","expectedState":"present","expectedHash":"__HASH__","priority":255,"auditClass":"Audit","raisedStateTagBitNumber":31,"triggerMode":"OnFallingEdge","acknowledgmentStateTag":"Motor1_AckState","acknowledgmentStateTagBitNumber":7,"acknowledgmentControlTag":"Motor1_AckControl","acknowledgmentControlTagBitNumber":8,"texts":{"en-US":{"eventText":"E0",__TEXT_FIELDS__,"infoText":"Info changed"}}}]}
        """.Replace("__HASH__", current.ContentHash).Replace("__TEXT_FIELDS__", textFields));
        var result = await new HmiDiscreteAlarmService(access).ApplyAsync(input.Path, false, true);
        Assert.IsTrue(result.Success, result.Message);
        var alarm = result.Alarms.Single().Persisted!;
        Assert.AreEqual(byte.MaxValue, alarm.Priority);
        Assert.AreEqual("Audit", alarm.AuditClass);
        Assert.AreEqual(31u, alarm.RaisedStateTagBitNumber);
        Assert.AreEqual("OnFallingEdge", alarm.TriggerMode);
        Assert.AreEqual("Motor1_AckState", alarm.AcknowledgmentStateTag);
        Assert.AreEqual(7, alarm.AcknowledgmentStateTagBitNumber);
        Assert.AreEqual("Motor1_AckControl", alarm.AcknowledgmentControlTag);
        Assert.AreEqual(8, alarm.AcknowledgmentControlTagBitNumber);
        Assert.AreEqual("E9", alarm.Texts["en-US"].EventText9);
        Assert.AreEqual("Info changed", alarm.Texts["en-US"].InfoText);
    }

    [TestMethod]
    public async Task ConcurrencyAndExpectedState_AreFailClosed()
    {
        var access = new FakeAccess(Snapshot("Alarm_1"));
        var service = new HmiDiscreteAlarmService(access);
        using var absent = TempDocument(CreateJson("Alarm_1"));
        var collision = await service.ApplyAsync(absent.Path, true, true);
        Assert.AreEqual("preflight-blocked", collision.ErrorCode);

        using var stale = TempDocument("""{"schemaVersion":"1.0","alarms":[{"name":"Alarm_1","expectedState":"present","expectedHash":"stale","origin":"X"}]}""");
        var staleResult = await service.ApplyAsync(stale.Path, true, true);
        Assert.AreEqual("preflight-blocked", staleResult.ErrorCode);

        using var noHash = TempDocument("""{"schemaVersion":"1.0","alarms":[{"name":"Alarm_1","expectedState":"present","origin":"X"}]}""");
        var warningResult = await service.ApplyAsync(noHash.Path, true, true);
        Assert.IsTrue(warningResult.SafetyBlocked);
        Assert.IsTrue(warningResult.Alarms.Single().Warnings.Any());

        using var idChange = TempDocument("""
        {"schemaVersion":"1.0","alarms":[{"name":"Alarm_1","expectedState":"present","expectedHash":"__HASH__","id":999}]}
        """.Replace("__HASH__", HmiDiscreteAlarmService.Hash(access.FindExact("Alarm_1", false)!)));
        var idResult = await service.ApplyAsync(idChange.Path, true, false);
        Assert.IsTrue(idResult.SafetyBlocked);
        StringAssert.Contains(idResult.Alarms.Single().Errors.Single(), "ID");
    }

    [TestMethod]
    public async Task UnknownProjectCulture_IsRejectedBeforeTransaction()
    {
        var access = new FakeAccess(Snapshot("Alarm_1"));
        var hash = HmiDiscreteAlarmService.Hash(access.FindExact("Alarm_1", false)!);
        using var input = TempDocument("""
        {"schemaVersion":"1.0","alarms":[{"name":"Alarm_1","expectedState":"present","expectedHash":"__HASH__","texts":{"fr-FR":{"eventText":"Texte"}}}]}
        """.Replace("__HASH__", hash));
        var result = await new HmiDiscreteAlarmService(access).ApplyAsync(input.Path, true, true);
        Assert.AreEqual("preflight-blocked", result.ErrorCode);
        StringAssert.Contains(result.Alarms.Single().Errors.Single(), "fr-FR");
        Assert.IsFalse(result.Transaction.Created);
    }

    [DataTestMethod]
    [DataRow("{\"schemaVersion\":\"2.0\",\"alarms\":[]}")]
    [DataRow("{\"schemaVersion\":\"1.0\",\"alarms\":[{\"name\":\"A\",\"expectedState\":\"sometimes\"}]}")]
    [DataRow("{\"schemaVersion\":\"1.0\",\"alarms\":[{\"name\":\"A\",\"triggerMode\":\"Level\"}]}")]
    [DataRow("{\"schemaVersion\":\"1.0\",\"alarms\":[{\"name\":\"A\"},{\"name\":\"A\"}]}")]
    [DataRow("{\"schemaVersion\":\"1.0\",\"alarms\":[{\"name\":\"A\",\"id\":1},{\"name\":\"B\",\"id\":1}]}")]
    public async Task InvalidDocuments_AreRejected(string json)
    {
        using var input = TempDocument(json);
        var result = await new HmiDiscreteAlarmService(new FakeAccess()).ApplyAsync(input.Path, true, true);
        Assert.IsTrue(result.SafetyBlocked);
        Assert.AreEqual("invalid-input", result.ErrorCode);
    }

    [TestMethod]
    public async Task PreflightValidatesClassesTagsAndTagLimit()
    {
        var tags = string.Join(",", Enumerable.Range(0, 11).Select(x => $"\"T{x}\""));
        using var input = TempDocument($$"""
        {"schemaVersion":"1.0","alarms":[{"name":"A","expectedState":"absent","alarmClass":"Missing","raisedStateTag":"MissingTag","triggerMode":"OnRisingEdge","alarmParameterTags":[{{tags}}]}]}
        """);
        var result = await new HmiDiscreteAlarmService(new FakeAccess()).ApplyAsync(input.Path, true, true);
        StringAssert.Contains(result.Message, "maximum");
        Assert.AreEqual("invalid-input", result.ErrorCode);
    }

    [TestMethod]
    public async Task SiemensValidationErrorAndWarningPoliciesRollback()
    {
        var errorAccess = new FakeAccess { NextValidationError = "Invalid tag" };
        using var first = TempDocument(CreateJson("A"));
        var errorResult = await new HmiDiscreteAlarmService(errorAccess).ApplyAsync(first.Path, false, true);
        Assert.AreEqual("siemens-validation-blocked", errorResult.ErrorCode);
        Assert.IsNull(errorAccess.FindExact("A", false));

        var warningAccess = new FakeAccess { NextValidationWarning = "Review value" };
        using var second = TempDocument(CreateJson("B"));
        var warningBlocked = await new HmiDiscreteAlarmService(warningAccess).ApplyAsync(second.Path, false, true);
        Assert.IsTrue(warningBlocked.SafetyBlocked);

        using var third = TempDocument(CreateJson("B"));
        var warningAllowed = await new HmiDiscreteAlarmService(warningAccess).ApplyAsync(third.Path, false, false);
        Assert.IsTrue(warningAllowed.Success, warningAllowed.Message);
    }

    [TestMethod]
    public async Task LaterSetterFailure_RollsBackEarlierAlarm()
    {
        var access = new FakeAccess { FailOnName = "B" };
        using var input = TempDocument("""
        {"schemaVersion":"1.0","alarms":[
          {"name":"A","expectedState":"absent","id":10,"alarmClass":"Error","raisedStateTag":"Motor1_Status","raisedStateTagBitNumber":0,"triggerMode":"OnRisingEdge"},
          {"name":"B","expectedState":"absent","id":11,"alarmClass":"Error","raisedStateTag":"Motor1_Status","raisedStateTagBitNumber":0,"triggerMode":"OnRisingEdge"}
        ]}
        """);
        var result = await new HmiDiscreteAlarmService(access).ApplyAsync(input.Path, false, true);
        Assert.IsTrue(result.SafetyBlocked);
        Assert.IsTrue(result.Transaction.RolledBack);
        Assert.AreEqual(0, access.List(false).Count);
    }

    [TestMethod]
    public async Task Delete_IsExactHashGuardedReferenceAwareAndTransactional()
    {
        var access = new FakeAccess(Snapshot("Alarm"), Snapshot("Alarm_Extended"));
        var service = new HmiDiscreteAlarmService(access);
        var alarm = await service.GetAsync("Alarm", true);
        access.References = new[] { new HmiDiscreteAlarmCrossReference { Name = "Screen_1" } };

        var referenced = await service.DeleteAsync("Alarm", alarm!.ContentHash, true, false, false);
        Assert.AreEqual("alarm-is-referenced", referenced.ErrorCode);
        var dryRun = await service.DeleteAsync("Alarm", alarm.ContentHash, true, false, true);
        Assert.IsTrue(dryRun.Success);
        Assert.IsNotNull(access.FindExact("Alarm", false));
        var deleted = await service.DeleteAsync("Alarm", alarm.ContentHash, false, true, true);
        Assert.IsTrue(deleted.Deleted);
        Assert.IsNull(access.FindExact("Alarm", false));
        Assert.IsNotNull(access.FindExact("Alarm_Extended", false));
    }

    private static Dictionary<string, object?> CreateRequest(string name, uint id) => new()
    {
        ["name"] = name,
        ["expectedState"] = "absent",
        ["id"] = id,
        ["alarmClass"] = "Error",
        ["raisedStateTag"] = "Motor1_Status",
        ["raisedStateTagBitNumber"] = 0,
        ["triggerMode"] = "OnRisingEdge",
        ["texts"] = new Dictionary<string, object> { ["en-US"] = new { eventText = "Alarm" } }
    };

    private static TempFile Batch(params Dictionary<string, object?>[] alarms) =>
        TempDocument(JsonSerializer.Serialize(new { schemaVersion = "1.0", alarms }));

    private static string CreateJson(string name) => """
    {"schemaVersion":"1.0","alarms":[{"name":"__NAME__","expectedState":"absent","id":1001,"alarmClass":"Error","priority":10,"origin":"Motor","area":"Area","raisedStateTag":"Motor1_Status","raisedStateTagBitNumber":3,"triggerMode":"OnRisingEdge","alarmParameterTags":["Motor1_Speed","Motor1_Current"],"texts":{"en-US":{"eventText":"Motor overload","infoText":"Check motor"},"it-IT":{"eventText":"Sovraccarico"}}}]}
    """.Replace("__NAME__", name);

    private static HmiDiscreteAlarmSnapshot Snapshot(string name) => new()
    {
        Name = name,
        Id = (uint)(100 + name.Length),
        AlarmClass = "Error",
        Priority = 10,
        Origin = "Origin",
        Area = "Area",
        RaisedStateTag = "Motor1_Status",
        RaisedStateTagBitNumber = 3,
        TriggerMode = "OnRisingEdge",
        TriggerBitAddress = "Motor1_Status.3",
        AlarmParameterTags = new[] { "Motor1_Speed", "Motor1_Current" },
        Texts = new Dictionary<string, HmiDiscreteAlarmLocalizedText> { ["en-US"] = new() { EventText = "Event", InfoText = "Info" }, ["it-IT"] = new() }
    };

    private static HmiDiscreteAlarmSnapshot Clone(HmiDiscreteAlarmSnapshot value) => JsonSerializer.Deserialize<HmiDiscreteAlarmSnapshot>(JsonSerializer.Serialize(value))!;

    private static TempFile TempDocument(string json) => new(json);

    private sealed class TempFile : IDisposable
    {
        public TempFile(string content) { Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ott-alarm-" + Guid.NewGuid().ToString("N") + ".json"); File.WriteAllText(Path, content); }
        public string Path { get; }
        public void Dispose() { if (File.Exists(Path)) File.Delete(Path); }
    }

    private sealed class FakeAccess : IHmiDiscreteAlarmAccess
    {
        private Dictionary<string, HmiDiscreteAlarmSnapshot> _alarms;
        public FakeAccess(params HmiDiscreteAlarmSnapshot[] alarms) => _alarms = alarms.ToDictionary(x => x.Name, Clone, StringComparer.Ordinal);
        public bool IsAvailable => true;
        public int ClassReads { get; private set; }
        public int CultureReads { get; private set; }
        public int TagReads { get; private set; }
        public int IdReads { get; private set; }
        public bool ClassAvailable { get; set; } = true;
        public bool TagAvailable { get; set; } = true;
        public bool CultureAvailable { get; set; } = true;
        public bool IsReadOnly { get; set; }
        public string? FailOnName { get; set; }
        public string? NextValidationError { get; set; }
        public string? NextValidationWarning { get; set; }
        public IReadOnlyList<HmiDiscreteAlarmCrossReference> References { get; set; } = Array.Empty<HmiDiscreteAlarmCrossReference>();
        public string? ReferenceLimitation { get; set; }
        public IReadOnlyList<HmiDiscreteAlarmSnapshot> List(bool includeValidation) => _alarms.Values.Select(Clone).ToArray();
        public HmiDiscreteAlarmSnapshot? FindExact(string name, bool includeValidation) => _alarms.TryGetValue(name, out var value) ? Clone(value) : null;
        public bool AlarmClassExists(string name) { ClassReads++; return ClassAvailable && name == "Error"; }
        public bool TagExists(string name) { TagReads++; return TagAvailable && name.StartsWith("Motor1_", StringComparison.Ordinal); }
        public bool CultureExists(string cultureName) { CultureReads++; return CultureAvailable && (cultureName == "en-US" || cultureName == "it-IT"); }
        public bool IdExists(uint id, string? exceptName = null) { IdReads++; return _alarms.Values.Any(x => x.Id == id && x.Name != exceptName); }
        public void SetReferenceAvailability(string reference, bool available)
        {
            switch (reference)
            {
                case "alarmClass": ClassAvailable = available; break;
                case "tag": TagAvailable = available; break;
                case "culture": CultureAvailable = available; break;
                default: throw new ArgumentException("Unknown reference kind", nameof(reference));
            }
        }
        public HmiDiscreteAlarmSnapshot CreateAndApply(HmiDiscreteAlarmChangeRequest request)
        {
            if (_alarms.ContainsKey(request.Name)) throw new InvalidOperationException("collision");
            var alarm = new HmiDiscreteAlarmSnapshot { Name = request.Name, Texts = new Dictionary<string, HmiDiscreteAlarmLocalizedText> { ["en-US"] = new(), ["it-IT"] = new() } };
            ApplyRequest(alarm, request);
            AddValidation(alarm);
            _alarms[alarm.Name] = Clone(alarm);
            return Clone(alarm);
        }
        public HmiDiscreteAlarmSnapshot Apply(string name, HmiDiscreteAlarmChangeRequest request)
        {
            var alarm = _alarms[name]; ApplyRequest(alarm, request); AddValidation(alarm); return Clone(alarm);
        }
        public void DeleteExact(string name) { if (!_alarms.Remove(name)) throw new InvalidOperationException("not found"); }
        public IReadOnlyList<HmiDiscreteAlarmCrossReference> GetCrossReferences(string name, out string? limitation) { limitation = ReferenceLimitation; return References; }
        public void ExecuteTransaction(string description, bool commit, Action action)
        {
            var before = _alarms.ToDictionary(x => x.Key, x => Clone(x.Value), StringComparer.Ordinal);
            try { action(); if (!commit) _alarms = before; }
            catch { _alarms = before; throw; }
        }
        private void ApplyRequest(HmiDiscreteAlarmSnapshot alarm, HmiDiscreteAlarmChangeRequest request)
        {
            if (request.Name == FailOnName) throw new InvalidOperationException("Injected setter failure");
            if (request.PresentProperties.Contains("id")) alarm.Id = request.Id!.Value;
            if (request.PresentProperties.Contains("alarmClass")) alarm.AlarmClass = request.AlarmClass ?? string.Empty;
            if (request.PresentProperties.Contains("priority")) alarm.Priority = request.Priority!.Value;
            if (request.PresentProperties.Contains("origin")) alarm.Origin = request.Origin ?? string.Empty;
            if (request.PresentProperties.Contains("area")) alarm.Area = request.Area ?? string.Empty;
            if (request.PresentProperties.Contains("auditClass")) alarm.AuditClass = request.AuditClass ?? string.Empty;
            if (request.PresentProperties.Contains("raisedStateTag")) alarm.RaisedStateTag = request.RaisedStateTag ?? string.Empty;
            if (request.PresentProperties.Contains("raisedStateTagBitNumber")) alarm.RaisedStateTagBitNumber = request.RaisedStateTagBitNumber!.Value;
            if (request.PresentProperties.Contains("triggerMode")) alarm.TriggerMode = request.TriggerMode ?? string.Empty;
            if (request.PresentProperties.Contains("acknowledgmentStateTag")) alarm.AcknowledgmentStateTag = request.AcknowledgmentStateTag ?? string.Empty;
            if (request.PresentProperties.Contains("acknowledgmentStateTagBitNumber")) alarm.AcknowledgmentStateTagBitNumber = request.AcknowledgmentStateTagBitNumber!.Value;
            if (request.PresentProperties.Contains("acknowledgmentControlTag")) alarm.AcknowledgmentControlTag = request.AcknowledgmentControlTag ?? string.Empty;
            if (request.PresentProperties.Contains("acknowledgmentControlTagBitNumber")) alarm.AcknowledgmentControlTagBitNumber = request.AcknowledgmentControlTagBitNumber!.Value;
            if (request.PresentProperties.Contains("alarmParameterTags")) alarm.AlarmParameterTags = request.AlarmParameterTags ?? Array.Empty<string>();
            if (request.Texts != null)
            {
                var texts = alarm.Texts.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
                foreach (var culture in request.Texts)
                {
                    if (!texts.TryGetValue(culture.Key, out var target)) throw new InvalidOperationException($"Culture '{culture.Key}' is not configured");
                    foreach (var property in culture.Value.PresentProperties) typeof(HmiDiscreteAlarmLocalizedText).GetProperty(property)!.SetValue(target, typeof(HmiDiscreteAlarmTextPatch).GetProperty(property)!.GetValue(culture.Value) ?? string.Empty);
                }
                alarm.Texts = texts;
            }
            alarm.TriggerBitAddress = alarm.RaisedStateTag + "." + alarm.RaisedStateTagBitNumber;
            _alarms[alarm.Name] = Clone(alarm);
        }
        private void AddValidation(HmiDiscreteAlarmSnapshot alarm)
        {
            alarm.Validation = new[] { new HmiDiscreteAlarmValidationDiagnostic { PropertyName = "Alarm", Errors = NextValidationError == null ? Array.Empty<string>() : new[] { NextValidationError }, Warnings = NextValidationWarning == null ? Array.Empty<string>() : new[] { NextValidationWarning } } };
        }
    }
}
