using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Hmi
{
    /// <summary>
    /// Command to create or update Unified HMI trend logging infrastructure.
    /// </summary>
    public class CreateHmiTrendCommand : IContextualCommand
    {
        private readonly IHmiService _hmiService;

        public CreateHmiTrendCommand(IHmiService hmiService)
        {
            _hmiService = hmiService;
        }

        public string Name => "create-hmi-trend";

        public string Description => "Creates or updates Unified HMI logging infrastructure for a tag";

        public CommandSchema Schema => new CommandSchema(
            Description,
            new[]
            {
                new CommandParameter("hmi-name", CommandParameterType.String, "The Unified HMI name", required: true),
                new CommandParameter("tag-path", CommandParameterType.String, "The HMI tag path", required: true),
                new CommandParameter("datalog-name", CommandParameterType.String, "The target data log name", required: true),
                new CommandParameter("cyclic", CommandParameterType.Boolean, "Whether cyclic logging should be used", required: false, defaultValue: false),
                new CommandParameter("cycle", CommandParameterType.String, "Optional cycle value, such as T5s", required: false),
                new CommandParameter("storage-folder", CommandParameterType.String, "Compatibility option; Unified HMI storage folder is read-only and is not changed", required: false, defaultValue: "/Trends"),
                new CommandParameter("log-max-size", CommandParameterType.Integer, "Optional data log max size", required: false, defaultValue: 2000),
                new CommandParameter("duration-days", CommandParameterType.Integer, "Optional log duration in days", required: false, defaultValue: 365),
                new CommandParameter("segment-max-size", CommandParameterType.Integer, "Optional segment max size", required: false, defaultValue: 500)
            });

        public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            return ExecuteAsync(args, logger, CommandExecutionContext.None);
        }

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger, CommandExecutionContext context)
        {
            context.StartCommand(Name, "Configuring Unified HMI trend.");
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

            if (!_hmiService.IsHmiOpen || !string.Equals(_hmiService.CurrentHmiName, hmiName, StringComparison.OrdinalIgnoreCase))
            {
                context.Checkpoint($"Opening Unified HMI: {hmiName}.", 25, "open-hmi");
                var opened = await _hmiService.OpenUnifiedHmiAsync(hmiName);
                if (!opened)
                {
                    return CommandResult.CreateFailure($"Failed to open Unified HMI: {hmiName}");
                }
            }

            context.Checkpoint($"Configuring Unified HMI trend for tag: {tagPath}.", 65, "configure-trend");
            var trendInfo = await _hmiService.CreateTrendAsync(
                tagPath,
                dataLogName,
                args.Has("cyclic") && args.GetBool("cyclic"),
                args.Has("cycle") ? args.GetString("cycle") : null,
                args.Has("storage-folder") ? args.GetString("storage-folder") : "/Trends",
                args.Has("log-max-size") ? args.GetInt("log-max-size") : 2000,
                args.Has("duration-days") ? args.GetInt("duration-days") : 365,
                args.Has("segment-max-size") ? args.GetInt("segment-max-size") : 500);

            context.CompleteCommand(Name, trendInfo != null ? $"Unified HMI trend configured for tag: {tagPath}" : $"Failed to configure Unified HMI trend for tag: {tagPath}");
            return trendInfo != null
                ? CommandResult.CreateSuccess(trendInfo, $"Unified HMI trend configured for tag: {tagPath}")
                : CommandResult.CreateFailure($"Failed to configure Unified HMI trend for tag: {tagPath}");
        }
    }
}
