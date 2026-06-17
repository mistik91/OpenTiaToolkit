using OpenTiaToolkit.Core.Models;
using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Portal
{
    /// <summary>
    /// Command to get information about the currently open project.
    /// </summary>
    public class GetProjectInfoCommand : IContextualCommand
    {
        private readonly IPortalService _portalService;

        /// <summary>
        /// Initializes a new instance of the <see cref="GetProjectInfoCommand"/> class.
        /// </summary>
        /// <param name="portalService">The portal service instance.</param>
        public GetProjectInfoCommand(IPortalService portalService)
        {
            _portalService = portalService;
        }

        /// <inheritdoc/>
        public string Name => "get-project-info";

        /// <inheritdoc/>
        public string Description => "Gets information about the currently open project";

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
                context.StartCommand(Name, "Reading current project information.");
                var projectInfo = await _portalService.GetProjectInfoAsync();

                if (projectInfo == null)
                {
                    context.CompleteCommand(Name, "No project is currently open");
                    return CommandResult.CreateFailure(
                        "No project is currently open"
                    );
                }
                else
                {
                    context.CompleteCommand(Name, $"Project: {projectInfo.Name}");
                    return CommandResult.CreateSuccess(
                        data: projectInfo,
                        message: $"Project: {projectInfo.Name}"
                    );
                }
            }
            catch (System.Exception ex)
            {
                logger.LogError(ex, "Error getting project info");
                return CommandResult.CreateFailure(
                    $"Error getting project info: {ex.Message}",
                    ex
                );
            }
        }
    }
}
