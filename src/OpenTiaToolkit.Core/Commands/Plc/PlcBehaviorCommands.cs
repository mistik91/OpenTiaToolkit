using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Services;

namespace OpenTiaToolkit.Core.Commands.Plc;

public sealed class ValidatePlcBehaviorVectorCommand : ICommand
{
    private readonly PlcBehaviorVectorService _service;
    public ValidatePlcBehaviorVectorCommand(PlcBehaviorVectorService service) => _service = service;
    public string Name => "validate-plc-behavior-vector";
    public string Description => "Validates a versioned deterministic PLCSIM behavior vector without changing TIA or simulator state";
    public CommandSchema Schema => new CommandSchema(Description, new[] { new CommandParameter("vector-path", CommandParameterType.Path, "Behavior vector JSON file", true) });
    public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
    {
        PlcBehaviorVectorValidationResult result = _service.Validate(args.GetString("vector-path"));
        return Task.FromResult(result.Valid ? CommandResult.CreateSuccess(result, "Behavior vector is structurally valid; disposable tag ownership is enforced when it is paired with a probe artifact.") : CommandResult.CreateFailure("Behavior vector is invalid: " + string.Join("; ", result.Errors), result));
    }
}

public sealed class DiscoverPlcSimulatorCapabilitiesCommand : ICommand
{
    private readonly IPlcSimulationRuntime _runtime;
    public DiscoverPlcSimulatorCapabilitiesCommand(IPlcSimulationRuntime runtime) => _runtime = runtime;
    public string Name => "discover-plc-simulator-capabilities";
    public string Description => "Discovers the installed PLCSIM Advanced Runtime API and selects a powered local simulator by exact controller or instance identity";
    public CommandSchema Schema => new CommandSchema(Description, new[]
    {
        new CommandParameter("plc-name", CommandParameterType.String, "Exact TIA PLC device name", true),
        new CommandParameter("instance-name", CommandParameterType.String, "Optional exact PLCSIM Advanced instance name", false),
        new CommandParameter("refresh-tags", CommandParameterType.Boolean, "Refresh and count the downloaded runtime tag list", false, false)
    });
    public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
    {
        PlcSimulatorCapabilityDiscovery result = _runtime.Discover(args.GetString("plc-name"), args.GetString("instance-name"), args.GetBool("refresh-tags"));
        return Task.FromResult(result.Available ? CommandResult.CreateSuccess(result, "A compatible local PLCSIM Advanced instance was selected.") : CommandResult.CreateFailure("PLCSIM capability discovery failed: " + result.Failure, result));
    }
}

public sealed class ValidatePlcBehaviorPackageCommand : ICommand
{
    private readonly PlcDocumentWorkflowService _service;
    public ValidatePlcBehaviorPackageCommand(PlcDocumentWorkflowService service) => _service = service;
    public string Name => "validate-plc-behavior-package";
    public string Description => "Validates a versioned multi-artifact disposable behavior package and its required contract DB and scheduler OB";
    public CommandSchema Schema => new CommandSchema(Description, new[] { new CommandParameter("package-path", CommandParameterType.Path, "Behavior probe package JSON file", true) });
    public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
    {
        PlcCodeArtifactValidationResult result = _service.ValidateBehaviorPackageArtifact(args.GetString("package-path"));
        return Task.FromResult(CommandResult.CreateSuccess(result, "Behavior probe package and every referenced code artifact are valid."));
    }
}

