using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Portal
{
    /// <summary>
    /// Command to close the currently open TIA Portal project.
    /// </summary>
    public class CloseProjectCommand : IContextualCommand
    {
        private readonly IPortalService _portalService;

        /// <summary>
        /// Initializes a new instance of the <see cref="CloseProjectCommand"/> class.
        /// </summary>
        /// <param name="portalService">The portal service instance.</param>
        public CloseProjectCommand(IPortalService portalService)
        {
            _portalService = portalService;
        }

        /// <inheritdoc/>
        public string Name => "close-project";

        /// <inheritdoc/>
        public string Description => "Closes the currently open project";

        /// <inheritdoc/>
        public CommandSchema Schema => new CommandSchema(
            Description,
            new[]
            {
                new CommandParameter(
                    "save",
                    CommandParameterType.Boolean,
                    "Whether to save changes before closing (default: false)",
                    required: false,
                    defaultValue: false
                )
            }
        );

        /// <inheritdoc/>
        public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            return ExecuteAsync(args, logger, CommandExecutionContext.None);
        }

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger, CommandExecutionContext context)
        {
            logger.LogInformation("Executing command: {CommandName}", Name);

            try
            {
                context.StartCommand(Name, "Closing current project.");
                var saveChanges = args.GetBool("save", false);

                logger.LogInformation("Closing project (save: {SaveChanges})", saveChanges);

                var projectInfo = await _portalService.GetProjectInfoAsync();
                if (projectInfo == null)
                {
                    var openProjects = await _portalService.GetOpenProjectsAsync();
                    if (openProjects.Count == 1)
                    {
                        projectInfo = openProjects[0];
                    }
                }

                context.Checkpoint(saveChanges ? "Saving and closing current project." : "Closing current project without saving.", 55, "close-project");
                var success = await _portalService.CloseProjectAsync(saveChanges);

                if (success)
                {
                    context.CompleteCommand(Name, saveChanges ? "Project saved and closed" : "Project closed (changes not saved)");
                    return CommandResult.CreateSuccess(
                        data: projectInfo is null
                            ? null
                            : new
                            {
                                projectInfo.Name,
                                projectInfo.Path,
                                projectInfo.PortalProcessId,
                                projectInfo.IsPortalVisible,
                                projectInfo.IsToolkitOwned,
                                Saved = saveChanges,
                                Closed = true
                            },
                        message: saveChanges
                            ? "Project saved and closed"
                            : "Project closed (changes not saved)"
                    );
                }
                else
                {
                    context.CompleteCommand(Name, "Failed to close project");
                    var detail = string.IsNullOrWhiteSpace(_portalService.LastError)
                        ? null
                        : $": {_portalService.LastError}";
                    return CommandResult.CreateFailure($"Failed to close project{detail}");
                }
            }
            catch (System.Exception ex)
            {
                logger.LogError(ex, "Error closing project");
                return CommandResult.CreateFailure(
                    $"Error closing project: {ex.Message}",
                    ex
                );
            }
        }
    }
}
