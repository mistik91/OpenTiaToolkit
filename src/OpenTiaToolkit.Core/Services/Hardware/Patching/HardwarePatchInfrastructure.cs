using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Models;
using Siemens.Engineering.HW;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Services.Hardware.Patching
{
    internal static class HardwarePatchRules
    {
        public static string NormalizeOperation(string operation) => operation.Trim().Replace("_", "-").ToLowerInvariant();
        public static void RequireText(string? value, string property)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"{property} is required.");
        }
        public static void RequireDestructiveConfirmation(bool confirmed, string operation)
        {
            if (!confirmed) throw new InvalidOperationException($"{operation} requires confirmDestructive=true.");
        }
        public static void AddResultReference(string? id, HardwareResolvedTarget value, IDictionary<string, HardwareResolvedTarget> references)
        {
            HardwareResultReferenceRules.Add(id, value, references);
        }
        public static HardwareOperationResult Success(int index, HardwarePatchOperation operation, HardwareObjectRef? target, string message, object? data = null) =>
            new HardwareOperationResult
            {
                Index = index,
                Op = operation.Op,
                Success = true,
                Message = message,
                Target = target,
                ResultId = operation.ResultId,
                Data = data
            };
    }

    internal static class HardwareResultReferenceRules
    {
        public static void Add<T>(string? id, T value, IDictionary<string, T> references)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            if (references.ContainsKey(id!)) throw new InvalidOperationException($"Duplicate resultId: {id}");
            references[id!] = value;
        }
    }

    internal static class HardwarePatchTransactionPolicy
    {
        public static bool ShouldCommit(bool dryRun, bool allOperationsSucceeded) => !dryRun && allOperationsSucceeded;
    }

    internal static class HardwarePatchSafetyPolicy
    {
        public static bool RequiresConfirmation(
            HardwarePatchOperationRegistration registration,
            HardwarePatchOperation operation,
            bool dryRun) => registration.Destructive ||
                (registration.ApplyOnlyConfirmation && !dryRun && string.IsNullOrWhiteSpace(operation.ExpectedDataHex));
    }

    internal sealed class HardwarePatchExecutionContext
    {
        public HardwarePatchExecutionContext(
            HardwareEngineeringContext engineering,
            V21HardwareAdapter adapter,
            HardwareTargetResolver resolver,
            HardwareTopologyReader topology,
            bool dryRun,
            bool confirmDestructive)
        {
            Engineering = engineering;
            Adapter = adapter;
            Resolver = resolver;
            Topology = topology;
            DryRun = dryRun;
            ConfirmDestructive = confirmDestructive;
        }

        public HardwareEngineeringContext Engineering { get; }
        public V21HardwareAdapter Adapter { get; }
        public HardwareTargetResolver Resolver { get; }
        public HardwareTopologyReader Topology { get; }
        public bool DryRun { get; }
        public bool ConfirmDestructive { get; }
        public IDictionary<string, HardwareResolvedTarget> References { get; } =
            new Dictionary<string, HardwareResolvedTarget>(StringComparer.OrdinalIgnoreCase);

        public HardwareResolvedTarget Resolve(HardwarePatchOperation operation) => Resolver.ResolveOperationTarget(operation, References);
        public void Register(string? id, HardwareResolvedTarget target) => HardwarePatchRules.AddResultReference(id, target, References);
        public HardwareOperationResult Success(int index, HardwarePatchOperation operation, HardwareObjectRef? target, string message, object? data = null) =>
            HardwarePatchRules.Success(index, operation, target, message, data);

        public void EnsureExpectedAttributes(HardwareObject target, IDictionary<string, object?>? expected) =>
            EnsureExpected(expected, name => Adapter.GetAttribute(target, name), name => $"Hardware attribute '{name}' differs from expectedAttributes.");
        public void EnsureExpectedFeatureAttributes(HardwareObject target, string service, IDictionary<string, object?>? expected) =>
            EnsureExpected(expected, name => Adapter.GetFeatureAttribute(target, service, name), name => $"{service} feature attribute '{name}' differs from expectedAttributes.");
        public void EnsureExpectedNetworkItemAttributes(HardwareObject target, string collection, int index, IDictionary<string, object?>? expected) =>
            EnsureExpected(expected, name => Adapter.GetNetworkItemAttribute(target, collection, index, name), name => $"{collection} item attribute '{name}' differs from expectedAttributes.");

        public object? TryGetAttribute(HardwareObject target, string name) => TryGet(() => Adapter.GetAttribute(target, name));
        public object? TryGetFeatureAttribute(HardwareObject target, string service, string name) => TryGet(() => Adapter.GetFeatureAttribute(target, service, name));
        public object? TryGetNetworkItemAttribute(HardwareObject target, string collection, int index, string name) => TryGet(() => Adapter.GetNetworkItemAttribute(target, collection, index, name));

        private static object? TryGet(Func<object?> read) { try { return read(); } catch { return null; } }
        private static void EnsureExpected(IDictionary<string, object?>? expected, Func<string, object?> read, Func<string, string> error)
        {
            if (expected == null) return;
            foreach (var item in expected)
            {
                var value = HardwareAttributeValueConverter.ConvertJsonValue(item.Value);
                if (!HardwareAttributeValueConverter.ExpectedValueMatches(read(item.Key), value))
                    throw new InvalidOperationException(error(item.Key));
            }
        }
    }

    internal interface IHardwarePatchOperationHandler
    {
        IReadOnlyCollection<string> Operations { get; }
        HardwareOperationResult Execute(string operation, int index, HardwarePatchOperation request, HardwarePatchExecutionContext context);
    }

    internal sealed class HardwarePatchOperationRegistration
    {
        public HardwarePatchOperationRegistration(string name, IHardwarePatchOperationHandler handler, bool destructive, bool applyOnlyConfirmation)
        {
            Name = name;
            Handler = handler;
            Destructive = destructive;
            ApplyOnlyConfirmation = applyOnlyConfirmation;
        }
        public string Name { get; }
        public IHardwarePatchOperationHandler Handler { get; }
        public bool Destructive { get; }
        public bool ApplyOnlyConfirmation { get; }
    }

    internal sealed class HardwarePatchOperationCatalog
    {
        private static readonly HashSet<string> Destructive = new HashSet<string>(new[]
        {
            "erase-drive-telegram", "disconnect-node", "disconnect-io-connector", "delete-device", "delete-item"
        }, StringComparer.OrdinalIgnoreCase);
        private readonly IReadOnlyDictionary<string, HardwarePatchOperationRegistration> _registrations;

        public HardwarePatchOperationCatalog(IEnumerable<IHardwarePatchOperationHandler> handlers)
        {
            var registrations = new Dictionary<string, HardwarePatchOperationRegistration>(StringComparer.OrdinalIgnoreCase);
            foreach (var handler in handlers)
                foreach (var declared in handler.Operations)
                {
                    var name = HardwarePatchRules.NormalizeOperation(declared);
                    if (registrations.ContainsKey(name)) throw new InvalidOperationException($"Duplicate hardware patch operation registration: {name}");
                    registrations[name] = new HardwarePatchOperationRegistration(name, handler, Destructive.Contains(name), name == "set-gsd-parameter-data");
                }
            _registrations = registrations;
        }

        public IReadOnlyCollection<string> OperationNames => _registrations.Keys.ToArray();
        public HardwarePatchOperationRegistration Resolve(string operation)
        {
            var normalized = HardwarePatchRules.NormalizeOperation(operation);
            return _registrations.TryGetValue(normalized, out var registration)
                ? registration
                : throw new InvalidOperationException($"Unsupported hardware patch operation: {operation}");
        }
    }

    internal sealed class HardwarePatchWorkflow
    {
        private readonly HardwareEngineeringContext _engineering;
        private readonly V21HardwareAdapter _adapter;
        private readonly HardwareTargetResolver _resolver;
        private readonly HardwareTopologyReader _topology;
        private readonly HardwarePatchOperationCatalog _catalog;
        private readonly ILogger? _logger;

        public HardwarePatchWorkflow(HardwareEngineeringContext engineering, V21HardwareAdapter adapter, HardwareTargetResolver resolver, HardwareTopologyReader topology, ILogger? logger)
        {
            _engineering = engineering;
            _adapter = adapter;
            _resolver = resolver;
            _topology = topology;
            _logger = logger;
            _catalog = new HardwarePatchOperationCatalog(new IHardwarePatchOperationHandler[]
            {
                new HardwareStructurePatchHandler(), new HardwareAttributePatchHandler(), new HardwareDrivePatchHandler(),
                new HardwareGsdPatchHandler(), new HardwareNetworkPatchHandler()
            });
        }

        public Task<HardwarePatchResult> ApplyAsync(IReadOnlyList<HardwarePatchOperation> operations, bool dryRun, bool confirmDestructive, string? description) =>
            Task.Run(() => Apply(operations, dryRun, confirmDestructive, description));

        internal HardwarePatchResult Apply(IReadOnlyList<HardwarePatchOperation> operations, bool dryRun, bool confirmDestructive, string? description)
        {
            if (operations == null || operations.Count == 0)
                throw new ArgumentException("At least one hardware operation is required.", nameof(operations));
            var result = new HardwarePatchResult
            {
                DryRun = dryRun,
                TransactionDescription = string.IsNullOrWhiteSpace(description) ? "OpenTiaToolkit hardware patch" : description
            };
            var execution = new HardwarePatchExecutionContext(_engineering, _adapter, _resolver, _topology, dryRun, confirmDestructive);
            try
            {
                var project = _engineering.RequireProject();
                using (var access = _engineering.RequirePortal().ExclusiveAccess(result.TransactionDescription))
                using (var transaction = access.Transaction(project, result.TransactionDescription))
                {
                    for (var index = 0; index < operations.Count; index++)
                    {
                        var operation = operations[index];
                        if (operation == null || string.IsNullOrWhiteSpace(operation.Op))
                            throw new InvalidOperationException($"Operation {index} has no op value.");
                        var registration = _catalog.Resolve(operation.Op);
                        if (HardwarePatchSafetyPolicy.RequiresConfirmation(registration, operation, dryRun))
                            HardwarePatchRules.RequireDestructiveConfirmation(confirmDestructive, registration.Name);
                        result.Operations.Add(registration.Handler.Execute(registration.Name, index, operation, execution));
                    }
                    if (HardwarePatchTransactionPolicy.ShouldCommit(dryRun, allOperationsSucceeded: true))
                    { transaction.CommitOnDispose(); result.Applied = true; }
                    else result.RolledBack = true;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Hardware patch failed");
                result.Applied = false;
                result.RolledBack = true;
                result.SafetyBlocked = true;
                result.SafetyMessage = ex.Message;
            }
            return result;
        }
    }
}
