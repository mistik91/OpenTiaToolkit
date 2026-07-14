using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Services;

namespace OpenTiaToolkit.Core.Commands.Plc;

public sealed class RunPlcCodeProbeCommand : ICommand
{
    private readonly PlcDocumentWorkflowService _service;
    public RunPlcCodeProbeCommand(PlcDocumentWorkflowService service) => _service = service;
    public string Name => "run-plc-code-probe";
    public string Description => "Runs an exact all-create SCL or S7DCL probe, verifies it, removes every probe artifact, and records versioned evidence";
    public CommandSchema Schema => new CommandSchema(Description, new[]
    {
        new CommandParameter("plc-name", CommandParameterType.String, "Exact PLC device name", true),
        new CommandParameter("capability-ids", CommandParameterType.String, "Comma-separated Create capability id for every declared probe object", true),
        new CommandParameter("code-format", CommandParameterType.String, "auto, scl, or s7dcl", false, "auto"),
        new CommandParameter("input-path", CommandParameterType.Path, "Disposable SCL or S7DCL probe artifact", true),
        new CommandParameter("group-path", CommandParameterType.String, "Exact target block group", false),
        new CommandParameter("type-group-path", CommandParameterType.String, "Exact target PLC type group for SCL TYPE declarations", false),
        new CommandParameter("compile-policy", CommandParameterType.String, "NoNewErrors (default) or CleanCompile", false, "NoNewErrors"),
        new CommandParameter("maximum-new-warnings", CommandParameterType.Integer, "Optional maximum warning increase accepted by the probe", false),
        new CommandParameter("accept-unknown-impact", CommandParameterType.Boolean, "Explicitly accept reviewed unresolved impact in the disposable target", false, false),
        new CommandParameter("force", CommandParameterType.Boolean, "Run again even when exact verified evidence already exists", false, false),
        new CommandParameter("confirm", CommandParameterType.Boolean, "Confirm temporary live mutation and mandatory cleanup", true)
    });
    public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
    {
        if (!args.GetBool("confirm")) return CommandResult.CreateFailure("Running a live PLC code probe requires confirm=true.");
        int? maximumNewWarnings = args.Has("maximum-new-warnings") ? args.GetInt("maximum-new-warnings") : (int?)null;
        PlcCodeProbeRunResult result = await _service.RunProbeAsync(args.GetString("plc-name"), args.GetString("capability-ids"), args.GetString("code-format") ?? "auto", args.GetString("input-path"), args.GetString("group-path"), args.GetString("type-group-path"), args.GetString("compile-policy"), maximumNewWarnings, args.GetBool("accept-unknown-impact"), args.GetBool("force")).ConfigureAwait(false);
        if (result.ReusedEvidence) return CommandResult.CreateSuccess(result, "Exact verified evidence already exists; no TIA mutation was performed.");
        return result.Evidence.Status == "Verified"
            ? CommandResult.CreateSuccess(result, "Probe compiled and exported canonically, then every disposable artifact was removed with compile proof. Exact evidence was promoted.")
            : CommandResult.CreateFailure(result.Evidence.Status == "Quarantined" ? "Probe evidence was quarantined because cleanup was not proven." : "Probe did not qualify, but cleanup was proven.", result);
    }
}

public sealed class QueryPlcCodeEvidenceCommand : ICommand
{
    private readonly PlcDocumentWorkflowService _service;
    public QueryPlcCodeEvidenceCommand(PlcDocumentWorkflowService service) => _service = service;
    public string Name => "query-plc-code-evidence";
    public string Description => "Finds verified evidence matching the current exact TIA/PLC/route compatibility fingerprint and normalized code construct signature";
    public CommandSchema Schema => new CommandSchema(Description, new[]
    {
        new CommandParameter("capability-ids", CommandParameterType.String, "Comma-separated current capability ids for every declared object", true),
        new CommandParameter("code-format", CommandParameterType.String, "auto, scl, or s7dcl", false, "auto"),
        new CommandParameter("input-path", CommandParameterType.Path, "SCL or S7DCL artifact to qualify", true)
    });
    public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
    {
        PlcCodeEvidenceQueryResult result = await _service.QueryProbeEvidenceAsync(args.GetString("capability-ids"), args.GetString("code-format") ?? "auto", args.GetString("input-path")).ConfigureAwait(false);
        return CommandResult.CreateSuccess(result, result.Qualified ? "Exact compatible verified evidence was found." : "No exact compatible verified evidence was found; offline classification is unchanged.");
    }
}

