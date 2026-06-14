using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Plc
{
    /// <summary>
    /// Command to get or establish PLC online status.
    /// </summary>
    public class GetOnlineStatusCommand : ICommand
    {
        private readonly IPlcService _plcService;

        public GetOnlineStatusCommand(IPlcService plcService)
        {
            _plcService = plcService;
        }

        public string Name => "get-online-status";

        public string Description => "Gets PLC online status, optionally going online first";

        public CommandSchema Schema => new CommandSchema(
            Description,
            new[]
            {
                new CommandParameter("plc-name", CommandParameterType.String, "The name of the PLC device", required: true),
                new CommandParameter("go-online", CommandParameterType.Boolean, "Attempt to go online before reading status", required: false, defaultValue: true)
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

                var result = await _plcService.GetOnlineStatusAsync(args.GetBool("go-online", true));
                return result.Success
                    ? CommandResult.CreateSuccess(result, result.Message)
                    : CommandResult.CreateFailure(result.Message, result);
            }
            catch (System.Exception ex)
            {
                logger.LogError(ex, "Error getting PLC online status");
                return CommandResult.CreateFailure($"Error getting PLC online status: {ex.Message}", ex);
            }
        }
    }
}
