using System;
using System.Collections.Generic;
using System.Text.Json;
using OpenTiaToolkit.Contracts.Commands;

namespace OpenTiaToolkit.Contracts.Blueprints;

public sealed class EngineeringBlueprint
{
    public string SchemaVersion { get; set; } = "1.0";
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? RequiredWorker { get; set; }
    public IReadOnlyList<BlueprintStep> Steps { get; set; } = Array.Empty<BlueprintStep>();
}

public sealed class BlueprintStep
{
    public string Id { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public IReadOnlyDictionary<string, JsonElement> Arguments { get; set; } =
        new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
    public bool ContinueOnError { get; set; }
}

public sealed class BlueprintPlannedStep
{
    public string Id { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public CommandRisk Risk { get; set; }
    public bool SupportsPreview { get; set; }
    public IReadOnlyList<string> ArgumentNames { get; set; } = Array.Empty<string>();
}

public sealed class BlueprintPlan
{
    public string PlanId { get; set; } = string.Empty;
    public string BlueprintName { get; set; } = string.Empty;
    public string BlueprintHash { get; set; } = string.Empty;
    public string StateFingerprint { get; set; } = string.Empty;
    public string ApplyToken { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public CommandRisk MaximumRisk { get; set; }
    public IReadOnlyList<BlueprintPlannedStep> Steps { get; set; } = Array.Empty<BlueprintPlannedStep>();
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
}

public sealed class BlueprintStepOutcome
{
    public string Id { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public bool Success { get; set; }
    public bool TransportSuccess { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
    public JsonElement? Data { get; set; }
}

public sealed class BlueprintApplyResult
{
    public string PlanId { get; set; } = string.Empty;
    public bool Applied { get; set; }
    public bool StateMatched { get; set; }
    public IReadOnlyList<BlueprintStepOutcome> Steps { get; set; } = Array.Empty<BlueprintStepOutcome>();
}
