using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Services;

namespace OpenTiaToolkit.Core.Commands.Plc;

public sealed class BeginPlcCodeTransactionCommand : ICommand
{
    private readonly PlcDocumentWorkflowService _service;
    public BeginPlcCodeTransactionCommand(PlcDocumentWorkflowService service) => _service = service;
    public string Name => "begin-plc-code-transaction";
    public string Description => "Begins a persistent PLC code transaction bound to the current TIA process, project, and PLC";
    public CommandSchema Schema => new CommandSchema(Description, new[]
    {
        new CommandParameter("plc-name", CommandParameterType.String, "Exact PLC device name", true),
        new CommandParameter("description", CommandParameterType.String, "Human-readable engineering intent", false),
        new CommandParameter("compile-policy", CommandParameterType.String, "NoNewErrors (default) or CleanCompile; CleanCompile requires zero errors but does not limit warnings", false, "NoNewErrors"),
        new CommandParameter("maximum-new-warnings", CommandParameterType.Integer, "Optional maximum warning increase accepted by final compile; use 0 to reject any new warnings", false),
        new CommandParameter("capability-id", CommandParameterType.String, "One live capability discovery id to bind and revalidate at plan/apply", false),
        new CommandParameter("capability-ids", CommandParameterType.String, "Comma-separated capability ids for an atomic multi-target transaction", false)
    });
    public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
    {
        int? maximumNewWarnings = args.Has("maximum-new-warnings") ? args.GetInt("maximum-new-warnings") : (int?)null;
        PlcCodeTransaction result = await _service.BeginTransactionAsync(args.GetString("plc-name"), args.GetString("description"), args.GetString("compile-policy"), maximumNewWarnings, args.GetString("capability-id"), args.GetString("capability-ids")).ConfigureAwait(false);
        return CommandResult.CreateSuccess(result, "PLC code transaction started.");
    }
}

public sealed class AddPlcCodeTransactionOperationCommand : ICommand
{
    private readonly PlcDocumentWorkflowService _service;
    public AddPlcCodeTransactionOperationCommand(PlcDocumentWorkflowService service) => _service = service;
    public string Name => "add-plc-code-transaction-operation";
    public string Description => "Adds a create-only or baseline-bound SIMATIC SD block change to a draft PLC code transaction";
    public CommandSchema Schema => new CommandSchema(Description, new[]
    {
        new CommandParameter("transaction-id", CommandParameterType.String, "Draft transaction id", true),
        new CommandParameter("import-directory", CommandParameterType.Path, "Directory containing the SIMATIC SD files", true),
        new CommandParameter("file-name", CommandParameterType.String, "Block/file name without extension", true),
        new CommandParameter("group-path", CommandParameterType.String, "Exact target block group", false),
        new CommandParameter("import-option", CommandParameterType.String, "Siemens document import option", false, "Override"),
        new CommandParameter("target-mode", CommandParameterType.String, "create-only (default) or baseline-bound", false, "create-only"),
        new CommandParameter("baseline-directory", CommandParameterType.Path, "Directory containing the untouched exported S7DCL/S7RES pair; required for baseline-bound", false),
        new CommandParameter("baseline-file-name", CommandParameterType.String, "Untouched baseline file name without extension; defaults to file-name", false),
        new CommandParameter("allowed-changes", CommandParameterType.String, "Comma-separated authorized sections: Attributes, Interface, Implementation, Resources", false),
        new CommandParameter("depends-on", CommandParameterType.String, "Optional comma-separated operation sequences that must apply first", false),
        new CommandParameter("confirm", CommandParameterType.Boolean, "Confirm adding this local manifest operation", true)
    });
    public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
    {
        if (!args.GetBool("confirm")) return Task.FromResult(CommandResult.CreateFailure("Adding a PLC code transaction operation requires confirm=true."));
        PlcCodeTransaction result = _service.AddTransactionOperation(args.GetString("transaction-id"), args.GetString("import-directory"), args.GetString("file-name"), args.GetString("group-path"), args.GetString("import-option"), args.GetString("depends-on"), args.GetString("target-mode"), args.GetString("baseline-directory"), args.GetString("baseline-file-name"), args.GetString("allowed-changes"));
        return Task.FromResult(CommandResult.CreateSuccess(result, "Operation added to the draft transaction."));
    }
}

