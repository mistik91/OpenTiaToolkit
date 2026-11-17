using OpenTiaToolkit.Core.Models;
using System;
using System.Collections.Generic;

namespace OpenTiaToolkit.Core.Services;

internal interface IHmiDiscreteAlarmAccess
{
    bool IsAvailable { get; }
    bool IsReadOnly { get; }
    IReadOnlyList<HmiDiscreteAlarmSnapshot> List(bool includeValidation);
    // Complete name/ID inventory and full snapshots only for exact requested names.
    HmiDiscreteAlarmPreflightInventory ReadPreflight(IReadOnlyCollection<string> alarmNames);
    HmiDiscreteAlarmSnapshot? FindExact(string name, bool includeValidation);
    bool AlarmClassExists(string name);
    bool TagExists(string name);
    Func<string, bool> CreateTagLookup(IReadOnlyList<string> references);
    bool CultureExists(string cultureName);
    bool IdExists(uint id, string? exceptName = null);
    HmiDiscreteAlarmSnapshot CreateAndApply(HmiDiscreteAlarmChangeRequest request);
    HmiDiscreteAlarmSnapshot Apply(string name, HmiDiscreteAlarmChangeRequest request);
    void DeleteExact(string name);
    IReadOnlyList<HmiDiscreteAlarmCrossReference> GetCrossReferences(string name, out string? limitation);
    void ExecuteTransaction(string description, bool commit, Action action);
}
