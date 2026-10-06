using System;
using System.Collections.Generic;

namespace OpenTiaToolkit.Core.Models;

public sealed class PlcCodeEvidenceCompatibility
{
    public string SchemaVersion { get; set; } = "1.0";
    public string Fingerprint { get; set; } = string.Empty;
    public string Scope { get; set; } = "Environment";
    public string TiaVersion { get; set; } = string.Empty;
    public string TiaBuild { get; set; } = string.Empty;
    public string PlcFamily { get; set; } = string.Empty;
    public string PlcTypeIdentifier { get; set; } = string.Empty;
    public string PlcOrderNumber { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
    public string CodeFormat { get; set; } = string.Empty;
    public string SafetyContext { get; set; } = "Standard";
    public string ProjectFingerprint { get; set; } = string.Empty;
    public IReadOnlyList<string> ObjectShapes { get; set; } = Array.Empty<string>();
}

public sealed class PlcCodeEvidenceRecord
{
    public string SchemaVersion { get; set; } = "1.0";
    public int RegistryVersion { get; set; } = 1;
    public string EvidenceId { get; set; } = string.Empty;
    public string EvidenceKind { get; set; } = "Qualification";
    public string Status { get; set; } = "Running";
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string CodeFormat { get; set; } = string.Empty;
    public string ValidatorCatalogVersion { get; set; } = string.Empty;
    public string QualificationSignature { get; set; } = string.Empty;
    public string InputHash { get; set; } = string.Empty;
    public PlcCodeEvidenceCompatibility Compatibility { get; set; } = new PlcCodeEvidenceCompatibility();
    public IReadOnlyList<string> CapabilityIds { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> CapabilityFingerprints { get; set; } = Array.Empty<string>();
    public IReadOnlyList<PlcCodeDeclaredObject> DeclaredObjects { get; set; } = Array.Empty<PlcCodeDeclaredObject>();
    public IReadOnlyList<string> Constructs { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Dependencies { get; set; } = Array.Empty<string>();
    public string TransactionId { get; set; } = string.Empty;
    public PlcCodeTransactionCompileEvidence? BaselineCompilation { get; set; }
    public PlcCodeTransactionCompileEvidence? ProbeCompilation { get; set; }
    public PlcCodeTransactionCompileEvidence? CleanupCompilation { get; set; }
    public IReadOnlyList<string> CanonicalResultHashes { get; set; } = Array.Empty<string>();
    public bool CleanupProven { get; set; }
    public PlcCodeBehaviorEvidence? Behavior { get; set; }
    public string Failure { get; set; } = string.Empty;
}

public sealed class PlcCodeEvidenceQueryResult
{
    public string QualificationSignature { get; set; } = string.Empty;
    public PlcCodeEvidenceCompatibility Compatibility { get; set; } = new PlcCodeEvidenceCompatibility();
    public IReadOnlyList<PlcCodeEvidenceRecord> Matches { get; set; } = Array.Empty<PlcCodeEvidenceRecord>();
    public bool Qualified => Matches.Count > 0;
}

public sealed class PlcCodeProbeRunResult
{
    public bool ReusedEvidence { get; set; }
    public bool ProbeApplied { get; set; }
    public bool CleanupProven { get; set; }
    public PlcCodeEvidenceRecord Evidence { get; set; } = new PlcCodeEvidenceRecord();
    public PlcCodeTransactionApplyResult? ApplyResult { get; set; }
    public PlcCodeTransactionApplyResult? CleanupResult { get; set; }
}
