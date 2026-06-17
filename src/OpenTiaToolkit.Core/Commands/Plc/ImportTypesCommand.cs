using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Plc
{
    /// <summary>
    /// Command to import multiple types (UDT) from XML files in a directory.
    /// </summary>
    public class ImportTypesCommand : IContextualCommand
    {
        private readonly IPlcService _plcService;

        /// <summary>
        /// Initializes a new instance of <see cref="ImportTypesCommand"/> class.
        /// </summary>
        /// <param name="plcService">The PLC service instance.</param>
        public ImportTypesCommand(IPlcService plcService)
        {
            _plcService = plcService;
        }

        /// <inheritdoc/>
        public string Name => "import-types";

        /// <inheritdoc/>
        public string Description => "Imports multiple types (UDT) from XML files in a directory";

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
                    "Optional path within type groups",
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
            context.StartCommand(Name, "Validating PLC types import inputs.");

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
                    context.Checkpoint($"Opening PLC software: {plcName}.", 25, "open-plc");
                    await _plcService.OpenPlcAsync(plcName);
                }

                context.Checkpoint("Importing PLC types into TIA Portal.", 55, "import-types");
                var result = await _plcService.ImportTypesAsync(importDirectory, filterRegex, groupPath);

                context.CompleteCommand(Name, result.SuccessCount > 0
                    ? $"Successfully imported {result.SuccessCount}/{result.TotalCount} types"
                    : "Failed to import any types");
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
                        ? $"Successfully imported {result.SuccessCount}/{result.TotalCount} types"
                        : "Failed to import any types"
                );
            }
            catch (System.Exception ex)
            {
                logger.LogError(ex, "Error importing types");
                return CommandResult.CreateFailure(
                    $"Error importing types: {ex.Message}",
                    ex
                );
            }
        }
    }
}
