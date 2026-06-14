using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Plc
{
    /// <summary>
    /// Command to stop a PLC CPU when TIA exposes a supported runtime operation.
    /// </summary>
    public class StopCpuCommand : ICommand
    {
        private readonly IPlcService _plcService;

        public StopCpuCommand(IPlcService plcService)
        {
            _plcService = plcService;
        }

        public string Name => "stop-cpu";

        public string Description => "Stops the PLC CPU when a supported online runtime operation is available";

        public CommandSchema Schema => new CommandSchema(
            Description,
            new[]
            {
                new CommandParameter("plc-name", CommandParameterType.String, "The name of the PLC device", required: true),
                new CommandParameter("confirm", CommandParameterType.Boolean, "Explicitly confirm CPU runtime state change", required: false, defaultValue: false)
            });

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            logger.LogInformation("Executing command: {CommandName}", Name);

            try
            {
                var plcName = args.GetString("plc-name");
                if (string.IsNullOrEmpty(plcName))
                {
                    return CommandResult.CreateFailure("Missing required parameter: plc-name");
                }

                if (!_plcService.IsSoftwareOpen || _plcService.CurrentPlcName != plcName)
                {
                    await _plcService.OpenPlcAsync(plcName);
                }

                var result = await _plcService.StopCpuAsync(args.GetBool("confirm"));
                return result.Success
                    ? CommandResult.CreateSuccess(result, result.Message)
                    : CommandResult.CreateFailure(result.Message, result);
            }
            catch (System.Exception ex)
            {
                logger.LogError(ex, "Error stopping PLC CPU");
                return CommandResult.CreateFailure($"Error stopping PLC CPU: {ex.Message}", ex);
            }
        }
    }
}
