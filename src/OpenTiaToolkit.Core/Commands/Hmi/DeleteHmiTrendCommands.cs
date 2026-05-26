using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Hmi
{
    /// <summary>
    /// Removes one Unified HMI logging tag from an expected data log.
    /// </summary>
    public sealed class RemoveHmiLoggingTagCommand : ICommand
    {
        private readonly IHmiService _hmiService;

        public RemoveHmiLoggingTagCommand(IHmiService hmiService)
        {
            _hmiService = hmiService;
        }

        public string Name => "remove-hmi-logging-tag";

        public string Description => "Removes one Unified HMI logging tag after verifying its data log association";

        public CommandSchema Schema => new CommandSchema(Description, new[]
        {
            new CommandParameter("hmi-name", CommandParameterType.String, "The Unified HMI name", required: true),
            new CommandParameter("tag-path", CommandParameterType.String, "The HMI tag path", required: true),
            new CommandParameter("datalog-name", CommandParameterType.String, "The expected data log name", required: true),
            new CommandParameter("logging-tag-name", CommandParameterType.String, "Optional explicit logging tag name; defaults to the toolkit-derived name", required: false),
            new CommandParameter("confirm", CommandParameterType.Boolean, "Confirm deletion", required: true)
        });

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            var hmiName = args.GetString("hmi-name");
            var tagPath = args.GetString("tag-path");
            var dataLogName = args.GetString("datalog-name");

            if (string.IsNullOrWhiteSpace(hmiName))
            {
                return CommandResult.CreateFailure("Missing required parameter: hmi-name");
            }

            if (string.IsNullOrWhiteSpace(tagPath))
            {
                return CommandResult.CreateFailure("Missing required parameter: tag-path");
            }

            if (string.IsNullOrWhiteSpace(dataLogName))
            {
                return CommandResult.CreateFailure("Missing required parameter: datalog-name");
            }

            if (!args.GetBool("confirm"))
            {
                return CommandResult.CreateFailure("Deletion requires confirm=true.");
            }

            if (!_hmiService.IsHmiOpen || !string.Equals(_hmiService.CurrentHmiName, hmiName, StringComparison.OrdinalIgnoreCase))
            {
                var opened = await _hmiService.OpenUnifiedHmiAsync(hmiName);
                if (!opened)
                {
                    return CommandResult.CreateFailure($"Failed to open Unified HMI: {hmiName}");
                }
            }

            var result = await _hmiService.RemoveLoggingTagAsync(
                tagPath,
                dataLogName,
                confirm: true,
                args.Has("logging-tag-name") ? args.GetString("logging-tag-name") : null);

            if (result == null)
            {
                return CommandResult.CreateFailure($"Failed to remove logging tag for Unified HMI tag: {tagPath}");
            }

            return result.Deleted
                ? CommandResult.CreateSuccess(result, $"Removed Unified HMI logging tag: {result.LoggingTagName}")
                : CommandResult.CreateFailure(result.Error ?? "Failed to remove Unified HMI logging tag.", result);
        }
    }

    /// <summary>
    /// Deletes one Unified HMI data log only when it is no longer referenced.
    /// </summary>
    public sealed class DeleteHmiDataLogCommand : ICommand
    {
        private readonly IHmiService _hmiService;

        public DeleteHmiDataLogCommand(IHmiService hmiService)
        {
            _hmiService = hmiService;
        }

        public string Name => "delete-hmi-data-log";

        public string Description => "Deletes one Unified HMI data log archive when no logging tags still reference it";

        public CommandSchema Schema => new CommandSchema(Description, new[]
        {
            new CommandParameter("hmi-name", CommandParameterType.String, "The Unified HMI name", required: true),
            new CommandParameter("datalog-name", CommandParameterType.String, "The data log archive name", required: true),
            new CommandParameter("confirm", CommandParameterType.Boolean, "Confirm deletion", required: true)
        });

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            var hmiName = args.GetString("hmi-name");
            var dataLogName = args.GetString("datalog-name");

            if (string.IsNullOrWhiteSpace(hmiName))
            {
                return CommandResult.CreateFailure("Missing required parameter: hmi-name");
            }

            if (string.IsNullOrWhiteSpace(dataLogName))
            {
                return CommandResult.CreateFailure("Missing required parameter: datalog-name");
            }

            if (!args.GetBool("confirm"))
            {
                return CommandResult.CreateFailure("Deletion requires confirm=true.");
            }

            if (!_hmiService.IsHmiOpen || !string.Equals(_hmiService.CurrentHmiName, hmiName, StringComparison.OrdinalIgnoreCase))
            {
                var opened = await _hmiService.OpenUnifiedHmiAsync(hmiName);
                if (!opened)
                {
                    return CommandResult.CreateFailure($"Failed to open Unified HMI: {hmiName}");
                }
            }

            var result = await _hmiService.DeleteDataLogAsync(dataLogName, confirm: true);
            if (result == null)
            {
                return CommandResult.CreateFailure($"Failed to delete Unified HMI data log: {dataLogName}");
            }

            return result.Deleted
                ? CommandResult.CreateSuccess(result, $"Deleted Unified HMI data log: {dataLogName}")
                : CommandResult.CreateFailure(result.Error ?? "Failed to delete Unified HMI data log.", result);
        }
    }
}
