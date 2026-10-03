using System;
using System.IO;
using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Services;

namespace OpenTiaToolkit.Tests.Services;

[TestClass]
public sealed class PlcPreApplyIntegrityTests
{
    private static PlcCodeTransaction Draft() => new PlcCodeTransaction
    {
        TransactionId = Guid.NewGuid().ToString("N"),
        Operations = new[] { new PlcCodeTransactionOperation() },
        Journal = new[] { new PlcCodeTransactionJournalEntry { Phase = "Lifecycle", State = "Draft" } }
    };

    [DataTestMethod]
    [DataRow("Draft")]
    [DataRow("Planned")]
    [DataRow("ImpactReviewRequired")]
    [DataRow("ImpactBlocked")]
    [DataRow("Expired")]
    [DataRow("Stale")]
    public void NeverApplied_RequiresPreApplyStateAndUntouchedOperations(string state)
    {
        var draft = Draft();
        draft.State = state;
        Assert.IsTrue(PlcCodeTransactionIntegrity.IsNeverApplied(draft));
        draft.Operations[0].ApplyState = "Starting";
        Assert.IsFalse(PlcCodeTransactionIntegrity.IsNeverApplied(draft));
    }

    [DataTestMethod]
    [DataRow("Applying")]
    [DataRow("Verifying")]
    [DataRow("Succeeded")]
    [DataRow("RollingBack")]
    [DataRow("RecoveryRequired")]
    [DataRow("RolledBackAndVerified")]
    [DataRow("unknown")]
    public void NeverApplied_NeverAcceptsPostApplyOrUnknownState(string state)
    {
        var draft = Draft();
        draft.State = state;
        Assert.IsFalse(PlcCodeTransactionIntegrity.IsNeverApplied(draft));
    }

    [DataTestMethod]
    [DataRow("apply-journal")]
    [DataRow("missing-journal")]
    [DataRow("null-first-entry")]
    [DataRow("unknown-journal")]
    [DataRow("compile")]
    [DataRow("rollback-compile")]
    [DataRow("recovery")]
    [DataRow("hash")]
    [DataRow("restored")]
    [DataRow("target-hash")]
    public void NeverApplied_InconsistentDraftFailsClosed(string scenario)
    {
        var draft = Draft();
        switch (scenario)
        {
            case "apply-journal": draft.Journal = new[] { draft.Journal[0], new PlcCodeTransactionJournalEntry { Phase = "Apply", State = "Starting" } }; break;
            case "missing-journal": draft.Journal = Array.Empty<PlcCodeTransactionJournalEntry>(); break;
            case "null-first-entry": draft.Journal = new PlcCodeTransactionJournalEntry[] { null! }; break;
            case "unknown-journal": draft.Journal = new[] { draft.Journal[0], new PlcCodeTransactionJournalEntry { Phase = "Future", State = "Unknown" } }; break;
            case "compile": draft.FinalCompilation = new PlcCodeTransactionCompileEvidence(); break;
            case "rollback-compile": draft.RollbackCompilation = new PlcCodeTransactionCompileEvidence(); break;
            case "recovery": draft.Journal = new[] { draft.Journal[0], new PlcCodeTransactionJournalEntry { Phase = "Recovery", State = "Starting" } }; break;
            case "hash": draft.Operations[0].CanonicalResultHash = "applied"; break;
            case "restored": draft.Operations[0].RestorationVerified = true; break;
            case "target-hash": draft.Operations[0].TargetSnapshots = new[] { new PlcCodeTransactionTargetSnapshot { CanonicalResultHash = "applied" } }; break;
        }
        Assert.IsFalse(PlcCodeTransactionIntegrity.IsNeverApplied(draft));
    }

    [TestMethod]
    public void NeverApplied_ChecksDurableJournalBeforeAcceptingOldManifest()
    {
        string root = Path.Combine(Path.GetTempPath(), "OpenTiaPreApplyTests-" + Guid.NewGuid().ToString("N"));
        var store = new PlcCodeTransactionStore(root);
        var draft = Draft();
        draft.Journal = Array.Empty<PlcCodeTransactionJournalEntry>();
        store.AddJournal(draft, null, "Lifecycle", "Draft", "created");
        store.Save(draft);
        Assert.IsTrue(store.IsNeverApplied(draft.TransactionId));
        store.AddJournal(draft, null, "Apply", "Starting", "persisted before manifest save");
        Assert.IsFalse(store.IsNeverApplied(draft.TransactionId));
        File.Delete(store.JournalPath(draft.TransactionId));
        Assert.IsFalse(store.IsNeverApplied(draft.TransactionId));
        Assert.ThrowsException<InvalidOperationException>(() => store.IsNeverApplied(Guid.NewGuid().ToString("N")));
    }
}
