using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Plc
{
    /// <summary>
    /// Command to export a single type (UDT) to XML.
    /// </summary>
    public class ExportTypeCommand : IContextualCommand
    {
        private readonly IPlcService _plcService;

        /// <summary>
        /// Initializes a new instance of <see cref="ExportTypeCommand"/> class.
        /// </summary>
        /// <param name="plcService">The PLC service instance.</param>
        public ExportTypeCommand(IPlcService plcService)
        {
            _plcService = plcService;
        }

        /// <inheritdoc/>
        public string Name => "export-type";

        /// <inheritdoc/>
        public string Description => "Exports a single type (UDT) to XML";

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
                    "type-name",
                    CommandParameterType.String,
                    "The name of the type to export",
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
            context.StartCommand(Name, "Validating PLC type export inputs.");

            try
            {
                var plcName = args.GetString("plc-name");
                if (string.IsNullOrEmpty(plcName))
                {
                    return CommandResult.CreateFailure("Missing required parameter: plc-name");
                }

                var typeName = args.GetString("type-name");
                if (string.IsNullOrEmpty(typeName))
                {
                    return CommandResult.CreateFailure("Missing required parameter: type-name");
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

                context.Checkpoint($"Exporting PLC type: {typeName}.", 55, "export-type");
                var success = await _plcService.ExportTypeAsync(typeName, exportPath);

                if (success)
                {
                    context.CompleteCommand(Name, $"Successfully exported type: {typeName}");
                }

                return success
                    ? CommandResult.CreateSuccess(
                        data: new
                        {
                            plcName,
                            typeName,
                            exportPath,
                            format = "XML"
                        },
                        message: $"Successfully exported type: {typeName}"
                    )
                    : CommandResult.CreateFailure($"Failed to export type: {typeName}");
            }
            catch (System.Exception ex)
            {
                logger.LogError(ex, "Error exporting type");
                return CommandResult.CreateFailure(
                    $"Error exporting type: {ex.Message}",
                    ex
                );
            }
        }
    }
}
