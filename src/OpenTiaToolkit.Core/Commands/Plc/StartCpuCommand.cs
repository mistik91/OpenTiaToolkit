using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Plc
{
    /// <summary>
    /// Command to start a PLC CPU when TIA exposes a supported runtime operation.
    /// </summary>
    public class StartCpuCommand : IContextualCommand
    {
        private readonly IPlcService _plcService;

        public StartCpuCommand(IPlcService plcService)
        {
            _plcService = plcService;
        }

        public string Name => "start-cpu";

        public string Description => "Starts the PLC CPU when a supported online runtime operation is available";

        public CommandSchema Schema => new CommandSchema(
            Description,
            new[]
            {
                new CommandParameter("plc-name", CommandParameterType.String, "The name of the PLC device", required: true),
                new CommandParameter("confirm", CommandParameterType.Boolean, "Explicitly confirm CPU runtime state change", required: false, defaultValue: false)
            });

        public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            return ExecuteAsync(args, logger, CommandExecutionContext.None);
        }

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger, CommandExecutionContext context)
        {
            logger.LogInformation("Executing command: {CommandName}", Name);

            try
            {
                context.StartCommand(Name, "Starting PLC CPU.");
                var plcName = args.GetString("plc-name");
                if (string.IsNullOrEmpty(plcName))
                {
                    return CommandResult.CreateFailure("Missing required parameter: plc-name");
                }

                if (!_plcService.IsSoftwareOpen || _plcService.CurrentPlcName != plcName)
                {
                    context.Checkpoint($"Opening PLC software: {plcName}.", 25, "open-plc");
                    await _plcService.OpenPlcAsync(plcName);
                }

                context.Checkpoint("Requesting PLC CPU start.", 65, "start-cpu");
                var result = await _plcService.StartCpuAsync(args.GetBool("confirm"));
                context.CompleteCommand(Name, result.Message);
                return result.Success
                    ? CommandResult.CreateSuccess(result, result.Message)
                    : CommandResult.CreateFailure(result.Message, result);
            }
            catch (System.Exception ex)
            {
                logger.LogError(ex, "Error starting PLC CPU");
                return CommandResult.CreateFailure($"Error starting PLC CPU: {ex.Message}", ex);
            }
        }
    }
}