public sealed class BeginPromotedPlcCodeTransactionCommand : ICommand
{
    private readonly PlcDocumentWorkflowService _service;
    public BeginPromotedPlcCodeTransactionCommand(PlcDocumentWorkflowService service) => _service = service;
    public string Name => "begin-promoted-plc-code-transaction";
    public string Description => "Begins a production transaction bound to exact verified disposable evidence, the intended artifact, current capabilities, and explicit disposition decisions";
    public CommandSchema Schema => new CommandSchema(Description, new[]
    {
        new CommandParameter("plc-name", CommandParameterType.String, "Exact PLC device name", true),
        new CommandParameter("evidence-id", CommandParameterType.String, "Exact reusable Verified qualification evidence id", true),
        new CommandParameter("capability-ids", CommandParameterType.String, "Fresh intended-target capability id for every declared production object", true),
        new CommandParameter("code-format", CommandParameterType.String, "auto, scl, or s7dcl", false, "auto"),
        new CommandParameter("input-path", CommandParameterType.Path, "Exact production artifact whose normalized shape matches the evidence", true),
        new CommandParameter("description", CommandParameterType.String, "Human-readable production engineering intent", true),
        new CommandParameter("compile-policy", CommandParameterType.String, "NoNewErrors (default) or CleanCompile", false, "NoNewErrors"),
        new CommandParameter("maximum-new-warnings", CommandParameterType.Integer, "Optional maximum warning increase accepted by final compile", false),
        new CommandParameter("save-decision", CommandParameterType.String, "Defer or ManualAfterVerification; this command never silently saves the project", true),
        new CommandParameter("source-control-decision", CommandParameterType.String, "None or VCISeparateAfterSave; direct import never impersonates VCI synchronization", true)
    });
    public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
    {
        int? maximumNewWarnings = args.Has("maximum-new-warnings") ? args.GetInt("maximum-new-warnings") : (int?)null;
        PlcCodeTransaction result = await _service.BeginPromotedTransactionAsync(args.GetString("plc-name"), args.GetString("evidence-id"), args.GetString("capability-ids"), args.GetString("code-format") ?? "auto", args.GetString("input-path"), args.GetString("description"), args.GetString("compile-policy"), maximumNewWarnings, args.GetString("save-decision"), args.GetString("source-control-decision")).ConfigureAwait(false);
        return CommandResult.CreateSuccess(result, "Evidence-bound production transaction started. Add only the bound artifact, then plan normally; impact review and rollback gates remain mandatory.");
    }
}

public sealed class GetPlcCodeEvidenceCommand : ICommand
{
    private readonly PlcCodeEvidenceRegistry _registry;
    public GetPlcCodeEvidenceCommand(PlcCodeEvidenceRegistry registry) => _registry = registry;
    public string Name => "get-plc-code-evidence";
    public string Description => "Gets one persistent PLC code qualification evidence manifest";
    public CommandSchema Schema => new CommandSchema(Description, new[] { new CommandParameter("evidence-id", CommandParameterType.String, "Evidence id", true) });
    public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger) => Task.FromResult(CommandResult.CreateSuccess(_registry.Get(args.GetString("evidence-id")), "PLC code evidence loaded."));
}

public sealed class ListPlcCodeEvidenceCommand : ICommand
{
    private readonly PlcCodeEvidenceRegistry _registry;
    public ListPlcCodeEvidenceCommand(PlcCodeEvidenceRegistry registry) => _registry = registry;
    public string Name => "list-plc-code-evidence";
    public string Description => "Lists versioned PLC code evidence, including failed and cleanup-quarantined probes";
    public CommandSchema Schema => new CommandSchema(Description, new[]
    {
        new CommandParameter("status", CommandParameterType.String, "Optional Running, Verified, Failed, or Quarantined filter", false),
        new CommandParameter("code-format", CommandParameterType.String, "Optional exact code-format filter", false)
    });
    public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger) => Task.FromResult(CommandResult.CreateSuccess(_registry.List(args.GetString("status"), args.GetString("code-format")), "PLC code evidence listed."));
}
