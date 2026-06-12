using OpenTiaToolkit.Core.Models;
using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Hmi
{
    /// <summary>
    /// Command to preflight or import a Unified HMI JSON export or patch.
    /// </summary>
    public class ImportHmiJsonCommand : ICommand
    {
        private readonly IHmiService _hmiService;

        public ImportHmiJsonCommand(IHmiService hmiService)
        {
            _hmiService = hmiService;
        }

        public string Name => "import-hmi-json";

        public string Description => "Preflights or imports a Unified HMI JSON export or patch";

        public CommandSchema Schema => new CommandSchema(
            Description,
            new[]
            {
                new CommandParameter("hmi-name", CommandParameterType.String, "The Unified HMI name", required: true),
                new CommandParameter("input-path", CommandParameterType.Path, "Source folder or .json file path", required: true),
                new CommandParameter("dry-run", CommandParameterType.Boolean, "Validate changes without applying them", required: false, defaultValue: false),
                new CommandParameter("fail-on-warning", CommandParameterType.Boolean, "Treat warnings as a safety block", required: false, defaultValue: false),
                new CommandParameter("require-clean-preflight", CommandParameterType.Boolean, "Run a blocking clean preflight before apply", required: false, defaultValue: false),
                new CommandParameter("backup", CommandParameterType.Boolean, "Create a backup export before applying changes", required: false, defaultValue: false),
                new CommandParameter("backup-path", CommandParameterType.Path, "Optional backup folder or .json file path", required: false),
                new CommandParameter("screens", CommandParameterType.String, "Optional comma-separated screen paths", required: false),
                new CommandParameter("item-names", CommandParameterType.String, "Optional comma-separated item names", required: false),
                new CommandParameter("properties", CommandParameterType.String, "Optional comma-separated property paths", required: false)
            });

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            var hmiName = args.GetString("hmi-name");
            var inputPath = args.GetString("input-path");

            if (string.IsNullOrWhiteSpace(hmiName))
            {
                return CommandResult.CreateFailure("Missing required parameter: hmi-name");
            }

            if (string.IsNullOrWhiteSpace(inputPath))
            {
                return CommandResult.CreateFailure("Missing required parameter: input-path");
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
            var executionOptions = new HmiJsonImportExecutionOptions
            {
                FailOnWarning = args.Has("fail-on-warning") && args.GetBool("fail-on-warning"),
                RequireCleanPreflight = args.Has("require-clean-preflight") && args.GetBool("require-clean-preflight"),
                BackupBeforeImport = args.Has("backup") && args.GetBool("backup"),
                BackupPath = args.Has("backup-path") ? args.GetString("backup-path") : null
            };

            var dryRun = args.Has("dry-run") && args.GetBool("dry-run");
            var importResult = await _hmiService.ImportJsonAsync(
                inputPath,
                HasAnyFilter(filters) ? filters : null,
                dryRun,
                executionOptions);

            if (importResult == null)
            {
                return CommandResult.CreateFailure("Failed to run Unified HMI JSON import.");
            }

            var verb = dryRun ? "preflight" : "import";
            var message = importResult.SafetyBlocked
                ? $"Unified HMI JSON {verb} completed with a safety block."
                : $"Unified HMI JSON {verb} completed.";

            return CommandResult.CreateSuccess(importResult, message);
        }

        private static bool HasAnyFilter(HmiJsonFilterOptions filters)
        {
            return filters.Screens.Any() || filters.ItemNames.Any() || filters.Properties.Any();
        }
    }
}
