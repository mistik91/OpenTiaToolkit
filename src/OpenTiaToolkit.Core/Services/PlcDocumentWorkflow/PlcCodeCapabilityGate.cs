using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTiaToolkit.Core.Models;

namespace OpenTiaToolkit.Core.Services;

internal sealed class PlcCodeCapabilityGate
{
    private readonly PlcCodeCapabilityDiscoveryService? _discovery;
    internal PlcCodeCapabilityGate(PlcCodeCapabilityDiscoveryService? discovery) => _discovery = discovery;

    internal void RequireOperation(PlcCodeTransaction transaction, string operationKind, string objectKind, string? objectName, string? objectType, string groupPath)
    {
        if (transaction.CapabilityBindings.Count == 0 && string.IsNullOrWhiteSpace(transaction.CapabilityId)) return;
        if (_discovery == null) throw new InvalidOperationException("Capability discovery is unavailable in this host.");
        PlcCodeCapabilityBinding? binding = transaction.CapabilityBindings.FirstOrDefault(item => item.ObjectKind.Equals(objectKind, StringComparison.OrdinalIgnoreCase) && item.ObjectName.Equals(objectName ?? string.Empty, StringComparison.OrdinalIgnoreCase) && item.GroupPath.Trim().Trim('/').Equals((groupPath ?? string.Empty).Trim().Trim('/'), StringComparison.OrdinalIgnoreCase));
        PlcCodeCapabilityDiscovery capability = binding != null ? _discovery.Get(binding.CapabilityId) : _discovery.Get(transaction.CapabilityId!);
        RequireOperation(capability, operationKind, objectKind, objectName, objectType, groupPath);
    }

    internal void RequireOperation(PlcCodeCapabilityDiscovery capability, string operationKind, string objectKind, string? objectName, string? objectType, string groupPath)
    {
        if (_discovery == null) throw new InvalidOperationException("Capability discovery is unavailable in this host.");
        _discovery.RequireOperationRoute(capability, operationKind);
        if (!capability.Request.ObjectKind.Equals(objectKind, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException($"Capability discovery is bound to object kind '{capability.Request.ObjectKind}', but the operation targets '{objectKind}'.");
        if (!capability.Request.ObjectName.Equals(objectName ?? string.Empty, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException($"Capability discovery is bound to '{capability.Request.ObjectName}', but the operation targets '{objectName}'.");
        if (!string.IsNullOrWhiteSpace(capability.Request.BlockType) && !BlockTypesCompatible(capability.Request.BlockType!, objectType ?? string.Empty)) throw new InvalidOperationException($"Capability discovery is bound to object type '{capability.Request.BlockType}', but the operation declares '{objectType}'.");
        string expectedGroup = (capability.Request.GroupPath ?? string.Empty).Trim().Trim('/'); string actualGroup = (groupPath ?? string.Empty).Trim().Trim('/');
        if (!expectedGroup.Equals(actualGroup, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException($"Capability discovery is bound to group '{expectedGroup}', but the operation targets '{actualGroup}'.");
    }

    internal async Task VerifyCurrentAsync(PlcCodeTransaction transaction)
    {
        if (transaction.CapabilityBindings.Count == 0 && string.IsNullOrWhiteSpace(transaction.CapabilityId)) return;
        if (_discovery == null) throw new InvalidOperationException("Capability discovery is unavailable in this host.");
        if (transaction.CapabilityBindings.Count > 0)
        {
            foreach (PlcCodeCapabilityBinding binding in transaction.CapabilityBindings)
            {
                PlcCodeCapabilityDiscovery current = await _discovery.ValidateCurrentAsync(binding.CapabilityId, transaction.PlcName).ConfigureAwait(false);
                if (!PlcWorkflowPrimitives.FixedEquals(binding.Fingerprint, current.Fingerprint) || !string.Equals(binding.Route, current.SelectedRoute, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException($"Transaction capability binding '{binding.CapabilityId}' does not match the current discovery manifest.");
            }
            return;
        }
        PlcCodeCapabilityDiscovery legacy = await _discovery.ValidateCurrentAsync(transaction.CapabilityId!, transaction.PlcName).ConfigureAwait(false);
        if (!PlcWorkflowPrimitives.FixedEquals(transaction.CapabilityFingerprint, legacy.Fingerprint) || !string.Equals(transaction.CapabilityRoute, legacy.SelectedRoute, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The transaction capability binding does not match the current discovery manifest.");
    }

    internal PlcCodeCapabilityDiscoveryService RequireDiscovery() => _discovery ?? throw new InvalidOperationException("Capability discovery is unavailable in this host.");
    internal PlcCodeCapabilityDiscoveryService? Discovery => _discovery;
    private static bool BlockTypesCompatible(string expected, string actual) => expected.Equals(actual, StringComparison.OrdinalIgnoreCase) || expected.Equals("DB", StringComparison.OrdinalIgnoreCase) && (actual.Equals("GlobalDB", StringComparison.OrdinalIgnoreCase) || actual.Equals("InstanceDB", StringComparison.OrdinalIgnoreCase));
}
