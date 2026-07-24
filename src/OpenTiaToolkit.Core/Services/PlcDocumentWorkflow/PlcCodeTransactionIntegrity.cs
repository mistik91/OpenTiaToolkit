using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using OpenTiaToolkit.Core.Models;

namespace OpenTiaToolkit.Core.Services;

internal static class PlcCodeTransactionIntegrity
{
    internal static readonly string[] RecoverableStates = { "Applying", "Verifying", "DisposingProbe", "RollingBack", "RecoveryRequired" };

    internal static string ComputePlanHash(PlcCodeTransaction transaction)
    {
        string canonical = JsonSerializer.Serialize(new
        {
            transaction.SchemaVersion,
            transaction.TransactionId,
            transaction.TiaProcessId,
            transaction.ProjectName,
            transaction.ProjectPath,
            transaction.PlcName,
            transaction.CapabilityId,
            transaction.CapabilityFingerprint,
            transaction.CapabilityRoute,
            transaction.CapabilityBindings,
            transaction.Promotion,
            transaction.CompilePolicy,
            transaction.MaximumNewWarnings,
            transaction.ImpactAnalysis,
            transaction.ImpactAcknowledged,
            transaction.ImpactAcknowledgementFingerprint,
            Operations = transaction.Operations.OrderBy(item => item.Sequence).Select(item => new
            {
                item.Sequence,
                item.Kind,
                item.SourcePath,
                item.SourceName,
                item.TypeGroupPath,
                item.BaselinePath,
                item.ValidationBaselineHash,
                item.AllowTopologyChange,
                item.AllowedChangePaths,
                item.TargetMode,
                item.AuthoringBaselineDirectory,
                item.AuthoringBaselineFileName,
                item.ChangeContract,
                item.ImportDirectory,
                item.FileName,
                item.GroupPath,
                item.ImportOption,
                item.Action,
                item.InputHash,
                item.BaselineHash,
                item.Dependencies,
                item.ExplicitDependsOn,
                item.DependsOn,
                item.ExecutionOrder,
                item.RollbackMethod,
                item.Target,
                TargetSnapshots = item.TargetSnapshots.Select(target => new { target.Target, target.Action, target.BaselineHash, target.RollbackMethod }).ToArray()
            }).ToArray()
        });
        return PlcWorkflowPrimitives.Hash(Encoding.UTF8.GetBytes(canonical));
    }

    internal static IReadOnlyList<int> ParseDependencySequences(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Array.Empty<int>();
        var sequences = new List<int>();
        foreach (string token in value!.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(token, out int sequence) || sequence < 1) throw new InvalidOperationException($"depends-on contains invalid operation sequence '{token}'. Use positive comma-separated integers.");
            if (!sequences.Contains(sequence)) sequences.Add(sequence);
        }
        return sequences.OrderBy(sequence => sequence).ToArray();
    }

    internal static void AssignDependencyOrder(IReadOnlyList<PlcCodeTransactionOperation> operations)
    {
        var bySequence = operations.ToDictionary(item => item.Sequence);
        var producersByName = operations.SelectMany(operation => OperationTargets(operation).Select(target => new { target.Name, operation.Sequence })).GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.Select(item => item.Sequence).Distinct().OrderBy(value => value).ToArray(), StringComparer.OrdinalIgnoreCase);
        var dependencies = new Dictionary<int, HashSet<int>>();
        foreach (PlcCodeTransactionOperation operation in operations)
        {
            var resolved = new HashSet<int>();
            foreach (int sequence in operation.ExplicitDependsOn)
            {
                if (sequence == operation.Sequence) throw new InvalidOperationException($"Operation {operation.Sequence} cannot depend on itself.");
                if (!bySequence.ContainsKey(sequence)) throw new InvalidOperationException($"Operation {operation.Sequence} explicitly depends on missing operation {sequence}.");
                resolved.Add(sequence);
            }
            var unresolved = new List<string>();
            foreach (string dependency in operation.Dependencies.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!producersByName.TryGetValue(dependency, out int[]? producers)) { unresolved.Add(dependency); continue; }
                int[] other = producers.Where(sequence => sequence != operation.Sequence).ToArray();
                if (other.Length > 1) throw new InvalidOperationException($"Operation {operation.Sequence} dependency '{dependency}' is produced by multiple transaction operations ({string.Join(", ", other)}). Use depends-on with exact operation sequences.");
                if (other.Length == 1) resolved.Add(other[0]);
            }
            operation.DependsOn = resolved.OrderBy(value => value).ToArray();
            if (unresolved.Count > 0 && operation.Validation != null) operation.Validation.Warnings = operation.Validation.Warnings.Concat(new[] { $"External dependencies not produced by this transaction require bound-project resolution: {string.Join(", ", unresolved.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))}." }).ToArray();
            dependencies[operation.Sequence] = resolved;
        }
        var ready = new SortedSet<int>(dependencies.Where(pair => pair.Value.Count == 0).Select(pair => pair.Key));
        int order = 0;
        while (ready.Count > 0)
        {
            int sequence = ready.Min; ready.Remove(sequence); bySequence[sequence].ExecutionOrder = ++order;
            foreach (KeyValuePair<int, HashSet<int>> pair in dependencies) if (pair.Value.Remove(sequence) && pair.Value.Count == 0 && bySequence[pair.Key].ExecutionOrder == 0) ready.Add(pair.Key);
        }
        int[] cyclic = bySequence.Values.Where(item => item.ExecutionOrder == 0).Select(item => item.Sequence).OrderBy(value => value).ToArray();
        if (cyclic.Length > 0) throw new InvalidOperationException($"PLC code transaction dependency cycle detected among operations {string.Join(", ", cyclic)}. Combine mutually dependent blocks in one SCL source or remove the cycle.");
    }

    internal static void RequireState(PlcCodeTransaction transaction, string state)
    {
        if (!string.Equals(transaction.State, state, StringComparison.Ordinal)) throw new InvalidOperationException($"Transaction '{transaction.TransactionId}' must be in state '{state}', but is '{transaction.State}'.");
    }

    internal static IReadOnlyList<PlcBlockIdentity> OperationTargets(PlcCodeTransactionOperation operation) => operation.TargetSnapshots.Count > 0 ? operation.TargetSnapshots.Select(item => item.Target).ToArray() : operation.Target == null ? Array.Empty<PlcBlockIdentity>() : new[] { operation.Target };
    internal static IReadOnlyList<PlcCodeTransactionTargetSnapshot> EffectiveSnapshots(PlcCodeTransactionOperation operation) => operation.TargetSnapshots.Count > 0 ? operation.TargetSnapshots : operation.Target == null ? Array.Empty<PlcCodeTransactionTargetSnapshot>() : new[] { new PlcCodeTransactionTargetSnapshot { Target = operation.Target, Action = operation.Action, BaselineHash = operation.BaselineHash, RollbackMethod = operation.RollbackMethod, CanonicalResultHash = operation.CanonicalResultHash, RestorationVerified = operation.RestorationVerified, ActualRestoredHash = operation.ActualRestoredHash } };
    internal static string TypeIdentityHash(string groupPath, string typeName) => PlcWorkflowPrimitives.Hash(Encoding.UTF8.GetBytes("TYPE|" + PlcWorkflowPrimitives.NormalizeTypeGroupPath(groupPath) + "|" + typeName.Trim()));
}
