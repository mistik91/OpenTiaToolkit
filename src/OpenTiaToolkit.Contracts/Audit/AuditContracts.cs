using System;
using System.Collections.Generic;
using OpenTiaToolkit.Contracts.Commands;
using OpenTiaToolkit.Contracts.Outcomes;

namespace OpenTiaToolkit.Contracts.Audit;

public sealed class MutationAuditRecord
{
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset TimestampUtc { get; set; }
    public string CommandName { get; set; } = string.Empty;
    public CommandRisk Risk { get; set; }
    public bool Preview { get; set; }
    public bool Allowed { get; set; }
    public bool Success { get; set; }
    public ErrorCode ErrorCode { get; set; }
    public string Message { get; set; } = string.Empty;
    public IReadOnlyDictionary<string, string> RedactedArguments { get; set; } = new Dictionary<string, string>();
}
