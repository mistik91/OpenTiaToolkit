using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System.Linq;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Plc
{
    /// <summary>
    /// Command to search blocks by name or comment.
    /// </summary>
    public class SearchBlocksCommand : ICommand
    {
        private readonly IPlcService _plcService;

        /// <summary>
        /// Initializes a new instance of <see cref="SearchBlocksCommand"/> class.
        /// </summary>
        /// <param name="plcService">The PLC service instance.</param>
        public SearchBlocksCommand(IPlcService plcService)
        {
            _plcService = plcService;
        }

        /// <inheritdoc/>
        public string Name => "search-blocks";

        /// <inheritdoc/>
        public string Description => "Searches PLC blocks by name/comment with optional type filter";

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
                    "pattern",
                    CommandParameterType.String,
                    "Search text pattern",
                    required: true),
                new CommandParameter(
                    "search-in",
                    CommandParameterType.String,
                    "Where to search: 'name' or 'comment' (default: 'name')",
                    required: false),
                new CommandParameter(
                    "filter-type",
                    CommandParameterType.String,
                    "Optional block type filter (e.g., 'FB', 'FC', 'DB', 'OB')",
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

                var pattern = args.GetString("pattern");
                if (string.IsNullOrEmpty(pattern))
                {
                    return CommandResult.CreateFailure("Missing required parameter: pattern");
                }

                var searchIn = args.GetString("search-in") ?? "name";
                var blockType = args.GetString("filter-type");

                // Open PLC if not already open
                if (!_plcService.IsSoftwareOpen || _plcService.CurrentPlcName != plcName)
                {
                    await _plcService.OpenPlcAsync(plcName);
                }

                var blocks = await _plcService.SearchBlocksAsync(pattern, searchIn, blockType);

                // Return blocks directly as data for proper formatting
                return CommandResult.CreateSuccess(
                    data: blocks,
                    message: $"Found {blocks.Count} matching block(s) in {plcName}"
                );
            }
            catch (System.Exception ex)
            {
                logger.LogError(ex, "Error searching blocks");
                return CommandResult.CreateFailure(
                    $"Error searching blocks: {ex.Message}",
                    ex
                );
            }
        }
    }
}