public sealed class PlanPlcCodeTransactionCommand : ICommand
{
    private readonly PlcDocumentWorkflowService _service;
    public PlanPlcCodeTransactionCommand(PlcDocumentWorkflowService service) => _service = service;
    public string Name => "plan-plc-code-transaction";
    public string Description => "Snapshots every existing target and produces one dependency-ordered, atomic, state-bound PLC code transaction plan";
    public CommandSchema Schema => new CommandSchema(Description, new[] { new CommandParameter("transaction-id", CommandParameterType.String, "Draft transaction id", true) });
    public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
    {
        PlcCodeTransaction result = await _service.PlanTransactionAsync(args.GetString("transaction-id")).ConfigureAwait(false);
        string message = result.State == "Planned"
            ? "Transaction impact is safe and the plan is ready. Review every operation and the one-use apply token."
            : result.State == "ImpactBlocked"
                ? "Transaction planning completed, but impact findings block mutation. Review the persisted impact analysis and recreate a narrower transaction."
                : "Transaction planning completed without an apply token. Review the persisted impact analysis, then explicitly acknowledge its exact fingerprint.";
        return CommandResult.CreateSuccess(result, message);
    }
}

public sealed class GetPlcCodeImpactCommand : ICommand
{
    private readonly PlcDocumentWorkflowService _service;
    public GetPlcCodeImpactCommand(PlcDocumentWorkflowService service) => _service = service;
    public string Name => "get-plc-code-impact";
    public string Description => "Gets the semantic delta, resolved dependencies, live PLC consumers, policy status, and fingerprint for a planned PLC code transaction";
    public CommandSchema Schema => new CommandSchema(Description, new[] { new CommandParameter("transaction-id", CommandParameterType.String, "Planned or impact-review PLC code transaction id", true) });
    public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger) => Task.FromResult(CommandResult.CreateSuccess(_service.GetTransactionImpact(args.GetString("transaction-id")), "PLC code impact analysis loaded."));
}

public sealed class ApprovePlcCodeImpactCommand : ICommand
{
    private readonly PlcDocumentWorkflowService _service;
    public ApprovePlcCodeImpactCommand(PlcDocumentWorkflowService service) => _service = service;
    public string Name => "approve-plc-code-impact";
    public string Description => "Revalidates and explicitly acknowledges a fingerprint-bound ReviewRequired or Unknown PLC code impact before issuing an apply token";
    public CommandSchema Schema => new CommandSchema(Description, new[]
    {
        new CommandParameter("transaction-id", CommandParameterType.String, "ImpactReviewRequired PLC code transaction id", true),
        new CommandParameter("impact-fingerprint", CommandParameterType.String, "Exact fingerprint returned by transaction planning", true),
        new CommandParameter("accept-unknown", CommandParameterType.Boolean, "Explicitly accept unresolved impact evidence after review", false, false),
        new CommandParameter("confirm", CommandParameterType.Boolean, "Confirm the reviewed impact acknowledgement", true)
    });
    public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
    {
        if (!args.GetBool("confirm")) return CommandResult.CreateFailure("Approving PLC code impact requires confirm=true.");
        PlcCodeTransaction result = await _service.ApproveTransactionImpactAsync(args.GetString("transaction-id"), args.GetString("impact-fingerprint"), args.GetBool("accept-unknown")).ConfigureAwait(false);
        return CommandResult.CreateSuccess(result, "Impact evidence was revalidated and acknowledged. Review the new one-use apply token before mutation.");
    }
}

public sealed class AddSclSourceTransactionOperationCommand : ICommand
{
    private readonly PlcDocumentWorkflowService _service;
    public AddSclSourceTransactionOperationCommand(PlcDocumentWorkflowService service) => _service = service;
    public string Name => "add-scl-source-transaction-operation";
    public string Description => "Adds validated SCL source import and exact generated-block replacement to a persistent PLC code transaction";
    public CommandSchema Schema => new CommandSchema(Description, new[]
    {
        new CommandParameter("transaction-id", CommandParameterType.String, "Draft PLC code transaction id", true),
        new CommandParameter("source-path", CommandParameterType.Path, "UTF-8 SCL source file", true),
        new CommandParameter("group-path", CommandParameterType.String, "Target block subgroup relative to Program blocks; omit for root (PLC-prefixed software-tree paths are accepted)", false),
        new CommandParameter("type-group-path", CommandParameterType.String, "Target type subgroup relative to PLC data types; omit for root (PLC-prefixed software-tree paths are accepted)", false),
        new CommandParameter("depends-on", CommandParameterType.String, "Optional comma-separated operation sequences that must apply first", false),
        new CommandParameter("confirm", CommandParameterType.Boolean, "Confirm adding the reviewed source operation", true)
    });
    public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
    {
        if (!args.GetBool("confirm")) return Task.FromResult(CommandResult.CreateFailure("Adding an SCL transaction operation requires confirm=true."));
        PlcCodeTransaction result = _service.AddSclTransactionOperation(args.GetString("transaction-id"), args.GetString("source-path"), args.GetString("group-path"), args.GetString("type-group-path"), args.GetString("depends-on"));
        return Task.FromResult(CommandResult.CreateSuccess(result, "SCL source generation operation added to the transaction."));
    }
}

