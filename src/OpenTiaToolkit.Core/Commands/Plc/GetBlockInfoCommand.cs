using OpenTiaToolkit.Core.Models;
using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Plc
{
    /// <summary>
    /// Command to get detailed information about a specific block.
    /// </summary>
    public class GetBlockInfoCommand : IContextualCommand
    {
        private readonly IPlcService _plcService;

        /// <summary>
        /// Initializes a new instance of <see cref="GetBlockInfoCommand"/> class.
        /// </summary>
        /// <param name="plcService">The PLC service instance.</param>
        public GetBlockInfoCommand(IPlcService plcService)
        {
            _plcService = plcService;
        }

        /// <inheritdoc/>
        public string Name => "get-block-info";

        /// <inheritdoc/>
        public string Description => "Gets detailed information about a specific PLC block";

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
                    "block-name",
                    CommandParameterType.String,
                    "The name of the block",
                    required: true)
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
                context.StartCommand(Name, "Reading PLC block information.");
                var plcName = args.GetString("plc-name");
                if (string.IsNullOrEmpty(plcName))
                {
                    return CommandResult.CreateFailure("Missing required parameter: plc-name");
                }

                var blockName = args.GetString("block-name");
                if (string.IsNullOrEmpty(blockName))
                {
                    return CommandResult.CreateFailure("Missing required parameter: block-name");
                }

                // Open PLC if not already open
                if (!_plcService.IsSoftwareOpen || _plcService.CurrentPlcName != plcName)
                {
                    context.Checkpoint($"Opening PLC software: {plcName}.", 25, "open-plc");
                    await _plcService.OpenPlcAsync(plcName);
                }

                context.Checkpoint($"Reading block information: {blockName}.", 65, "read-block");
                var blockInfo = await _plcService.GetBlockInfoAsync(blockName);

                if (blockInfo == null)
                {
                    context.CompleteCommand(Name, $"Block not found: {blockName}");
                    return CommandResult.CreateFailure($"Block not found: {blockName}");
                }

                // Return BlockInfoDetail directly for proper formatting
                context.CompleteCommand(Name, $"Block: {blockInfo.Name} ({blockInfo.Type})");
                return CommandResult.CreateSuccess(
                    data: blockInfo,
                    message: $"Block: {blockInfo.Name} ({blockInfo.Type})"
                );
            }
            catch (System.Exception ex)
            {
                logger.LogError(ex, "Error getting block info");
                return CommandResult.CreateFailure(
                    $"Error getting block info: {ex.Message}",
                    ex
                );
            }
        }
    }
}
