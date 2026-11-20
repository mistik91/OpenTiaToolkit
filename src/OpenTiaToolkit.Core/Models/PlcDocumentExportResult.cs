using System;
using System.Collections.Generic;

namespace OpenTiaToolkit.Core.Models;

public sealed class PlcDocumentExportResult
{
    public bool Success { get; internal set; }
    public string? SiemensState { get; internal set; }
    public IReadOnlyList<string> Diagnostics { get; internal set; } = Array.Empty<string>();
    public string Message { get; internal set; } = string.Empty;
}
