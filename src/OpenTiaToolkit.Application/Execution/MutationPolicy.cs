using System;
using System.Text.Json;
using OpenTiaToolkit.Contracts.Commands;

namespace OpenTiaToolkit.Application.Execution;

public sealed class MutationPolicy : IMutationPolicy
{
    public PolicyDecision Evaluate(CommandDescriptor descriptor, CommandInvocation invocation)
    {
        if (descriptor.Risk == CommandRisk.ReadOnly)
        {
            return PolicyDecision.Allow(preview: false);
        }

        bool preview = ReadBoolean(invocation, "dry-run", defaultValue: descriptor.SupportsPreview);
        if (preview)
        {
            return PolicyDecision.Allow(preview: true);
        }

        if (!ReadBoolean(invocation, "confirm", false) &&
            !ReadBoolean(invocation, "apply", false) &&
            !ReadBoolean(invocation, "confirm-destructive", false))
        {
            return PolicyDecision.Deny(
                $"Command '{descriptor.Name}' mutates engineering state and requires an explicit apply or confirmation argument.");
        }

        if (descriptor.Risk == CommandRisk.Destructive &&
            !ReadBoolean(invocation, "confirm", false) &&
            !ReadBoolean(invocation, "confirm-destructive", false))
        {
            return PolicyDecision.Deny($"Destructive command '{descriptor.Name}' requires explicit confirmation.");
        }

        return PolicyDecision.Allow(preview: false);
    }

    private static bool ReadBoolean(CommandInvocation invocation, string name, bool defaultValue)
    {
        if (!invocation.Arguments.TryGetValue(name, out JsonElement value))
        {
            return defaultValue;
        }

        if (value.ValueKind == JsonValueKind.True) return true;
        if (value.ValueKind == JsonValueKind.False) return false;
        return value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out bool parsed)
            ? parsed
            : defaultValue;
    }
}
