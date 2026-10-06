using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Application.Blueprints;
using OpenTiaToolkit.Contracts.Blueprints;
using OpenTiaToolkit.Contracts.Commands;
using OpenTiaToolkit.Contracts.Outcomes;
using OpenTiaToolkit.Contracts.Protocol;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands;

namespace OpenTiaToolkit.Worker.V21;

internal sealed class BlueprintCoordinator
{
    private static readonly TimeSpan PlanLifetime = TimeSpan.FromMinutes(10);
    private static readonly JsonSerializerOptions JsonOptions = ContractJson.CreateOptions();
    private readonly IServiceProvider _services;
    private readonly Dictionary<string, StoredPlan> _plans = new Dictionary<string, StoredPlan>(StringComparer.Ordinal);
    private readonly object _sync = new object();
    private static readonly HashSet<string> ContextCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "open-tia-portal", "connect-instance", "connect-portal", "open-project", "retrieve-project", "open-plc", "open-unified-hmi"
    };

    public BlueprintCoordinator(IServiceProvider services) => _services = services;

    public async Task<BlueprintPlan> PlanAsync(string path)
    {
        EngineeringBlueprint blueprint = ReadBlueprint(path);
        WorkerCommandExecutor executor = _services.GetRequiredService<WorkerCommandExecutor>();
        IReadOnlyDictionary<string, CommandDescriptor> catalog = executor.GetCatalog()
            .ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
        ValidateBlueprint(blueprint, catalog);

        string canonical = JsonSerializer.Serialize(blueprint, JsonOptions);
        string blueprintHash = Sha256(canonical);
        string stateFingerprint = await GetStateFingerprintAsync(executor).ConfigureAwait(false);
        string planId = Guid.NewGuid().ToString("N");
        string token = RandomToken();
        DateTimeOffset expires = DateTimeOffset.UtcNow.Add(PlanLifetime);
        var steps = blueprint.Steps.Select(step =>
        {
            CommandDescriptor descriptor = catalog[step.Command];
            return new BlueprintPlannedStep
            {
                Id = step.Id,
                Command = step.Command,
                Risk = descriptor.Risk,
                SupportsPreview = descriptor.SupportsPreview,
                ArgumentNames = step.Arguments.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray()
            };
        }).ToArray();
        var plan = new BlueprintPlan
        {
            PlanId = planId,
            BlueprintName = blueprint.Name,
            BlueprintHash = blueprintHash,
            StateFingerprint = stateFingerprint,
            ApplyToken = token,
            ExpiresAtUtc = expires,
            MaximumRisk = steps.Max(step => step.Risk),
            Steps = steps,
            Warnings = steps.Where(step => !step.SupportsPreview && step.Risk != CommandRisk.ReadOnly)
                .Select(step => $"Step '{step.Id}' does not expose a command-level dry-run; the blueprint plan is its preview boundary.")
                .ToArray()
        };
        lock (_sync)
        {
            PruneExpired();
            _plans[token] = new StoredPlan(plan, blueprint);
        }
        return plan;
    }

    public EngineeringBlueprint Validate(string path)
    {
        EngineeringBlueprint blueprint = ReadBlueprint(path);
        WorkerCommandExecutor executor = _services.GetRequiredService<WorkerCommandExecutor>();
        ValidateBlueprint(blueprint, executor.GetCatalog().ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase));
        return blueprint;
    }

    public async Task<BlueprintApplyResult> ApplyAsync(string token)
    {
        StoredPlan stored;
        lock (_sync)
        {
            PruneExpired();
            if (!_plans.TryGetValue(token, out stored!))
                throw new InvalidOperationException("The apply token is invalid, expired, or already consumed. Create a new blueprint plan.");
            _plans.Remove(token);
        }

        WorkerCommandExecutor executor = _services.GetRequiredService<WorkerCommandExecutor>();
        string currentFingerprint = await GetStateFingerprintAsync(executor).ConfigureAwait(false);
        if (!FixedEquals(stored.Plan.StateFingerprint, currentFingerprint))
            throw new InvalidOperationException("TIA/project state changed after planning. The apply token is stale; create and review a new plan.");

        return await BlueprintStepExecutor.ExecuteAsync(
            stored.Plan.PlanId, stored.Blueprint.Steps, executor.ExecuteAsync).ConfigureAwait(false);
    }

    private static EngineeringBlueprint ReadBlueprint(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("blueprint-path is required.");
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("Blueprint file was not found.", fullPath);
        EngineeringBlueprint? blueprint = JsonSerializer.Deserialize<EngineeringBlueprint>(File.ReadAllText(fullPath), JsonOptions);
        return blueprint ?? throw new InvalidDataException("Blueprint JSON was empty.");
    }

    private static void ValidateBlueprint(EngineeringBlueprint blueprint, IReadOnlyDictionary<string, CommandDescriptor> catalog)
    {
        if (!string.Equals(blueprint.SchemaVersion, "1.0", StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported blueprint schema '{blueprint.SchemaVersion}'. Expected '1.0'.");
        if (string.IsNullOrWhiteSpace(blueprint.Name)) throw new InvalidDataException("Blueprint name is required.");
        if (!string.IsNullOrWhiteSpace(blueprint.RequiredWorker) &&
            !string.Equals(blueprint.RequiredWorker, "V21", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Blueprint requires worker '{blueprint.RequiredWorker}', but this package is V21.");
        if (blueprint.Steps.Count == 0) throw new InvalidDataException("Blueprint must contain at least one step.");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (BlueprintStep step in blueprint.Steps)
        {
            if (string.IsNullOrWhiteSpace(step.Id) || !ids.Add(step.Id))
                throw new InvalidDataException("Every blueprint step requires a unique non-empty id.");
            if (!catalog.TryGetValue(step.Command, out CommandDescriptor? descriptor))
                throw new InvalidDataException($"Blueprint step '{step.Id}' references unknown command '{step.Command}'.");
            if (step.Command.IndexOf("blueprint", StringComparison.OrdinalIgnoreCase) >= 0)
                throw new InvalidDataException("Blueprints cannot invoke blueprint orchestration commands.");
            if (descriptor.Risk == CommandRisk.ReadOnly && !ContextCommands.Contains(step.Command))
                throw new InvalidDataException($"Blueprint step '{step.Id}' is read-only. Only explicit session/target context commands may precede desired-state mutations.");
            if (step.Arguments.Keys.Any(name => string.Equals(name, "confirm", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException($"Blueprint step '{step.Id}' must not embed confirmation. Confirmation is supplied only after token validation.");
        }
        if (!blueprint.Steps.Any(step => catalog[step.Command].Risk != CommandRisk.ReadOnly))
            throw new InvalidDataException("Blueprint must contain at least one desired-state mutation after any context-establishment steps.");
    }

    private static async Task<string> GetStateFingerprintAsync(WorkerCommandExecutor executor)
    {
        CommandOutcome portal = await executor.ExecuteAsync(new CommandInvocation { CommandName = "portal-status" }).ConfigureAwait(false);
        CommandOutcome project = await executor.ExecuteAsync(new CommandInvocation { CommandName = "get-project-info" }).ConfigureAwait(false);
        string material = (portal.Data?.GetRawText() ?? portal.Message) + "\n" +
            (project.Data?.GetRawText() ?? project.Message);
        return Sha256(material);
    }

    private void PruneExpired()
    {
        foreach (string key in _plans.Where(pair => pair.Value.Plan.ExpiresAtUtc <= DateTimeOffset.UtcNow)
                     .Select(pair => pair.Key).ToArray())
            _plans.Remove(key);
    }

    private static string Sha256(string value)
    {
        using (SHA256 sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", string.Empty).ToLowerInvariant();
    }

    private static string RandomToken()
    {
        var bytes = new byte[32];
        using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static bool FixedEquals(string left, string right)
    {
        byte[] a = Encoding.UTF8.GetBytes(left);
        byte[] b = Encoding.UTF8.GetBytes(right);
        if (a.Length != b.Length) return false;
        int difference = 0;
        for (int index = 0; index < a.Length; index++) difference |= a[index] ^ b[index];
        return difference == 0;
    }

    private sealed class StoredPlan
    {
        public StoredPlan(BlueprintPlan plan, EngineeringBlueprint blueprint) { Plan = plan; Blueprint = blueprint; }
        public BlueprintPlan Plan { get; }
        public EngineeringBlueprint Blueprint { get; }
    }
}

internal sealed class ValidateBlueprintCommand : ICommand
{
    private readonly BlueprintCoordinator _coordinator;
    public ValidateBlueprintCommand(BlueprintCoordinator coordinator) { _coordinator = coordinator; }
    public string Name => "validate-blueprint";
    public string Description => "Validates a declarative engineering blueprint against the V21 worker command contract.";
    public CommandSchema Schema => new CommandSchema(Description, new[] { new CommandParameter("blueprint-path", CommandParameterType.Path, "Blueprint JSON file", true) });
    public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger) => Task.FromResult(CommandResult.CreateSuccess(_coordinator.Validate(args.GetString("blueprint-path")), "Blueprint is valid."));
}

internal sealed class PlanBlueprintCommand : ICommand
{
    private readonly BlueprintCoordinator _coordinator;
    public PlanBlueprintCommand(BlueprintCoordinator coordinator) { _coordinator = coordinator; }
    public string Name => "plan-blueprint";
    public string Description => "Builds a state-bound engineering plan and short-lived one-use apply token.";
    public CommandSchema Schema => new CommandSchema(Description, new[] { new CommandParameter("blueprint-path", CommandParameterType.Path, "Blueprint JSON file", true) });
    public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger) => CommandResult.CreateSuccess(await _coordinator.PlanAsync(args.GetString("blueprint-path")).ConfigureAwait(false), "Blueprint plan created. Review it before applying.");
}

internal sealed class ApplyBlueprintCommand : ICommand
{
    private readonly BlueprintCoordinator _coordinator;
    public ApplyBlueprintCommand(BlueprintCoordinator coordinator) { _coordinator = coordinator; }
    public string Name => "apply-blueprint";
    public string Description => "Applies a previously reviewed blueprint plan using its state-bound one-use token.";
    public CommandSchema Schema => new CommandSchema(Description, new[] { new CommandParameter("apply-token", CommandParameterType.String, "One-use token returned by plan-blueprint", true) });
    public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
    {
        BlueprintApplyResult result = await _coordinator.ApplyAsync(args.GetString("apply-token")).ConfigureAwait(false);
        return result.Applied ? CommandResult.CreateSuccess(result, "Blueprint applied.")
            : CommandResult.CreateFailure("Blueprint did not fully apply. Inspect step outcomes; earlier successful mutations are not rolled back.", result);
    }
}
