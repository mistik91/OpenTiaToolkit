using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Services;
using System;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Hmi;

public sealed class ExportHmiTagsCommand : IContextualCommand
{
    private readonly IHmiService _hmi;

    public ExportHmiTagsCommand(IHmiService hmi) => _hmi = hmi;

    public string Name => "export-hmi-tags";
    public string Description => "Exports one Unified HMI tag table as native WinCCML";

    public CommandSchema Schema => new CommandSchema(Description, new[]
    {
        new CommandParameter("hmi-name", CommandParameterType.String, "Unified HMI name", required: true),
        new CommandParameter("table-path", CommandParameterType.String, "Table name or slash-delimited path through existing groups", required: true),
        new CommandParameter("output-path", CommandParameterType.Path, "Destination .hmi.yml file or .zip containing the WinCCML files", required: true),
        new CommandParameter("overwrite", CommandParameterType.Boolean, "Replace the existing destination file", defaultValue: false)
    });

    public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger) =>
        ExecuteAsync(args, logger, CommandExecutionContext.None);

    public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger, CommandExecutionContext context)
    {
        var hmiName = args.GetString("hmi-name");
        var tablePath = args.GetString("table-path");
        if (string.IsNullOrWhiteSpace(hmiName) || !HmiTagTablePath.IsValid(tablePath))
            return CommandResult.CreateFailure("Specify hmi-name and a table-path with no empty segments.");

        try
        {
            var overwrite = args.GetBool("overwrite");
            var outputPath = HmiTagExport.ValidateDestination(args.GetString("output-path"), overwrite);
            if ((!_hmi.IsHmiOpen || !string.Equals(_hmi.CurrentHmiName, hmiName, StringComparison.OrdinalIgnoreCase)) &&
                !await _hmi.OpenUnifiedHmiAsync(hmiName).ConfigureAwait(false))
                return CommandResult.CreateFailure($"Unified HMI '{hmiName}' was not found.");

            context.Checkpoint("Exporting the selected Unified HMI tag table.", 50, "export-hmi-tags");
            var result = await _hmi.ExportTagsAsync(hmiName, tablePath, outputPath, overwrite, context.CancellationToken)
                .ConfigureAwait(false);
            return CommandResult.CreateSuccess(result, $"Unified HMI tag table exported to '{result.OutputPath}'.");
        }
        catch (Exception ex) when (!(ex is OperationCanceledException))
        {
            return CommandResult.CreateFailure(ex.Message);
        }
    }
}
