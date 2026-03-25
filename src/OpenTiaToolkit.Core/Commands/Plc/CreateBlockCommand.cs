using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Plc
{
    /// <summary>
    /// Command to create a new block in PLC software.
    /// </summary>
    public class CreateBlockCommand : ICommand
    {
        private readonly IPlcService _plcService;

        /// <summary>
        /// Initializes a new instance of <see cref="CreateBlockCommand"/> class.
        /// </summary>
        /// <param name="plcService">The PLC service instance.</param>
        public CreateBlockCommand(IPlcService plcService)
        {
            _plcService = plcService;
        }

        /// <inheritdoc/>
        public string Name => "create-block";

        /// <inheritdoc/>
        public string Description => "Creates a new block in PLC software (currently validated for ProDiag FB creation)";

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
                    "The name of the block to create",
                    required: true),
                new CommandParameter(
                    "block-type",
                    CommandParameterType.String,
                    "The type of block (currently validated: FB)",
                    required: true),
                new CommandParameter(
                    "language",
                    CommandParameterType.String,
                    "The programming language (currently validated: ProDiag)",
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

                var blockName = args.GetString("block-name");
                if (string.IsNullOrEmpty(blockName))
                {
                    return CommandResult.CreateFailure("Missing required parameter: block-name");
                }

                var blockType = args.GetString("block-type");
                if (string.IsNullOrEmpty(blockType))
                {
                    return CommandResult.CreateFailure("Missing required parameter: block-type");
                }

                var language = args.GetString("language");

                // Validate block type
                var validBlockTypes = new[] { "FB" };
                if (!Array.Exists(validBlockTypes, t => t.Equals(blockType, StringComparison.OrdinalIgnoreCase)))
                {
                    return CommandResult.CreateFailure(
                        $"Invalid block type: {blockType}. Valid types are: {string.Join(", ", validBlockTypes)}"
                    );
                }

                // Validate programming language if provided
                if (!string.IsNullOrEmpty(language))
                {
                    var validLanguages = new[] { "ProDiag", "S7_PDIAG" };
                    if (!Array.Exists(validLanguages, l => l.Equals(language, StringComparison.OrdinalIgnoreCase)))
                    {
                        return CommandResult.CreateFailure(
                            $"Invalid programming language: {language}. Valid languages are: {string.Join(", ", validLanguages)}"
                        );
                    }
                }

                // Open PLC if not already open
                if (!_plcService.IsSoftwareOpen || _plcService.CurrentPlcName != plcName)
                {
                    await _plcService.OpenPlcAsync(plcName);
                }

                var success = await _plcService.CreateBlockAsync(blockName, blockType, language);

                return success
                    ? CommandResult.CreateSuccess(
                        data: new
                        {
                            plcName,
                            blockName,
                            blockType,
                            language
                        },
                        message: $"Block '{blockName}' created successfully"
                    )
                    : CommandResult.CreateFailure(
                        $"Failed to create block '{blockName}' (check logs for details)"
                    );
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error creating block");
                return CommandResult.CreateFailure(
                    $"Error creating block: {ex.Message}",
                    ex
                );
            }
        }
    }
}
