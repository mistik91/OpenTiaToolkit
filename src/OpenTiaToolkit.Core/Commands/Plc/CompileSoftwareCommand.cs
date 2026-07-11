using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Plc
{
    /// <summary>
    /// Command to compile PLC software.
    /// </summary>
    public class CompileSoftwareCommand : IContextualCommand
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
        public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            return ExecuteAsync(args, logger, CommandExecutionContext.None);
        }

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger, CommandExecutionContext context)
        {
            logger.LogInformation("Executing command: {CommandName}", Name);
            context.StartCommand(Name, "Validating PLC compile inputs.");

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
                    context.Checkpoint($"Opening PLC software: {plcName}.", 25, "open-plc");
                    await _plcService.OpenPlcAsync(plcName);
                }

                context.Checkpoint("Compiling PLC software in TIA Portal.", 50, "compile-software");
                var result = await _plcService.CompileSoftwareAsync(password);
                context.CompleteCommand(Name, result.Success ? "PLC software compile completed." : "PLC software compile completed with errors.");

                var data = new
                {
                    plcName,
                    success = result.Success,
                    errorCount = result.ErrorCount,
                    warningCount = result.WarningCount,
                    message = result.Message,
                    diagnostics = result.Diagnostics
                };
                var message = result.Success
                    ? $"Compilation successful: {result.ErrorCount} errors, {result.WarningCount} warnings"
                    : $"Compilation failed: {result.Message}";

                return result.Success
                    ? CommandResult.CreateSuccess(data, message)
                    : CommandResult.CreateFailure(message, data);
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
