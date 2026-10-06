using System;
using System.Collections.Generic;
using System.Text.Json;

namespace OpenTiaToolkit.Contracts.Outcomes;

public enum ErrorCode
{
    None,
    InvalidRequest,
    ValidationFailed,
    PolicyDenied,
    NotConnected,
    NotFound,
    Conflict,
    SiemensRejected,
    PartialFailure,
    Cancelled,
    TimedOut,
    WorkerUnavailable,
    ProtocolMismatch,
    InternalError
}

public sealed class ErrorDetail
{
    public ErrorCode Code { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Target { get; set; }
    public bool Retryable { get; set; }
    public IReadOnlyDictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
}

public sealed class CommandOutcome
{
    public string ProtocolVersion { get; set; } = Protocol.ProtocolConstants.Version;
    public string CorrelationId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public JsonElement? Data { get; set; }
    public ErrorDetail? Error { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset CompletedAtUtc { get; set; }
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
}
