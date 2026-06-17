using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Hmi
{
    /// <summary>
    /// Command to create a Unified HMI screen.
    /// </summary>
    public class CreateHmiScreenCommand : IContextualCommand
    {
        private readonly IHmiService _hmiService;

        public CreateHmiScreenCommand(IHmiService hmiService)
        {
            _hmiService = hmiService;
        }

        public string Name => "create-hmi-screen";

        public string Description => "Creates a Unified HMI screen if it does not already exist";

        public CommandSchema Schema => new CommandSchema(
            Description,
            new[]
            {
                new CommandParameter("hmi-name", CommandParameterType.String, "The Unified HMI name", required: true),
                new CommandParameter("screen-path", CommandParameterType.String, "The slash-delimited screen path", required: true),
                new CommandParameter("width", CommandParameterType.Integer, "Optional screen width", required: false),
                new CommandParameter("height", CommandParameterType.Integer, "Optional screen height", required: false)
            });

        public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            return ExecuteAsync(args, logger, CommandExecutionContext.None);
        }

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger, CommandExecutionContext context)
        {
            context.StartCommand(Name, "Creating Unified HMI screen.");
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

            var width = args.Has("width") ? (int?)args.GetInt("width") : null;
            var height = args.Has("height") ? (int?)args.GetInt("height") : null;
            context.Checkpoint($"Ensuring Unified HMI screen: {screenPath}.", 65, "create-screen");
            var screenInfo = await _hmiService.CreateScreenAsync(screenPath, width, height);

            context.CompleteCommand(Name, screenInfo != null ? $"Unified HMI screen ensured: {screenPath}" : $"Failed to create Unified HMI screen: {screenPath}");
            return screenInfo != null
                ? CommandResult.CreateSuccess(screenInfo, $"Unified HMI screen ensured: {screenPath}")
                : CommandResult.CreateFailure($"Failed to create Unified HMI screen: {screenPath}");
        }
    }
}
