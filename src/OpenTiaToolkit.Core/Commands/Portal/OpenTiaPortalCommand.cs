using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Portal
{
    /// <summary>
    /// Command to open a new TIA Portal instance.
    /// Starts with user interface by default (can be disabled with --with-ui=false).
    /// </summary>
    public class OpenTiaPortalCommand : ICommand
    {
        private readonly IPortalService _portalService;

        /// <summary>
        /// Initializes a new instance of the <see cref="OpenTiaPortalCommand"/> class.
        /// </summary>
        /// <param name="portalService">The portal service instance.</param>
        public OpenTiaPortalCommand(IPortalService portalService)
        {
            _portalService = portalService;
        }

        /// <inheritdoc/>
        public string Name => "open-tia-portal";

        /// <inheritdoc/>
        public string Description => "Opens a new TIA Portal instance (with UI by default)";

        /// <inheritdoc/>
        public CommandSchema Schema => new CommandSchema(
            Description,
            new[]
            {
                new CommandParameter(
                    "with-ui",
                    CommandParameterType.Boolean,
                    "Start TIA Portal with user interface",
                    required: false,
                    defaultValue: true)
            }
        );

        /// <inheritdoc/>
        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            logger.LogInformation("Executing command: {CommandName}", Name);

            try
            {
                var withUi = args.GetBool("with-ui", true);

                var success = await _portalService.OpenTiaPortalAsync(withUi);

                if (success)
                {
                    return CommandResult.CreateSuccess(
                        data: new
                        {
                            opened = true,
                            version = _portalService.Version,
                            withUi
                        },
                        message: $"Opened new TIA Portal instance {_portalService.Version} (UI: {withUi})"
                    );
                }
                else
                {
                    return CommandResult.CreateFailure(
                        "Failed to open TIA Portal instance"
                    );
                }
            }
            catch (System.Exception ex)
            {
                logger.LogError(ex, "Error opening TIA Portal");
                return CommandResult.CreateFailure(
                    $"Error opening TIA Portal: {ex.Message}",
                    ex
                );
            }
        }
    }
}
