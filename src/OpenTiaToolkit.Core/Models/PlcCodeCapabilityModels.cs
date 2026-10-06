using System;
using System.Collections.Generic;

namespace OpenTiaToolkit.Core.Models;

public sealed class PlcCodeCapabilityRequest
{
    public string PlcName { get; set; } = string.Empty;
    public string ObjectKind { get; set; } = "Block";
    public string ObjectName { get; set; } = string.Empty;
    public string GroupPath { get; set; } = string.Empty;
    public string Operation { get; set; } = "Modify";
    public string? BlockType { get; set; }
    public string? RequestedLanguage { get; set; }
    public string? RequestedFormat { get; set; }
    public bool PreferSourceControl { get; set; }
}

public sealed class PlcCodeCapabilityDiscovery
{
    public string SchemaVersion { get; set; } = "1.0";
    public string CapabilityId { get; set; } = string.Empty;
    public DateTimeOffset ObservedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public string Fingerprint { get; set; } = string.Empty;
    public PlcCodeCapabilityRequest Request { get; set; } = new PlcCodeCapabilityRequest();
    public PlcCodeEnvironmentEvidence Environment { get; set; } = new PlcCodeEnvironmentEvidence();
    public PlcCodeTargetEvidence Target { get; set; } = new PlcCodeTargetEvidence();
    public PlcCodeSafetyEvidence Safety { get; set; } = new PlcCodeSafetyEvidence();
    public PlcCodeVciEvidence Vci { get; set; } = new PlcCodeVciEvidence();
    public PlcCodeImportExportEvidence ImportExport { get; set; } = new PlcCodeImportExportEvidence();
    public string SelectedRoute { get; set; } = "Blocked";
    public IReadOnlyList<PlcCodeRouteDecision> Routes { get; set; } = Array.Empty<PlcCodeRouteDecision>();
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
}

public sealed class PlcCodeEnvironmentEvidence
{
    public int? TiaProcessId { get; set; }
    public string TiaVersion { get; set; } = string.Empty;
    public string TiaBuild { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string ProjectPath { get; set; } = string.Empty;
    public string PlcName { get; set; } = string.Empty;
    public string PlcFamily { get; set; } = string.Empty;
    public string PlcTypeIdentifier { get; set; } = string.Empty;
    public string PlcOrderNumber { get; set; } = string.Empty;
}

public sealed class PlcCodeTargetEvidence
{
    public bool Exists { get; set; }
    public bool IdentityAmbiguous { get; set; }
    public bool ExactIdentityResolved { get; set; } = true;
    public string IdentityIssue { get; set; } = string.Empty;
    public string ObjectKind { get; set; } = string.Empty;
    public string ObjectName { get; set; } = string.Empty;
    public string GroupPath { get; set; } = string.Empty;
    public string ObjectType { get; set; } = string.Empty;
    public string ProgrammingLanguage { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public bool IsReadOnly { get; set; }
}

public sealed class PlcCodeSafetyEvidence
{
    public bool ServiceAvailable { get; set; }
    public bool IsFailSafeTarget { get; set; }
    public bool PasswordSet { get; set; }
    public bool LoggedOn { get; set; }
    public bool MutationBlocked { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class PlcCodeVciEvidence
{
    public bool Available { get; set; }
    public string WorkspaceName { get; set; } = string.Empty;
    public bool ExactMappingFound { get; set; }
    public string MappingFileBasePath { get; set; } = string.Empty;
    public string MappingFormat { get; set; } = string.Empty;
    public string MappingStatus { get; set; } = string.Empty;
    public IReadOnlyList<string> SupportedFormats { get; set; } = Array.Empty<string>();
    public string ProbeError { get; set; } = string.Empty;
}

public sealed class PlcCodeImportExportEvidence
{
    public bool DocumentRouteObserved { get; set; }
    public bool SclSourceRouteObserved { get; set; }
    public bool SimaticMlRouteObserved { get; set; }
    public string DocumentProbe { get; set; } = string.Empty;
    public string SclProbe { get; set; } = string.Empty;
    public string SimaticMlProbe { get; set; } = string.Empty;
}

public sealed class PlcCodeRouteDecision
{
    public string Route { get; set; } = string.Empty;
    public int Rank { get; set; }
    public bool Eligible { get; set; }
    public string Reason { get; set; } = string.Empty;
}
