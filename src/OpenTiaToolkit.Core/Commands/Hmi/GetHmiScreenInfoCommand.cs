using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Hmi
{
    /// <summary>
    /// Command to get detailed Unified HMI screen information.
    /// </summary>
    public class GetHmiScreenInfoCommand : ICommand
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

            var screenInfo = await _hmiService.GetScreenInfoAsync(screenPath);
            return screenInfo != null
                ? CommandResult.CreateSuccess(screenInfo, $"Retrieved screen info for: {screenPath}")
                : CommandResult.CreateFailure($"Unified HMI screen not found: {screenPath}");
        }
    }
}
