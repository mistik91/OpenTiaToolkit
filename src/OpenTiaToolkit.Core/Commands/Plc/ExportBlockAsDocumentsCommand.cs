using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Plc
{
    /// <summary>
    /// Command to export a single block as SIMATIC SD documents (.s7dcl + .s7res).
    /// Requires a supported TIA Portal version.
    /// </summary>
    public class ExportBlockAsDocumentsCommand : IContextualCommand
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
        public string Description => "Exports a single block as SIMATIC SD documents (.s7dcl + .s7res)";

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
        public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            return ExecuteAsync(args, logger, CommandExecutionContext.None);
        }

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger, CommandExecutionContext context)
        {
            logger.LogInformation("Executing command: {CommandName}", Name);
            context.StartCommand(Name, "Validating PLC block document export inputs.");

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
                    context.Checkpoint($"Opening PLC software: {plcName}.", 25, "open-plc");
                    await _plcService.OpenPlcAsync(plcName);
                }

                context.Checkpoint($"Exporting PLC block as SIMATIC SD documents: {blockName}.", 55, "export-block-docs");
                var result = await _plcService.ExportBlockAsDocumentsWithResultAsync(blockName, exportPath);
                var data = new
                {
                    plcName,
                    blockName,
                    exportPath,
                    format = "SIMATIC SD (.s7dcl + .s7res)",
                    siemensState = result.SiemensState,
                    diagnostics = result.Diagnostics
                };
                if (!result.Success)
                    return CommandResult.CreateFailure(result.Message, data);
                context.CompleteCommand(Name, result.Message);
                return CommandResult.CreateSuccess(data, result.Message);
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
