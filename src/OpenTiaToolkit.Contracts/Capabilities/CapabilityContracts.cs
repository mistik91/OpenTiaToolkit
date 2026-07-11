using System;
using System.Collections.Generic;

namespace OpenTiaToolkit.Contracts.Capabilities;

public sealed class CapabilityProfile
{
    public string ToolkitVersion { get; set; } = string.Empty;
    public string ProtocolVersion { get; set; } = Protocol.ProtocolConstants.Version;
    public string WorkerRuntime { get; set; } = string.Empty;
    public string? TiaPortalVersion { get; set; }
    public IReadOnlyList<string> SupportedCommands { get; set; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, string> SiemensAssemblyHashes { get; set; } = new Dictionary<string, string>();
}

public sealed class PageRequest
{
    public string? ContinuationToken { get; set; }
    public int PageSize { get; set; } = 100;
}

public sealed class PageResult<T>
{
    public IReadOnlyList<T> Items { get; set; } = Array.Empty<T>();
    public string? ContinuationToken { get; set; }
    public bool HasMore { get; set; }
}
