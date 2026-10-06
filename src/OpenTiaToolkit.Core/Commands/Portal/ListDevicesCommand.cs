using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Portal
{
    /// <summary>
    /// Command to list all devices in the currently open project.
    /// </summary>
    public class ListDevicesCommand : IContextualCommand
    {
        private readonly IPortalService _portalService;

        /// <summary>
        /// Initializes a new instance of the <see cref="ListDevicesCommand"/> class.
        /// </summary>
        /// <param name="portalService">The portal service instance.</param>
        public ListDevicesCommand(IPortalService portalService)
        {
            _portalService = portalService;
        }

        /// <inheritdoc/>
        public string Name => "list-devices";

        /// <inheritdoc/>
        public string Description => "Lists all devices in the currently open project";

        /// <inheritdoc/>
        public CommandSchema Schema => new CommandSchema(Description);

        /// <inheritdoc/>
        public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            return ExecuteAsync(args, logger, CommandExecutionContext.None);
        }

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger, CommandExecutionContext context)
        {
            logger.LogInformation("Executing command: {CommandName}", Name);
            context.StartCommand(Name, "Listing project devices.");

            try
            {
                var devices = await _portalService.GetDevicesAsync();

                if (devices.Count == 0)
                {
                    context.CompleteCommand(Name, "No devices found (project may not be open)");
                    return CommandResult.CreateSuccess(
                        data: devices,
                        message: "No devices found (project may not be open)"
                    );
                }
                else
                {
                    context.CompleteCommand(Name, $"Found {devices.Count} device(s)");
                    return CommandResult.CreateSuccess(
                        data: devices,
                        message: $"Found {devices.Count} device(s)"
                    );
                }
            }
            catch (System.Exception ex)
            {
                logger.LogError(ex, "Error listing devices");
                return CommandResult.CreateFailure(
                    $"Error listing devices: {ex.Message}",
                    ex
                );
            }
        }
    }
}
