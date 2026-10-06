using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Portal
{
    /// <summary>
    /// Command to list all running TIA Portal instances.
    /// </summary>
    public class ListInstancesCommand : ICommand
    {
        private readonly IPortalService _portalService;

        /// <summary>
        /// Initializes a new instance of the <see cref="ListInstancesCommand"/> class.
        /// </summary>
        /// <param name="portalService">The portal service instance.</param>
        public ListInstancesCommand(IPortalService portalService)
        {
            _portalService = portalService;
        }

        /// <inheritdoc/>
        public string Name => "list-instances";

        /// <inheritdoc/>
        public string Description => "Lists all running TIA Portal instances";

        /// <inheritdoc/>
        public CommandSchema Schema => new CommandSchema(Description);

        /// <inheritdoc/>
        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            logger.LogInformation("Executing command: {CommandName}", Name);

            try
            {
                var instances = await _portalService.GetInstancesAsync();

                if (instances.Any())
                {
                    return CommandResult.CreateSuccess(
                        data: new
                        {
                            count = instances.Count,
                            instances = instances.Select(i => new
                            {
                                processId = i.ProcessId,
                                version = i.Version,
                                isAttached = i.IsAttached
                            })
                        },
                        message: $"Found {instances.Count} TIA Portal instance(s)"
                    );
                }
                else
                {
                    return CommandResult.CreateSuccess(
                        data: new
                        {
                            count = 0,
                            instances = new List<object>()
                        },
                        message: "No TIA Portal instances found"
                    );
                }
            }
            catch (System.Exception ex)
            {
                logger.LogError(ex, "Error listing TIA Portal instances");
                return CommandResult.CreateFailure(
                    $"Error listing TIA Portal instances: {ex.Message}",
                    ex
                );
            }
        }
    }
}
