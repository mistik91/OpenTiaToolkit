using System;
using System.Collections.Generic;
using System.IO;

namespace OpenTiaToolkit.Core.Models;

public sealed class HmiTagBatchTag
{
    public string Name { get; set; } = "";
    public string DataType { get; set; } = "";
    public string? Connection { get; set; }
    public string? PlcTag { get; set; }
    public string? InitialValue { get; set; }
    internal string? WinCcMlControllerDataType { get; set; }
}

public sealed class HmiTagBatchRequest
{
    public string HmiName { get; set; } = "";
    public string TablePath { get; set; } = "";
    public bool DryRun { get; set; } = true;
    public bool Overwrite { get; set; }
    public IReadOnlyList<HmiTagBatchTag> Tags { get; set; } = Array.Empty<HmiTagBatchTag>();
    internal DirectoryInfo? ImportDirectory { get; set; }
}

public sealed class HmiTagBatchResult
{
    public string HmiName { get; set; } = "";
    public string TablePath { get; set; } = "";
    public bool DryRun { get; set; }
    public bool CreatesTable { get; set; }
    public bool RequiresOverwrite { get; set; }
    public IReadOnlyList<HmiTagBatchTag> Tags { get; set; } = Array.Empty<HmiTagBatchTag>();
    public IReadOnlyList<string> ExistingNames { get; set; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, string> PlcByConnection { get; set; } = new Dictionary<string, string>();
    public int VerifiedTagCount { get; set; }
}
