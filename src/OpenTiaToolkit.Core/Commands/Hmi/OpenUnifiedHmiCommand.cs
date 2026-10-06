using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Hmi
{
    /// <summary>
    /// Command to open a Unified HMI by name.
    /// </summary>
    public class OpenUnifiedHmiCommand : IContextualCommand
    {
        private readonly IHmiService _hmiService;

        public OpenUnifiedHmiCommand(IHmiService hmiService)
        {
            _hmiService = hmiService;
        }

        public string Name => "open-unified-hmi";

        public string Description => "Opens a Unified HMI by device or device item name";

        public CommandSchema Schema => new CommandSchema(
            Description,
            new[]
            {
                new CommandParameter(
                    "hmi-name",
                    CommandParameterType.String,
                    "The Unified HMI device or device item name",
                    required: true)
            });

        public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            return ExecuteAsync(args, logger, CommandExecutionContext.None);
        }

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger, CommandExecutionContext context)
        {
            context.StartCommand(Name, "Opening Unified HMI.");
            var hmiName = args.GetString("hmi-name");
            if (string.IsNullOrWhiteSpace(hmiName))
            {
                return CommandResult.CreateFailure("Missing required parameter: hmi-name");
            }

            context.Checkpoint($"Opening Unified HMI: {hmiName}.", 50, "open-hmi");
            var success = await _hmiService.OpenUnifiedHmiAsync(hmiName);
            context.CompleteCommand(Name, success ? $"Unified HMI opened: {hmiName}" : $"Failed to open Unified HMI: {hmiName}");
            return success
                ? CommandResult.CreateSuccess(new { opened = true, hmiName }, $"Unified HMI opened: {hmiName}")
                : CommandResult.CreateFailure($"Failed to open Unified HMI: {hmiName}");
        }
    }
}
