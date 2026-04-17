using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Hmi
{
    /// <summary>
    /// Command to create a Unified HMI screen.
    /// </summary>
    public class CreateHmiScreenCommand : ICommand
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

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
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
                var opened = await _hmiService.OpenUnifiedHmiAsync(hmiName);
                if (!opened)
                {
                    return CommandResult.CreateFailure($"Failed to open Unified HMI: {hmiName}");
                }
            }

            var width = args.Has("width") ? (int?)args.GetInt("width") : null;
            var height = args.Has("height") ? (int?)args.GetInt("height") : null;
            var screenInfo = await _hmiService.CreateScreenAsync(screenPath, width, height);

            return screenInfo != null
                ? CommandResult.CreateSuccess(screenInfo, $"Unified HMI screen ensured: {screenPath}")
                : CommandResult.CreateFailure($"Failed to create Unified HMI screen: {screenPath}");
        }
    }
}
