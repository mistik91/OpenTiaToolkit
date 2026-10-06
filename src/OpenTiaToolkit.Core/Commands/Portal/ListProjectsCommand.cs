using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Portal
{
    /// <summary>
    /// Command to list all open TIA Portal projects.
    /// </summary>
    public class ListProjectsCommand : IContextualCommand
    {
        private readonly IPortalService _portalService;

        /// <summary>
        /// Initializes a new instance of the <see cref="ListProjectsCommand"/> class.
        /// </summary>
        /// <param name="portalService">The portal service instance.</param>
        public ListProjectsCommand(IPortalService portalService)
        {
            _portalService = portalService;
        }

        /// <inheritdoc/>
        public string Name => "list-projects";

        /// <inheritdoc/>
        public string Description => "Lists all open TIA Portal projects and sessions";

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

            try
            {
                context.StartCommand(Name, "Listing open TIA Portal projects.");
                var projects = await _portalService.GetOpenProjectsAsync();

                if (projects.Count == 0)
                {
                    context.CompleteCommand(Name, "No projects are currently open");
                    return CommandResult.CreateSuccess(
                        data: projects,
                        message: "No projects are currently open"
                    );
                }
                else
                {
                    context.CompleteCommand(Name, $"Found {projects.Count} open project(s)");
                    return CommandResult.CreateSuccess(
                        data: projects,
                        message: $"Found {projects.Count} open project(s)"
                    );
                }
            }
            catch (System.Exception ex)
            {
                logger.LogError(ex, "Error listing projects");
                return CommandResult.CreateFailure(
                    $"Error listing projects: {ex.Message}",
                    ex
                );
            }
        }
    }
}
