using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Hmi
{
    /// <summary>
    /// Command to dry-run or apply an agent-provided single-screen JSON edit.
    /// </summary>
    public class ApplyHmiScreenJsonEditCommand : ICommand
    {
        private readonly IHmiService _hmiService;

        public ApplyHmiScreenJsonEditCommand(IHmiService hmiService)
        {
            _hmiService = hmiService;
        }

        public string Name => "apply-hmi-screen-json-edit";

        public string Description => "Dry-runs or applies one Unified HMI screen JSON edit with safety checks";

        public CommandSchema Schema => new CommandSchema(
            Description,
            new[]
            {
                new CommandParameter("hmi-name", CommandParameterType.String, "The Unified HMI name", required: true),
                new CommandParameter("screen-path", CommandParameterType.String, "The slash-delimited screen path", required: true),
                new CommandParameter("edited-screen", CommandParameterType.Object, "The edited HmiJsonScreen object", required: true),
                new CommandParameter("base-hash", CommandParameterType.String, "Optional expected hash from get-hmi-screen-json", required: false),
                new CommandParameter("dry-run", CommandParameterType.Boolean, "Validate changes without applying them", required: false, defaultValue: true),
                new CommandParameter("allow-stale-base", CommandParameterType.Boolean, "Allow applying when base-hash no longer matches", required: false, defaultValue: false),
                new CommandParameter("fail-on-warning", CommandParameterType.Boolean, "Treat warnings as a safety block", required: false, defaultValue: true),
                new CommandParameter("require-clean-preflight", CommandParameterType.Boolean, "Run a blocking clean preflight before apply", required: false, defaultValue: true),
                new CommandParameter("backup", CommandParameterType.Boolean, "Create a backup export before applying changes", required: false, defaultValue: true),
                new CommandParameter("backup-path", CommandParameterType.Path, "Optional backup folder or .json file path", required: false),
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

            if (!HmiJsonCommandSupport.TryGetScreenArgument(args, "edited-screen", out var editedScreen, out var parseError))
            {
                return CommandResult.CreateFailure(parseError ?? "Missing required parameter: edited-screen");
            }

            if (!_hmiService.IsHmiOpen || !string.Equals(_hmiService.CurrentHmiName, hmiName, StringComparison.OrdinalIgnoreCase))
            {
                var opened = await _hmiService.OpenUnifiedHmiAsync(hmiName);
                if (!opened)
                {
                    return CommandResult.CreateFailure($"Failed to open Unified HMI: {hmiName}");
                }
            }

            var dryRun = !args.Has("dry-run") || args.GetBool("dry-run", true);
            var filters = HmiJsonCommandSupport.BuildFilters(args);
            var executionOptions = new HmiJsonImportExecutionOptions
            {
                FailOnWarning = !args.Has("fail-on-warning") || args.GetBool("fail-on-warning", true),
                RequireCleanPreflight = !args.Has("require-clean-preflight") || args.GetBool("require-clean-preflight", true),
                BackupBeforeImport = !dryRun && (!args.Has("backup") || args.GetBool("backup", true)),
                BackupPath = args.Has("backup-path") ? args.GetString("backup-path") : null
            };

            var result = await _hmiService.ApplyScreenJsonEditAsync(
                screenPath,
                editedScreen!,
                args.Has("base-hash") ? args.GetString("base-hash") : null,
                HasAgentFilters(filters) ? filters : null,
                dryRun,
                executionOptions,
                args.Has("working-path") ? args.GetString("working-path") : null,
                args.Has("allow-stale-base") && args.GetBool("allow-stale-base"));

            if (result == null)
            {
                return CommandResult.CreateFailure($"Failed to process Unified HMI screen JSON edit: {screenPath}");
            }

            var action = dryRun ? "preflighted" : result.Applied ? "applied" : "blocked";
            return CommandResult.CreateSuccess(result, $"Unified HMI screen JSON edit {action}: {screenPath}");
        }

        private static bool HasAgentFilters(HmiJsonFilterOptions filters)
        {
            return filters.ItemNames.Any() || filters.Properties.Any();
        }
    }
}
