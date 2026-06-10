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
                    required: false),
                new CommandParameter(
                    "output-path",
                    CommandParameterType.Path,
                    "Optional CSV file or folder path for matching PLC block rows",
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

                var searchIn = args.Has("search-in") && !string.IsNullOrWhiteSpace(args.GetString("search-in"))
                    ? args.GetString("search-in")
                    : "name";
                var blockType = args.Has("filter-type") ? args.GetString("filter-type") : null;

                // Open PLC if not already open
                if (!_plcService.IsSoftwareOpen || _plcService.CurrentPlcName != plcName)
                {
                    await _plcService.OpenPlcAsync(plcName);
                }

                var blocks = await _plcService.SearchBlocksAsync(pattern, searchIn, blockType);
                var message = $"Found {blocks.Count} matching block(s) in {plcName}";

                if (args.Has("output-path") || CommandArtifactSupport.ExceedsInlineThreshold(blocks, message))
                {
                    var outputPath = PlcCommandSupport.ResolveOutputPath(
                        args.Has("output-path")
                            ? args.GetString("output-path")
                            : CommandArtifactSupport.CreateDefaultArtifactPath(Name, ".csv"),
                        "plc_block_search_results.csv");
                    var bytesWritten = PlcCommandSupport.WriteBlocksCsv(blocks, outputPath);
                    var artifact = new CommandArtifactResult
                    {
                        OutputPath = outputPath,
                        Format = "csv",
                        DataKind = "plc-block-search-results",
                        BytesWritten = bytesWritten,
                        ItemCount = blocks.Count,
                        InlineOutputSuppressed = !args.Has("output-path"),
                        Reason = args.Has("output-path")
                            ? "output-path was provided"
                            : $"inline result exceeded {CommandArtifactSupport.InlineResultThresholdBytes} bytes",
                        Columns = PlcCommandSupport.BlockCsvColumns,
                        Metadata = new
                        {
                            plcName,
                            pattern,
                            searchIn,
                            blockType
                        }
                    };

                    return CommandResult.CreateSuccess(
                        data: artifact,
                        message: $"{message}. CSV written to: {outputPath}"
                    );
                }

                // Return blocks directly as data for proper formatting
                return CommandResult.CreateSuccess(
                    data: blocks,
                    message: message
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
