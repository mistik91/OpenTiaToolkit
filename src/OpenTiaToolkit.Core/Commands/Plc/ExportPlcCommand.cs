using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Plc
{
    /// <summary>
    /// Command to export all PLC blocks to XML files.
    /// </summary>
    public class ExportPlcCommand : ICommand
    {
        private readonly IPlcService _plcService;

        /// <summary>
        /// Initializes a new instance of the <see cref="ExportPlcCommand"/> class.
        /// </summary>
        /// <param name="plcService">The PLC service instance.</param>
        public ExportPlcCommand(IPlcService plcService)
        {
            _plcService = plcService;
        }

        /// <inheritdoc/>
        public string Name => "export-plc";

        /// <inheritdoc/>
        public string Description => "Exports all PLC blocks to XML files";

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
                    CommandParameterType.Path,
                    "The directory path for XML export",
                    required: true)
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

                // Open PLC if not already open
                if (!_plcService.IsSoftwareOpen || _plcService.CurrentPlcName != plcName)
                {
                    await _plcService.OpenPlcAsync(plcName);
                }

                var success = await _plcService.ExportPlcAsync(exportPath);

                if (success)
                {
                    return CommandResult.CreateSuccess(
                        data: new
                        {
                            plcName,
                            exportPath,
                            exported = true
                        },
                        message: $"PLC blocks exported to: {exportPath}"
                    );
                }
                else
                {
                    return CommandResult.CreateFailure(
                        "Failed to export PLC blocks"
                    );
                }
            }
            catch (System.Exception ex)
            {
                logger.LogError(ex, "Error exporting PLC blocks");
                return CommandResult.CreateFailure(
                    $"Error exporting PLC blocks: {ex.Message}",
                    ex
                );
            }
        }
    }
}
