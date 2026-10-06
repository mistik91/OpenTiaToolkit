using System;
using System.Collections.Generic;

namespace OpenTiaToolkit.Contracts.Compatibility;

public sealed class WorkerPackageManifest
{
    public string SchemaVersion { get; set; } = "1.0";
    public string WorkerId { get; set; } = string.Empty;
    public string Executable { get; set; } = string.Empty;
    public string ProtocolVersion { get; set; } = Protocol.ProtocolConstants.Version;
    public IReadOnlyList<string> SupportedTiaVersions { get; set; } = Array.Empty<string>();
    public string CompatibilityProfile { get; set; } = string.Empty;
}

public sealed class ApiAssemblySurface
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public int PublicTypeCount { get; set; }
    public int PublicMemberCount { get; set; }
    public string PublicSurfaceSha256 { get; set; } = string.Empty;
}

public sealed class ApiSelfDescription
{
    public string SchemaVersion { get; set; } = "1.0";
    public string TiaVersion { get; set; } = string.Empty;
    public DateTimeOffset CapturedAtUtc { get; set; }
    public IReadOnlyList<ApiAssemblySurface> Assemblies { get; set; } = Array.Empty<ApiAssemblySurface>();
    public IReadOnlyDictionary<string, bool> FeatureProbes { get; set; } = new Dictionary<string, bool>();
    public string AggregateSurfaceSha256 { get; set; } = string.Empty;
}

public sealed class ApiMigrationDifference
{
    public string Path { get; set; } = string.Empty;
    public string? Baseline { get; set; }
    public string? Current { get; set; }
    public string Severity { get; set; } = string.Empty;
}

public sealed class ApiMigrationReport
{
    public bool Compatible { get; set; }
    public string BaselinePath { get; set; } = string.Empty;
    public string CurrentAggregateSurfaceSha256 { get; set; } = string.Empty;
    public IReadOnlyList<ApiMigrationDifference> Differences { get; set; } = Array.Empty<ApiMigrationDifference>();
}
