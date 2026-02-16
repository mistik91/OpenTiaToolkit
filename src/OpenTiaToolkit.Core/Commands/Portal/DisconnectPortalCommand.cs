using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Portal
{
    /// <summary>
    /// Command to disconnect from the TIA Portal instance.
    /// </summary>
    public class DisconnectPortalCommand : ICommand
    {
        private readonly IPortalService _portalService;

        /// <summary>
        /// Initializes a new instance of the <see cref="DisconnectPortalCommand"/> class.
        /// </summary>
        /// <param name="portalService">The portal service instance.</param>
        public DisconnectPortalCommand(IPortalService portalService)
        {
            _portalService = portalService;
        }

        /// <inheritdoc/>
        public string Name => "disconnect-portal";

        /// <inheritdoc/>
        public string Description => "Disconnects from the TIA Portal instance";

        /// <inheritdoc/>
        public CommandSchema Schema => new CommandSchema(Description);

        /// <inheritdoc/>
        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            logger.LogInformation("Executing command: {CommandName}", Name);

            try
            {
                var success = await _portalService.DisconnectAsync();

                if (success)
                {
                    return CommandResult.CreateSuccess(
                        message: "Disconnected from TIA Portal"
                    );
                }
                else
                {
                    return CommandResult.CreateFailure(
                        "Failed to disconnect from TIA Portal"
                    );
                }
            }
            catch (System.Exception ex)
            {
                logger.LogError(ex, "Error disconnecting from TIA Portal");
                return CommandResult.CreateFailure(
                    $"Error disconnecting from TIA Portal: {ex.Message}",
                    ex
                );
            }
        }
    }
}
