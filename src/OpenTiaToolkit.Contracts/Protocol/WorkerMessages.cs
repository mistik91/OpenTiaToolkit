using System;
using System.Text.Json;
using OpenTiaToolkit.Contracts.Commands;
using OpenTiaToolkit.Contracts.Outcomes;

namespace OpenTiaToolkit.Contracts.Protocol;

public enum WorkerOperation
{
    Health,
    Catalog,
    Execute,
    Shutdown
}

public sealed class WorkerRequest
{
    public string ProtocolVersion { get; set; } = ProtocolConstants.Version;
    public string RequestId { get; set; } = Guid.NewGuid().ToString("N");
    public WorkerOperation Operation { get; set; }
    public CommandInvocation? Invocation { get; set; }
}

public sealed class WorkerResponse
{
    public string ProtocolVersion { get; set; } = ProtocolConstants.Version;
    public string RequestId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public CommandOutcome? Outcome { get; set; }
    public CommandDescriptor[]? Catalog { get; set; }
    public ErrorDetail? Error { get; set; }
    public JsonElement? Data { get; set; }
}
