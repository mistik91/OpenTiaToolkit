using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Portal
{
    /// <summary>
    /// Command to close the current TIA Portal instance.
    /// </summary>
    public class CloseTiaPortalCommand : ICommand
    {
        private readonly IPortalService _portalService;

        /// <summary>
        /// Initializes a new instance of the <see cref="CloseTiaPortalCommand"/> class.
        /// </summary>
        /// <param name="portalService">The portal service instance.</param>
        public CloseTiaPortalCommand(IPortalService portalService)
        {
            _portalService = portalService;
        }

        /// <inheritdoc/>
        public string Name => "close-tia-portal";

        /// <inheritdoc/>
        public string Description => "Closes the current TIA Portal instance";

        /// <inheritdoc/>
        public CommandSchema Schema => new CommandSchema(Description);

        /// <inheritdoc/>
        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            logger.LogInformation("Executing command: {CommandName}", Name);

            try
            {
                var success = await _portalService.CloseTiaPortalAsync();

                if (success)
                {
                    return CommandResult.CreateSuccess(
                        data: new
                        {
                            closed = true
                        },
                        message: "TIA Portal instance closed"
                    );
                }
                else
                {
                    return CommandResult.CreateFailure(
                        "Failed to close TIA Portal or no instance was open"
                    );
                }
            }
            catch (System.Exception ex)
            {
                logger.LogError(ex, "Error closing TIA Portal");
                return CommandResult.CreateFailure(
                    $"Error closing TIA Portal: {ex.Message}",
                    ex
                );
            }
        }
    }
}
