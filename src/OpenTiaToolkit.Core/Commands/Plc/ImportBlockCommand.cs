using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Plc
{
    /// <summary>
    /// Command to import a single block from XML file.
    /// </summary>
    public class ImportBlockCommand : IContextualCommand
    {
        private readonly IPlcService _plcService;

        /// <summary>
        /// Initializes a new instance of <see cref="ImportBlockCommand"/> class.
        /// </summary>
        /// <param name="plcService">The PLC service instance.</param>
        public ImportBlockCommand(IPlcService plcService)
        {
            _plcService = plcService;
        }

        /// <inheritdoc/>
        public string Name => "import-block";

        /// <inheritdoc/>
        public string Description => "Imports a single block from XML file";

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
                    "import-path",
                    CommandParameterType.String,
                    "Full path to the XML file to import",
                    required: true),
                new CommandParameter(
                    "group-path",
                    CommandParameterType.String,
                    "Optional path within PLC program (e.g., 'Program Blocks/Motors')",
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
            context.StartCommand(Name, "Validating PLC block import inputs.");

            try
            {
                var plcName = args.GetString("plc-name");
                if (string.IsNullOrEmpty(plcName))
                {
                    return CommandResult.CreateFailure("Missing required parameter: plc-name");
                }

                var importPath = args.GetString("import-path");
                if (string.IsNullOrEmpty(importPath))
                {
                    return CommandResult.CreateFailure("Missing required parameter: import-path");
                }

                var groupPath = args.GetString("group-path");

                // Open PLC if not already open
                if (!_plcService.IsSoftwareOpen || _plcService.CurrentPlcName != plcName)
                {
                    context.Checkpoint($"Opening PLC software: {plcName}.", 25, "open-plc");
                    await _plcService.OpenPlcAsync(plcName);
                }

                context.Checkpoint($"Importing PLC block from: {importPath}.", 55, "import-block");
                var success = await _plcService.ImportBlockAsync(importPath, groupPath);

                if (success)
                {
                    context.CompleteCommand(Name, $"Successfully imported block from: {importPath}");
                }

                return success
                    ? CommandResult.CreateSuccess(
                        data: new
                        {
                            plcName,
                            importPath,
                            groupPath
                        },
                        message: $"Successfully imported block from: {importPath}"
                    )
                    : CommandResult.CreateFailure($"Failed to import block from: {importPath}");
            }
            catch (System.Exception ex)
            {
                logger.LogError(ex, "Error importing block");
                return CommandResult.CreateFailure(
                    $"Error importing block: {ex.Message}",
                    ex
                );
            }
        }
    }
}
