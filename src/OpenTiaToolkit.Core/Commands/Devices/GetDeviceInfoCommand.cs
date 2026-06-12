using OpenTiaToolkit.Core.Models;
using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Devices
{
    /// <summary>
    /// Command to get detailed information about a device.
    /// </summary>
    public class GetDeviceInfoCommand : ICommand
    {
        private readonly IPortalService _portalService;

        /// <summary>
        /// Initializes a new instance of the <see cref="GetDeviceInfoCommand"/> class.
        /// </summary>
        /// <param name="portalService">The portal service instance.</param>
        public GetDeviceInfoCommand(IPortalService portalService)
        {
            _portalService = portalService;
        }

        /// <inheritdoc/>
        public string Name => "get-device-info";

        /// <inheritdoc/>
        public string Description => "Gets detailed information about a device";

        /// <inheritdoc/>
        public CommandSchema Schema => new CommandSchema(
            Description,
            new[]
            {
                new CommandParameter(
                    "device-name",
                    CommandParameterType.String,
                    "The name of the device",
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
                if (string.IsNullOrEmpty(deviceName))
                {
                    return CommandResult.CreateFailure("Missing required parameter: device-name");
                }

                var deviceInfo = await _portalService.GetDeviceInfoAsync(deviceName);

                if (deviceInfo != null)
                {
                    return CommandResult.CreateSuccess(
                        data: new
                        {
                            name = deviceInfo.Name,
                            type = deviceInfo.Type,
                            identification = deviceInfo.Identification
                        },
                        message: $"Device info retrieved for: {deviceName}"
                    );
                }
                else
                {
                    return CommandResult.CreateFailure(
                        $"Device not found: {deviceName}"
                    );
                }
            }
            catch (System.Exception ex)
            {
                logger.LogError(ex, "Error getting device info");
                return CommandResult.CreateFailure(
                    $"Error getting device info: {ex.Message}",
                    ex
                );
            }
        }
    }
}
