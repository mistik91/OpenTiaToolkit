using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Plc
{
    /// <summary>
    /// Command to export a single block to XML.
    /// </summary>
    public class ExportBlockCommand : IContextualCommand
    {
        private readonly IPlcService _plcService;

        /// <summary>
        /// Initializes a new instance of <see cref="ExportBlockCommand"/> class.
        /// </summary>
        /// <param name="plcService">The PLC service instance.</param>
        public ExportBlockCommand(IPlcService plcService)
        {
            _plcService = plcService;
        }

        /// <inheritdoc/>
        public string Name => "export-block";

        /// <inheritdoc/>
        public string Description => "Exports a single PLC block to XML";

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
                    "The full file path where the XML will be saved",
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
            context.StartCommand(Name, "Validating PLC block export inputs.");

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

                context.Checkpoint($"Exporting PLC block: {blockName}.", 55, "export-block");
                var success = await _plcService.ExportBlockAsync(blockName, exportPath);

                if (success)
                {
                    context.CompleteCommand(Name, $"Successfully exported block: {blockName}");
                }

                return success
                    ? CommandResult.CreateSuccess(
                        data: new
                        {
                            plcName,
                            blockName,
                            exportPath
                        },
                        message: $"Successfully exported block: {blockName}"
                    )
                    : CommandResult.CreateFailure($"Failed to export block: {blockName}");
            }
            catch (System.Exception ex)
            {
                logger.LogError(ex, "Error exporting block");
                return CommandResult.CreateFailure(
                    $"Error exporting block: {ex.Message}",
                    ex
                );
            }
        }
    }
}
