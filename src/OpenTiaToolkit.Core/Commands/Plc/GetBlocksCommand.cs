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
                    required: false),
                new CommandParameter(
                    "output-path",
                    CommandParameterType.Path,
                    "Optional CSV file or folder path for PLC block rows",
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
                var message = $"Found {blocks.Count} block(s) in {plcName}";

                if (args.Has("output-path") || CommandArtifactSupport.ExceedsInlineThreshold(blocks, message))
                {
                    var outputPath = PlcCommandSupport.ResolveOutputPath(
                        args.Has("output-path")
                            ? args.GetString("output-path")
                            : CommandArtifactSupport.CreateDefaultArtifactPath(Name, ".csv"),
                        "plc_blocks.csv");
                    var bytesWritten = PlcCommandSupport.WriteBlocksCsv(blocks, outputPath);
                    var artifact = new CommandArtifactResult
                    {
                        OutputPath = outputPath,
                        Format = "csv",
                        DataKind = "plc-blocks",
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
                            filter
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
                logger.LogError(ex, "Error getting blocks");
                return CommandResult.CreateFailure(
                    $"Error getting blocks: {ex.Message}",
                    ex
                );
            }
        }
    }
}
