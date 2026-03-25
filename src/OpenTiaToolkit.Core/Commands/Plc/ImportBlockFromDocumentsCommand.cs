using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Plc
{
    /// <summary>
    /// Command to import a single block from SIMATIC SD documents (.s7dcl/.s7res) - V20+ only.
    /// </summary>
    public class ImportBlockFromDocumentsCommand : ICommand
    {
        private readonly IPlcService _plcService;

        /// <summary>
        /// Initializes a new instance of <see cref="ImportBlockFromDocumentsCommand"/> class.
        /// </summary>
        /// <param name="plcService">The PLC service instance.</param>
        public ImportBlockFromDocumentsCommand(IPlcService plcService)
        {
            _plcService = plcService;
        }

        /// <inheritdoc/>
        public string Name => "import-block-from-docs";

        /// <inheritdoc/>
        public string Description => "Imports a single block from SIMATIC SD documents (.s7dcl/.s7res) - V20+ only";

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
                    "file-name",
                    CommandParameterType.String,
                    "Block name without extension (e.g., 'MotorControl')",
                    required: true),
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

                var importDirectory = args.GetString("import-directory");
                if (string.IsNullOrEmpty(importDirectory))
                {
                    return CommandResult.CreateFailure("Missing required parameter: import-directory");
                }

                var fileName = args.GetString("file-name");
                if (string.IsNullOrEmpty(fileName))
                {
                    return CommandResult.CreateFailure("Missing required parameter: file-name");
                }

                var groupPath = args.GetString("group-path");
                var importOption = args.GetString("import-option") ?? "Override";

                // Open PLC if not already open
                if (!_plcService.IsSoftwareOpen || _plcService.CurrentPlcName != plcName)
                {
                    await _plcService.OpenPlcAsync(plcName);
                }

                var success = await _plcService.ImportBlockFromDocumentsAsync(importDirectory, fileName, groupPath, importOption);

                return success
                    ? CommandResult.CreateSuccess(
                        data: new
                        {
                            plcName,
                            importDirectory,
                            fileName,
                            groupPath,
                            importOption
                        },
                        message: $"Successfully imported block from documents: {fileName}"
                    )
                    : CommandResult.CreateFailure($"Failed to import block from documents: {fileName}");
            }
            catch (System.InvalidOperationException ex)
            {
                // Re-throw version check errors with clear message
                logger.LogError(ex, "Error importing block from documents");
                return CommandResult.CreateFailure(ex.Message, ex);
            }
            catch (System.Exception ex)
            {
                logger.LogError(ex, "Error importing block from documents");
                return CommandResult.CreateFailure(
                    $"Error importing block from documents: {ex.Message}",
                    ex
                );
            }
        }
    }
}
