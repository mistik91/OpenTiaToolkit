using System.Collections.Generic;
using OpenTiaToolkit.Core.Models;

namespace OpenTiaToolkit.Core.Abstractions;

public interface IPlcSimulationRuntime
{
    PlcSimulatorCapabilityDiscovery Discover(string plcName, string? instanceName = null, bool refreshTags = false);
    PlcBehaviorExecutionResult Execute(PlcBehaviorVector vector, PlcSimulatorCapabilityDiscovery capability, IReadOnlyList<string> allowedTagOwners);
    bool AreTagsAbsent(PlcSimulatorCapabilityDiscovery capability, IReadOnlyList<string> tagOwners, out string failure);
}
