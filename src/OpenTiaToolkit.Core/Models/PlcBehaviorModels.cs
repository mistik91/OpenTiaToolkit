using System;
using System.Collections.Generic;
using System.Text.Json;

namespace OpenTiaToolkit.Core.Models;

public sealed class PlcBehaviorProbePackage
{
    public string SchemaVersion { get; set; } = "1.0";
    public string PackageId { get; set; } = string.Empty;
    public IReadOnlyList<PlcBehaviorProbeArtifact> Artifacts { get; set; } = Array.Empty<PlcBehaviorProbeArtifact>();
}

public sealed class PlcBehaviorProbeArtifact
{
    public string CodeFormat { get; set; } = "auto";
    public string InputPath { get; set; } = string.Empty;
    public string GroupPath { get; set; } = string.Empty;
    public string TypeGroupPath { get; set; } = string.Empty;
}

public sealed class PlcBehaviorVector
{
    public string SchemaVersion { get; set; } = "1.0";
    public string VectorId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public IReadOnlyList<PlcBehaviorWrite> Reset { get; set; } = Array.Empty<PlcBehaviorWrite>();
    public IReadOnlyList<PlcBehaviorStep> Steps { get; set; } = Array.Empty<PlcBehaviorStep>();
    public IReadOnlyList<PlcBehaviorWrite> Cleanup { get; set; } = Array.Empty<PlcBehaviorWrite>();
}

public sealed class PlcBehaviorStep
{
    public string Name { get; set; } = string.Empty;
    public IReadOnlyList<PlcBehaviorWrite> Writes { get; set; } = Array.Empty<PlcBehaviorWrite>();
    public int SettleMilliseconds { get; set; } = 50;
    public IReadOnlyList<PlcBehaviorAssertion> Assertions { get; set; } = Array.Empty<PlcBehaviorAssertion>();
}

public sealed class PlcBehaviorWrite
{
    public string Tag { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public JsonElement Value { get; set; }
}

public sealed class PlcBehaviorAssertion
{
    public string Tag { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Operator { get; set; } = "Equal";
    public JsonElement Expected { get; set; }
    public double? Tolerance { get; set; }
    public int TimeoutMilliseconds { get; set; } = 1000;
    public int PollMilliseconds { get; set; } = 25;
    public int StableMilliseconds { get; set; }
}

public sealed class PlcBehaviorVectorValidationResult
{
    public bool Valid => Errors.Count == 0;
    public string VectorHash { get; set; } = string.Empty;
    public PlcBehaviorVector? Vector { get; set; }
    public IReadOnlyList<string> Errors { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> ReferencedTags { get; set; } = Array.Empty<string>();
}

public sealed class PlcSimulatorInstanceCapability
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ControllerName { get; set; } = string.Empty;
    public string CpuType { get; set; } = string.Empty;
    public string ArticleNumber { get; set; } = string.Empty;
    public string OperatingState { get; set; } = string.Empty;
    public IReadOnlyList<string> ControllerAddresses { get; set; } = Array.Empty<string>();
    public int TagCount { get; set; }
    public string Fingerprint { get; set; } = string.Empty;
}

public sealed class PlcSimulatorCapabilityDiscovery
{
    public string SchemaVersion { get; set; } = "1.0";
    public DateTimeOffset ObservedAtUtc { get; set; }
    public bool Available { get; set; }
    public string ApiVersion { get; set; } = string.Empty;
    public string ApiPath { get; set; } = string.Empty;
    public string RequestedPlcName { get; set; } = string.Empty;
    public string RequestedInstanceName { get; set; } = string.Empty;
    public IReadOnlyList<PlcSimulatorInstanceCapability> Instances { get; set; } = Array.Empty<PlcSimulatorInstanceCapability>();
    public PlcSimulatorInstanceCapability? SelectedInstance { get; set; }
    public string SelectionReason { get; set; } = string.Empty;
    public string Failure { get; set; } = string.Empty;
}

public sealed class PlcBehaviorObservation
{
    public DateTimeOffset ObservedAtUtc { get; set; }
    public string Tag { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public bool Matched { get; set; }
}

public sealed class PlcBehaviorAssertionResult
{
    public string Tag { get; set; } = string.Empty;
    public string Operator { get; set; } = string.Empty;
    public string Expected { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public long ElapsedMilliseconds { get; set; }
    public IReadOnlyList<PlcBehaviorObservation> Observations { get; set; } = Array.Empty<PlcBehaviorObservation>();
    public string Failure { get; set; } = string.Empty;
}

public sealed class PlcBehaviorStepResult
{
    public string Name { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public IReadOnlyList<string> WrittenTags { get; set; } = Array.Empty<string>();
    public IReadOnlyList<PlcBehaviorAssertionResult> Assertions { get; set; } = Array.Empty<PlcBehaviorAssertionResult>();
}

public sealed class PlcBehaviorExecutionResult
{
    public bool Passed { get; set; }
    public bool ResetApplied { get; set; }
    public bool CleanupApplied { get; set; }
    public IReadOnlyList<PlcBehaviorStepResult> Steps { get; set; } = Array.Empty<PlcBehaviorStepResult>();
    public string Failure { get; set; } = string.Empty;
}

public sealed class PlcCodeBehaviorEvidence
{
    public string SchemaVersion { get; set; } = "1.0";
    public string PlcName { get; set; } = string.Empty;
    public string VectorId { get; set; } = string.Empty;
    public string VectorHash { get; set; } = string.Empty;
    public string SimulatorFingerprint { get; set; } = string.Empty;
    public PlcSimulatorCapabilityDiscovery? Simulator { get; set; }
    public PlcSoftwareCompareResult? BaselineCompare { get; set; }
    public PlcSoftwareDownloadResult? ProbeDownload { get; set; }
    public PlcOnlineStatusResult? OnlineStatus { get; set; }
    public PlcSoftwareCompareResult? ProbeCompare { get; set; }
    public PlcBehaviorExecutionResult? Execution { get; set; }
    public PlcSoftwareDownloadResult? CleanupDownload { get; set; }
    public PlcSoftwareCompareResult? CleanupCompare { get; set; }
    public bool SimulatorCleanupProven { get; set; }
    public string LifecycleState { get; set; } = "Created";
}

public sealed class PlcCodeBehaviorProbeRunResult
{
    public bool ReusedEvidence { get; set; }
    public PlcCodeEvidenceRecord Evidence { get; set; } = new PlcCodeEvidenceRecord();
    public PlcCodeTransactionApplyResult? ApplyResult { get; set; }
    public PlcCodeTransactionApplyResult? CleanupResult { get; set; }
}
