using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Portal
{
    /// <summary>
    /// Command to get the current TIA Portal connection status.
    /// </summary>
    public class PortalStatusCommand : ICommand
    {
        private readonly IPortalService _portalService;

        /// <summary>
        /// Initializes a new instance of the <see cref="PortalStatusCommand"/> class.
        /// </summary>
        /// <param name="portalService">The portal service instance.</param>
        public PortalStatusCommand(IPortalService portalService)
        {
            _portalService = portalService;
        }

        /// <inheritdoc/>
        public string Name => "portal-status";

        /// <inheritdoc/>
        public string Description => "Gets the current TIA Portal connection status";

        /// <inheritdoc/>
        public CommandSchema Schema => new CommandSchema(Description);

        /// <inheritdoc/>
        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            logger.LogInformation("Executing command: {CommandName}", Name);

            try
            {
                var status = new
                {
                    connected = _portalService.IsConnected,
                    version = _portalService.Version ?? "Not connected"
                };

                return await Task.FromResult(
                    CommandResult.CreateSuccess(
                        data: status,
                        message: _portalService.IsConnected
                            ? $"Connected to TIA Portal {_portalService.Version}"
                            : "Not connected to TIA Portal"
                    )
                );
            }
            catch (System.Exception ex)
            {
                logger.LogError(ex, "Error getting portal status");
                return CommandResult.CreateFailure(
                    $"Error getting portal status: {ex.Message}",
                    ex
                );
            }
        }
    }
}
