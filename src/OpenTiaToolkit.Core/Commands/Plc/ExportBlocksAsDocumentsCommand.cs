using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Plc
{
    /// <summary>
    /// Command to export multiple blocks as SIMATIC SD documents (.s7dcl + .s7res).
    /// Requires TIA Portal V20 or newer.
    /// </summary>
    public class ExportBlocksAsDocumentsCommand : IContextualCommand
    {
        private readonly IPlcService _plcService;

        /// <summary>
        /// Initializes a new instance of <see cref="ExportBlocksAsDocumentsCommand"/> class.
        /// </summary>
        /// <param name="plcService">The PLC service instance.</param>
        public ExportBlocksAsDocumentsCommand(IPlcService plcService)
        {
            _plcService = plcService;
        }

        /// <inheritdoc/>
        public string Name => "export-blocks-as-docs";

        /// <inheritdoc/>
        public string Description => "Exports multiple blocks as SIMATIC SD documents (.s7dcl + .s7res) - requires V20+";

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
                    "export-path",
                    CommandParameterType.String,
                    "The directory path where files will be saved",
                    required: true),
                new CommandParameter(
                    "filter-regex",
                    CommandParameterType.String,
                    "Optional regex filter for block names (e.g., \"Motor_.*\")",
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
            context.StartCommand(Name, "Validating PLC blocks document export inputs.");

            try
            {
                var plcName = args.GetString("plc-name");
                if (string.IsNullOrEmpty(plcName))
                {
                    return CommandResult.CreateFailure("Missing required parameter: plc-name");
                }

                var exportPath = args.GetString("export-path");
                if (string.IsNullOrEmpty(exportPath))
                {
                    return CommandResult.CreateFailure("Missing required parameter: export-path");
                }

                var filterRegex = args.GetString("filter-regex");

                // Open PLC if not already open
                if (!_plcService.IsSoftwareOpen || _plcService.CurrentPlcName != plcName)
                {
                    context.Checkpoint($"Opening PLC software: {plcName}.", 25, "open-plc");
                    await _plcService.OpenPlcAsync(plcName);
                }

                context.Checkpoint("Exporting PLC blocks as SIMATIC SD documents.", 55, "export-blocks-docs");
                var result = await _plcService.ExportBlocksAsDocumentsAsync(filterRegex, exportPath);

                // Build human-readable summary message
                var message = $"Exported {result.SuccessCount}/{result.TotalCount} blocks as SIMATIC SD documents";
                if (result.FailedCount > 0)
                {
                    message += $" ({result.FailedCount} skipped/failed)";
                }

                context.CompleteCommand(Name, message);
                return CommandResult.CreateSuccess(
                    data: new
                    {
                        summary = new
                        {
                            total = result.TotalCount,
                            succeeded = result.SuccessCount,
                            failed = result.FailedCount
                        },
                        exported = result.ExportedItems,
                        failed = result.FailedItems,
                        errors = result.Errors
                    },
                    message: message
                );
            }
            catch (System.InvalidOperationException ex)
            {
                logger.LogError(ex, "Version check failed for export-blocks-as-docs");
                return CommandResult.CreateFailure(
                    ex.Message,
                    ex
                );
            }
            catch (System.Exception ex)
            {
                logger.LogError(ex, "Error exporting blocks as documents");
                return CommandResult.CreateFailure(
                    $"Error exporting blocks as documents: {ex.Message}",
                    ex
                );
            }
        }
    }
}