public sealed class QueryPlcBehaviorEvidenceCommand : ICommand
{
    private readonly PlcDocumentWorkflowService _service;
    public QueryPlcBehaviorEvidenceCommand(PlcDocumentWorkflowService service) => _service = service;
    public string Name => "query-plc-behavior-evidence";
    public string Description => "Finds behavior evidence matching the exact code signature, TIA/PLC environment, vector hash, and simulator fingerprint";
    public CommandSchema Schema => Common(includeExecution: false);
    public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
    {
        PlcCodeEvidenceQueryResult result = await _service.QueryBehaviorEvidenceAsync(args.GetString("plc-name"), args.GetString("capability-ids"), args.GetString("package-path"), args.GetString("vector-path"), args.GetString("instance-name")).ConfigureAwait(false);
        return CommandResult.CreateSuccess(result, result.Qualified ? "Exact compatible behavioral evidence was found." : "No exact compatible behavioral evidence was found.");
    }
    internal static CommandSchema Common(bool includeExecution)
    {
        var parameters = new System.Collections.Generic.List<CommandParameter>
        {
            new CommandParameter("plc-name", CommandParameterType.String, "Exact PLC device name", true),
            new CommandParameter("capability-ids", CommandParameterType.String, "Comma-separated current Create capability id for every declared probe object", true),
            new CommandParameter("package-path", CommandParameterType.Path, "Versioned behavior probe package manifest listing all disposable code, contract DB, and scheduler artifacts", true),
            new CommandParameter("vector-path", CommandParameterType.Path, "Versioned behavior vector JSON", true),
            new CommandParameter("instance-name", CommandParameterType.String, "Optional exact PLCSIM Advanced instance name", false)
        };
        if (includeExecution)
        {
            parameters.Add(new CommandParameter("compile-policy", CommandParameterType.String, "NoNewErrors (default) or CleanCompile", false, "NoNewErrors"));
            parameters.Add(new CommandParameter("maximum-new-warnings", CommandParameterType.Integer, "Optional maximum warning increase", false));
            parameters.Add(new CommandParameter("accept-unknown-impact", CommandParameterType.Boolean, "Accept a reviewed Unknown impact fingerprint", false, false));
            parameters.Add(new CommandParameter("only-changes", CommandParameterType.Boolean, "Use software-changes downloads for probe and cleanup", false, true));
            parameters.Add(new CommandParameter("force", CommandParameterType.Boolean, "Run despite exact existing behavioral evidence", false, false));
            parameters.Add(new CommandParameter("confirm", CommandParameterType.Boolean, "Confirm temporary project mutation, simulator download/stimulation, and mandatory cleanup", true));
        }
        return new CommandSchema(includeExecution ? "Runs a transaction-bound disposable code probe in PLCSIM Advanced and promotes evidence only after behavior and cleanup proof" : "Queries exact behavioral evidence", parameters);
    }
}

public sealed class RunPlcCodeBehaviorProbeCommand : ICommand
{
    private readonly PlcDocumentWorkflowService _service;
    public RunPlcCodeBehaviorProbeCommand(PlcDocumentWorkflowService service) => _service = service;
    public string Name => "run-plc-code-behavior-probe";
    public string Description => "Runs a disposable code probe through compile, simulator-only download, deterministic stimulation/observation, rollback, cleanup download, and online comparison";
    public CommandSchema Schema => QueryPlcBehaviorEvidenceCommand.Common(includeExecution: true);
    public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
    {
        if (!args.GetBool("confirm")) return CommandResult.CreateFailure("Behavioral probing requires confirm=true.");
        int? maximumWarnings = args.Has("maximum-new-warnings") ? args.GetInt("maximum-new-warnings") : (int?)null;
        PlcCodeBehaviorProbeRunResult result = await _service.RunBehaviorProbeAsync(args.GetString("plc-name"), args.GetString("capability-ids"), args.GetString("package-path"), args.GetString("vector-path"), args.GetString("instance-name"), args.GetString("compile-policy"), maximumWarnings, args.GetBool("accept-unknown-impact"), args.GetBool("only-changes", true), args.GetBool("force")).ConfigureAwait(false);
        if (result.ReusedEvidence) return CommandResult.CreateSuccess(result, "Exact verified behavioral evidence already exists; no mutation or simulator stimulation was performed.");
        if (result.Evidence.Status == "Verified") return CommandResult.CreateSuccess(result, "Behavior matched the vector and both offline and simulator cleanup were proven; exact evidence was promoted.");
        return CommandResult.CreateFailure(result.Evidence.Status == "Quarantined" ? "Behavior probe was quarantined because complete cleanup was not proven; use recover-plc-code-behavior-probe." : "Behavior did not qualify, but offline and simulator cleanup were proven.", result);
    }
}

public sealed class RecoverPlcCodeBehaviorProbeCommand : ICommand
{
    private readonly PlcDocumentWorkflowService _service;
    public RecoverPlcCodeBehaviorProbeCommand(PlcDocumentWorkflowService service) => _service = service;
    public string Name => "recover-plc-code-behavior-probe";
    public string Description => "Recovers an interrupted behavior probe, removes its disposable objects offline and from PLCSIM, and never promotes its behavior evidence";
    public CommandSchema Schema => new CommandSchema(Description, new[]
    {
        new CommandParameter("evidence-id", CommandParameterType.String, "Behavior evidence id in Running or Quarantined state", true),
        new CommandParameter("only-changes", CommandParameterType.Boolean, "Use a software-changes cleanup download", false, true),
        new CommandParameter("confirm", CommandParameterType.Boolean, "Confirm mandatory offline and simulator cleanup", true)
    });
    public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
    {
        if (!args.GetBool("confirm")) return CommandResult.CreateFailure("Behavior probe recovery requires confirm=true.");
        PlcCodeBehaviorProbeRunResult result = await _service.RecoverBehaviorProbeAsync(args.GetString("evidence-id"), args.GetBool("only-changes", true)).ConfigureAwait(false);
        return result.Evidence.CleanupProven ? CommandResult.CreateSuccess(result, "Interrupted behavior probe recovered; evidence remains Failed and is not reusable.") : CommandResult.CreateFailure("Behavior probe recovery remains incomplete and quarantined.", result);
    }
}
