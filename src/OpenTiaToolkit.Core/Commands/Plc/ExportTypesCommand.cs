using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Models;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Plc
{
    /// <summary>
    /// Command to export multiple types (UDT) to XML.
    /// </summary>
    public class ExportTypesCommand : ICommand
    {
        private readonly IPlcService _plcService;

        /// <summary>
        /// Initializes a new instance of <see cref="ExportTypesCommand"/> class.
        /// </summary>
        /// <param name="plcService">The PLC service instance.</param>
        public ExportTypesCommand(IPlcService plcService)
        {
            _plcService = plcService;
        }

        /// <inheritdoc/>
        public string Name => "export-types";

        /// <inheritdoc/>
        public string Description => "Exports multiple types (UDT) to XML";

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
                    "The directory path where XML files will be saved",
                    required: true),
                new CommandParameter(
                    "filter-regex",
                    CommandParameterType.String,
                    "Optional regex filter for type names (e.g., \"Motor_.*\")",
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

                var exportPath = args.GetString("export-path");
                if (string.IsNullOrEmpty(exportPath))
                {
                    return CommandResult.CreateFailure("Missing required parameter: export-path");
                }

                var filterRegex = args.GetString("filter-regex");

                // Open PLC if not already open
                if (!_plcService.IsSoftwareOpen || _plcService.CurrentPlcName != plcName)
                {
                    await _plcService.OpenPlcAsync(plcName);
                }

                var result = await _plcService.ExportTypesAsync(filterRegex, exportPath);

                // Build human-readable summary message
                var message = $"Exported {result.SuccessCount}/{result.TotalCount} types to XML";
                if (result.FailedCount > 0)
                {
                    message += $" ({result.FailedCount} failed)";
                }

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
            catch (System.Exception ex)
            {
                logger.LogError(ex, "Error exporting types");
                return CommandResult.CreateFailure(
                    $"Error exporting types: {ex.Message}",
                    ex
                );
            }
        }
    }
}
