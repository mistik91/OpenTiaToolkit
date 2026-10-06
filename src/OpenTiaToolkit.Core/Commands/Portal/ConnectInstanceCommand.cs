using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Portal
{
    /// <summary>
    /// Command to connect to a specific TIA Portal instance by process ID.
    /// </summary>
    public class ConnectInstanceCommand : ICommand
    {
        private readonly IPortalService _portalService;

        /// <summary>
        /// Initializes a new instance of the <see cref="ConnectInstanceCommand"/> class.
        /// </summary>
        /// <param name="portalService">The portal service instance.</param>
        public ConnectInstanceCommand(IPortalService portalService)
        {
            _portalService = portalService;
        }

        /// <inheritdoc/>
        public string Name => "connect-instance";

        /// <inheritdoc/>
        public string Description => "Connects to a specific TIA Portal instance by process ID";

        /// <inheritdoc/>
        public CommandSchema Schema => new CommandSchema(
            Description,
            new[]
            {
                new CommandParameter(
                    "instance-id",
                    CommandParameterType.Integer,
                    "The process ID of the TIA Portal instance to connect to",
                    required: true)
            }
        );

        /// <inheritdoc/>
        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            logger.LogInformation("Executing command: {CommandName}", Name);

            try
            {
                var instanceId = args.GetInt("instance-id");
                if (instanceId == 0 && !args.Has("instance-id"))
                {
                    return CommandResult.CreateFailure("Missing required parameter: instance-id");
                }

                var success = await _portalService.ConnectToInstanceAsync(instanceId);

                if (success)
                {
                    return CommandResult.CreateSuccess(
                        data: new
                        {
                            connected = true,
                            instanceId,
                            version = _portalService.Version
                        },
                        message: $"Connected to TIA Portal instance {instanceId} ({_portalService.Version})"
                    );
                }
                else
                {
                    return CommandResult.CreateFailure(
                        $"Failed to connect to TIA Portal instance {instanceId}"
                    );
                }
            }
            catch (System.Exception ex)
            {
                logger.LogError(ex, "Error connecting to TIA Portal instance");
                return CommandResult.CreateFailure(
                    $"Error connecting to TIA Portal instance: {ex.Message}",
                    ex
                );
            }
        }
    }
}
