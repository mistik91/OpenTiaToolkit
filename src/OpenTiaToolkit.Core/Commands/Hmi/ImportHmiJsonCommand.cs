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
    public class ImportHmiJsonCommand : IContextualCommand
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
                new CommandParameter("dry-run", CommandParameterType.Boolean, "Validate changes without applying them", required: false, defaultValue: true),
                new CommandParameter("apply", CommandParameterType.Boolean, "Explicitly request an import apply; requires dry-run=false and confirm=true", required: false, defaultValue: false),
                new CommandParameter("confirm", CommandParameterType.Boolean, "Confirm the reviewed import apply", required: false, defaultValue: false),
                new CommandParameter("fail-on-warning", CommandParameterType.Boolean, "Treat warnings as a safety block", required: false, defaultValue: true),
                new CommandParameter("require-clean-preflight", CommandParameterType.Boolean, "Run a blocking clean preflight before apply", required: false, defaultValue: true),
                new CommandParameter("backup", CommandParameterType.Boolean, "Create a backup export before applying changes", required: false, defaultValue: true),
                new CommandParameter("backup-path", CommandParameterType.Path, "Optional backup folder or .json file path", required: false),
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
            context.StartCommand(Name, "Validating Unified HMI JSON import inputs.");
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

            var dryRun = !args.Has("dry-run") || args.GetBool("dry-run", true);
            var apply = args.Has("apply") && args.GetBool("apply");
            var confirm = args.Has("confirm") && args.GetBool("confirm");
            if (dryRun && apply)
            {
                return CommandResult.CreateFailure("Parameter apply=true requires dry-run=false.");
            }

            if (!dryRun && (!apply || !confirm))
            {
                return CommandResult.CreateFailure(
                    "Applying Unified HMI JSON requires dry-run=false, apply=true, and confirm=true after reviewing a dry-run result.");
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

            context.Checkpoint("Building Unified HMI JSON import filters and safety options.", 35, "prepare-import");
            var filters = HmiJsonCommandSupport.BuildFilters(args);
            var executionOptions = new HmiJsonImportExecutionOptions
            {
                FailOnWarning = !args.Has("fail-on-warning") || args.GetBool("fail-on-warning", true),
                RequireCleanPreflight = !args.Has("require-clean-preflight") || args.GetBool("require-clean-preflight", true),
                BackupBeforeImport = !dryRun && (!args.Has("backup") || args.GetBool("backup", true)),
                BackupPath = args.Has("backup-path") ? args.GetString("backup-path") : null
            };

            context.Checkpoint(
                dryRun ? "Preflighting Unified HMI JSON import." : "Importing Unified HMI JSON into TIA Portal.",
                55,
                dryRun ? "preflight-hmi-json" : "import-hmi-json");
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

            context.CompleteCommand(Name, message);
            return importResult.SafetyBlocked
                ? CommandResult.CreateFailure(message, importResult)
                : CommandResult.CreateSuccess(importResult, message);
        }

        private static bool HasAnyFilter(HmiJsonFilterOptions filters)
        {
            return filters.Screens.Any() || filters.ItemNames.Any() || filters.Properties.Any();
        }
    }
}
