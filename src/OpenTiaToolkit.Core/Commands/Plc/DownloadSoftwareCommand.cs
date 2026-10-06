using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Plc
{
    /// <summary>
    /// Command to download PLC software through a guarded simulator-only path.
    /// </summary>
    public class DownloadSoftwareCommand : IContextualCommand
    {
        private readonly IPlcService _plcService;

        public DownloadSoftwareCommand(IPlcService plcService)
        {
            _plcService = plcService;
        }

        public string Name => "download-software";

        public string Description => "Downloads PLC software to PLCSIM Advanced with dry-run and confirmation gates";

        public CommandSchema Schema => new CommandSchema(
            Description,
            new[]
            {
                new CommandParameter("plc-name", CommandParameterType.String, "The name of the PLC device", required: true),
                new CommandParameter("to-simulator", CommandParameterType.Boolean, "Restrict download to PLCSIM Advanced", required: true, defaultValue: false),
                new CommandParameter("dry-run", CommandParameterType.Boolean, "Validate and return the planned download without applying", required: false, defaultValue: true),
                new CommandParameter("only-changes", CommandParameterType.Boolean, "Download only software changes instead of the full software load", required: false, defaultValue: false),
                new CommandParameter("confirm", CommandParameterType.Boolean, "Explicitly confirm a non-dry-run download", required: false, defaultValue: false)
            });

        public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            return ExecuteAsync(args, logger, CommandExecutionContext.None);
        }

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger, CommandExecutionContext context)
        {
            logger.LogInformation("Executing command: {CommandName}", Name);
            context.StartCommand(Name, "Validating PLC download inputs.");

            try
            {
                var plcName = args.GetString("plc-name");
                if (string.IsNullOrEmpty(plcName))
                {
                    return CommandResult.CreateFailure("Missing required parameter: plc-name");
                }

                var toSimulator = args.GetBool("to-simulator");
                var dryRun = args.GetBool("dry-run", true);
                var onlyChanges = args.GetBool("only-changes");
                var confirm = args.GetBool("confirm");

                if (!_plcService.IsSoftwareOpen || _plcService.CurrentPlcName != plcName)
                {
                    context.Checkpoint($"Opening PLC software: {plcName}.", 25, "open-plc");
                    await _plcService.OpenPlcAsync(plcName);
                }

                context.Checkpoint(
                    dryRun ? "Planning guarded PLC software download." : "Executing guarded PLC software download.",
                    55,
                    dryRun ? "plan-download" : "download-software");
                var result = await _plcService.DownloadSoftwareAsync(toSimulator, dryRun, confirm, onlyChanges);
                context.CompleteCommand(Name, result.Message);
                return result.Success
                    ? CommandResult.CreateSuccess(result, result.Message)
                    : CommandResult.CreateFailure(result.Message, result);
            }
            catch (System.Exception ex)
            {
                logger.LogError(ex, "Error downloading PLC software");
                return CommandResult.CreateFailure($"Error downloading PLC software: {ex.Message}", ex);
            }
        }
    }
}
