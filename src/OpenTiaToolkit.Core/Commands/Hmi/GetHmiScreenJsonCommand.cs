using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Hmi
{
    /// <summary>
    /// Command to export one Unified HMI screen as canonical JSON for agent editing.
    /// </summary>
    public class GetHmiScreenJsonCommand : ICommand
    {
        private readonly IHmiService _hmiService;

        public GetHmiScreenJsonCommand(IHmiService hmiService)
        {
            _hmiService = hmiService;
        }

        public string Name => "get-hmi-screen-json";

        public string Description => "Gets one Unified HMI screen as canonical JSON for agent editing";

        public CommandSchema Schema => new CommandSchema(
            Description,
            new[]
            {
                new CommandParameter("hmi-name", CommandParameterType.String, "The Unified HMI name", required: true),
                new CommandParameter("screen-path", CommandParameterType.String, "The slash-delimited screen path", required: true),
                new CommandParameter("item-names", CommandParameterType.StringArray, "Optional item names to include", required: false),
                new CommandParameter("properties", CommandParameterType.StringArray, "Optional property/composite names to include", required: false),
                new CommandParameter("working-path", CommandParameterType.Path, "Optional artifact root path", required: false)
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

            var filters = HmiJsonCommandSupport.BuildFilters(args);
            var result = await _hmiService.GetScreenJsonAsync(
                screenPath,
                HasAgentFilters(filters) ? filters : null,
                args.Has("working-path") ? args.GetString("working-path") : null);

            return result != null
                ? CommandResult.CreateSuccess(result, $"Unified HMI screen JSON exported: {result.ScreenPath}")
                : CommandResult.CreateFailure($"Failed to get Unified HMI screen JSON: {screenPath}");
        }

        private static bool HasAgentFilters(HmiJsonFilterOptions filters)
        {
            return filters.ItemNames.Any() || filters.Properties.Any();
        }
    }
}
