using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenTiaToolkit.Core.Models;

namespace OpenTiaToolkit.Core.Services;

internal sealed class PlcDocumentPlanStore
{
    internal static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private readonly Dictionary<string, StoredPlan> _plans = new Dictionary<string, StoredPlan>(StringComparer.Ordinal);
    private readonly object _sync = new object();
    private readonly Func<DateTimeOffset> _clock;
    private readonly string _planRoot;

    internal PlcDocumentPlanStore() : this(Path.Combine(Path.GetTempPath(), "OpenTiaToolkit", "plc-document-plans"), () => DateTimeOffset.UtcNow) { }

    internal PlcDocumentPlanStore(string planRoot, Func<DateTimeOffset>? clock = null)
    {
        _planRoot = Path.GetFullPath(planRoot);
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        CleanupOrphans();
    }

    internal string PlanRoot => _planRoot;
    internal string CurrentRoot => Path.Combine(Path.GetDirectoryName(_planRoot)!, "plc-document-current");
    internal string RollbackRoot => Path.Combine(Path.GetDirectoryName(_planRoot)!, "plc-document-rollback");
    internal DateTimeOffset ExpiresAt() => _clock().Add(Lifetime);

    internal void Add(string token, PlcDocumentImportPlan plan, string backupDirectory)
    {
        lock (_sync) { PruneExpired(); _plans[token] = new StoredPlan(plan, backupDirectory); }
    }

    internal StoredPlan Consume(string token)
    {
        lock (_sync)
        {
            PruneExpired();
            if (!_plans.TryGetValue(token, out StoredPlan? plan)) throw new InvalidOperationException("The apply token is invalid, expired, or already consumed. Create a new import plan.");
            _plans.Remove(token);
            return plan;
        }
    }

    private void PruneExpired()
    {
        foreach (string token in _plans.Where(pair => pair.Value.Plan.ExpiresAtUtc <= _clock()).Select(pair => pair.Key).ToArray())
        {
            PlcWorkflowPrimitives.Cleanup(_plans[token].BackupDirectory);
            _plans.Remove(token);
        }
    }

    private void CleanupOrphans()
    {
        try
        {
            if (!Directory.Exists(_planRoot)) return;
            foreach (string directory in Directory.GetDirectories(_planRoot))
                if (Directory.GetLastWriteTimeUtc(directory) < _clock().UtcDateTime.Subtract(Lifetime)) PlcWorkflowPrimitives.Cleanup(directory);
        }
        catch { }
    }

    internal sealed class StoredPlan
    {
        internal StoredPlan(PlcDocumentImportPlan plan, string backupDirectory) { Plan = plan; BackupDirectory = backupDirectory; }
        internal PlcDocumentImportPlan Plan { get; }
        internal string BackupDirectory { get; }
    }
}
