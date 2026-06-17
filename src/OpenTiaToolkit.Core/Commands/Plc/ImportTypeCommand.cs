using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Plc
{
    /// <summary>
    /// Command to import a single type (UDT) from XML file.
    /// </summary>
    public class ImportTypeCommand : IContextualCommand
    {
        private readonly IPlcService _plcService;

        /// <summary>
        /// Initializes a new instance of <see cref="ImportTypeCommand"/> class.
        /// </summary>
        /// <param name="plcService">The PLC service instance.</param>
        public ImportTypeCommand(IPlcService plcService)
        {
            _plcService = plcService;
        }

        /// <inheritdoc/>
        public string Name => "import-type";

        /// <inheritdoc/>
        public string Description => "Imports a single type (UDT) from XML file";

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
                    "Optional path within type groups (e.g., 'Types/Subgroups')",
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
            context.StartCommand(Name, "Validating PLC type import inputs.");

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

                context.Checkpoint($"Importing PLC type from: {importPath}.", 55, "import-type");
                var success = await _plcService.ImportTypeAsync(importPath, groupPath);

                if (success)
                {
                    context.CompleteCommand(Name, $"Successfully imported type from: {importPath}");
                }

                return success
                    ? CommandResult.CreateSuccess(
                        data: new
                        {
                            plcName,
                            importPath,
                            groupPath
                        },
                        message: $"Successfully imported type from: {importPath}"
                    )
                    : CommandResult.CreateFailure($"Failed to import type from: {importPath}");
            }
            catch (System.Exception ex)
            {
                logger.LogError(ex, "Error importing type");
                return CommandResult.CreateFailure(
                    $"Error importing type: {ex.Message}",
                    ex
                );
            }
        }
    }
}
