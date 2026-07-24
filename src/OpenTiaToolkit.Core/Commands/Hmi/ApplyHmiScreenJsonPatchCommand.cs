using OpenTiaToolkit.Core.Models;
using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Hmi
{
    /// <summary>
    /// Command to dry-run or apply compact agent-oriented screen JSON patch operations.
    /// </summary>
    public class ApplyHmiScreenJsonPatchCommand : IContextualCommand
    {
        private readonly IHmiService _hmiService;

        public ApplyHmiScreenJsonPatchCommand(IHmiService hmiService)
        {
            _hmiService = hmiService;
        }

        public string Name => "apply-hmi-screen-json-patch";

        public string Description => "Dry-runs or applies compact Unified HMI screen patch operations with safety checks";

        public CommandSchema Schema => new CommandSchema(
            Description,
            new[]
            {
                new CommandParameter("hmi-name", CommandParameterType.String, "The Unified HMI name", required: true),
                new CommandParameter("screen-path", CommandParameterType.String, "The slash-delimited screen path", required: true),
                new CommandParameter("operations", CommandParameterType.Object, "Patch operations array or { operations: [...] } object", required: false),
                new CommandParameter("operations-path", CommandParameterType.Path, "Optional JSON file containing patch operations array or { operations: [...] } object", required: false),
                new CommandParameter("base-hash", CommandParameterType.String, "Optional expected hash from get-hmi-screen-json", required: false),
                new CommandParameter("dry-run", CommandParameterType.Boolean, "Validate changes without applying them", required: false, defaultValue: true),
                new CommandParameter("allow-stale-base", CommandParameterType.Boolean, "Allow applying when base-hash no longer matches", required: false, defaultValue: false),
                new CommandParameter("fail-on-warning", CommandParameterType.Boolean, "Treat warnings as a safety block", required: false, defaultValue: true),
                new CommandParameter("require-clean-preflight", CommandParameterType.Boolean, "Run a blocking clean preflight before apply", required: false, defaultValue: true),
                new CommandParameter("backup", CommandParameterType.Boolean, "Create a backup export before applying changes", required: false, defaultValue: true),
                new CommandParameter("backup-path", CommandParameterType.Path, "Optional backup folder or .json file path", required: false),
                new CommandParameter("item-names", CommandParameterType.StringArray, "Optional item names to include; keep identical to the snapshot when using base-hash", required: false),
                new CommandParameter("properties", CommandParameterType.StringArray, "Optional property/composite names to include; keep identical to the snapshot when using base-hash", required: false),
                new CommandParameter("working-path", CommandParameterType.Path, "Optional artifact root path", required: false)
            });

        public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            return ExecuteAsync(args, logger, CommandExecutionContext.None);
        }

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger, CommandExecutionContext context)
        {
            context.StartCommand(Name, "Validating Unified HMI screen patch inputs.");
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

            context.Checkpoint("Parsing Unified HMI screen patch operations.", 20, "parse-patch");
            if (!TryGetPatchOperations(args, out var operations, out var parseError))
            {
                return CommandResult.CreateFailure(parseError ?? "Missing required parameter: operations or operations-path");
            }

            if (!_hmiService.IsHmiOpen || !string.Equals(_hmiService.CurrentHmiName, hmiName, StringComparison.OrdinalIgnoreCase))
            {
                context.Checkpoint($"Opening Unified HMI: {hmiName}.", 30, "open-hmi");
                var opened = await _hmiService.OpenUnifiedHmiAsync(hmiName);
                if (!opened)
                {
                    return CommandResult.CreateFailure($"Failed to open Unified HMI: {hmiName}");
                }
            }

            context.Checkpoint("Preparing Unified HMI screen patch safety options.", 45, "prepare-patch");
            var dryRun = !args.Has("dry-run") || args.GetBool("dry-run", true);
            var filters = HmiJsonCommandSupport.BuildFilters(args);
            var executionOptions = new HmiJsonImportExecutionOptions
            {
                FailOnWarning = !args.Has("fail-on-warning") || args.GetBool("fail-on-warning", true),
                RequireCleanPreflight = !args.Has("require-clean-preflight") || args.GetBool("require-clean-preflight", true),
                BackupBeforeImport = !dryRun && (!args.Has("backup") || args.GetBool("backup", true)),
                BackupPath = args.Has("backup-path") ? args.GetString("backup-path") : null
            };

            context.Checkpoint(
                dryRun ? "Preflighting Unified HMI screen patch." : "Applying Unified HMI screen patch.",
                60,
                dryRun ? "preflight-screen-patch" : "apply-screen-patch");
            var result = await _hmiService.ApplyScreenJsonPatchAsync(
                screenPath,
                operations!,
                args.Has("base-hash") ? args.GetString("base-hash") : null,
                HasAgentFilters(filters) ? filters : null,
                dryRun,
                executionOptions,
                args.Has("working-path") ? args.GetString("working-path") : null,
                args.Has("allow-stale-base") && args.GetBool("allow-stale-base"));

            if (result == null)
            {
                return CommandResult.CreateFailure($"Failed to process Unified HMI screen JSON patch: {screenPath}");
            }

            var action = dryRun ? "preflighted" : result.Applied ? "applied" : "blocked";
            context.CompleteCommand(Name, $"Unified HMI screen JSON patch {action}: {screenPath}");
            var message = $"Unified HMI screen JSON patch {action}: {screenPath}";
            // Safety blocks are expected, structured outcomes for agent patch workflows
            // (for example, stale base hashes). Keep the command transport successful so
            // callers can inspect SafetyBlocked and recover without treating it as an
            // execution or protocol failure.
            if (result.SafetyBlocked)
            {
                return CommandResult.CreateSuccess(result, message);
            }

            return !dryRun && !result.Applied
                ? CommandResult.CreateFailure(message, result)
                : CommandResult.CreateSuccess(result, message);
        }

        private static bool HasAgentFilters(HmiJsonFilterOptions filters)
        {
            return filters.ItemNames.Any() || filters.Properties.Any();
        }

        private static bool TryGetPatchOperations(
            CommandArgs args,
            out System.Collections.Generic.IReadOnlyList<HmiScreenJsonPatchOperation>? operations,
            out string? error)
        {
            operations = null;
            error = null;

            if (args.Has("operations-path"))
            {
                var operationsPath = args.GetString("operations-path");
                if (string.IsNullOrWhiteSpace(operationsPath))
                {
                    error = "Parameter operations-path must contain a JSON file path.";
                    return false;
                }

                try
                {
                    return HmiJsonCommandSupport.TryParsePatchOperationsJson(
                        "operations-path",
                        File.ReadAllText(operationsPath),
                        out operations,
                        out error);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException)
                {
                    error = $"Failed to read operations-path '{operationsPath}': {ex.Message}";
                    return false;
                }
            }

            if (!args.Has("operations"))
            {
                error = "Missing required parameter: operations or operations-path";
                return false;
            }

            return HmiJsonCommandSupport.TryGetPatchOperationsArgument(args, "operations", out operations, out error);
        }
    }
}
