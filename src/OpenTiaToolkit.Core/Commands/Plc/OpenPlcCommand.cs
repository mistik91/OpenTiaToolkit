using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Plc
{
    /// <summary>
    /// Command to open a PLC device software.
    /// </summary>
    public class OpenPlcCommand : ICommand
    {
        private readonly IPlcService _plcService;

        /// <summary>
        /// Initializes a new instance of the <see cref="OpenPlcCommand"/> class.
        /// </summary>
        /// <param name="plcService">The PLC service instance.</param>
        public OpenPlcCommand(IPlcService plcService)
        {
            _plcService = plcService;
        }

        /// <inheritdoc/>
        public string Name => "open-plc";

        /// <inheritdoc/>
        public string Description => "Opens a PLC device software by name";

        /// <inheritdoc/>
        public CommandSchema Schema => new CommandSchema(
            Description,
            new[]
            {
                new CommandParameter(
                    "plc-name",
                    CommandParameterType.String,
                    "The name of the PLC device",
                    required: true)
            }
        );

        /// <inheritdoc/>
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

                var success = await _plcService.OpenPlcAsync(plcName);

                if (success)
                {
                    return CommandResult.CreateSuccess(
                        data: new
                        {
                            opened = true,
                            plcName
                        },
                        message: $"PLC software opened: {plcName}"
                    );
                }
                else
                {
                    return CommandResult.CreateFailure(
                        $"Failed to open PLC software: {plcName}"
                    );
                }
            }
            catch (System.Exception ex)
            {
                logger.LogError(ex, "Error opening PLC software");
                return CommandResult.CreateFailure(
                    $"Error opening PLC software: {ex.Message}",
                    ex
                );
            }
        }
    }
}
