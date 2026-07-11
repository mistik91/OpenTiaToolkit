using System;
using System.Collections.Generic;

namespace OpenTiaToolkit.Core.Models;

public sealed class EngineeringInsightItem
{
    public string Domain { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public IReadOnlyDictionary<string, string?> Properties { get; set; } = new Dictionary<string, string?>();
}

public sealed class EngineeringValidationResult
{
    public string Domain { get; set; } = string.Empty;
    public bool Available { get; set; }
    public bool Valid { get; set; }
    public IReadOnlyList<string> Checks { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, string?> Properties { get; set; } = new Dictionary<string, string?>();
}
