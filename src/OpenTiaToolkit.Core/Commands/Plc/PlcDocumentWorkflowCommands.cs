using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Services;

namespace OpenTiaToolkit.Core.Commands.Plc;

public sealed class ValidatePlcDocumentsCommand : ICommand
{
    private readonly PlcDocumentWorkflowService _service;
    public ValidatePlcDocumentsCommand(PlcDocumentWorkflowService service) => _service = service;
    public string Name => "validate-plc-documents";
    public string Description => "Validates SIMATIC SD (.s7dcl/.s7res) files offline before PLC import";
    public CommandSchema Schema => new CommandSchema(Description, new[]
    {
        new CommandParameter("import-directory", CommandParameterType.Path, "Directory containing the SIMATIC SD files", true),
        new CommandParameter("file-name", CommandParameterType.String, "Block/file name without extension", true)
    });
    public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
    {
        PlcDocumentValidationResult result = _service.Validate(args.GetString("import-directory"), args.GetString("file-name"));
        return Task.FromResult(result.Valid ? CommandResult.CreateSuccess(result, "PLC documents are valid.") : CommandResult.CreateFailure("PLC document validation failed.", result));
    }
}

public sealed class PlanBlockDocumentImportCommand : ICommand
{
    private readonly PlcDocumentWorkflowService _service;
    public PlanBlockDocumentImportCommand(PlcDocumentWorkflowService service) => _service = service;
    public string Name => "plan-block-document-import";
    public string Description => "Validates a SIMATIC SD import and creates a state-bound, rollback-capable one-use apply plan";
    public CommandSchema Schema => new CommandSchema(Description, new[]
    {
        new CommandParameter("plc-name", CommandParameterType.String, "PLC device name", true),
        new CommandParameter("import-directory", CommandParameterType.Path, "Directory containing the SIMATIC SD files", true),
        new CommandParameter("file-name", CommandParameterType.String, "Block/file name without extension", true),
        new CommandParameter("group-path", CommandParameterType.String, "Optional target block group", false),
        new CommandParameter("import-option", CommandParameterType.String, "Override, None, SkipInactiveCultures, or ActivateInactiveCultures", false, "Override")
    });
    public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
    {
        PlcDocumentImportPlan plan = await _service.PlanAsync(args.GetString("plc-name"), args.GetString("import-directory"), args.GetString("file-name"), args.GetString("group-path"), args.GetString("import-option") ?? "Override").ConfigureAwait(false);
        return CommandResult.CreateSuccess(plan, "Import plan created. Review it before applying.");
    }
}

public sealed class ApplyBlockDocumentImportCommand : ICommand
{
    private readonly PlcDocumentWorkflowService _service;
    public ApplyBlockDocumentImportCommand(PlcDocumentWorkflowService service) => _service = service;
    public string Name => "apply-block-document-import";
    public string Description => "Applies a reviewed SIMATIC SD import plan, verifies and compiles it, and rolls back failures";
    public CommandSchema Schema => new CommandSchema(Description, new[]
    {
        new CommandParameter("apply-token", CommandParameterType.String, "One-use token returned by plan-block-document-import", true),
        new CommandParameter("confirm", CommandParameterType.Boolean, "Confirm the reviewed import apply", true)
    });
    public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
    {
        if (!args.GetBool("confirm")) return CommandResult.CreateFailure("Applying a block document import requires confirm=true.");
        PlcDocumentImportApplyResult result = await _service.ApplyAsync(args.GetString("apply-token")).ConfigureAwait(false);
        return result.Applied ? CommandResult.CreateSuccess(result, result.Message) : CommandResult.CreateFailure(result.Message, result);
    }
}
