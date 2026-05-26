using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Hmi
{
    internal static class HmiTrendBulkCommandSupport
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        public static async Task<CommandResult?> EnsureHmiOpenAsync(
            IHmiService service,
            string hmiName,
            string operation)
        {
            if (string.IsNullOrWhiteSpace(hmiName))
            {
                return CommandResult.CreateFailure("Missing required parameter: hmi-name");
            }

            if (!service.IsHmiOpen || !string.Equals(service.CurrentHmiName, hmiName, StringComparison.OrdinalIgnoreCase))
            {
                var opened = await service.OpenUnifiedHmiAsync(hmiName);
                if (!opened)
                {
                    return CommandResult.CreateFailure($"Failed to open Unified HMI for {operation}: {hmiName}");
                }
            }

            return null;
        }

        public static bool TryParseConfigs<T>(CommandArgs args, out IReadOnlyList<T> configs, out string error)
        {
            configs = Array.Empty<T>();
            error = string.Empty;
            var raw = args.GetObject("configs");
            if (raw == null)
            {
                error = "Missing required parameter: configs";
                return false;
            }

            try
            {
                var json = raw is JsonElement element ? element.GetRawText() : raw.ToString();
                var parsed = JsonSerializer.Deserialize<List<T>>(json ?? string.Empty, JsonOptions);
                if (parsed == null || parsed.Count == 0)
                {
                    error = "Parameter configs must contain at least one configuration object.";
                    return false;
                }

                configs = parsed;
                return true;
            }
            catch (Exception ex)
            {
                error = $"Invalid configs JSON: {ex.Message}";
                return false;
            }
        }

        public static CommandResult Result(string operation, HmiTrendBulkResult? result)
        {
            if (result == null)
            {
                return CommandResult.CreateFailure($"Failed to execute {operation}.");
            }

            var message =
                $"{operation}: {result.AppliedCount} applied, {result.PlannedCount} planned, " +
                $"{result.BlockedCount} blocked, {result.NotFoundCount} not found, {result.FailedCount} failed.";
            return result.BlockedCount == 0 && result.NotFoundCount == 0 && result.FailedCount == 0
                ? CommandResult.CreateSuccess(result, message)
                : CommandResult.CreateFailure(message, result);
        }
    }

    public sealed class CreateHmiTrendsCommand : ICommand
    {
        private readonly IHmiService _hmiService;

        public CreateHmiTrendsCommand(IHmiService hmiService)
        {
            _hmiService = hmiService;
        }

        public string Name => "create-hmi-trends";

        public string Description => "Plans or applies multiple Unified HMI trend logging configurations";

        public CommandSchema Schema => new CommandSchema(Description, new[]
        {
            new CommandParameter("hmi-name", CommandParameterType.String, "The Unified HMI name", required: true),
            new CommandParameter("configs", CommandParameterType.ObjectArray, "Trend configs containing tagPath, dataLogName, cyclic, cycle, and optional sizing properties", required: true),
            new CommandParameter("dry-run", CommandParameterType.Boolean, "Validate and report planned configurations without applying them", required: false, defaultValue: true)
        });

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            var openFailure = await HmiTrendBulkCommandSupport.EnsureHmiOpenAsync(_hmiService, args.GetString("hmi-name"), Name);
            if (openFailure != null)
            {
                return openFailure;
            }

            if (!HmiTrendBulkCommandSupport.TryParseConfigs<HmiTrendCreateRequest>(args, out var configs, out var error))
            {
                return CommandResult.CreateFailure(error);
            }

            var result = await _hmiService.CreateTrendsAsync(configs, args.GetBool("dry-run", true));
            return HmiTrendBulkCommandSupport.Result("Unified HMI trend batch configuration", result);
        }
    }

    public sealed class RemoveHmiLoggingTagsCommand : ICommand
    {
        private readonly IHmiService _hmiService;

        public RemoveHmiLoggingTagsCommand(IHmiService hmiService)
        {
            _hmiService = hmiService;
        }

        public string Name => "remove-hmi-logging-tags";

        public string Description => "Plans or removes multiple Unified HMI logging tags after data log verification";

        public CommandSchema Schema => new CommandSchema(Description, new[]
        {
            new CommandParameter("hmi-name", CommandParameterType.String, "The Unified HMI name", required: true),
            new CommandParameter("configs", CommandParameterType.ObjectArray, "Removal configs containing tagPath, dataLogName, and optional loggingTagName", required: true),
            new CommandParameter("dry-run", CommandParameterType.Boolean, "Validate and report planned removals without applying them", required: false, defaultValue: true),
            new CommandParameter("confirm", CommandParameterType.Boolean, "Confirm deletion when dry-run is false", required: false, defaultValue: false)
        });

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            var dryRun = args.GetBool("dry-run", true);
            if (!dryRun && !args.GetBool("confirm"))
            {
                return CommandResult.CreateFailure("Deletion requires confirm=true when dry-run=false.");
            }

            var openFailure = await HmiTrendBulkCommandSupport.EnsureHmiOpenAsync(_hmiService, args.GetString("hmi-name"), Name);
            if (openFailure != null)
            {
                return openFailure;
            }

            if (!HmiTrendBulkCommandSupport.TryParseConfigs<HmiLoggingTagRemoveRequest>(args, out var configs, out var error))
            {
                return CommandResult.CreateFailure(error);
            }

            var result = await _hmiService.RemoveLoggingTagsAsync(configs, args.GetBool("confirm"), dryRun);
            return HmiTrendBulkCommandSupport.Result("Unified HMI logging tag batch removal", result);
        }
    }

    public sealed class DeleteHmiDataLogsCommand : ICommand
    {
        private readonly IHmiService _hmiService;

        public DeleteHmiDataLogsCommand(IHmiService hmiService)
        {
            _hmiService = hmiService;
        }

        public string Name => "delete-hmi-data-logs";

        public string Description => "Plans or deletes multiple unreferenced Unified HMI data log archives";

        public CommandSchema Schema => new CommandSchema(Description, new[]
        {
            new CommandParameter("hmi-name", CommandParameterType.String, "The Unified HMI name", required: true),
            new CommandParameter("configs", CommandParameterType.ObjectArray, "Data log configs containing dataLogName", required: true),
            new CommandParameter("dry-run", CommandParameterType.Boolean, "Validate and report planned archive deletions without applying them", required: false, defaultValue: true),
            new CommandParameter("confirm", CommandParameterType.Boolean, "Confirm deletion when dry-run is false", required: false, defaultValue: false)
        });

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            var dryRun = args.GetBool("dry-run", true);
            if (!dryRun && !args.GetBool("confirm"))
            {
                return CommandResult.CreateFailure("Deletion requires confirm=true when dry-run=false.");
            }

            var openFailure = await HmiTrendBulkCommandSupport.EnsureHmiOpenAsync(_hmiService, args.GetString("hmi-name"), Name);
            if (openFailure != null)
            {
                return openFailure;
            }

            if (!HmiTrendBulkCommandSupport.TryParseConfigs<HmiDataLogDeleteRequest>(args, out var configs, out var error))
            {
                return CommandResult.CreateFailure(error);
            }

            var result = await _hmiService.DeleteDataLogsAsync(configs, args.GetBool("confirm"), dryRun);
            return HmiTrendBulkCommandSupport.Result("Unified HMI data log batch deletion", result);
        }
    }
}
