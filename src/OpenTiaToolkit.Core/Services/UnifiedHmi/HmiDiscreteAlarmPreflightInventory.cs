using OpenTiaToolkit.Core.Models;
using System.Collections.Generic;

namespace OpenTiaToolkit.Core.Services;

internal sealed class HmiDiscreteAlarmPreflightInventory
{
    internal HmiDiscreteAlarmPreflightInventory(IReadOnlyDictionary<string, uint> idsByName, IReadOnlyList<HmiDiscreteAlarmSnapshot> requestedSnapshots)
    {
        IdsByName = idsByName;
        RequestedSnapshots = requestedSnapshots;
    }

    internal IReadOnlyDictionary<string, uint> IdsByName { get; }
    internal IReadOnlyList<HmiDiscreteAlarmSnapshot> RequestedSnapshots { get; }
}
