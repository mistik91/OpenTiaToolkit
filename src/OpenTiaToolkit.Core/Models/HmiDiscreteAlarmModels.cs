using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OpenTiaToolkit.Core.Models;

public sealed class HmiDiscreteAlarmValidationDiagnostic
{
    public string PropertyName { get; set; } = string.Empty;
    public IReadOnlyList<string> Errors { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
}

public sealed class HmiDiscreteAlarmLocalizedText
{
    public string EventText { get; set; } = string.Empty;
    public string EventText1 { get; set; } = string.Empty;
    public string EventText2 { get; set; } = string.Empty;
    public string EventText3 { get; set; } = string.Empty;
    public string EventText4 { get; set; } = string.Empty;
    public string EventText5 { get; set; } = string.Empty;
    public string EventText6 { get; set; } = string.Empty;
    public string EventText7 { get; set; } = string.Empty;
    public string EventText8 { get; set; } = string.Empty;
    public string EventText9 { get; set; } = string.Empty;
    public string InfoText { get; set; } = string.Empty;
}

public sealed class HmiDiscreteAlarmSnapshot
{
    public string Name { get; set; } = string.Empty;
    public uint Id { get; set; }
    public string AlarmClass { get; set; } = string.Empty;
    public byte Priority { get; set; }
    public string Origin { get; set; } = string.Empty;
    public string Area { get; set; } = string.Empty;
    public string AuditClass { get; set; } = string.Empty;
    public string RaisedStateTag { get; set; } = string.Empty;
    public uint RaisedStateTagBitNumber { get; set; }
    public string TriggerMode { get; set; } = string.Empty;
    public string TriggerBitAddress { get; set; } = string.Empty;
    public string AcknowledgmentStateTag { get; set; } = string.Empty;
    public int AcknowledgmentStateTagBitNumber { get; set; }
    public string AcknowledgmentControlTag { get; set; } = string.Empty;
    public int AcknowledgmentControlTagBitNumber { get; set; }
    public IReadOnlyList<string> AlarmParameterTags { get; set; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, HmiDiscreteAlarmLocalizedText> Texts { get; set; } =
        new SortedDictionary<string, HmiDiscreteAlarmLocalizedText>(StringComparer.Ordinal);
    public IReadOnlyList<HmiDiscreteAlarmValidationDiagnostic> Validation { get; set; } =
        Array.Empty<HmiDiscreteAlarmValidationDiagnostic>();
    public string ContentHash { get; set; } = string.Empty;
}

public sealed class HmiDiscreteAlarmTextPatch
{
    public string? EventText { get; set; }
    public string? EventText1 { get; set; }
    public string? EventText2 { get; set; }
    public string? EventText3 { get; set; }
    public string? EventText4 { get; set; }
    public string? EventText5 { get; set; }
    public string? EventText6 { get; set; }
    public string? EventText7 { get; set; }
    public string? EventText8 { get; set; }
    public string? EventText9 { get; set; }
    public string? InfoText { get; set; }

    [JsonIgnore]
    internal ISet<string> PresentProperties { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}

public sealed class HmiDiscreteAlarmChangeRequest
{
    public string Name { get; set; } = string.Empty;
    public string ExpectedState { get; set; } = "any";
    public string? ExpectedHash { get; set; }
    public uint? Id { get; set; }
    public string? AlarmClass { get; set; }
    public byte? Priority { get; set; }
    public string? Origin { get; set; }
    public string? Area { get; set; }
    public string? AuditClass { get; set; }
    public string? RaisedStateTag { get; set; }
    public uint? RaisedStateTagBitNumber { get; set; }
    public string? TriggerMode { get; set; }
    public string? AcknowledgmentStateTag { get; set; }
    public int? AcknowledgmentStateTagBitNumber { get; set; }
    public string? AcknowledgmentControlTag { get; set; }
    public int? AcknowledgmentControlTagBitNumber { get; set; }
    public IReadOnlyList<string>? AlarmParameterTags { get; set; }
    public IReadOnlyDictionary<string, HmiDiscreteAlarmTextPatch>? Texts { get; set; }

    [JsonIgnore]
    internal ISet<string> PresentProperties { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}

public sealed class HmiDiscreteAlarmDocument
{
    public string SchemaVersion { get; set; } = string.Empty;
    public IReadOnlyList<HmiDiscreteAlarmChangeRequest> Alarms { get; set; } = Array.Empty<HmiDiscreteAlarmChangeRequest>();
}

public sealed class HmiDiscreteAlarmPropertyDiff
{
    public string Property { get; set; } = string.Empty;
    public object? Before { get; set; }
    public object? After { get; set; }
}

public sealed class HmiDiscreteAlarmPlanItem
{
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public HmiDiscreteAlarmSnapshot? Before { get; set; }
    public HmiDiscreteAlarmSnapshot? ProposedAfter { get; set; }
    public HmiDiscreteAlarmSnapshot? Persisted { get; set; }
    public IReadOnlyList<HmiDiscreteAlarmPropertyDiff> Differences { get; set; } = Array.Empty<HmiDiscreteAlarmPropertyDiff>();
    public IReadOnlyList<string> Errors { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
}

public sealed class HmiDiscreteAlarmTransactionInfo
{
    public bool Created { get; set; }
    public bool Committed { get; set; }
    public bool RolledBack { get; set; }
}

public sealed class HmiDiscreteAlarmApplyResult
{
    public bool DryRun { get; set; }
    public bool Success { get; set; }
    public bool SafetyBlocked { get; set; }
    public bool ProjectHasUnsavedChanges { get; set; }
    public string? ErrorCode { get; set; }
    public string Message { get; set; } = string.Empty;
    public IReadOnlyList<HmiDiscreteAlarmPlanItem> Alarms { get; set; } = Array.Empty<HmiDiscreteAlarmPlanItem>();
    public HmiDiscreteAlarmTransactionInfo Transaction { get; set; } = new();
}

public sealed class HmiDiscreteAlarmCrossReference
{
    public string Name { get; set; } = string.Empty;
    public string? Path { get; set; }
    public string? Type { get; set; }
    public string? Detail { get; set; }
}

public sealed class HmiDiscreteAlarmDeleteResult
{
    public bool DryRun { get; set; }
    public bool Success { get; set; }
    public bool Deleted { get; set; }
    public bool SafetyBlocked { get; set; }
    public string? ErrorCode { get; set; }
    public string Message { get; set; } = string.Empty;
    public HmiDiscreteAlarmSnapshot? Before { get; set; }
    public IReadOnlyList<HmiDiscreteAlarmCrossReference> CrossReferences { get; set; } = Array.Empty<HmiDiscreteAlarmCrossReference>();
    public string? CrossReferenceLimitation { get; set; }
    public HmiDiscreteAlarmTransactionInfo Transaction { get; set; } = new();
}
