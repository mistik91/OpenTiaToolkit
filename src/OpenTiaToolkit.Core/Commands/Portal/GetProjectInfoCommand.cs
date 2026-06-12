using OpenTiaToolkit.Core.Models;
using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Portal
{
    /// <summary>
    /// Command to get information about the currently open project.
    /// </summary>
    public class GetProjectInfoCommand : ICommand
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
        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            logger.LogInformation("Executing command: {CommandName}", Name);

            try
            {
                var projectInfo = await _portalService.GetProjectInfoAsync();

                if (projectInfo == null)
                {
                    return CommandResult.CreateFailure(
                        "No project is currently open"
                    );
                }
                else
                {
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
