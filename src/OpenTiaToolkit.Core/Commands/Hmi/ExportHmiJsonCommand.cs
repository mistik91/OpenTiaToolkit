using OpenTiaToolkit.Core.Models;
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
    public class ExportHmiJsonCommand : IContextualCommand
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

        public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            return ExecuteAsync(args, logger, CommandExecutionContext.None);
        }

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger, CommandExecutionContext context)
        {
            context.StartCommand(Name, "Validating Unified HMI JSON export inputs.");
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
                context.Checkpoint($"Opening Unified HMI: {hmiName}.", 20, "open-hmi");
                var opened = await _hmiService.OpenUnifiedHmiAsync(hmiName);
                if (!opened)
                {
                    return CommandResult.CreateFailure($"Failed to open Unified HMI: {hmiName}");
                }
            }

            context.Checkpoint("Building Unified HMI JSON export filters.", 35, "prepare-filters");
            var filters = HmiJsonCommandSupport.BuildFilters(args);
            context.Checkpoint("Exporting Unified HMI JSON from TIA Portal.", 55, "export-hmi-json");
            var exportResult = await _hmiService.ExportJsonAsync(
                outputPath,
                HasAnyFilter(filters) ? filters : null);

            if (exportResult == null)
            {
                return CommandResult.CreateFailure("Failed to export Unified HMI JSON.");
            }

            context.CompleteCommand(Name, $"Unified HMI JSON export written to: {exportResult.OutputPath}");
            return CommandResult.CreateSuccess(exportResult, $"Unified HMI JSON export written to: {exportResult.OutputPath}");
        }

        private static bool HasAnyFilter(HmiJsonFilterOptions filters)
        {
            return filters.Screens.Any() || filters.ItemNames.Any() || filters.Properties.Any();
        }
    }
}