public sealed class AddSimaticMlTransactionOperationCommand : ICommand
{
    private readonly PlcDocumentWorkflowService _service;
    public AddSimaticMlTransactionOperationCommand(PlcDocumentWorkflowService service) => _service = service;
    public string Name => "add-simaticml-transaction-operation";
    public string Description => "Adds a baseline-bound SimaticML block or PLC type patch to a persistent PLC code transaction";
    public CommandSchema Schema => new CommandSchema(Description, new[]
    {
        new CommandParameter("transaction-id", CommandParameterType.String, "Draft PLC code transaction id", true),
        new CommandParameter("input-path", CommandParameterType.Path, "Patched SimaticML block or PLC type XML", true),
        new CommandParameter("baseline-path", CommandParameterType.Path, "Untouched SimaticML export of the exact target", true),
        new CommandParameter("group-path", CommandParameterType.String, "Exact target block or PLC type group", false),
        new CommandParameter("allow-flgnet-topology", CommandParameterType.Boolean, "Allow an explicitly reviewed FlgNet topology change", false, false),
        new CommandParameter("allowed-change-paths", CommandParameterType.String, "Comma-separated exact/prefix XML paths returned by validation; required and fail-closed", true),
        new CommandParameter("depends-on", CommandParameterType.String, "Optional comma-separated operation sequences that must apply first", false),
        new CommandParameter("confirm", CommandParameterType.Boolean, "Confirm adding the reviewed XML patch operation", true)
    });
    public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
    {
        if (!args.GetBool("confirm")) return Task.FromResult(CommandResult.CreateFailure("confirm=true is required to add a SimaticML transaction operation."));
        PlcCodeTransaction transaction = _service.AddSimaticMlTransactionOperation(args.GetString("transaction-id"), args.GetString("input-path"), args.GetString("baseline-path"), args.GetString("group-path"), args.GetBool("allow-flgnet-topology"), args.GetString("depends-on"), args.GetString("allowed-change-paths"));
        return Task.FromResult(CommandResult.CreateSuccess(transaction, "Baseline-bound SimaticML operation added to the transaction."));
    }
}

public sealed class AddDeletePlcTypeTransactionOperationCommand : ICommand
{
    private readonly PlcDocumentWorkflowService _service;
    public AddDeletePlcTypeTransactionOperationCommand(PlcDocumentWorkflowService service) => _service = service;
    public string Name => "add-delete-plc-type-transaction-operation";
    public string Description => "Adds an exact PLC type deletion with XML snapshot and rollback proof to a persistent PLC code transaction";
    public CommandSchema Schema => new CommandSchema(Description, new[]
    {
        new CommandParameter("transaction-id", CommandParameterType.String, "Draft PLC code transaction id", true),
        new CommandParameter("type-name", CommandParameterType.String, "Exact PLC type name", true),
        new CommandParameter("group-path", CommandParameterType.String, "Exact PLC type group path", false),
        new CommandParameter("depends-on", CommandParameterType.String, "Optional comma-separated operation sequences that must apply first", false),
        new CommandParameter("confirm", CommandParameterType.Boolean, "Confirm adding the reviewed deletion operation", true)
    });
    public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
    {
        if (!args.GetBool("confirm")) return Task.FromResult(CommandResult.CreateFailure("confirm=true is required to add a PLC type deletion operation."));
        PlcCodeTransaction transaction = _service.AddDeleteTypeTransactionOperation(args.GetString("transaction-id"), args.GetString("type-name"), args.GetString("group-path"), args.GetString("depends-on"));
        return Task.FromResult(CommandResult.CreateSuccess(transaction, "Exact PLC type deletion operation added to the transaction."));
    }
}

