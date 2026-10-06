using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Portal
{
    /// <summary>
    /// Command to connect to a TIA Portal instance.
    /// Attaches to a running instance or starts a new one if needed.
    /// </summary>
    public class ConnectPortalCommand : ICommand
    {
        private readonly IPortalService _portalService;

        /// <summary>
        /// Initializes a new instance of the <see cref="ConnectPortalCommand"/> class.
        /// </summary>
        /// <param name="portalService">The portal service instance.</param>
        public ConnectPortalCommand(IPortalService portalService)
        {
            _portalService = portalService;
        }

        /// <inheritdoc/>
        public string Name => "connect-portal";

        /// <inheritdoc/>
        public string Description => "Connects to a running TIA Portal instance or starts a new one";

        /// <inheritdoc/>
        public CommandSchema Schema => new CommandSchema(Description);

        /// <inheritdoc/>
        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            logger.LogInformation("Executing command: {CommandName}", Name);

            try
            {
                var success = await _portalService.ConnectAsync();

                if (success)
                {
                    return CommandResult.CreateSuccess(
                        data: new
                        {
                            connected = true,
                            version = _portalService.Version
                        },
                        message: $"Connected to TIA Portal {_portalService.Version}"
                    );
                }
                else
                {
                    return CommandResult.CreateFailure(
                        "Failed to connect to TIA Portal"
                    );
                }
            }
            catch (System.Exception ex)
            {
                logger.LogError(ex, "Error connecting to TIA Portal");
                return CommandResult.CreateFailure(
                    $"Error connecting to TIA Portal: {ex.Message}",
                    ex
                );
            }
        }
    }
}
