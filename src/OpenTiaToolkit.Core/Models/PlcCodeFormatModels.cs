using System;
using System.Collections.Generic;

namespace OpenTiaToolkit.Core.Models;

public sealed class PlcCodeFormatCapability
{
    public string Format { get; set; } = string.Empty;
    public string[] Extensions { get; set; } = Array.Empty<string>();
    public string AuthoringStatus { get; set; } = string.Empty;
    public string MutationRoute { get; set; } = string.Empty;
    public bool OfflineValidation { get; set; }
    public bool TransactionalApply { get; set; }
    public string[] SupportedObjects { get; set; } = Array.Empty<string>();
    public string[] RequiredVerification { get; set; } = Array.Empty<string>();
    public string[] KnownLimitations { get; set; } = Array.Empty<string>();
}

public sealed class PlcCodeArtifactValidationResult
{
    public bool Valid { get; set; }
    public string Format { get; set; } = string.Empty;
    public string Classification { get; set; } = "verified";
    public string InputPath { get; set; } = string.Empty;
    public string InputHash { get; set; } = string.Empty;
    public string QualificationSignature { get; set; } = string.Empty;
    public bool HasUtf8Bom { get; set; }
    public IReadOnlyList<PlcCodeDeclaredObject> DeclaredObjects { get; set; } = Array.Empty<PlcCodeDeclaredObject>();
    public IReadOnlyList<PlcCodeValidationFinding> Findings { get; set; } = Array.Empty<PlcCodeValidationFinding>();
    public IReadOnlyList<string> Dependencies { get; set; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
    public IReadOnlyList<string> Errors { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
}

public sealed class PlcCodeDeclaredObject
{
    public string Name { get; set; } = string.Empty;
    public string ObjectType { get; set; } = string.Empty;
    public string? Number { get; set; }
    public int Line { get; set; }
}

public sealed class PlcCodeValidationFinding
{
    public string Construct { get; set; } = string.Empty;
    public string Classification { get; set; } = string.Empty;
    public int? Line { get; set; }
    public string Message { get; set; } = string.Empty;
}

public sealed class PlcCodeChangeContract
{
    public string SchemaVersion { get; set; } = "1.0";
    public string Mode { get; set; } = "CreateOnly";
    public string BaselineHash { get; set; } = string.Empty;
    public string CandidateHash { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;
    public IReadOnlyList<string> AllowedSections { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> ChangedSections { get; set; } = Array.Empty<string>();
}
