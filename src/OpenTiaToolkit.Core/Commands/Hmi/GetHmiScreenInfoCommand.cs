using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Hmi
{
    /// <summary>
    /// Command to get detailed Unified HMI screen information.
    /// </summary>
    public class GetHmiScreenInfoCommand : IContextualCommand
    {
        private readonly IHmiService _hmiService;

        public GetHmiScreenInfoCommand(IHmiService hmiService)
        {
            _hmiService = hmiService;
        }

        public string Name => "get-hmi-screen-info";

        public string Description => "Gets detailed information about a Unified HMI screen";

        public CommandSchema Schema => new CommandSchema(
            Description,
            new[]
            {
                new CommandParameter("hmi-name", CommandParameterType.String, "The Unified HMI name", required: true),
                new CommandParameter("screen-path", CommandParameterType.String, "The slash-delimited screen path", required: true)
            });

        public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            return ExecuteAsync(args, logger, CommandExecutionContext.None);
        }

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger, CommandExecutionContext context)
        {
            context.StartCommand(Name, "Reading Unified HMI screen information.");
            var hmiName = args.GetString("hmi-name");
            var screenPath = args.GetString("screen-path");

            if (string.IsNullOrWhiteSpace(hmiName))
            {
                return CommandResult.CreateFailure("Missing required parameter: hmi-name");
            }

            if (string.IsNullOrWhiteSpace(screenPath))
            {
                return CommandResult.CreateFailure("Missing required parameter: screen-path");
            }

            if (!_hmiService.IsHmiOpen || !string.Equals(_hmiService.CurrentHmiName, hmiName, StringComparison.OrdinalIgnoreCase))
            {
                context.Checkpoint($"Opening Unified HMI: {hmiName}.", 25, "open-hmi");
                var opened = await _hmiService.OpenUnifiedHmiAsync(hmiName);
                if (!opened)
                {
                    return CommandResult.CreateFailure($"Failed to open Unified HMI: {hmiName}");
                }
            }

            context.Checkpoint($"Reading Unified HMI screen: {screenPath}.", 65, "read-screen");
            var screenInfo = await _hmiService.GetScreenInfoAsync(screenPath);
            context.CompleteCommand(Name, screenInfo != null ? $"Retrieved screen info for: {screenPath}" : $"Unified HMI screen not found: {screenPath}");
            return screenInfo != null
                ? CommandResult.CreateSuccess(screenInfo, $"Retrieved screen info for: {screenPath}")
                : CommandResult.CreateFailure($"Unified HMI screen not found: {screenPath}");
        }
    }
}
