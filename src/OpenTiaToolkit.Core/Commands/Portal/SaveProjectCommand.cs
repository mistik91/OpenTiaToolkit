using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Portal
{
    /// <summary>
    /// Command to save the current TIA Portal project.
    /// </summary>
    public class SaveProjectCommand : IContextualCommand
    {
        private readonly IPortalService _portalService;

        /// <summary>
        /// Initializes a new instance of the <see cref="SaveProjectCommand"/> class.
        /// </summary>
        /// <param name="portalService">The portal service instance.</param>
        public SaveProjectCommand(IPortalService portalService)
        {
            _portalService = portalService;
        }

        /// <inheritdoc/>
        public string Name => "save-project";

        /// <inheritdoc/>
        public string Description => "Saves the current project";

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
            context.StartCommand(Name, "Saving current TIA Portal project.");

            try
            {
                if (!_portalService.IsConnected)
                {
                    return CommandResult.CreateFailure("Not connected to TIA Portal");
                }

                var success = await _portalService.SaveProjectAsync();

                if (success)
                {
                    context.CompleteCommand(Name, "Project saved successfully");
                    return CommandResult.CreateSuccess(
                        data: new
                        {
                            saved = true
                        },
                        message: "Project saved successfully"
                    );
                }
                else
                {
                    return CommandResult.CreateFailure(
                        "Failed to save project (no project open or save failed)"
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
