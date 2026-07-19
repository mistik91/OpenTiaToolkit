using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Services;

namespace OpenTiaToolkit.Tests.Services;

[TestClass]
public class PlcDocumentWorkflowCollaboratorTests
{
    private string _root = null!;

    [TestInitialize]
    public void Initialize() { _root = Path.Combine(Path.GetTempPath(), "OpenTiaToolkit.CollaboratorTests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_root); }
    [TestCleanup]
    public void Cleanup() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [TestMethod]
    public void DocumentValidator_IsPureAndPreservesStrictUtf8AndResourceRules()
    {
        File.WriteAllText(Path.Combine(_root, "Probe.s7dcl"), "FUNCTION \"Probe\" : Void\r\n{ S7_BlockTitle := \"MLC_Title\" }\r\nEND_FUNCTION", new System.Text.UTF8Encoding(true));
        File.WriteAllText(Path.Combine(_root, "Probe.s7res"), "MultiLingualTexts:\r\n  - id: MLC_Title\r\n    en-US: Probe", new System.Text.UTF8Encoding(true));
        var validator = new PlcDocumentValidator();
        Assert.IsTrue(validator.Validate(_root, "Probe").Valid);
        File.WriteAllBytes(Path.Combine(_root, "Probe.s7dcl"), new byte[] { 0xff, 0xfe, 0xfd });
        StringAssert.Contains(string.Join("; ", validator.Validate(_root, "Probe").Errors), "not valid UTF-8");
    }

    [TestMethod]
    public void DocumentPlanStore_ConsumesTokenOnceAndExpiresAgainstInjectedClock()
    {
        DateTimeOffset now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var store = new PlcDocumentPlanStore(Path.Combine(_root, "plans"), () => now);
        var plan = new PlcDocumentImportPlan { PlanId = "one", ExpiresAtUtc = now.AddMinutes(10) };
        store.Add("token", plan, Path.Combine(_root, "backup"));
        Assert.AreSame(plan, store.Consume("token").Plan);
        Assert.ThrowsException<InvalidOperationException>(() => store.Consume("token"));
        store.Add("expired", new PlcDocumentImportPlan { ExpiresAtUtc = now.AddSeconds(1) }, Path.Combine(_root, "expired-backup"));
        Directory.CreateDirectory(Path.Combine(_root, "expired-backup"));
        now = now.AddMinutes(11);
        Assert.ThrowsException<InvalidOperationException>(() => store.Consume("expired"));
        Assert.IsFalse(Directory.Exists(Path.Combine(_root, "expired-backup")));
    }

    [TestMethod]
    public void DocumentPlanStore_CleansOrphansAndKeepsProductionLayoutShape()
    {
        DateTimeOffset now = new DateTimeOffset(2026, 1, 1, 0, 30, 0, TimeSpan.Zero);
        string planRoot = Path.Combine(_root, "plc-document-plans");
        string orphan = Path.Combine(planRoot, "orphan");
        Directory.CreateDirectory(orphan);
        Directory.SetLastWriteTimeUtc(orphan, now.UtcDateTime.AddMinutes(-11));
        var store = new PlcDocumentPlanStore(planRoot, () => now);
        Assert.IsFalse(Directory.Exists(orphan));
        Assert.AreEqual(Path.Combine(_root, "plc-document-current"), store.CurrentRoot);
        Assert.AreEqual(Path.Combine(_root, "plc-document-rollback"), store.RollbackRoot);
    }

    [TestMethod]
    public void DocumentPlanStore_ConcurrentConsumptionHasOneWinner()
    {
        var store = new PlcDocumentPlanStore(Path.Combine(_root, "plans"));
        store.Add("token", new PlcDocumentImportPlan { ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10) }, Path.Combine(_root, "backup"));
        int winners = 0;
        Parallel.For(0, 20, _ => { try { store.Consume("token"); Interlocked.Increment(ref winners); } catch (InvalidOperationException) { } });
        Assert.AreEqual(1, winners);
    }

    [TestMethod]
    public void TransactionStore_RoundTripsJournalAndRecoveryCompatibility()
    {
        var store = new PlcCodeTransactionStore(Path.Combine(_root, "transactions"));
        var transaction = new PlcCodeTransaction { TransactionId = Guid.NewGuid().ToString("N"), State = "Draft", CreatedAtUtc = DateTimeOffset.UtcNow, Description = "roundtrip" };
        store.AddJournal(transaction, 1, "Authoring", "Added", "one");
        store.AddJournal(transaction, 2, "Authoring", "Added", "two");
        store.Save(transaction);
        PlcCodeTransaction loaded = store.Load(transaction.TransactionId);
        Assert.AreEqual("1.4", loaded.SchemaVersion);
        CollectionAssert.AreEqual(new[] { 1, 2 }, loaded.Journal.Select(item => item.OperationSequence!.Value).ToArray());
        string journal = File.ReadAllText(store.JournalPath(transaction.TransactionId));
        Assert.AreEqual(2, journal.Split(new[] { "\"TimestampUtc\"" }, StringSplitOptions.None).Length - 1);
        Assert.IsTrue(journal.IndexOf("\"OperationSequence\": 1", StringComparison.Ordinal) < journal.IndexOf("\"OperationSequence\": 2", StringComparison.Ordinal));
        loaded.State = "RecoveryRequired"; store.Save(loaded);
        Assert.IsTrue(File.Exists(Path.Combine(store.DirectoryPath(transaction.TransactionId), "recovery.json")));
        loaded.State = "RolledBackAndVerified"; store.Save(loaded);
        Assert.IsFalse(File.Exists(Path.Combine(store.DirectoryPath(transaction.TransactionId), "recovery.json")));
    }

    [TestMethod]
    public void TransactionStore_RejectsInvalidIdAndUnsafeDiscard()
    {
        var store = new PlcCodeTransactionStore(Path.Combine(_root, "transactions"));
        Assert.ThrowsException<InvalidOperationException>(() => store.Load("../escape"));
        var transaction = new PlcCodeTransaction { TransactionId = Guid.NewGuid().ToString("N"), State = "RecoveryRequired" };
        store.Save(transaction);
        Assert.ThrowsException<InvalidOperationException>(() => store.Discard(transaction.TransactionId));
    }

    [TestMethod]
    public void TransactionStore_ExpiresAtomicallyAndListsNewestFirst()
    {
        DateTimeOffset now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var store = new PlcCodeTransactionStore(Path.Combine(_root, "transactions"), () => now);
        var expired = new PlcCodeTransaction { TransactionId = Guid.NewGuid().ToString("N"), State = "Planned", ApplyToken = "secret", ExpiresAtUtc = now.AddMinutes(1) };
        store.Save(expired);
        now = now.AddMinutes(2);
        var newest = new PlcCodeTransaction { TransactionId = Guid.NewGuid().ToString("N"), State = "Draft" };
        store.Save(newest);
        now = now.AddSeconds(1);
        PlcCodeTransaction loaded = store.Load(expired.TransactionId);
        Assert.AreEqual("Expired", loaded.State);
        Assert.IsNull(loaded.ApplyToken);
        Assert.IsFalse(File.Exists(Path.Combine(store.DirectoryPath(expired.TransactionId), "manifest.tmp")));
        Assert.AreEqual(expired.TransactionId, store.List()[0].TransactionId, "The expiration transition is the newest persisted update.");
    }

    [TestMethod]
    public void TransactionIntegrity_IsDeterministicCanonicalAndTamperSensitive()
    {
        var first = Fixture(new[] { 2, 1 });
        var second = Fixture(new[] { 1, 2 });
        string hash = PlcCodeTransactionIntegrity.ComputePlanHash(first);
        Assert.AreEqual(hash, PlcCodeTransactionIntegrity.ComputePlanHash(second), "Operation ordering must be canonical by sequence.");
        second.Operations[0].InputHash = "tampered";
        Assert.AreNotEqual(hash, PlcCodeTransactionIntegrity.ComputePlanHash(second));
        Assert.IsTrue(PlcWorkflowPrimitives.FixedEquals(hash, string.Concat(hash)));
        Assert.IsFalse(PlcWorkflowPrimitives.FixedEquals(hash, hash + "0"));
    }

    [TestMethod]
    public void TransactionIntegrity_ParsesDependenciesAndOrdersProviderFirst()
    {
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, PlcCodeTransactionIntegrity.ParseDependencySequences("3, 1;2 2").ToArray());
        var provider = new PlcCodeTransactionOperation { Sequence = 2, Target = new PlcBlockIdentity { Name = "Provider" } };
        var consumer = new PlcCodeTransactionOperation { Sequence = 1, Dependencies = new[] { "Provider" }, Target = new PlcBlockIdentity { Name = "Consumer" } };
        PlcCodeTransactionIntegrity.AssignDependencyOrder(new[] { consumer, provider });
        Assert.AreEqual(1, provider.ExecutionOrder);
        Assert.AreEqual(2, consumer.ExecutionOrder);

        var first = new PlcCodeTransactionOperation { Sequence = 1, ExplicitDependsOn = new[] { 2 } };
        var second = new PlcCodeTransactionOperation { Sequence = 2, ExplicitDependsOn = new[] { 1 } };
        StringAssert.Contains(Assert.ThrowsException<InvalidOperationException>(() => PlcCodeTransactionIntegrity.AssignDependencyOrder(new[] { first, second })).Message, "dependency cycle");
        CollectionAssert.Contains(PlcCodeTransactionIntegrity.RecoverableStates, "RecoveryRequired");
    }

    private static PlcCodeTransaction Fixture(int[] order) => new PlcCodeTransaction
    {
        TransactionId = "0123456789abcdef0123456789abcdef",
        PlcName = "PLC_1",
        CompilePolicy = "NoNewErrors",
        Operations = order.Select(sequence => new PlcCodeTransactionOperation { Sequence = sequence, Kind = "UpsertBlockDocuments", InputHash = "input-" + sequence, BaselineHash = "missing", Target = new PlcBlockIdentity { PlcName = "PLC_1", Name = "Block" + sequence, BlockType = "FC" } }).ToArray()
    };
}
