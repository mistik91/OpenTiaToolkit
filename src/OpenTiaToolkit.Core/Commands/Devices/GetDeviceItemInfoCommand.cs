using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Devices
{
    /// <summary>
    /// Command to get detailed information about a device item.
    /// </summary>
    public class GetDeviceItemInfoCommand : ICommand
    {
        private readonly IPortalService _portalService;

        /// <summary>
        /// Initializes a new instance of the <see cref="GetDeviceItemInfoCommand"/> class.
        /// </summary>
        /// <param name="portalService">The portal service instance.</param>
        public GetDeviceItemInfoCommand(IPortalService portalService)
        {
            _portalService = portalService;
        }

        /// <inheritdoc/>
        public string Name => "get-device-item-info";

        /// <inheritdoc/>
        public string Description => "Gets detailed information about a device item";

        /// <inheritdoc/>
        public CommandSchema Schema => new CommandSchema(
            Description,
            new[]
            {
                new CommandParameter(
                    "device-name",
                    CommandParameterType.String,
                    "The name of the parent device",
                    required: true),
                new CommandParameter(
                    "item-name",
                    CommandParameterType.String,
                    "The name of the device item",
                    required: true)
            }
        );

        /// <inheritdoc/>
        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            logger.LogInformation("Executing command: {CommandName}", Name);

            try
            {
                if (!_portalService.IsConnected)
                {
                    return CommandResult.CreateFailure("Not connected to TIA Portal");
                }

                var deviceName = args.GetString("device-name");
                var itemName = args.GetString("item-name");

                if (string.IsNullOrEmpty(deviceName))
                {
                    return CommandResult.CreateFailure("Missing required parameter: device-name");
                }
                if (string.IsNullOrEmpty(itemName))
                {
                    return CommandResult.CreateFailure("Missing required parameter: item-name");
                }

                var itemInfo = await _portalService.GetDeviceItemInfoAsync(deviceName, itemName);

                if (itemInfo != null)
                {
                    return CommandResult.CreateSuccess(
                        data: new
                        {
                            name = itemInfo.Name,
                            type = itemInfo.Type,
                            identification = itemInfo.Identification
                        },
                        message: $"Device item info retrieved for: {deviceName}/{itemName}"
                    );
                }
                else
                {
                    return CommandResult.CreateFailure(
                        $"Device item not found: {deviceName}/{itemName}"
                    );
                }
            }
            catch (System.Exception ex)
            {
                logger.LogError(ex, "Error getting device item info");
                return CommandResult.CreateFailure(
                    $"Error getting device item info: {ex.Message}",
                    ex
                );
            }
        }
    }
}
