using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Plc
{
    /// <summary>
    /// Command to list blocks from PLC software.
    /// </summary>
    public class GetBlocksCommand : ICommand
    {
        private readonly IPlcService _plcService;

        /// <summary>
        /// Initializes a new instance of the <see cref="GetBlocksCommand"/> class.
        /// </summary>
        /// <param name="plcService">The PLC service instance.</param>
        public GetBlocksCommand(IPlcService plcService)
        {
            _plcService = plcService;
        }

        /// <inheritdoc/>
        public string Name => "get-blocks";

        /// <inheritdoc/>
        public string Description => "Lists blocks from PLC software";

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
                    "Optional regex filter for block names",
                    required: false)
            }
        );

        /// <inheritdoc/>
        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            logger.LogInformation("Executing command: {CommandName}", Name);

            try
            {
                var plcName = args.GetString("plc-name");
                if (string.IsNullOrEmpty(plcName))
                {
                    return CommandResult.CreateFailure("Missing required parameter: plc-name");
                }

                var filter = args.GetString("filter");

                // Open PLC if not already open
                if (!_plcService.IsSoftwareOpen || _plcService.CurrentPlcName != plcName)
                {
                    await _plcService.OpenPlcAsync(plcName);
                }

                var blocks = await _plcService.GetBlocksAsync(filter);

                // Return blocks directly as data for proper formatting
                return CommandResult.CreateSuccess(
                    data: blocks,
                    message: $"Found {blocks.Count} block(s) in {plcName}"
                );
            }
            catch (System.Exception ex)
            {
                logger.LogError(ex, "Error getting blocks");
                return CommandResult.CreateFailure(
                    $"Error getting blocks: {ex.Message}",
                    ex
                );
            }
        }
    }
}
