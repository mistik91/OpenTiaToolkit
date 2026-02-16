using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Portal
{
    /// <summary>
    /// Command to save the current TIA Portal project to a new location.
    /// </summary>
    public class SaveAsProjectCommand : ICommand
    {
        private readonly IPortalService _portalService;

        /// <summary>
        /// Initializes a new instance of the <see cref="SaveAsProjectCommand"/> class.
        /// </summary>
        /// <param name="portalService">The portal service instance.</param>
        public SaveAsProjectCommand(IPortalService portalService)
        {
            _portalService = portalService;
        }

        /// <inheritdoc/>
        public string Name => "save-as-project";

        /// <inheritdoc/>
        public string Description => "Saves the project to a new location";

        /// <inheritdoc/>
        public CommandSchema Schema => new CommandSchema(
            Description,
            new[]
            {
                new CommandParameter(
                    "destination-path",
                    CommandParameterType.Path,
                    "The destination path for the project",
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

                var destinationPath = args.GetString("destination-path");
                if (string.IsNullOrEmpty(destinationPath))
                {
                    return CommandResult.CreateFailure("Missing required parameter: destination-path");
                }

                var success = await _portalService.SaveAsProjectAsync(destinationPath);

                if (success)
                {
                    return CommandResult.CreateSuccess(
                        data: new
                        {
                            saved = true,
                            destinationPath
                        },
                        message: $"Project saved to: {destinationPath}"
                    );
                }
                else
                {
                    return CommandResult.CreateFailure(
                        "Failed to save project (no project open, session, or save failed)"
                    );
                }
            }
            catch (System.Exception ex)
            {
                logger.LogError(ex, "Error saving project");
                return CommandResult.CreateFailure(
                    $"Error saving project: {ex.Message}",
                    ex
                );
            }
        }
    }
}
