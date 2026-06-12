using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Plc
{
    /// <summary>
    /// Command to import multiple blocks from XML files in a directory.
    /// </summary>
    public class ImportBlocksCommand : ICommand
    {
        private readonly IPlcService _plcService;

        /// <summary>
        /// Initializes a new instance of <see cref="ImportBlocksCommand"/> class.
        /// </summary>
        /// <param name="plcService">The PLC service instance.</param>
        public ImportBlocksCommand(IPlcService plcService)
        {
            _plcService = plcService;
        }

        /// <inheritdoc/>
        public string Name => "import-blocks";

        /// <inheritdoc/>
        public string Description => "Imports multiple blocks from XML files in a directory";

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
                    "import-directory",
                    CommandParameterType.String,
                    "Directory containing XML files to import",
                    required: true),
                new CommandParameter(
                    "filter-regex",
                    CommandParameterType.String,
                    "Optional regex filter for file names",
                    required: false),
                new CommandParameter(
                    "group-path",
                    CommandParameterType.String,
                    "Optional path within PLC program",
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

                var importDirectory = args.GetString("import-directory");
                if (string.IsNullOrEmpty(importDirectory))
                {
                    return CommandResult.CreateFailure("Missing required parameter: import-directory");
                }

                var filterRegex = args.GetString("filter-regex");
                var groupPath = args.GetString("group-path");

                // Open PLC if not already open
                if (!_plcService.IsSoftwareOpen || _plcService.CurrentPlcName != plcName)
                {
                    await _plcService.OpenPlcAsync(plcName);
                }

                var result = await _plcService.ImportBlocksAsync(importDirectory, filterRegex, groupPath);

                return CommandResult.CreateSuccess(
                    data: new
                    {
                        plcName,
                        importDirectory,
                        filterRegex,
                        groupPath,
                        result.TotalCount,
                        result.SuccessCount,
                        result.FailedCount,
                        result.ImportedItems,
                        result.FailedItems,
                        result.Errors
                    },
                    message: result.SuccessCount > 0
                        ? $"Successfully imported {result.SuccessCount}/{result.TotalCount} blocks"
                        : "Failed to import any blocks"
                );
            }
            catch (System.Exception ex)
            {
                logger.LogError(ex, "Error importing blocks");
                return CommandResult.CreateFailure(
                    $"Error importing blocks: {ex.Message}",
                    ex
                );
            }
        }
    }
}
