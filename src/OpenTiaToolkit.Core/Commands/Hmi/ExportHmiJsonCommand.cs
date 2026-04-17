using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Hmi
{
    /// <summary>
    /// Command to export the supported Unified HMI surface to JSON.
    /// </summary>
    public class ExportHmiJsonCommand : ICommand
    {
        private readonly IHmiService _hmiService;

        public ExportHmiJsonCommand(IHmiService hmiService)
        {
            _hmiService = hmiService;
        }

        public string Name => "export-hmi-json";

        public string Description => "Exports the supported Unified HMI surface to JSON";

        public CommandSchema Schema => new CommandSchema(
            Description,
            new[]
            {
                new CommandParameter("hmi-name", CommandParameterType.String, "The Unified HMI name", required: true),
                new CommandParameter("output-path", CommandParameterType.Path, "Target folder or .json file path", required: true),
                new CommandParameter("screens", CommandParameterType.String, "Optional comma-separated screen paths", required: false),
                new CommandParameter("item-names", CommandParameterType.String, "Optional comma-separated item names", required: false),
                new CommandParameter("properties", CommandParameterType.String, "Optional comma-separated property paths", required: false)
            });

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            var hmiName = args.GetString("hmi-name");
            var outputPath = args.GetString("output-path");

            if (string.IsNullOrWhiteSpace(hmiName))
            {
                return CommandResult.CreateFailure("Missing required parameter: hmi-name");
            }

            if (string.IsNullOrWhiteSpace(outputPath))
            {
                return CommandResult.CreateFailure("Missing required parameter: output-path");
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
            var exportResult = await _hmiService.ExportJsonAsync(
                outputPath,
                HasAnyFilter(filters) ? filters : null);

            return exportResult != null
                ? CommandResult.CreateSuccess(exportResult, $"Unified HMI JSON export written to: {exportResult.OutputPath}")
                : CommandResult.CreateFailure("Failed to export Unified HMI JSON.");
        }

        private static bool HasAnyFilter(HmiJsonFilterOptions filters)
        {
            return filters.Screens.Any() || filters.ItemNames.Any() || filters.Properties.Any();
        }
    }
}
