using System;
using System.Collections.Generic;

namespace OpenTiaToolkit.Core.Models;

public sealed class HmiTagExportResult
{
    public string HmiName { get; set; } = "";
    public string TablePath { get; set; } = "";
    public string OutputPath { get; set; } = "";
    public long OutputBytes { get; set; }
    public IReadOnlyList<string> ArchiveEntries { get; set; } = Array.Empty<string>();
    public bool SupportsToolkitImport { get; set; }
    public int? ImportableTagCount { get; set; }
    public string? ImportLimitation { get; set; }
}
