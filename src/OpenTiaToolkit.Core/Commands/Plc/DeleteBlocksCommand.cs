using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System.Linq;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Plc
{
    /// <summary>
    /// Command to delete blocks from PLC software.
    /// </summary>
    public class DeleteBlocksCommand : IContextualCommand
    {
        private readonly IPlcService _plcService;

        /// <summary>
        /// Initializes a new instance of <see cref="DeleteBlocksCommand"/> class.
        /// </summary>
        /// <param name="plcService">The PLC service instance.</param>
        public DeleteBlocksCommand(IPlcService plcService)
        {
            _plcService = plcService;
        }

        /// <inheritdoc/>
        public string Name => "delete-blocks";

        /// <inheritdoc/>
        public string Description => "Deletes blocks matching a regex pattern from PLC software";

        /// <inheritdoc/>
        public CommandSchema Schema => new CommandSchema(
            Description,
            new[]
            {
                new CommandParameter(
                    "plc-name",
                    CommandParameterType.String,
                    "The name of the PLC device",
                    required: true),
                new CommandParameter(
                    "filter",
                    CommandParameterType.String,
                    "Regex pattern for block names to delete",
                    required: true),
                new CommandParameter(
                    "confirm",
                    CommandParameterType.Boolean,
                    "Confirm deletion without prompting (default: false)",
                    required: false)
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
                context.StartCommand(Name, "Deleting PLC blocks.");
                var plcName = args.GetString("plc-name");
                if (string.IsNullOrEmpty(plcName))
                {
                    return CommandResult.CreateFailure("Missing required parameter: plc-name");
                }

                var filter = args.GetString("filter");
                if (string.IsNullOrEmpty(filter))
                {
                    return CommandResult.CreateFailure("Missing required parameter: filter");
                }

                var confirm = args.GetBool("confirm");

                // Open PLC if not already open
                if (!_plcService.IsSoftwareOpen || _plcService.CurrentPlcName != plcName)
                {
                    context.Checkpoint($"Opening PLC software: {plcName}.", 25, "open-plc");
                    await _plcService.OpenPlcAsync(plcName);
                }

                context.Checkpoint("Applying PLC block deletion.", 65, "delete-blocks");
                var success = await _plcService.DeleteBlocksAsync(filter, confirm);
                context.CompleteCommand(Name, success ? "Blocks deleted successfully" : "PLC block deletion did not complete.");

                return success
                    ? CommandResult.CreateSuccess(
                        data: new
                        {
                            plcName,
                            filter,
                            confirmed = confirm
                        },
                        message: "Blocks deleted successfully"
                    )
                    : CommandResult.CreateFailure(
                        confirm
                            ? "Failed to delete blocks (check logs for details)"
                            : "Deletion requires confirmation. Use --confirm=true to proceed."
                    );
            }
            catch (System.Exception ex)
            {
                logger.LogError(ex, "Error deleting blocks");
                return CommandResult.CreateFailure(
                    $"Error deleting blocks: {ex.Message}",
                    ex
                );
            }
        }
    }
}
