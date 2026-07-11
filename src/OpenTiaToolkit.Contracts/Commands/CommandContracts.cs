using System;
using System.Collections.Generic;
using System.Text.Json;

namespace OpenTiaToolkit.Contracts.Commands;

public enum CommandParameterKind
{
    String,
    Integer,
    Boolean,
    StringArray,
    ObjectArray,
    Path,
    Object
}

public enum CommandRisk
{
    ReadOnly,
    Mutating,
    Destructive,
    Online
}

public sealed class CommandParameterContract
{
    public string Name { get; set; } = string.Empty;
    public CommandParameterKind Kind { get; set; }
    public string Description { get; set; } = string.Empty;
    public bool Required { get; set; }
    public JsonElement? DefaultValue { get; set; }
}

public sealed class CommandDescriptor
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public CommandRisk Risk { get; set; }
    public bool SupportsPreview { get; set; }
    public IReadOnlyList<CommandParameterContract> Parameters { get; set; } = Array.Empty<CommandParameterContract>();
}

public sealed class CommandInvocation
{
    public string ProtocolVersion { get; set; } = Protocol.ProtocolConstants.Version;
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString("N");
    public string CommandName { get; set; } = string.Empty;
    public IReadOnlyDictionary<string, JsonElement> Arguments { get; set; } = new Dictionary<string, JsonElement>();
    public DateTimeOffset RequestedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
