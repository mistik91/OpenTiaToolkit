using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Plc
{
    /// <summary>
    /// Command to export a single block as SIMATIC SD documents (.s7dcl + .s7res).
    /// Requires TIA Portal V20 or newer.
    /// </summary>
    public class ExportBlockAsDocumentsCommand : ICommand
    {
        private readonly IPlcService _plcService;

        /// <summary>
        /// Initializes a new instance of <see cref="ExportBlockAsDocumentsCommand"/> class.
        /// </summary>
        /// <param name="plcService">The PLC service instance.</param>
        public ExportBlockAsDocumentsCommand(IPlcService plcService)
        {
            _plcService = plcService;
        }

        /// <inheritdoc/>
        public string Name => "export-block-as-docs";

        /// <inheritdoc/>
        public string Description => "Exports a single block as SIMATIC SD documents (.s7dcl + .s7res) - requires V20+";

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
                    "block-name",
                    CommandParameterType.String,
                    "The name of the block to export",
                    required: true),
                new CommandParameter(
                    "export-path",
                    CommandParameterType.String,
                    "The directory path where files will be saved",
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

                var blockName = args.GetString("block-name");
                if (string.IsNullOrEmpty(blockName))
                {
                    return CommandResult.CreateFailure("Missing required parameter: block-name");
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

                var success = await _plcService.ExportBlockAsDocumentsAsync(blockName, exportPath);

                return success
                    ? CommandResult.CreateSuccess(
                        data: new
                        {
                            plcName,
                            blockName,
                            exportPath,
                            format = "SIMATIC SD (.s7dcl + .s7res)"
                        },
                        message: $"Successfully exported block as SIMATIC SD documents: {blockName}"
                    )
                    : CommandResult.CreateFailure($"Failed to export block as documents: {blockName}");
            }
            catch (System.InvalidOperationException ex)
            {
                logger.LogError(ex, "Version check failed for export-block-as-docs");
                return CommandResult.CreateFailure(
                    ex.Message,
                    ex
                );
            }
            catch (System.Exception ex)
            {
                logger.LogError(ex, "Error exporting block as documents");
                return CommandResult.CreateFailure(
                    $"Error exporting block as documents: {ex.Message}",
                    ex
                );
            }
        }
    }
}
