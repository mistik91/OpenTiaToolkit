using OpenTiaToolkit.Core.Models;
using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Portal
{
    /// <summary>
    /// Command to open a TIA Portal project file.
    /// </summary>
    public class OpenProjectCommand : ICommand
    {
        private readonly IPortalService _portalService;

        /// <summary>
        /// Initializes a new instance of the <see cref="OpenProjectCommand"/> class.
        /// </summary>
        /// <param name="portalService">The portal service instance.</param>
        public OpenProjectCommand(IPortalService portalService)
        {
            _portalService = portalService;
        }

        /// <inheritdoc/>
        public string Name => "open-project";

        /// <inheritdoc/>
        public string Description => "Opens a TIA Portal project file (.ap21/.als21 primary; legacy project formats also supported)";

        /// <inheritdoc/>
        public CommandSchema Schema => new CommandSchema(
            Description,
            new[]
            {
                new CommandParameter(
                    "project-path",
                    CommandParameterType.Path,
                    "Full path to the project file",
                    required: true
                )
            }
        );

        /// <inheritdoc/>
        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            logger.LogInformation("Executing command: {CommandName}", Name);

            try
            {
                var projectPath = args.GetString("project-path");

                if (string.IsNullOrEmpty(projectPath))
                {
                    return CommandResult.CreateFailure("Project path is required");
                }

                logger.LogInformation("Opening project: {ProjectPath}", projectPath);

                var success = await _portalService.OpenProjectAsync(projectPath);

                if (success)
                {
                    var projectInfo = await _portalService.GetProjectInfoAsync();

                    return CommandResult.CreateSuccess(
                        data: projectInfo is null ? new { path = projectPath } : (object)projectInfo,
                        message: $"Project opened: {projectInfo?.Name}"
                    );
                }
                else
                {
                    return CommandResult.CreateFailure(
                        $"Failed to open project: {projectPath}"
                    );
                }
            }
            catch (System.Exception ex)
            {
                logger.LogError(ex, "Error opening project");
                return CommandResult.CreateFailure(
                    $"Error opening project: {ex.Message}",
                    ex
                );
            }
        }
    }
}