public sealed class ApplyPlcCodeTransactionCommand : ICommand
{
    private readonly PlcDocumentWorkflowService _service;
    public ApplyPlcCodeTransactionCommand(PlcDocumentWorkflowService service) => _service = service;
    public string Name => "apply-plc-code-transaction";
    public string Description => "Atomically preflights, applies, compiles, verifies, or fully rolls back a planned PLC code transaction";
    public CommandSchema Schema => new CommandSchema(Description, new[]
    {
        new CommandParameter("transaction-id", CommandParameterType.String, "Planned transaction id", true),
        new CommandParameter("apply-token", CommandParameterType.String, "One-use token returned by transaction planning", true),
        new CommandParameter("confirm", CommandParameterType.Boolean, "Confirm the complete reviewed transaction", true)
    });
    public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
    {
        if (!args.GetBool("confirm")) return CommandResult.CreateFailure("Applying a PLC code transaction requires confirm=true.");
        PlcCodeTransactionApplyResult result = await _service.ApplyTransactionAsync(args.GetString("transaction-id"), args.GetString("apply-token")).ConfigureAwait(false);
        return result.Applied ? CommandResult.CreateSuccess(result, result.Message) : CommandResult.CreateFailure(result.Message, result);
    }
}

public sealed class RecoverPlcCodeTransactionCommand : ICommand
{
    private readonly PlcDocumentWorkflowService _service;
    public RecoverPlcCodeTransactionCommand(PlcDocumentWorkflowService service) => _service = service;
    public string Name => "recover-plc-code-transaction";
    public string Description => "Conservatively restores every baseline in an interrupted or unverified PLC code transaction";
    public CommandSchema Schema => new CommandSchema(Description, new[]
    {
        new CommandParameter("transaction-id", CommandParameterType.String, "Interrupted or recovery-required transaction id", true),
        new CommandParameter("confirm", CommandParameterType.Boolean, "Confirm restoration from the retained recovery package", true)
    });
    public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
    {
        if (!args.GetBool("confirm")) return CommandResult.CreateFailure("Recovering a PLC code transaction requires confirm=true.");
        PlcCodeTransactionApplyResult result = await _service.RecoverTransactionAsync(args.GetString("transaction-id")).ConfigureAwait(false);
        return result.RolledBackAndVerified ? CommandResult.CreateSuccess(result, result.Message) : CommandResult.CreateFailure(result.Message, result);
    }
}

public sealed class GetPlcCodeTransactionCommand : ICommand
{
    private readonly PlcDocumentWorkflowService _service;
    public GetPlcCodeTransactionCommand(PlcDocumentWorkflowService service) => _service = service;
    public string Name => "get-plc-code-transaction";
    public string Description => "Gets the persistent manifest, journal, hashes, compile evidence, and recovery state for one PLC code transaction";
    public CommandSchema Schema => new CommandSchema(Description, new[] { new CommandParameter("transaction-id", CommandParameterType.String, "Transaction id", true) });
    public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger) => Task.FromResult(CommandResult.CreateSuccess(_service.GetTransaction(args.GetString("transaction-id")), "PLC code transaction loaded."));
}

public sealed class ListPlcCodeTransactionsCommand : ICommand
{
    private readonly PlcDocumentWorkflowService _service;
    public ListPlcCodeTransactionsCommand(PlcDocumentWorkflowService service) => _service = service;
    public string Name => "list-plc-code-transactions";
    public string Description => "Lists persistent PLC code transactions, including interrupted and recovery-required changes";
    public CommandSchema Schema => new CommandSchema(Description);
    public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger) => Task.FromResult(CommandResult.CreateSuccess(_service.ListTransactions(), "PLC code transactions listed."));
}

public sealed class DiscardPlcCodeTransactionCommand : ICommand
{
    private readonly PlcDocumentWorkflowService _service;
    public DiscardPlcCodeTransactionCommand(PlcDocumentWorkflowService service) => _service = service;
    public string Name => "discard-plc-code-transaction";
    public string Description => "Deletes a terminal or never-applied PLC code transaction and its retained local artifacts";
    public CommandSchema Schema => new CommandSchema(Description, new[]
    {
        new CommandParameter("transaction-id", CommandParameterType.String, "Transaction id", true),
        new CommandParameter("confirm", CommandParameterType.Boolean, "Confirm local transaction artifact deletion", true)
    });
    public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
    {
        if (!args.GetBool("confirm")) return Task.FromResult(CommandResult.CreateFailure("Discarding a PLC code transaction requires confirm=true."));
        bool discarded = _service.DiscardTransaction(args.GetString("transaction-id"));
        return Task.FromResult(discarded ? CommandResult.CreateSuccess(new { Discarded = true }, "PLC code transaction discarded.") : CommandResult.CreateFailure("PLC code transaction could not be discarded."));
    }
}
