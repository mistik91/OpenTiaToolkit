using OpenTiaToolkit.Core.Models;
using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Hmi
{
    /// <summary>
    /// Command to list all HMI devices in the current project.
    /// </summary>
    public class ListHmisCommand : IContextualCommand
    {
        private readonly IPortalService _portalService;

        /// <summary>
        /// Initializes a new instance of the <see cref="ListHmisCommand"/> class.
        /// </summary>
        /// <param name="portalService">The portal service instance.</param>
        public ListHmisCommand(IPortalService portalService)
        {
            _portalService = portalService;
        }

        /// <inheritdoc/>
        public string Name => "list-hmis";

        /// <inheritdoc/>
        public string Description => "Lists all HMI devices found in the project";

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
                context.StartCommand(Name, "Listing HMI devices.");
                if (!_portalService.IsConnected)
                {
                    return CommandResult.CreateFailure("Not connected to TIA Portal");
                }

                context.Checkpoint("Scanning project HMI devices.", 50, "list-hmis");
                var hmis = await _portalService.GetHmisAsync();

                if (hmis == null || !hmis.Any())
                {
                    context.CompleteCommand(Name, "No HMI devices found in the project");
                    return CommandResult.CreateSuccess(
                        data: Array.Empty<HmiInfo>(),
                        message: "No HMI devices found in the project"
                    );
                }

                var hmiList = hmis.ToList();
                context.CompleteCommand(Name, $"Found {hmiList.Count} HMI device(s)");
                return CommandResult.CreateSuccess(
                    data: hmiList,
                    message: $"Found {hmiList.Count} HMI device(s)"
                );
            }
            catch (System.Exception ex)
            {
                logger.LogError(ex, "Error listing HMIs");
                return CommandResult.CreateFailure(
                    $"Error listing HMIs: {ex.Message}",
                    ex
                );
            }
        }
    }
}
