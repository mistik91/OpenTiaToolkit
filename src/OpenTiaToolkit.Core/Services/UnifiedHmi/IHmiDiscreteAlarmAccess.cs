using OpenTiaToolkit.Core.Models;
using System;
using System.Collections.Generic;

namespace OpenTiaToolkit.Core.Services;

internal interface IHmiDiscreteAlarmAccess
{
    bool IsAvailable { get; }
    bool IsReadOnly { get; }
    IReadOnlyList<HmiDiscreteAlarmSnapshot> List(bool includeValidation);
    HmiDiscreteAlarmSnapshot? FindExact(string name, bool includeValidation);
    bool AlarmClassExists(string name);
    bool TagExists(string name);
    bool CultureExists(string cultureName);
    bool IdExists(uint id, string? exceptName = null);
    HmiDiscreteAlarmSnapshot CreateAndApply(HmiDiscreteAlarmChangeRequest request);
    HmiDiscreteAlarmSnapshot Apply(string name, HmiDiscreteAlarmChangeRequest request);
    void DeleteExact(string name);
    IReadOnlyList<HmiDiscreteAlarmCrossReference> GetCrossReferences(string name, out string? limitation);
    void ExecuteTransaction(string description, bool commit, Action action);
}
