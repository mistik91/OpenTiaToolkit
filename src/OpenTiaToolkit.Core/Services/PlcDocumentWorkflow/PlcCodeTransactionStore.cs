using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenTiaToolkit.Core.Models;

namespace OpenTiaToolkit.Core.Services;

internal sealed class PlcCodeTransactionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly string _root;
    private readonly Func<DateTimeOffset> _clock;

    internal PlcCodeTransactionStore() : this(Path.Combine(Path.GetTempPath(), "OpenTiaToolkit", "plc-code-transactions"), () => DateTimeOffset.UtcNow) { }
    internal PlcCodeTransactionStore(string root, Func<DateTimeOffset>? clock = null) { _root = Path.GetFullPath(root); _clock = clock ?? (() => DateTimeOffset.UtcNow); }

    internal string DirectoryPath(string id) => Path.Combine(_root, ValidateId(id));
    internal string ManifestPath(string id) => Path.Combine(DirectoryPath(id), "manifest.json");
    internal string JournalPath(string id) => Path.Combine(DirectoryPath(id), "journal.jsonl");
    internal string BaselineRoot(string id) => Path.Combine(DirectoryPath(id), "baseline");
    internal string OperationBaseline(string id, int sequence) => Path.Combine(BaselineRoot(id), sequence.ToString());
    internal string SclTargetBaseline(string id, int sequence, int targetIndex) => Path.Combine(OperationBaseline(id, sequence), targetIndex.ToString());
    internal string SclTypeBaseline(string id, int sequence, int targetIndex) => Path.Combine(OperationBaseline(id, sequence), targetIndex.ToString(), "type.xml");
    internal string OperationTypeBaseline(string id, int sequence) => Path.Combine(OperationBaseline(id, sequence), "type.xml");

    internal PlcCodeTransaction Load(string id, bool updateExpiry = true)
    {
        string manifest = ManifestPath(id);
        if (!File.Exists(manifest)) throw new InvalidOperationException($"PLC code transaction '{id}' was not found.");
        PlcCodeTransaction transaction = JsonSerializer.Deserialize<PlcCodeTransaction>(File.ReadAllText(manifest), JsonOptions) ?? throw new InvalidDataException($"PLC code transaction '{id}' manifest is invalid.");
        if (updateExpiry && new[] { "Planned", "ImpactReviewRequired" }.Contains(transaction.State, StringComparer.Ordinal) && transaction.ExpiresAtUtc <= _clock())
        {
            transaction.State = "Expired";
            transaction.ApplyToken = null;
            AddJournal(transaction, null, "Lifecycle", "Expired", "Transaction expired without mutation.");
            Save(transaction);
        }
        return transaction;
    }

    internal void Save(PlcCodeTransaction transaction)
    {
        transaction.UpdatedAtUtc = _clock();
        string directory = DirectoryPath(transaction.TransactionId);
        Directory.CreateDirectory(directory);
        string manifest = Path.Combine(directory, "manifest.json");
        string temporary = Path.Combine(directory, "manifest.tmp");
        File.WriteAllText(temporary, JsonSerializer.Serialize(transaction, JsonOptions));
        if (File.Exists(manifest)) File.Replace(temporary, manifest, null); else File.Move(temporary, manifest);
        string recovery = Path.Combine(directory, "recovery.json");
        if (transaction.State == "RecoveryRequired")
            File.WriteAllText(recovery, JsonSerializer.Serialize(new { transaction.TransactionId, transaction.State, transaction.Failure, transaction.ProjectName, transaction.PlcName, transaction.Operations }, JsonOptions));
        else if (File.Exists(recovery)) File.Delete(recovery);
    }

    internal void AddJournal(PlcCodeTransaction transaction, int? operation, string phase, string state, string message)
    {
        var entry = new PlcCodeTransactionJournalEntry { TimestampUtc = _clock(), OperationSequence = operation, Phase = phase, State = state, Message = message };
        transaction.Journal = transaction.Journal.Concat(new[] { entry }).ToArray();
        string directory = DirectoryPath(transaction.TransactionId);
        Directory.CreateDirectory(directory);
        File.AppendAllText(JournalPath(transaction.TransactionId), JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine);
    }

    internal IReadOnlyList<PlcCodeTransaction> List()
    {
        if (!Directory.Exists(_root)) return Array.Empty<PlcCodeTransaction>();
        var transactions = new List<PlcCodeTransaction>();
        foreach (string directory in Directory.GetDirectories(_root)) { try { transactions.Add(Load(Path.GetFileName(directory))); } catch { } }
        return transactions.OrderByDescending(item => item.UpdatedAtUtc).ToArray();
    }

    internal bool Discard(string id)
    {
        PlcCodeTransaction transaction = Load(id, updateExpiry: false);
        if (PlcCodeTransactionIntegrity.RecoverableStates.Contains(transaction.State, StringComparer.Ordinal)) throw new InvalidOperationException("A transaction with unverified recovery state cannot be discarded. Recover it first.");
        string directory = DirectoryPath(id);
        PlcWorkflowPrimitives.Cleanup(directory);
        return !Directory.Exists(directory);
    }

    internal static string ValidateId(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidOperationException("Transaction id is invalid.");
        return id;
    }
}
