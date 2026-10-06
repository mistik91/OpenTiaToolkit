using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Models;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Plc
{
    /// <summary>
    /// Command to import multiple blocks from SIMATIC SD documents (.s7dcl/.s7res).
    /// </summary>
    public class ImportBlocksFromDocumentsCommand : IContextualCommand
    {
        private readonly IPlcService _plcService;

        /// <summary>
        /// Initializes a new instance of <see cref="ImportBlocksFromDocumentsCommand"/> class.
        /// </summary>
        /// <param name="plcService">The PLC service instance.</param>
        public ImportBlocksFromDocumentsCommand(IPlcService plcService)
        {
            _plcService = plcService;
        }

        /// <inheritdoc/>
        public string Name => "import-blocks-from-docs";

        /// <inheritdoc/>
        public string Description => "Imports multiple blocks from SIMATIC SD documents (.s7dcl/.s7res)";

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
                    "Directory containing .s7dcl and .s7res files",
                    required: true),
                new CommandParameter(
                    "filter-regex",
                    CommandParameterType.String,
                    "Optional regex filter for block names",
                    required: false),
                new CommandParameter(
                    "group-path",
                    CommandParameterType.String,
                    "Optional path within PLC program",
                    required: false),
                new CommandParameter(
                    "import-option",
                    CommandParameterType.String,
                    "Import behavior: Override, None, SkipInactiveCultures, ActivateInactiveCultures (default: Override)",
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
            context.StartCommand(Name, "Validating PLC blocks document import inputs.");

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
                var importOption = args.GetString("import-option") ?? "Override";

                // Open PLC if not already open
                if (!_plcService.IsSoftwareOpen || _plcService.CurrentPlcName != plcName)
                {
                    context.Checkpoint($"Opening PLC software: {plcName}.", 25, "open-plc");
                    await _plcService.OpenPlcAsync(plcName);
                }

                context.Checkpoint("Importing PLC blocks from SIMATIC SD documents.", 55, "import-blocks-docs");
                var result = await _plcService.ImportBlocksFromDocumentsAsync(importDirectory, filterRegex, groupPath, importOption);

                context.CompleteCommand(Name, result.SuccessCount > 0
                    ? $"Successfully imported {result.SuccessCount}/{result.TotalCount} blocks from documents"
                    : "Failed to import any blocks from documents");
                var data = PlcImportCommandResult.From(plcName, importDirectory, filterRegex, groupPath, result, importOption);
                var message = result.SuccessCount > 0
                    ? $"Imported {result.SuccessCount}/{result.TotalCount} blocks from documents ({result.FailedCount} failed)"
                    : "Failed to import any blocks from documents";
                return result.SuccessCount > 0 && result.FailedCount == 0
                    ? CommandResult.CreateSuccess(data, message)
                    : CommandResult.CreateFailure(message, data);
            }
            catch (System.Exception ex)
            {
                logger.LogError(ex, "Error importing blocks from documents");
                return CommandResult.CreateFailure(
                    $"Error importing blocks from documents: {ex.Message}",
                    ex
                );
            }
        }
    }
}
