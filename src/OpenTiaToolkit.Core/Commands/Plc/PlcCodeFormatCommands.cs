using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Services;

namespace OpenTiaToolkit.Core.Commands.Plc;

public sealed class DescribePlcCodeFormatsCommand : ICommand
{
    private readonly PlcCodeFormatService _service;
    public DescribePlcCodeFormatsCommand(PlcCodeFormatService service) => _service = service;
    public string Name => "describe-plc-code-formats";
    public string Description => "Describes format-specific PLC authoring support, mutation routes, verification requirements, and limitations";
    public CommandSchema Schema => new CommandSchema(Description);
    public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger) => Task.FromResult(CommandResult.CreateSuccess(_service.DescribeFormats(), "PLC code format capabilities described."));
}

public sealed class ValidatePlcCodeArtifactCommand : ICommand
{
    private readonly PlcCodeFormatService _service;
    public ValidatePlcCodeArtifactCommand(PlcCodeFormatService service) => _service = service;
    public string Name => "validate-plc-code-artifact";
    public string Description => "Validates SCL, S7DCL, or SimaticML with format-specific structural and semantic guardrails";
    public CommandSchema Schema => new CommandSchema(Description, new[]
    {
        new CommandParameter("code-format", CommandParameterType.String, "auto, scl, s7dcl, or simaticml", false, "auto"),
        new CommandParameter("input-path", CommandParameterType.Path, "PLC code artifact path", true),
        new CommandParameter("baseline-path", CommandParameterType.Path, "Untouched exported SimaticML baseline for patch validation", false),
        new CommandParameter("allow-flgnet-topology", CommandParameterType.Boolean, "Allow reviewed FlgNet topology changes to proceed as a live probe", false, false),
        new CommandParameter("allowed-change-paths", CommandParameterType.String, "Optional comma-separated exact/prefix SimaticML paths to enforce after inspecting changedPaths", false)
    });
    public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
    {
        PlcCodeArtifactValidationResult result = _service.Validate(args.GetString("code-format") ?? "auto", args.GetString("input-path"), args.GetString("baseline-path"), args.GetBool("allow-flgnet-topology"), args.GetString("allowed-change-paths"));
        return Task.FromResult(result.Valid ? CommandResult.CreateSuccess(result, $"{result.Format} artifact is valid ({result.Classification}).") : CommandResult.CreateFailure($"{result.Format} artifact validation failed.", result));
    }
}
