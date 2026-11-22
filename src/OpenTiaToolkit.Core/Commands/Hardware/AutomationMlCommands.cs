using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Services.AutomationMl;
using OpenTiaToolkit.Core.Services.V21;

namespace OpenTiaToolkit.Core.Commands.Hardware;

public sealed class InspectAutomationMlCommand : ICommand
{
    internal static CommandParameter SourceParameter => new("source-path", CommandParameterType.Path,
        "Path to an AR APC AutomationML .aml or .xml file", required: true);

    public string Name => "inspect-automationml";
    public string Description => "Reads an AR APC AutomationML configuration without connecting to TIA Portal";
    public CommandSchema Schema => new(Description, new[] { SourceParameter });

    public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
    {
        try
        {
            using var source = File.OpenRead(HardwareCommandSupport.Required(args, "source-path"));
            return Task.FromResult(CommandResult.CreateSuccess(AmlReader.Read(source), "Read AutomationML configuration."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HardwareCommandSupport.Failure("Reading AutomationML", ex, logger));
        }
    }
}

internal sealed class ApplyAutomationMlCommand : ICommand
{
    private readonly V21AutomationMlAccess _access;

    public ApplyAutomationMlCommand(V21AutomationMlAccess access) => _access = access;

    public string Name => "apply-automationml";
    public string Description => "Previews and applies AR APC AutomationML configuration with guarded additions to existing projects";
    public CommandSchema Schema => new(Description, new[]
    {
        InspectAutomationMlCommand.SourceParameter,
        new CommandParameter("dry-run", CommandParameterType.Boolean, "Read and compare without changing the project",
            required: false, defaultValue: true)
    });

    public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
    {
        try
        {
            var dryRun = args.GetBool("dry-run", true);
            if (!dryRun && !args.GetBool("confirm"))
                return Task.FromResult(CommandResult.CreateFailure("AutomationML apply requires confirm=true."));

            var result = _access.Reconcile(HardwareCommandSupport.Required(args, "source-path"), dryRun);
            return Task.FromResult(result.Success
                ? CommandResult.CreateSuccess(result.Data, result.Message)
                : CommandResult.CreateFailure(result.Message, result.Data));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HardwareCommandSupport.Failure("Reconciling AutomationML", ex, logger));
        }
    }
}
