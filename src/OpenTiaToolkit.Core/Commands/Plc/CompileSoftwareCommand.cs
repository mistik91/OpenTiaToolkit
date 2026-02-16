using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Plc
{
    /// <summary>
    /// Command to compile PLC software.
    /// </summary>
    public class CompileSoftwareCommand : ICommand
    {
        private readonly IPlcService _plcService;

        /// <summary>
        /// Initializes a new instance of the <see cref="CompileSoftwareCommand"/> class.
        /// </summary>
        /// <param name="plcService">The PLC service instance.</param>
        public CompileSoftwareCommand(IPlcService plcService)
        {
            _plcService = plcService;
        }

        /// <inheritdoc/>
        public string Name => "compile-software";

        /// <inheritdoc/>
        public string Description => "Compiles the PLC software";

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
                    "password",
                    CommandParameterType.String,
                    "Optional password for protected blocks",
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

                var password = args.GetString("password");

                // Open PLC if not already open
                if (!_plcService.IsSoftwareOpen || _plcService.CurrentPlcName != plcName)
                {
                    await _plcService.OpenPlcAsync(plcName);
                }

                var result = await _plcService.CompileSoftwareAsync(password);

                return CommandResult.CreateSuccess(
                    data: new
                    {
                        plcName,
                        success = result.Success,
                        errorCount = result.ErrorCount,
                        warningCount = result.WarningCount,
                        message = result.Message
                    },
                    message: result.Success
                        ? $"Compilation successful: {result.ErrorCount} errors, {result.WarningCount} warnings"
                        : $"Compilation failed: {result.Message}"
                );
            }
            catch (System.Exception ex)
            {
                logger.LogError(ex, "Error compiling software");
                return CommandResult.CreateFailure(
                    $"Error compiling software: {ex.Message}",
                    ex
                );
            }
        }
    }
}
