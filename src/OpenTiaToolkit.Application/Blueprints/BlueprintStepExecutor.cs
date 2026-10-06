using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using OpenTiaToolkit.Contracts.Blueprints;
using OpenTiaToolkit.Contracts.Commands;
using OpenTiaToolkit.Contracts.Outcomes;

namespace OpenTiaToolkit.Application.Blueprints;

/// <summary>
/// Executes a reviewed blueprint without mistaking request handling for completed mutations.
/// This is sequential orchestration, not a transaction or rollback coordinator.
/// </summary>
public static class BlueprintStepExecutor
{
    public static async Task<BlueprintApplyResult> ExecuteAsync(
        string planId,
        IReadOnlyList<BlueprintStep> steps,
        Func<CommandInvocation, Task<CommandOutcome>> execute)
    {
        if (steps == null) throw new ArgumentNullException(nameof(steps));
        if (execute == null) throw new ArgumentNullException(nameof(execute));
        var outcomes = new List<BlueprintStepOutcome>();
        foreach (BlueprintStep step in steps)
        {
            var arguments = step.Arguments.ToDictionary(
                pair => pair.Key, pair => pair.Value.Clone(), StringComparer.OrdinalIgnoreCase);
            arguments["confirm"] = JsonSerializer.SerializeToElement(true);
            if (arguments.ContainsKey("dry-run")) arguments["dry-run"] = JsonSerializer.SerializeToElement(false);
            CommandOutcome outcome = await execute(new CommandInvocation
            {
                CommandName = step.Command,
                Arguments = arguments
            }).ConfigureAwait(false);
            BlueprintStepOutcome completion = Evaluate(step, outcome);
            outcomes.Add(completion);
            if (!completion.Success && !step.ContinueOnError) break;
        }
        return new BlueprintApplyResult
        {
            PlanId = planId,
            StateMatched = true,
            Applied = steps.Count > 0 && outcomes.Count == steps.Count && outcomes.All(item => item.Success),
            Steps = outcomes
        };
    }

    private static BlueprintStepOutcome Evaluate(BlueprintStep step, CommandOutcome outcome)
    {
        var result = new BlueprintStepOutcome
        {
            Id = step.Id,
            Command = step.Command,
            Success = outcome.Success,
            TransportSuccess = outcome.Success,
            Message = outcome.Message,
            ErrorCode = outcome.Error?.Code.ToString(),
            Data = outcome.Data?.Clone()
        };
        if (!outcome.Success || !outcome.Data.HasValue || outcome.Data.Value.ValueKind != JsonValueKind.Object)
            return result;

        // Only root completion fields describe this command's outcome. Nested preflight,
        // backup, and cleanup records can legitimately include dry runs or rollbacks.
        // Inspect every matching property, including duplicate/case-variant JSON fields.
        string? reason = null;
        ErrorCode failureCode = ErrorCode.PartialFailure;
        foreach (JsonProperty property in outcome.Data.Value.EnumerateObject())
        {
            string name = property.Name;
            bool blocking = name.Equals("SafetyBlocked", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("ValidationBlocked", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("RolledBack", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("DryRun", StringComparison.OrdinalIgnoreCase);
            bool required = name.Equals("Applied", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Success", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Verified", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("StateMatched", StringComparison.OrdinalIgnoreCase);
            if (!blocking && !required) continue;
            if (property.Value.ValueKind != JsonValueKind.True && property.Value.ValueKind != JsonValueKind.False)
            {
                Reject(result, ErrorCode.ValidationFailed, $"Structured completion field '{name}' is not boolean.");
                return result;
            }
            bool value = property.Value.GetBoolean();
            if ((blocking && value) || (required && !value))
            {
                ErrorCode code = name.Equals("SafetyBlocked", StringComparison.OrdinalIgnoreCase) ? ErrorCode.PolicyDenied
                    : name.Equals("ValidationBlocked", StringComparison.OrdinalIgnoreCase) ? ErrorCode.ValidationFailed
                    : ErrorCode.PartialFailure;
                if (reason == null || code == ErrorCode.PolicyDenied ||
                    (code == ErrorCode.ValidationFailed && failureCode != ErrorCode.PolicyDenied))
                {
                    failureCode = code;
                    reason = $"Structured outcome reports {name}={value.ToString().ToLowerInvariant()}; this step did not complete.";
                }
            }
        }
        if (reason != null) Reject(result, failureCode, reason);
        return result;
    }

    private static void Reject(BlueprintStepOutcome result, ErrorCode code, string reason)
    {
        result.Success = false;
        result.ErrorCode = code.ToString();
        result.Message = string.IsNullOrWhiteSpace(result.Message) ? reason : result.Message + " " + reason;
    }
}
